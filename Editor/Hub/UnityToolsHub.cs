#if UNITY_EDITOR
using System;
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

    // ── 添加工具（导入已有编辑器扩展）状态 ────────────────
    private bool _showAddToolPanel;
    private string _addToolScanDir = "Assets/Editor";
    private Vector2 _addToolScroll;
    private Vector2 _addToolFormScroll;
    private List<AddToolCandidate> _addToolCandidates = new List<AddToolCandidate>();
    private int _addToolSelectedIndex = -1;
    // 导入表单字段
    private string _addToolName = "";
    private string _addToolClassName = "";
    private string _addToolCategory = "编辑器工具";
    private string _addToolDescription = "";
    private string _addToolIcon = "⚙";
    private string _addToolTags = "";
    private string _addToolShortcut = "";
    private bool _addToolScanRecursive = true;
    private string _addToolScanError = "";
    private List<string> _pendingDropPaths = new List<string>();

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

    // ── 文件夹分类配置 ──────────────────────────────────
    private FolderConfig _folderConfig = new FolderConfig();
    private HashSet<string> _defaultCategoryNames = new HashSet<string>(); // 从 [ToolInfo] 发现的默认分类名
    private const string FolderConfigPrefsKey = "UnityToolsHub.FolderConfig";

    // ── 拖放状态 ────────────────────────────────────────
    private enum DragType { None, Tool, Category }
    private DragType _dragType = DragType.None;
    private string _dragToolTypeName;           // 正在拖动的工具 typeName
    private string _dragSourceCategory;         // 拖动工具的来源分类名
    private string _dragCategoryName;           // 正在拖动的分类名
    private int _dragCategorySourceIndex;       // 分类拖动时的原始索引
    private bool _isDragActive;                 // 是否有拖动操作进行中
    private string _hoveredCategoryName;        // 当前鼠标悬停的分类名（高亮拖放目标）
    private Vector2 _dragStartMousePos;         // 拖动起始鼠标位置
    private bool _dragPending;                  // 拖动待确认（鼠标移动超过阈值才真正开始）
    private const float DragThreshold = 5f;     // 拖动启动阈值（像素）
    private Rect _dragGhostRect;                // 拖动幽灵矩形的位置（用于绘制跟随鼠标的半透明预览）

    // ── 新建/重命名分类对话框 ──────────────────────────
    private bool _showNewCategoryDialog;
    private string _newCategoryNameInput = "";
    private string _newCategoryIconInput = "📁";
    private bool _showRenameCategoryDialog;
    private string _renameCategoryOldName = "";
    private string _renameCategoryNewName = "";
    private bool _showDeleteCategoryConfirm;
    private string _deleteCategoryTargetName = "";

    // ── 排序模式 ────────────────────────────────────────
    private enum SortMode { ByName, ByRecent, ByMostUsed }
    private SortMode _sortMode = SortMode.ByName;
    private const string SortModePrefsKey = "UnityToolsHub.SortMode";
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
        LoadFolderConfig();
        _sortMode = (SortMode)EditorPrefs.GetInt(SortModePrefsKey, 0);
        DiscoverTools();
        ApplyFolderConfig();
        ApplySorting();
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

        // ── 拖放处理（最简原生方案，无条件执行）────────
        HandleDragAndDrop();

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

        // ── 对话框（在 OnGUI 层级绘制，避免被 Horizontal 布局裁剪）──
        if (_showNewCategoryDialog) DrawNewCategoryDialog();
        if (_showRenameCategoryDialog) DrawRenameCategoryDialog();
        if (_showDeleteCategoryConfirm) DrawDeleteCategoryConfirm();

        // ── 绘制拖动幽灵矩形（最后绘制，覆盖在最上层）──
        DrawDragGhost();
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

    #region 文件夹分类持久化
    private void LoadFolderConfig()
    {
        var json = EditorPrefs.GetString(FolderConfigPrefsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { _folderConfig = JsonUtility.FromJson<FolderConfig>(json) ?? new FolderConfig(); }
            catch { _folderConfig = new FolderConfig(); }
        }
    }

    private void SaveFolderConfig()
    {
        EditorPrefs.SetString(FolderConfigPrefsKey, JsonUtility.ToJson(_folderConfig));
    }

    /// <summary>
    /// 将 FolderConfig 中的自定义分类顺序和工具分配应用到 _categories 列表。
    /// 在 DiscoverTools() 之后调用。
    /// </summary>
    private void ApplyFolderConfig()
    {
        if (_folderConfig.folders.Count == 0) return;

        // 收集当前所有分类名
        var existingCatNames = new HashSet<string>(_categories.Select(c => c.name));

        // 1. 为 FolderConfig 中有但 _categories 中没有的分类创建新节点（用户自定义但尚无工具）
        foreach (var fi in _folderConfig.folders)
        {
            if (!existingCatNames.Contains(fi.name))
            {
                _categoryColors.TryGetValue(fi.name, out var accent);
                if (accent == default) accent = Theme.DefaultPalette[_categories.Count % Theme.DefaultPalette.Length];
                _categories.Add(new CategoryNode { name = fi.name, icon = fi.icon ?? "📁", accent = accent });
            }
        }

        // 2. 应用工具分类重分配
        bool reordered = false;
        foreach (var assignment in _folderConfig.toolAssignments)
        {
            var targetCat = _categories.Find(c => c.name == assignment.folderName);
            if (targetCat == null) continue;

            // 在所有分类中查找并移除该工具
            ToolEntry tool = null;
            foreach (var cat in _categories)
            {
                tool = cat.tools.Find(t => t.typeName == assignment.toolTypeName);
                if (tool != null)
                {
                    cat.tools.Remove(tool);
                    break;
                }
            }
            if (tool != null)
            {
                targetCat.tools.Add(tool);
                tool.category = assignment.folderName;
            }
        }

        // 3. 按 FolderConfig 的顺序重排 _categories
        var catMap = new Dictionary<string, CategoryNode>();
        foreach (var c in _categories) catMap[c.name] = c;

        var reorderedList = new List<CategoryNode>();
        foreach (var fi in _folderConfig.folders)
        {
            if (catMap.TryGetValue(fi.name, out var node))
            {
                reorderedList.Add(node);
                catMap.Remove(fi.name);
            }
        }
        // 剩余未在配置中的分类追加到末尾
        foreach (var c in catMap.Values)
            reorderedList.Add(c);

        _categories.Clear();
        _categories.AddRange(reorderedList);

        // 4. 移除空分类（用户自定义的保留空的，自动发现的不保留）
        _categories.RemoveAll(c => c.tools.Count == 0 && !_folderConfig.folders.Any(f => f.name == c.name && f.isCustom));
    }

    /// <summary>根据当前排序模式对分类和工具进行排序</summary>
    private void ApplySorting()
    {
        switch (_sortMode)
        {
            case SortMode.ByRecent:
                // 分类按最近使用排序，工具按最近使用排序
                _categories.Sort((a, b) =>
                {
                    int ca = _usageStats.GetCategoryCount(a.name);
                    int cb = _usageStats.GetCategoryCount(b.name);
                    return cb.CompareTo(ca); // 使用次数多 = 最近用过
                });
                foreach (var cat in _categories)
                    cat.tools.Sort((a, b) => _usageStats.GetToolCount(b.typeName).CompareTo(_usageStats.GetToolCount(a.typeName)));
                break;

            case SortMode.ByMostUsed:
                // 与 ByRecent 相同的排序逻辑（使用次数即频率）
                _categories.Sort((a, b) =>
                {
                    int ca = _usageStats.GetCategoryCount(a.name);
                    int cb = _usageStats.GetCategoryCount(b.name);
                    return cb.CompareTo(ca);
                });
                foreach (var cat in _categories)
                    cat.tools.Sort((a, b) => _usageStats.GetToolCount(b.typeName).CompareTo(_usageStats.GetToolCount(a.typeName)));
                break;

            case SortMode.ByName:
            default:
                _categories.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
                foreach (var cat in _categories)
                    cat.tools.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
                break;
        }
    }

    /// <summary>切换排序模式并重新应用</summary>
    private void SetSortMode(SortMode mode)
    {
        if (_sortMode == mode) return;
        _sortMode = mode;
        EditorPrefs.SetInt(SortModePrefsKey, (int)mode);
        ApplySorting();
    }

    /// <summary>全部折叠</summary>
    private void CollapseAllCategories()
    {
        foreach (var cat in _categories) cat.expanded = false;
    }

    /// <summary>全部展开</summary>
    private void ExpandAllCategories()
    {
        foreach (var cat in _categories) cat.expanded = true;
    }

    /// <summary>将当前 _categories 顺序同步到 FolderConfig 并持久化</summary>
    private void SyncCategoriesToFolderConfig()
    {
        // 保留旧的 isCustom 标记
        var oldCustomMap = new Dictionary<string, bool>();
        foreach (var fi in _folderConfig.folders)
            oldCustomMap[fi.name] = fi.isCustom;

        _folderConfig.folders.Clear();
        for (int i = 0; i < _categories.Count; i++)
        {
            var c = _categories[i];
            // 默认分类来自 [ToolInfo]，其余为用户自定义
            bool isDefault = _defaultCategoryNames.Contains(c.name);
            bool isCustom;
            if (oldCustomMap.TryGetValue(c.name, out var wasCustom))
                isCustom = wasCustom; // 保留已有标记
            else
                isCustom = !isDefault; // 新出现的分类默认为自定义

            _folderConfig.folders.Add(new FolderItem
            {
                name = c.name,
                icon = c.icon,
                order = i,
                isCustom = isCustom
            });
        }
    }

    /// <summary>判断分类是否为默认分类（来自 [ToolInfo]），默认分类不可删除</summary>
    private bool IsDefaultCategory(string name)
    {
        return _defaultCategoryNames.Contains(name);
    }

    /// <summary>移动工具到指定分类（拖放调用）</summary>
    private void MoveToolToCategory(string toolTypeName, string targetCategoryName)
    {
        if (string.IsNullOrEmpty(toolTypeName) || string.IsNullOrEmpty(targetCategoryName)) return;

        ToolEntry tool = null;
        CategoryNode sourceCat = null;
        foreach (var cat in _categories)
        {
            tool = cat.tools.Find(t => t.typeName == toolTypeName);
            if (tool != null) { sourceCat = cat; break; }
        }
        if (tool == null || sourceCat == null) return;

        var targetCat = _categories.Find(c => c.name == targetCategoryName);
        if (targetCat == null || targetCat == sourceCat) return;

        sourceCat.tools.Remove(tool);
        targetCat.tools.Add(tool);
        tool.category = targetCategoryName;

        _folderConfig.SetToolFolder(toolTypeName, targetCategoryName);
        SaveFolderConfig();

        // 重建索引
        RebuildToolIndex();
    }

    /// <summary>将工具还原到 [ToolInfo] 原始分类</summary>
    private void RestoreToolToDefaultCategory(string toolTypeName)
    {
        if (string.IsNullOrEmpty(toolTypeName)) return;

        ToolEntry tool = null;
        CategoryNode sourceCat = null;
        foreach (var cat in _categories)
        {
            tool = cat.tools.Find(t => t.typeName == toolTypeName);
            if (tool != null) { sourceCat = cat; break; }
        }
        if (tool == null) return;

        string defaultCat = tool.originalCategory;
        if (string.IsNullOrEmpty(defaultCat)) return;

        // 如果已在原始分类，无需操作
        if (sourceCat != null && sourceCat.name == defaultCat) return;

        // 确保目标分类存在
        var targetCat = _categories.Find(c => c.name == defaultCat);
        if (targetCat == null)
        {
            _categoryColors.TryGetValue(defaultCat, out var accent);
            if (accent == default) accent = Theme.DefaultPalette[_categories.Count % Theme.DefaultPalette.Length];
            var catIcon = GetCategoryIcon(defaultCat);
            targetCat = new CategoryNode { name = defaultCat, icon = catIcon, accent = accent };
            _categories.Add(targetCat);
        }

        // 从源分类移除
        if (sourceCat != null) sourceCat.tools.Remove(tool);

        targetCat.tools.Add(tool);
        tool.category = defaultCat;

        // 清除自定义分配
        _folderConfig.SetToolFolder(toolTypeName, null);
        SyncCategoriesToFolderConfig();
        SaveFolderConfig();

        RebuildToolIndex();
    }

    /// <summary>重排分类顺序</summary>
    private void ReorderCategory(string categoryName, int newIndex)
    {
        var cat = _categories.Find(c => c.name == categoryName);
        if (cat == null) return;
        _categories.Remove(cat);
        newIndex = Mathf.Clamp(newIndex, 0, _categories.Count);
        _categories.Insert(newIndex, cat);
        SyncCategoriesToFolderConfig();
        SaveFolderConfig();
    }

    /// <summary>创建新分类</summary>
    private void CreateNewCategory(string name, string icon)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (_categories.Any(c => c.name == name)) return;

        _categoryColors.TryGetValue(name, out var accent);
        if (accent == default) accent = Theme.DefaultPalette[_categories.Count % Theme.DefaultPalette.Length];

        var cat = new CategoryNode { name = name, icon = icon ?? "📁", accent = accent };
        _categories.Add(cat);

        _folderConfig.GetOrCreateFolder(name).isCustom = true;
        _folderConfig.GetOrCreateFolder(name).icon = icon ?? "📁";
        SyncCategoriesToFolderConfig();
        SaveFolderConfig();
    }

    /// <summary>重命名分类</summary>
    private void RenameCategory(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;
        if (_categories.Any(c => c.name == newName)) return;

        var cat = _categories.Find(c => c.name == oldName);
        if (cat == null) return;

        // 更新颜色字典
        if (_categoryColors.TryGetValue(oldName, out var clr))
        {
            _categoryColors[newName] = clr;
            _categoryColors.Remove(oldName);
        }

        cat.name = newName;
        foreach (var tool in cat.tools) tool.category = newName;

        _folderConfig.RenameFolder(oldName, newName);
        SyncCategoriesToFolderConfig();
        SaveFolderConfig();
    }

    /// <summary>删除分类（仅自定义分类可删除，将工具移到第一个分类）</summary>
    private void DeleteCategory(string name)
    {
        if (IsDefaultCategory(name)) return; // 默认分类不可删除
        var cat = _categories.Find(c => c.name == name);
        if (cat == null) return;

        // 将工具移到第一个非目标分类
        var fallbackCat = _categories.FirstOrDefault(c => c.name != name);
        if (fallbackCat != null)
        {
            foreach (var tool in cat.tools)
            {
                fallbackCat.tools.Add(tool);
                tool.category = fallbackCat.name;
                _folderConfig.SetToolFolder(tool.typeName, fallbackCat.name);
            }
        }

        _categories.Remove(cat);
        _folderConfig.RemoveFolder(name);
        SyncCategoriesToFolderConfig();
        SaveFolderConfig();
    }

    private void RebuildToolIndex()
    {
        _toolIndex.Clear();
        _shortcutIndex.Clear();
        _totalToolCount = 0;
        foreach (var cat in _categories)
        {
            foreach (var tool in cat.tools)
            {
                if (!string.IsNullOrEmpty(tool.typeName))
                    _toolIndex[tool.typeName] = tool;
                _totalToolCount++;
                var sc = GetEffectiveShortcut(tool.typeName);
                if (sc.IsValid)
                    _shortcutIndex[sc] = tool;
            }
        }
    }
    #endregion
}
#endif
