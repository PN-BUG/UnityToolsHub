#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub 主菜单面板
/// 左侧显示分类工具列表，右侧显示对应面板详情
///
/// 使用 partial class 拆分为多个文件：
///   DataStructures.cs  — 数据结构（ToolEntry, CategoryNode, UsageStats, HiddenItems）
///   ShortcutBinding.cs — 快捷键绑定结构体
///   HubCompat.cs       — 兼容层，别名引用已迁移到 Nodin 包的 Theme/Styles/Drawing
///   ToolDiscovery.cs   — 工具自动发现与创建
///   ShortcutManager.cs — 快捷键管理逻辑
///   LeftPanel.cs       — 左侧面板绘制
///   RightPanel.cs      — 右侧面板绘制（欢迎/详情/创建/隐藏项）
/// </summary>
public partial class UnityToolsHub : EditorWindow
{
    #region 状态
    private List<CategoryNode> _categories = new List<CategoryNode>();
    private ToolEntry _selectedTool;
    private CategoryNode _selectedCategory;
    private Vector2 _leftScroll;
    private Vector2 _rightScroll;
    private string _searchText = "";
    private float _animTimer;

    // ── 创建工具表单状态 ────────────────────────────────
    private bool _showCreateForm;
    private string _createToolName = "新工具";
    private string _createClassName = "NewTool";
    private string _createDirectory = "Assets/Editor/Tools";
    private string _createDescription = "";
    private string _createCategory = "编辑器工具";
    private string _createIcon = "⚙";
    private string _createTags = "";
    private string _createShortcut = "";
    private Vector2 _createScroll;
    private string _lastAutoClassName = "NewTool";
    private double _lastRepaintTime;

    // ── 快捷键隐藏/恢复状态 ────────────────────────────
    private static bool _isHidden;
    private static Rect _savedPosition;
    private static Vector2 _savedMinSize;
    private const string SavedPositionKey = "UnityToolsHub.SavedPosition";

    // ── 快捷键录制状态 ────────────────────────────────
    private bool _isRecordingShortcut;
    private string _recordingForTypeName;
    private double _recordingStartTime;

    // ── 使用频率 & 隐藏项状态 ──────────────────────────
    private UsageStats _usageStats = new UsageStats();
    private HiddenItems _hiddenItems = new HiddenItems();
    private bool _showHiddenManager;
    private Vector2 _hiddenMgrScroll;
    // ── 缓存索引（避免每帧 LINQ 遍历）───────────────
    private Dictionary<string, ToolEntry> _toolIndex = new Dictionary<string, ToolEntry>();
    private Dictionary<ShortcutBinding, ToolEntry> _shortcutIndex = new Dictionary<ShortcutBinding, ToolEntry>();
    private int _totalToolCount;
    private const string UsageStatsPrefsKey   = "UnityToolsHub.UsageStats";
    private const string HiddenItemsPrefsKey  = "UnityToolsHub.HiddenItems";
    #endregion

    #region 多显示器 P/Invoke
#if UNITY_EDITOR_WIN
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_XVIRTUALSCREEN  = 76;
    private const int SM_CXVIRTUALSCREEN = 78;

    private static (int x, int width) GetVirtualScreenBounds()
    {
        return (GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_CXVIRTUALSCREEN));
    }
#else
    private static (int x, int width) GetVirtualScreenBounds()
    {
        return (-200, 1920);
    }
#endif
    #endregion

    #region DockArea / ContainerWindow 反射操作
    private static object GetDockArea(EditorWindow wnd)
    {
        foreach (var fieldName in new[] { "m_Parent", "m_DockArea", "m_ParentWindow" })
        {
            var f = typeof(EditorWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null)
            {
                var val = f.GetValue(wnd);
                if (val != null)
                    return val;
            }
        }
        return null;
    }

    private static object GetContainerWindow(object dockArea)
    {
        if (dockArea == null) return null;
        var dockType = dockArea.GetType();
        foreach (var propName in new[] { "window", "containerWindow" })
        {
            var prop = dockType.GetProperty(propName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                var val = prop.GetValue(dockArea);
                if (val != null)
                    return val;
            }
        }
        return null;
    }

    private static Rect GetFloatingWindowPosition(EditorWindow wnd)
    {
        var dockArea = GetDockArea(wnd);
        var container = GetContainerWindow(dockArea);
        if (container != null)
        {
            var posProp = container.GetType().GetProperty("position",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (posProp != null && posProp.CanRead)
                return (Rect)posProp.GetValue(container);
        }
        if (dockArea != null)
        {
            var posProp = dockArea.GetType().GetProperty("position",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (posProp != null && posProp.CanRead)
                return (Rect)posProp.GetValue(dockArea);
        }
        return wnd.position;
    }

    private static void SetFloatingWindowPosition(EditorWindow wnd, Rect pos)
    {
        var dockArea = GetDockArea(wnd);
        var container = GetContainerWindow(dockArea);
        if (container != null)
        {
            var posProp = container.GetType().GetProperty("position",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (posProp != null && posProp.CanWrite)
            {
                posProp.SetValue(container, pos);
                wnd.Repaint();
                return;
            }
        }
        if (dockArea != null)
        {
            var posProp = dockArea.GetType().GetProperty("position",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (posProp != null && posProp.CanWrite)
            {
                posProp.SetValue(dockArea, pos);
                wnd.Repaint();
                return;
            }
        }
        wnd.position = pos;
    }
    #endregion

    #region 菜单入口
    [MenuItem("UnityToolsHub/主面板 %#e", false, -100)]
    public static void ShowWindow()
    {
        // 已打开：切换隐藏/恢复（不关闭，保留状态）
        if (HasOpenInstances<UnityToolsHub>())
        {
            var existing = GetWindow<UnityToolsHub>();

            // 安全检查：如果 _isHidden 为 true 但保存的位置无效，直接显示窗口
            if (_isHidden && (_savedPosition.width <= 1 || _savedPosition.height <= 1))
            {
                _isHidden = false;
            }

            if (_isHidden)
            {
                // 恢复：还原位置 + 原始尺寸 + minSize
                existing.minSize = _savedMinSize.magnitude > 0 ? _savedMinSize : new Vector2(720, 460);
                SetFloatingWindowPosition(existing, _savedPosition);
                _isHidden = false;
                existing.Show();
                existing.Focus();
            }
            else
            {
                // 隐藏：保存当前位置/尺寸/minSize
                _savedPosition = GetFloatingWindowPosition(existing);
                _savedMinSize = existing.minSize;
                EditorPrefs.SetString(SavedPositionKey, JsonUtility.ToJson(_savedPosition));
                existing.minSize = new Vector2(1, 1);
                var (vsX, _) = GetVirtualScreenBounds();
                var hidePos = new Rect(
                    vsX - 200,
                    _savedPosition.y,
                    1,
                    1);
                SetFloatingWindowPosition(existing, hidePos);
                _isHidden = true;
            }
            return;
        }
        // 首次打开
        _isHidden = false;
        var wnd = CreateWindow<UnityToolsHub>("UnityToolsHub 主面板");
        wnd.minSize = new Vector2(720, 460);
        if (EditorPrefs.HasKey(SavedPositionKey))
        {
            var saved = JsonUtility.FromJson<Rect>(EditorPrefs.GetString(SavedPositionKey));
            if (saved.width > 0 && saved.height > 0)
                wnd.position = saved;
        }
        wnd.Show();
    }
    #endregion

    #region 生命周期
    private void OnEnable()
    {
        LoadUsageStats();
        LoadHiddenItems();
        DiscoverTools();
        _animTimer = 0f;
    }

    private void Update()
    {
        // 轻量动画刷新（欢迎页渐入）
        if (_selectedTool == null && Time.realtimeSinceStartup - _lastRepaintTime > 0.05f)
        {
            _animTimer += 0.02f;
            Repaint();
            _lastRepaintTime = Time.realtimeSinceStartup;
        }
    }
    #endregion

    #region 主 GUI
    private void OnGUI()
    {
        EnsureStyles();

        // 绘制整体背景
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ClrBg);

        // ── 快捷键录制处理（优先于所有其他输入）────────
        if (_isRecordingShortcut)
        {
            HandleShortcutRecording();
        }

        // ── 快捷键导航（非录制模式下检测已注册的快捷键）──
        if (!_isRecordingShortcut)
        {
            HandleShortcutNavigation();
        }

        EditorGUILayout.BeginHorizontal();

        DrawLeftPanel();
        DrawSplitter();
        DrawRightPanel();

        EditorGUILayout.EndHorizontal();
    }
    #endregion

    #region 使用频率与隐藏项管理
    private void RecordToolUsage(ToolEntry tool)
    {
        if (tool == null || string.IsNullOrEmpty(tool.typeName)) return;
        _usageStats.IncrementTool(tool.typeName);
        _usageStats.IncrementCategory(tool.category);
        SaveUsageStats();
    }

    private void LoadUsageStats()
    {
        var json = EditorPrefs.GetString(UsageStatsPrefsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { _usageStats = JsonUtility.FromJson<UsageStats>(json) ?? new UsageStats(); }
            catch { _usageStats = new UsageStats(); }
        }
    }

    private void SaveUsageStats()
    {
        EditorPrefs.SetString(UsageStatsPrefsKey, JsonUtility.ToJson(_usageStats));
    }

    private void LoadHiddenItems()
    {
        var json = EditorPrefs.GetString(HiddenItemsPrefsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { _hiddenItems = JsonUtility.FromJson<HiddenItems>(json) ?? new HiddenItems(); }
            catch { _hiddenItems = new HiddenItems(); }
        }
    }

    private void SaveHiddenItems()
    {
        EditorPrefs.SetString(HiddenItemsPrefsKey, JsonUtility.ToJson(_hiddenItems));
    }

    private void ToggleToolHidden(string typeName)
    {
        _hiddenItems.ToggleTool(typeName);
        SaveHiddenItems();
        if (_selectedTool != null && _selectedTool.typeName == typeName)
        {
            _selectedTool = null;
            _selectedCategory = null;
        }
    }

    private void ToggleCategoryHidden(string categoryName)
    {
        _hiddenItems.ToggleCategory(categoryName);
        SaveHiddenItems();
        if (_selectedCategory != null && _selectedCategory.name == categoryName)
        {
            _selectedTool = null;
            _selectedCategory = null;
        }
    }

    private void ResetAllUsageStats()
    {
        _usageStats = new UsageStats();
        SaveUsageStats();
        DiscoverTools();
    }

    private void UnhideAllItems()
    {
        _hiddenItems = new HiddenItems();
        SaveHiddenItems();
    }
    #endregion
}
#endif
