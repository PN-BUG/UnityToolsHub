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
///   Theme.cs           — 主题配色与图标
///   Styles.cs          — GUIStyle 缓存与初始化
///   ToolDiscovery.cs   — 工具自动发现与创建
///   ShortcutManager.cs — 快捷键管理逻辑
///   LeftPanel.cs       — 左侧面板绘制
///   RightPanel.cs      — 右侧面板绘制（欢迎/详情/创建/隐藏项）
///   DrawingUtils.cs    — 绘图工具方法
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
    // ── 设置面板状态 ──────────────────────────────────
    private SettingsTab _settingsTab = SettingsTab.HiddenItems;
    private Vector2 _settingsScroll;
    private string _updateCheckResult = "";
    private double _updateCheckTime;
    // ── 缓存索引（避免每帧 LINQ 遍历）───────────────
    private Dictionary<string, ToolEntry> _toolIndex = new Dictionary<string, ToolEntry>();
    private int _totalToolCount;
    private const string UsageStatsPrefsKey   = "UnityToolsHub.UsageStats";
    private const string HiddenItemsPrefsKey  = "UnityToolsHub.HiddenItems";

    // ── 搜索过滤缓存（避免每帧 LINQ + ToList 分配）──
    private string _lastSearchText = null;
    private int _lastSearchVersion = -1; // _categories 变化时递增
    private int _categoriesVersion;
    private readonly Dictionary<CategoryNode, List<ToolEntry>> _filteredToolsMapCache
        = new Dictionary<CategoryNode, List<ToolEntry>>();

    // ── 使用统计排序缓存（避免每帧 OrderByDescending + ToList 分配）──
    private int _usageStatsVersion = -1;
    private int _usageStatsSnapshotVersion; // UsageStats 变化时递增
    private readonly List<UsageEntry> _sortedCategoriesCache = new List<UsageEntry>();
    private readonly List<UsageEntry> _sortedToolsCache = new List<UsageEntry>();

    // ── 快捷键索引（避免每帧遍历所有工具查找快捷键）──
    private readonly Dictionary<ShortcutBinding, ToolEntry> _shortcutIndex
        = new Dictionary<ShortcutBinding, ToolEntry>();
    private int _shortcutIndexVersion = -1;
    #endregion

    #region 多显示器 P/Invoke
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_XVIRTUALSCREEN  = 76;
    private const int SM_CXVIRTUALSCREEN = 78;

    private static (int x, int width) GetVirtualScreenBounds()
    {
        return (GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_CXVIRTUALSCREEN));
    }
    #endregion

    #region DockArea / ContainerWindow 反射操作
    // 详细反射日志默认关闭，定义 UNITYTOOLS_HUB_VERBOSE 宏可启用
    [System.Diagnostics.Conditional("UNITYTOOLS_HUB_VERBOSE")]
    private static void VerboseLog(string msg) => Debug.Log($"[Hub] {msg}");

    private static object GetDockArea(EditorWindow wnd)
    {
        foreach (var fieldName in new[] { "m_Parent", "m_DockArea", "m_ParentWindow" })
        {
            var f = typeof(EditorWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null)
            {
                var val = f.GetValue(wnd);
                if (val != null)
                {
                    VerboseLog($"GetDockArea: field='{fieldName}', type='{val.GetType().FullName}'");
                    return val;
                }
            }
        }
        Debug.LogWarning("[Hub] GetDockArea: 所有字段都不存在或为 null");
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
                {
                    VerboseLog($"GetContainerWindow: '{dockType.Name}.{propName}' → '{val.GetType().FullName}'");
                    return val;
                }
            }
        }
        Debug.LogWarning($"[Hub] GetContainerWindow: {dockType.Name} 没有 window/containerWindow 属性");
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
            {
                var val = (Rect)posProp.GetValue(container);
                VerboseLog($"GetFloatingWindowPosition: ContainerWindow.position = {val}");
                return val;
            }
        }
        if (dockArea != null)
        {
            var posProp = dockArea.GetType().GetProperty("position",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (posProp != null && posProp.CanRead)
            {
                var val = (Rect)posProp.GetValue(dockArea);
                VerboseLog($"GetFloatingWindowPosition fallback: DockArea.position = {val}");
                return val;
            }
        }
        VerboseLog($"GetFloatingWindowPosition final fallback: wnd.position = {wnd.position}");
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
                VerboseLog($"SetFloatingWindowPosition: ContainerWindow.position = {pos}");
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
                VerboseLog($"SetFloatingWindowPosition fallback1: DockArea.position = {pos}");
                posProp.SetValue(dockArea, pos);
                wnd.Repaint();
                return;
            }
        }
        VerboseLog($"SetFloatingWindowPosition fallback2: wnd.position = {pos}");
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
            VerboseLog($"ShowWindow toggle: _isHidden={_isHidden}, existing.position={existing.position}");

            // 安全检查：如果 _isHidden 为 true 但保存的位置无效，直接显示窗口
            if (_isHidden && (_savedPosition.width <= 1 || _savedPosition.height <= 1))
            {
                Debug.LogWarning("[Hub] 检测到无效的隐藏状态，强制显示窗口");
                _isHidden = false;
            }

            if (_isHidden)
            {
                // 恢复：还原位置 + 原始尺寸 + minSize
                VerboseLog($"恢复位置到: {_savedPosition}, minSize={_savedMinSize}");
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
                VerboseLog($"保存位置: {_savedPosition}, minSize={_savedMinSize}");
                EditorPrefs.SetString(SavedPositionKey, JsonUtility.ToJson(_savedPosition));
                existing.minSize = new Vector2(1, 1);
                var (vsX, _) = GetVirtualScreenBounds();
                var hidePos = new Rect(
                    vsX - 200,
                    _savedPosition.y,
                    1,
                    1);
                VerboseLog($"隐藏到: {hidePos}, 虚拟屏幕左边界 vsX={vsX}");
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
        // 使使用统计排序缓存失效
        _usageStatsSnapshotVersion++;
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
        // 使使用统计排序缓存失效
        _usageStatsSnapshotVersion++;
    }

    private void UnhideAllItems()
    {
        _hiddenItems = new HiddenItems();
        SaveHiddenItems();
    }
    #endregion
}
#endif
