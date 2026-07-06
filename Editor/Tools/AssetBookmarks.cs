#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///  收藏夹工具
/// ═══════════════════════════════════════════════════════════════
///  
///  功能：
///  - 拖拽任意资源或场景对象到收藏夹面板
///  - 右键资源选择「添加到收藏夹」
///  - 右键 Hierarchy 对象选择「添加到收藏夹」
///  - 双击收藏项快速打开资源 / 定位场景对象
///  - Ctrl+点击 定位到 Project 窗口或场景视图
///  - 支持分组管理、搜索过滤、按使用频率排序
///  - 收藏内容持久化保存
///  
/// ═══════════════════════════════════════════════════════════════
/// </summary>
[ToolInfo("收藏夹", "资产工具",
    Description = "收藏常用资源和场景对象，快速访问。\n\n• 拖拽资源/场景对象到面板添加\n• 右键资源选择「添加到收藏夹」\n• 右键 Hierarchy 对象选择「添加到收藏夹」\n• 双击收藏项打开或定位\n• Ctrl+点击定位到 Project / 场景\n• 支持分组和搜索",
    Icon = "⭐",
    Tags = new[] { "收藏", "书签", "bookmark", "favorite", "快速访问", "场景对象" },
    Shortcut = "Ctrl+Shift+V",
    Priority = 5)]
public class AssetBookmarks : EditorWindow
{
    #region 数据结构
    /// <summary>
    /// 自动分组模式
    /// </summary>
    private enum AutoGroupMode
    {
        None,           // 手动分组
        ByTypeCategory, // 按资源类型分类（贴图、音频、脚本等）
        ByType,         // 按具体类型名（Texture2D、AudioClip 等）
        ByDirectory     // 按所在目录
    }

    /// <summary>
    /// 排序模式
    /// </summary>
    private enum SortMode
    {
        Default,     // 默认（按添加时间）
        MostUsed,    // 按使用次数（最多优先）
        RecentlyUsed // 最近使用
    }

    [Serializable]
    private class BookmarkItem
    {
        public string guid;
        public string path;
        public string name;
        public string type;      // 资源类型名称
        public string group;     // 所属分组
        public long addedTicks;  // 添加时间
        public int useCount;     // 使用次数
        public long lastUseTicks;// 最后使用时间
        public bool isSceneObject;  // 是否是场景中的对象
        public string scenePath;    // 场景路径（用于定位场景对象）
        public string globalObjectId; // 场景对象的持久化 GlobalObjectId
    }

    [Serializable]
    private class BookmarkData
    {
        public List<BookmarkItem> items = new List<BookmarkItem>();
        public List<string> groups = new List<string> { "默认" };
        public int autoGroupMode = 0; // AutoGroupMode 枚举值
        public int sortMode = 0;      // SortMode 枚举值
    }
    #endregion

    #region 常量
    private const string PREFS_KEY = "AssetBookmarks_Data";
    private const float ItemHeight = 40f;
    private const float GroupHeaderHeight = 26f;
    private const float ToolbarHeight = 36f;
    private const float SearchBarHeight = 28f;
    private const float StatusBarHeight = 22f;
    private const float GroupTabHeight = 28f;
    #endregion

    #region 调色板（与 UnityToolsHub 统一）
    private static readonly Color ClrBg           = new Color(0.16f, 0.16f, 0.17f, 1f);
    private static readonly Color ClrToolbarBg    = new Color(0.14f, 0.14f, 0.15f, 1f);
    private static readonly Color ClrSearchBg     = new Color(0.12f, 0.12f, 0.13f, 1f);
    private static readonly Color ClrItemBg       = new Color(0.19f, 0.19f, 0.20f, 1f);
    private static readonly Color ClrItemHover    = new Color(0.24f, 0.24f, 0.26f, 1f);
    private static readonly Color ClrItemSelected = new Color(0.22f, 0.45f, 0.85f, 0.30f);
    private static readonly Color ClrGroupBg      = new Color(0.15f, 0.15f, 0.16f, 1f);
    private static readonly Color ClrGroupActive  = new Color(0.22f, 0.45f, 0.85f, 0.45f);
    private static readonly Color ClrGroupNormal  = new Color(0.20f, 0.20f, 0.21f, 1f);
    private static readonly Color ClrText         = new Color(0.88f, 0.88f, 0.88f, 1f);
    private static readonly Color ClrTextDim      = new Color(0.50f, 0.50f, 0.52f, 1f);
    private static readonly Color ClrTextBright   = new Color(0.95f, 0.95f, 0.95f, 1f);
    private static readonly Color ClrAccent       = new Color(0.30f, 0.55f, 0.95f, 1f);
    private static readonly Color ClrAccentDim    = new Color(0.22f, 0.45f, 0.85f, 0.5f);
    private static readonly Color ClrStar         = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color ClrStarDim      = new Color(0.60f, 0.55f, 0.35f, 1f);
    private static readonly Color ClrDivider      = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color ClrDropOverlay  = new Color(0.30f, 0.55f, 0.95f, 0.18f);
    private static readonly Color ClrDropBorder   = new Color(0.30f, 0.55f, 0.95f, 0.60f);
    private static readonly Color ClrTagBg        = new Color(0.25f, 0.25f, 0.27f, 1f);
    private static readonly Color ClrStatusBar    = new Color(0.13f, 0.13f, 0.14f, 1f);
    private static readonly Color ClrCardBg       = new Color(0.21f, 0.21f, 0.22f, 1f);
    private static readonly Color ClrBtnNormal    = new Color(0.24f, 0.48f, 0.88f, 1f);
    private static readonly Color ClrBtnHover     = new Color(0.30f, 0.55f, 0.95f, 1f);
    private static readonly Color ClrBtnDanger    = new Color(0.75f, 0.28f, 0.28f, 1f);
    private static readonly Color ClrBtnDangerHov = new Color(0.85f, 0.35f, 0.35f, 1f);
    private static readonly Color ClrIconBg       = new Color(0.28f, 0.28f, 0.30f, 1f);
    // 分类配色
    private static readonly Color ClrCatDefault   = new Color(0.30f, 0.65f, 0.80f, 1f);
    private static readonly Color ClrCatAudio     = new Color(0.85f, 0.55f, 0.40f, 1f);
    private static readonly Color ClrCatScene     = new Color(0.55f, 0.75f, 0.45f, 1f);
    private static readonly Color ClrCatMaterial  = new Color(0.90f, 0.65f, 0.25f, 1f);
    private static readonly Color ClrCatScript    = new Color(0.75f, 0.50f, 0.70f, 1f);
    private static readonly Color ClrCatPrefab    = new Color(0.40f, 0.75f, 0.85f, 1f);
    #endregion

    #region 纹理缓存
    private Texture2D _texWhite;
    private Texture2D _texHover;
    private Texture2D _texSelected;
    private Texture2D _texTransparent;
    #endregion

    #region 样式缓存
    private GUIStyle _styleItemLabel;
    private GUIStyle _styleItemLabelSelected;
    private GUIStyle _styleGroupHeader;
    private GUIStyle _styleGroupTab;
    private GUIStyle _styleGroupTabActive;
    private GUIStyle _styleSearchField;
    private GUIStyle _styleSearchPlaceholder;
    private GUIStyle _styleIcon;
    private GUIStyle _styleLabel;
    private GUIStyle _styleLabelDim;
    private GUIStyle _styleLabelSmall;
    private GUIStyle _styleCenterLabel;
    private GUIStyle _styleCenterLabelLarge;
    private GUIStyle _styleStatusBar;
    private GUIStyle _styleTag;
    private GUIStyle _styleBtnPrimary;
    private GUIStyle _styleBtnFlat;
    private GUIStyle _styleStarBtn;
    private GUIStyle _styleTypeTag;
    private bool _stylesInitialized;
    #endregion

    #region 状态
    private static AssetBookmarks _instance;
    private BookmarkData _data = new BookmarkData();
    private string _searchFilter = "";
    private string _selectedGroup = "全部";
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private Vector2 _scrollPos;
    private bool _isDragging;
    private double _lastClickTime;
    private const double DoubleClickTime = 0.3;
    private bool _showGroupDropdown;
    private int _editingGroupIndex = -1;
    private string _editingGroupName = "";
    private AutoGroupMode _autoGroupMode = AutoGroupMode.None;
    private SortMode _sortMode = SortMode.Default;
    #endregion

    #region 菜单入口
    // 快捷键: Ctrl+Shift+V (Toggle)
    [MenuItem("UnityToolsHub/收藏夹 %#v", false, 100)]
    [MenuItem("Window/UnityFramework/收藏夹", false, 100)]
    public static void Toggle()
    {
        if (_instance != null)
        {
            _instance.Close();
        }
        else
        {
            Open();
        }
    }

    public static void Open()
    {
        var window = GetWindow<AssetBookmarks>("⭐ 收藏夹");
        window.minSize = new Vector2(280, 350);
        window.Show();
    }
    #endregion

    #region 右键菜单 - 添加到收藏夹
    [MenuItem("Assets/添加到收藏夹", false, 1000)]
    private static void AddSelectedToFavorites()
    {
        var guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0) return;

        var window = GetWindow<AssetBookmarks>(false, null, false);
        if (window == null)
        {
            window = CreateInstance<AssetBookmarks>();
        }

        int addedCount = 0;
        foreach (var guid in guids)
        {
            if (window.AddItemByGuid(guid))
            {
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            window.Save();
            window.Repaint();
            Debug.Log($"[收藏夹] 已添加 {addedCount} 个资源到收藏夹");
        }
        else
        {
            Debug.Log("[收藏夹] 选中的资源已在收藏夹中");
        }
    }

    [MenuItem("Assets/添加到收藏夹", true)]
    private static bool AddSelectedToFavoritesValidate()
    {
        return Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0;
    }

    // Hierarchy 右键菜单 - 添加场景对象到收藏夹
    [MenuItem("GameObject/添加到收藏夹", false, 1000)]
    private static void AddSelectedGameObjectToFavorites()
    {
        var selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0) return;

        var window = GetWindow<AssetBookmarks>(false, null, false);
        if (window == null)
        {
            window = CreateInstance<AssetBookmarks>();
        }

        int addedCount = 0;
        foreach (var go in selectedObjects)
        {
            if (window.AddGameObjectToBookmarks(go))
            {
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            window.Save();
            window.Repaint();
            Debug.Log($"[收藏夹] 已添加 {addedCount} 个 GameObject 到收藏夹");
        }
        else
        {
            Debug.Log("[收藏夹] 选中的对象已在收藏夹中");
        }
    }

    [MenuItem("GameObject/添加到收藏夹", true)]
    private static bool AddSelectedGameObjectToFavoritesValidate()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }
    #endregion

    #region 生命周期
    private void OnEnable()
    {
        _instance = this;
        Load();
        Undo.undoRedoPerformed += Repaint;
    }

    private void OnDisable()
    {
        _instance = null;
        Save();
        Undo.undoRedoPerformed -= Repaint;
    }

    private void OnGUI()
    {
        InitStyles();

        // 整体背景
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ClrBg);

        float y = 0;

        // 工具栏
        var toolbarRect = new Rect(0, y, position.width, ToolbarHeight);
        DrawToolbar(toolbarRect);
        y += ToolbarHeight;

        // 搜索栏
        var searchRect = new Rect(0, y, position.width, SearchBarHeight);
        DrawSearchBar(searchRect);
        y += SearchBarHeight;

        // 分组标签栏
        var groupRect = new Rect(0, y, position.width, GroupTabHeight);
        DrawGroupTabs(groupRect);
        y += GroupTabHeight;

        // 主内容区
        float statusBarH = StatusBarHeight;
        var contentRect = new Rect(0, y, position.width, position.height - y - statusBarH);

        // 内容
        if (_data.items.Count == 0 && string.IsNullOrEmpty(_searchFilter))
        {
            DrawEmptyState(contentRect);
        }
        else
        {
            DrawBookmarkList(contentRect);
        }

        // 拖拽处理
        HandleDragAndDrop(contentRect);

        // 右键菜单
        HandleContextMenu();

        // 底部状态栏
        var statusRect = new Rect(0, position.height - statusBarH, position.width, statusBarH);
        DrawStatusBar(statusRect);
    }
    #endregion

    #region 纹理 & 样式初始化
    private Texture2D MakeTex(int w, int h, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        tex.SetPixels(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    private void InitStyles()
    {
        if (_stylesInitialized) return;

        // 纹理
        _texWhite       = MakeTex(1, 1, Color.white);
        _texHover        = MakeTex(1, 1, ClrItemHover);
        _texSelected     = MakeTex(1, 1, ClrItemSelected);
        _texTransparent  = MakeTex(1, 1, new Color(0, 0, 0, 0));

        // 搜索框
        _styleSearchField = new GUIStyle("ToolbarSeachTextField")
        {
            fontSize = 12,
            padding = new RectOffset(28, 8, 4, 4),
            normal = { textColor = ClrText },
            fixedHeight = 22
        };

        _styleSearchPlaceholder = new GUIStyle()
        {
            fontSize = 12,
            normal = { textColor = ClrTextDim },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(28, 8, 4, 4)
        };

        // 分组标签
        _styleGroupTab = new GUIStyle()
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim, background = _texTransparent },
            hover = { textColor = ClrText, background = _texHover },
            padding = new RectOffset(10, 10, 4, 4),
            margin = new RectOffset(0, 0, 0, 0)
        };

        _styleGroupTabActive = new GUIStyle(_styleGroupTab)
        {
            normal = { textColor = ClrTextBright, background = MakeTex(1, 1, ClrGroupActive) }
        };

        // 分组标题
        _styleGroupHeader = new GUIStyle()
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(14, 8, 4, 4)
        };

        // 图标
        _styleIcon = new GUIStyle()
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16
        };

        // 文字标签
        _styleLabel = new GUIStyle()
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextBright },
            padding = new RectOffset(0, 0, 0, 0)
        };

        _styleLabelDim = new GUIStyle()
        {
            fontSize = 10,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(0, 0, 0, 0)
        };

        _styleLabelSmall = new GUIStyle()
        {
            fontSize = 9,
            normal = { textColor = ClrTextDim },
            alignment = TextAnchor.MiddleRight,
            padding = new RectOffset(0, 0, 0, 0)
        };

        // 居中标签
        _styleCenterLabel = new GUIStyle()
        {
            normal = { textColor = ClrTextDim },
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = 12
        };

        _styleCenterLabelLarge = new GUIStyle()
        {
            normal = { textColor = new Color(0.40f, 0.40f, 0.42f, 1f) },
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        // 状态栏
        _styleStatusBar = new GUIStyle()
        {
            fontSize = 10,
            normal = { textColor = ClrTextDim },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 0, 0)
        };

        // 标签
        _styleTag = new GUIStyle()
        {
            fontSize = 9,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrText },
            padding = new RectOffset(6, 6, 2, 2)
        };

        // 按钮
        _styleBtnPrimary = new GUIStyle()
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(1, 1, ClrBtnNormal) },
            hover = { textColor = Color.white, background = MakeTex(1, 1, ClrBtnHover) },
            active = { textColor = new Color(0.85f, 0.85f, 0.85f), background = MakeTex(1, 1, ClrAccent) },
            padding = new RectOffset(12, 12, 4, 4)
        };

        _styleBtnFlat = new GUIStyle()
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim },
            hover = { textColor = ClrText },
            padding = new RectOffset(8, 8, 4, 4)
        };

        // 星标按钮
        _styleStarBtn = new GUIStyle()
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            normal = { textColor = ClrStarDim, background = _texTransparent },
            hover = { textColor = ClrStar, background = _texTransparent },
            active = { textColor = new Color(1f, 0.9f, 0.5f), background = _texTransparent },
            padding = new RectOffset(0, 0, 0, 0)
        };

        // 类型标签
        _styleTypeTag = new GUIStyle()
        {
            fontSize = 9,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(5, 5, 1, 1)
        };

        // 工具列表项
        _styleItemLabel = new GUIStyle()
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = ClrText, background = _texTransparent },
            hover = { textColor = ClrTextBright, background = _texHover },
            padding = new RectOffset(0, 0, 0, 0)
        };

        _styleItemLabelSelected = new GUIStyle(_styleItemLabel)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextBright, background = _texSelected },
            hover = { textColor = Color.white, background = _texSelected }
        };

        _stylesInitialized = true;
    }
    #endregion

    #region 绘制 - 工具栏
    private void DrawToolbar(Rect rect)
    {
        // 背景
        EditorGUI.DrawRect(rect, ClrToolbarBg);
        // 底部分割线
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), ClrDivider);

        // 标题区
        var titleRect = new Rect(rect.x + 12, rect.y, 120, rect.height);
        GUI.Label(titleRect, "⭐  收藏夹", new GUIStyle()
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextBright },
            alignment = TextAnchor.MiddleLeft,
            richText = true
        });

        // 统计
        var countRect = new Rect(rect.x + 110, rect.y, 60, rect.height);
        GUI.Label(countRect, $"{_data.items.Count} 项", new GUIStyle()
        {
            fontSize = 10,
            normal = { textColor = ClrTextDim },
            alignment = TextAnchor.MiddleLeft
        });

        // 右侧按钮
        float btnX = rect.xMax - 10;

        // 排序按钮
        var sortLabel = GetSortModeLabel(_sortMode);
        var sortContent = new GUIContent($"↕ {sortLabel}");
        var sortSize = _styleBtnFlat.CalcSize(sortContent);
        btnX -= sortSize.x + 8;
        var sortRect = new Rect(btnX, rect.y + 6, sortSize.x + 8, rect.height - 12);
        bool sortHover = sortRect.Contains(Event.current.mousePosition);
        if (sortHover) EditorGUI.DrawRect(sortRect, new Color(0.30f, 0.55f, 0.95f, 0.15f));
        if (_sortMode != SortMode.Default)
        {
            EditorGUI.DrawRect(sortRect, new Color(0.22f, 0.45f, 0.85f, 0.12f));
        }
        if (GUI.Button(sortRect, sortContent, _styleBtnFlat))
        {
            ShowSortModeMenu();
        }

        // 自动分组 toggle 按钮
        bool autoGroupOn = _autoGroupMode != AutoGroupMode.None;
        var autoGroupContent = new GUIContent(autoGroupOn ? "🔄 自动分组 ON" : "🔄 自动分组");
        var autoGroupSize = _styleBtnFlat.CalcSize(autoGroupContent);
        btnX -= autoGroupSize.x + 8;
        var autoGroupRect = new Rect(btnX, rect.y + 6, autoGroupSize.x + 8, rect.height - 12);
        bool agHover = autoGroupRect.Contains(Event.current.mousePosition);
        // 开启状态背景高亮
        if (autoGroupOn)
        {
            EditorGUI.DrawRect(autoGroupRect, new Color(0.22f, 0.65f, 0.35f, 0.30f));
        }
        else if (agHover)
        {
            EditorGUI.DrawRect(autoGroupRect, new Color(0.30f, 0.55f, 0.95f, 0.15f));
        }
        if (GUI.Button(autoGroupRect, autoGroupContent, _styleBtnFlat))
        {
            ToggleAutoGroup();
        }

        // 清空按钮
        var clearContent = new GUIContent("🗑 清空");
        var clearSize = _styleBtnFlat.CalcSize(clearContent);
        btnX -= clearSize.x + 8;
        var clearRect = new Rect(btnX, rect.y + 6, clearSize.x + 8, rect.height - 12);
        bool clearHover = clearRect.Contains(Event.current.mousePosition);
        if (clearHover) EditorGUI.DrawRect(clearRect, new Color(0.75f, 0.28f, 0.28f, 0.20f));
        if (GUI.Button(clearRect, clearContent, _styleBtnFlat))
        {
            if (EditorUtility.DisplayDialog("清空收藏夹",
                $"确定要清空「{_selectedGroup}」分组中的所有收藏吗？", "确定", "取消"))
            {
                ClearCurrentGroup();
            }
        }

        // 新建分组按钮
        var addContent = new GUIContent("＋ 分组");
        var addSize = _styleBtnFlat.CalcSize(addContent);
        btnX -= addSize.x + 8;
        var addRect = new Rect(btnX, rect.y + 6, addSize.x + 8, rect.height - 12);
        bool addHover = addRect.Contains(Event.current.mousePosition);
        if (addHover) EditorGUI.DrawRect(addRect, new Color(1f, 1f, 1f, 0.06f));
        if (GUI.Button(addRect, addContent, _styleBtnFlat))
        {
            CreateNewGroup();
        }
    }
    #endregion

    #region 绘制 - 搜索栏
    private void DrawSearchBar(Rect rect)
    {
        EditorGUI.DrawRect(rect, ClrSearchBg);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), ClrDivider);

        // 搜索图标
        var iconRect = new Rect(rect.x + 10, rect.y + 3, 20, 22);
        GUI.Label(iconRect, "🔍", new GUIStyle()
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim }
        });

        // 输入框
        var fieldRect = new Rect(rect.x + 4, rect.y + 3, rect.width - 28, 22);
        GUI.SetNextControlName("SearchField");
        _searchFilter = EditorGUI.TextField(fieldRect, _searchFilter, _styleSearchField);

        // Placeholder
        if (string.IsNullOrEmpty(_searchFilter) && GUI.GetNameOfFocusedControl() != "SearchField")
        {
            GUI.Label(fieldRect, "  搜索收藏…", _styleSearchPlaceholder);
        }

        // 清除按钮
        if (!string.IsNullOrEmpty(_searchFilter))
        {
            var cancelRect = new Rect(rect.xMax - 22, rect.y + 5, 18, 18);
            if (GUI.Button(cancelRect, "✕", new GUIStyle()
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ClrTextDim },
                hover = { textColor = ClrText }
            }))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
        }
    }
    #endregion

    #region 绘制 - 分组标签栏
    private void DrawGroupTabs(Rect rect)
    {
        EditorGUI.DrawRect(rect, ClrGroupBg);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), ClrDivider);

        GUILayout.BeginArea(rect);
        EditorGUILayout.BeginHorizontal();

        GUILayout.Space(6);

        // "全部" 标签
        bool allSelected = _selectedGroup == "全部";
        var allStyle = allSelected ? _styleGroupTabActive : _styleGroupTab;
        var allItems = _data.items.Count;
        if (GUILayout.Button($"全部 ({allItems})", allStyle, GUILayout.Height(GroupTabHeight - 4)))
        {
            _selectedGroup = "全部";
            _selectedIndex = -1;
        }

        GUILayout.Space(2);

        // 各分组标签
        foreach (var group in _data.groups)
        {
            bool isSelected = _selectedGroup == group;
            var style = isSelected ? _styleGroupTabActive : _styleGroupTab;
            int groupCount = _data.items.Count(i => i.group == group);

            if (GUILayout.Button($"{group} ({groupCount})", style, GUILayout.Height(GroupTabHeight - 4)))
            {
                if (_selectedGroup != group)
                {
                    _selectedGroup = group;
                    _selectedIndex = -1;
                }
            }

            GUILayout.Space(2);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
    #endregion

    #region 绘制 - 空状态
    private void DrawEmptyState(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.FlexibleSpace();

        // 大图标
        GUILayout.Label("⭐", new GUIStyle()
        {
            fontSize = 48,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.82f, 0.28f, 0.6f) }
        }, GUILayout.Height(60));

        GUILayout.Space(8);

        GUILayout.Label("收藏夹为空", _styleCenterLabelLarge);

        GUILayout.Space(6);

        GUILayout.Label("拖拽资源到此处，或右键资源\n选择「添加到收藏夹」", _styleCenterLabel);

        // 快捷键提示
        GUILayout.Space(16);
        GUILayout.Label("Ctrl + Shift + V  快速开关", new GUIStyle()
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.35f, 0.35f, 0.38f, 1f) }
        });

        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }
    #endregion

    #region 绘制 - 收藏列表
    private void DrawBookmarkList(Rect rect)
    {
        var filteredItems = GetFilteredItems();
        var groupedItems = filteredItems.GroupBy(i => i.group);

        // 分组排序
        IEnumerable<IGrouping<string, BookmarkItem>> groups;
        switch (_sortMode)
        {
            case SortMode.MostUsed:
                groups = groupedItems.OrderByDescending(g => g.Sum(i => i.useCount));
                break;
            case SortMode.RecentlyUsed:
                groups = groupedItems.OrderByDescending(g => g.Max(i => i.lastUseTicks));
                break;
            default:
                groups = groupedItems.OrderBy(g => g.Key);
                break;
        }

        // 计算总高度
        float totalHeight = 0;
        foreach (var group in groups)
        {
            totalHeight += GroupHeaderHeight;
            totalHeight += group.Count() * ItemHeight;
        }
        totalHeight += 8; // 底部间距

        // 无结果提示
        if (filteredItems.Count == 0)
        {
            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();
            GUILayout.Label("未找到匹配的收藏", _styleCenterLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginArea(rect);
        _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, true);

        int globalIndex = 0;
        foreach (var group in groups)
        {
            // ── 分组标题 ──
            var groupRect = GUILayoutUtility.GetRect(rect.width, GroupHeaderHeight);
            EditorGUI.DrawRect(groupRect, ClrGroupBg);
            // 左侧色条
            var groupColor = GetGroupColor(group.Key);
            EditorGUI.DrawRect(new Rect(groupRect.x, groupRect.y + 4, 3, groupRect.height - 8), groupColor);

            // 分组标题文字（含使用次数）
            int groupUseCount = group.Sum(i => i.useCount);
            string groupTitle = groupUseCount > 0
                ? $"{group.Key}  ·  {group.Count()}  ({groupUseCount}次)"
                : $"{group.Key}  ·  {group.Count()}";
            GUI.Label(new Rect(groupRect.x + 10, groupRect.y, groupRect.width - 10, groupRect.height),
                groupTitle, _styleGroupHeader);

            // ── 分组内项目排序 ──
            IEnumerable<BookmarkItem> sortedItems;
            switch (_sortMode)
            {
                case SortMode.MostUsed:
                    sortedItems = group.OrderByDescending(i => i.useCount);
                    break;
                case SortMode.RecentlyUsed:
                    sortedItems = group.OrderByDescending(i => i.lastUseTicks);
                    break;
                default:
                    sortedItems = group.OrderBy(i => i.addedTicks);
                    break;
            }

            foreach (var item in sortedItems)
            {
                var itemRect = GUILayoutUtility.GetRect(rect.width, ItemHeight);
                DrawItem(itemRect, item, globalIndex);
                globalIndex++;
            }
        }

        GUILayout.Space(8);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
    #endregion

    #region 绘制 - 单个收藏项
    private void DrawItem(Rect rect, BookmarkItem item, int index)
    {
        Event evt = Event.current;
        bool isHover = rect.Contains(evt.mousePosition);
        bool isSelected = index == _selectedIndex;

        // 跟踪 hover
        if (evt.type == EventType.Repaint || evt.type == EventType.MouseMove)
        {
            if (isHover) _hoverIndex = index;
        }

        // ── 背景 ──
        Color bgColor = ClrItemBg;
        if (isSelected) bgColor = ClrItemSelected;
        else if (isHover) bgColor = ClrItemHover;
        EditorGUI.DrawRect(rect, bgColor);

        // 选中态左侧色条
        if (isSelected)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 6, 3, rect.height - 12), ClrAccent);
        }

        // 底部分割线
        EditorGUI.DrawRect(new Rect(rect.x + 12, rect.yMax - 1, rect.width - 24, 1), ClrDivider);

        // ── 图标区 ──
        var iconBgRect = new Rect(rect.x + 10, rect.y + 8, 24, 24);
        EditorGUI.DrawRect(iconBgRect, ClrIconBg);
        // 场景对象使用特殊图标和颜色
        var iconColor = item.isSceneObject ? new Color(0.35f, 0.78f, 0.45f) : GetTypeColor(item.type);
        var iconText = item.isSceneObject ? "🎯" : GetAssetIcon(item.type);
        var prevColor = GUI.color;
        GUI.color = iconColor;
        GUI.Label(iconBgRect, iconText, new GUIStyle()
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            normal = { textColor = iconColor }
        });
        GUI.color = prevColor;

        // ── 名称 ──
        var nameRect = new Rect(rect.x + 42, rect.y + 6, rect.width - 130, 16);
        GUI.Label(nameRect, item.name, isSelected ? _styleItemLabelSelected : _styleLabel);

        // ── 使用次数（如果有）──
        if (item.useCount > 0)
        {
            var useCountContent = new GUIContent($"{item.useCount}次");
            var useCountSize = _styleLabelSmall.CalcSize(useCountContent);
            var useCountRect = new Rect(rect.x + 42 + _styleLabel.CalcSize(new GUIContent(item.name)).x + 6, rect.y + 8, useCountSize.x + 8, 14);
            if (useCountRect.xMax < rect.xMax - 70) // 确保不超出边界
            {
                EditorGUI.DrawRect(useCountRect, new Color(0.30f, 0.55f, 0.95f, 0.15f));
                GUI.Label(useCountRect, item.useCount.ToString(), new GUIStyle()
                {
                    fontSize = 9,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = ClrAccent },
                    fontStyle = FontStyle.Bold
                });
            }
        }

        // ── 类型标签 ──
        var typeTagText = item.isSceneObject ? "Scene" : item.type;
        var typeTagContent = new GUIContent(typeTagText);
        var typeTagSize = _styleTypeTag.CalcSize(typeTagContent);
        var typeTagRect = new Rect(rect.x + 42, rect.y + 22, typeTagSize.x + 8, 14);
        EditorGUI.DrawRect(typeTagRect, ClrTagBg);
        GUI.Label(typeTagRect, typeTagText, _styleTypeTag);

        // ── 路径提示（右侧淡色）──
        var pathShort = item.isSceneObject ? ShortenPath(item.path) : ShortenPath(item.path);
        var pathContent = new GUIContent(pathShort);
        var pathSize = _styleLabelSmall.CalcSize(pathContent);
        var pathRect = new Rect(rect.xMax - pathSize.x - 36, rect.y + 8, pathSize.x, 14);
        GUI.Label(pathRect, pathShort, _styleLabelSmall);

        // ── 星标按钮 ──
        var starRect = new Rect(rect.xMax - 28, rect.y + 10, 20, 20);
        bool starHover = starRect.Contains(evt.mousePosition);
        if (starHover)
        {
            EditorGUI.DrawRect(starRect, new Color(0.75f, 0.28f, 0.28f, 0.15f));
        }
        GUI.Label(starRect, starHover ? "✕" : "★", starHover ? new GUIStyle()
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrBtnDanger }
        } : _styleStarBtn);

        // ── 点击处理 ──
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            if (evt.button == 0)
            {
                // 检查是否点击了星标
                if (starRect.Contains(evt.mousePosition))
                {
                    RemoveItem(item);
                    evt.Use();
                    return;
                }

                // Ctrl + 点击：在 Project 窗口中定位
                if (evt.control || evt.command)
                {
                    PingAsset(item);
                    evt.Use();
                    return;
                }

                double time = EditorApplication.timeSinceStartup;
                if (time - _lastClickTime < DoubleClickTime && _selectedIndex == index)
                {
                    OpenBookmarkItem(item);
                    evt.Use();
                }
                else
                {
                    _selectedIndex = index;
                    _lastClickTime = time;
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.path);
                    Repaint();
                }
                evt.Use();
            }
            else if (evt.button == 1)
            {
                ShowItemContextMenu(item);
                evt.Use();
            }
        }
    }
    #endregion

    #region 绘制 - 状态栏
    private void DrawStatusBar(Rect rect)
    {
        EditorGUI.DrawRect(rect, ClrStatusBar);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), ClrDivider);

        var filtered = GetFilteredItems();
        string statusText;
        if (!string.IsNullOrEmpty(_searchFilter))
        {
            statusText = $"搜索结果: {filtered.Count} / {_data.items.Count} 项";
        }
        else if (_selectedGroup == "全部")
        {
            statusText = $"共 {_data.items.Count} 项  ·  {_data.groups.Count} 个分组";
        }
        else
        {
            int groupCount = _data.items.Count(i => i.group == _selectedGroup);
            statusText = $"「{_selectedGroup}」{groupCount} 项";
        }

        GUI.Label(rect, statusText, _styleStatusBar);
    }
    #endregion

    #region 拖拽处理
    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(evt.mousePosition))
                    return;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    int addedCount = 0;
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        // 尝试作为资源路径添加
                        string path = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(path))
                        {
                            string guid = AssetDatabase.AssetPathToGUID(path);
                            if (AddItemByGuid(guid))
                            {
                                addedCount++;
                            }
                        }
                        else if (obj is GameObject go && go.scene.IsValid())
                        {
                            // 场景中的 GameObject
                            if (AddGameObjectToBookmarks(go))
                            {
                                addedCount++;
                            }
                        }
                    }

                    if (addedCount > 0)
                    {
                        Save();
                        Debug.Log($"[收藏夹] 已添加 {addedCount} 个资源到收藏夹");
                    }

                    _isDragging = false;
                    evt.Use();
                }
                else
                {
                    _isDragging = true;
                }
                break;

            case EventType.DragExited:
                _isDragging = false;
                break;
        }

        // 拖拽覆盖层
        if (_isDragging && dropArea.Contains(evt.mousePosition))
        {
            // 半透明覆盖
            EditorGUI.DrawRect(dropArea, ClrDropOverlay);

            // 虚线边框效果（用4条细线模拟）
            float border = 2f;
            EditorGUI.DrawRect(new Rect(dropArea.x, dropArea.y, dropArea.width, border), ClrDropBorder);
            EditorGUI.DrawRect(new Rect(dropArea.x, dropArea.yMax - border, dropArea.width, border), ClrDropBorder);
            EditorGUI.DrawRect(new Rect(dropArea.x, dropArea.y, border, dropArea.height), ClrDropBorder);
            EditorGUI.DrawRect(new Rect(dropArea.xMax - border, dropArea.y, border, dropArea.height), ClrDropBorder);

            // 中心提示
            var centerRect = new Rect(dropArea.x, dropArea.y + dropArea.height / 2 - 30, dropArea.width, 60);
            EditorGUI.DrawRect(centerRect, new Color(0.15f, 0.15f, 0.16f, 0.85f));
            GUI.Label(centerRect, "⭐  释放添加到收藏夹", new GUIStyle()
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ClrAccent }
            });
        }
    }
    #endregion

    #region 右键菜单
    private void ShowItemContextMenu(BookmarkItem item)
    {
        var menu = new GenericMenu();

        if (item.isSceneObject)
        {
            // 场景对象的菜单
            menu.AddItem(new GUIContent("🎯 定位到场景"), false, () => PingAsset(item));
            menu.AddItem(new GUIContent("📋 复制路径"), false, () => CopyPath(item));
        }
        else
        {
            // 资源文件的菜单
            menu.AddItem(new GUIContent("📂 打开资源"), false, () => OpenBookmarkItem(item));
            menu.AddItem(new GUIContent("🔍 在 Project 中定位"), false, () => PingAsset(item));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("📋 复制路径"), false, () => CopyPath(item));
        }

        menu.AddSeparator("");

        // 移动到分组
        menu.AddItem(new GUIContent("📁 移动到分组/默认"), false, () => MoveToGroup(item, "默认"));
        foreach (var group in _data.groups)
        {
            if (group != item.group)
            {
                menu.AddItem(new GUIContent($"📁 移动到分组/{group}"), false, () => MoveToGroup(item, group));
            }
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("✕ 从收藏夹移除"), false, () => RemoveItem(item));

        menu.ShowAsContext();
    }

    private void HandleContextMenu()
    {
        // 空白区域右键（可扩展）
    }
    #endregion

    #region 数据操作
    private bool AddItemByGuid(string guid)
    {
        if (_data.items.Any(i => i.guid == guid))
            return false;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return false;

        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (obj == null) return false;

        // 根据自动分组模式决定分组
        string group;
        if (_autoGroupMode != AutoGroupMode.None)
        {
            group = ResolveAutoGroup(obj, path);
        }
        else
        {
            group = _selectedGroup == "全部" ? "默认" : _selectedGroup;
        }

        var item = new BookmarkItem
        {
            guid = guid,
            path = path,
            name = obj.name,
            type = obj.GetType().Name,
            group = group,
            addedTicks = DateTime.Now.Ticks,
            isSceneObject = false
        };

        _data.items.Add(item);

        if (!_data.groups.Contains(item.group))
        {
            _data.groups.Add(item.group);
        }

        return true;
    }

    /// <summary>
    /// 添加场景中的 GameObject 到收藏夹
    /// </summary>
    private bool AddGameObjectToBookmarks(GameObject go)
    {
        // 检查是否已存在（通过 GlobalObjectId）
        string newId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
        if (_data.items.Any(i => i.isSceneObject && i.globalObjectId == newId))
            return false;

        // 获取场景路径
        string scenePath = go.scene.IsValid() ? go.scene.path : "Untitled Scene";

        // 根据自动分组模式决定分组
        string group;
        if (_autoGroupMode != AutoGroupMode.None)
        {
            group = ResolveAutoGroup(go, "");
        }
        else
        {
            group = _selectedGroup == "全部" ? "默认" : _selectedGroup;
        }

        var item = new BookmarkItem
        {
            guid = "",  // 场景对象没有 guid
            path = GetGameObjectPath(go),  // 存储层级路径
            name = go.name,
            type = "GameObject",
            group = group,
            addedTicks = DateTime.Now.Ticks,
            isSceneObject = true,
            scenePath = scenePath,
            globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString()
        };

        _data.items.Add(item);

        if (!_data.groups.Contains(item.group))
        {
            _data.groups.Add(item.group);
        }

        return true;
    }

    /// <summary>
    /// 获取 GameObject 的层级路径
    /// </summary>
    private string GetGameObjectPath(GameObject go)
    {
        string path = go.name;
        Transform parent = go.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    private void RemoveItem(BookmarkItem item)
    {
        _data.items.Remove(item);
        if (_selectedIndex >= _data.items.Count)
        {
            _selectedIndex = _data.items.Count - 1;
        }
        Save();
        Repaint();
    }

    private void ClearCurrentGroup()
    {
        if (_selectedGroup == "全部")
        {
            _data.items.Clear();
        }
        else
        {
            _data.items.RemoveAll(i => i.group == _selectedGroup);
        }
        _selectedIndex = -1;
        Save();
        Repaint();
    }

    private void MoveToGroup(BookmarkItem item, string group)
    {
        item.group = group;
        if (!_data.groups.Contains(group))
        {
            _data.groups.Add(group);
        }
        Save();
        Repaint();
    }

    private void CreateNewGroup()
    {
        string groupName = "新分组";
        int counter = 1;
        while (_data.groups.Contains(groupName))
        {
            groupName = $"新分组 {counter}";
            counter++;
        }
        _data.groups.Add(groupName);
        _selectedGroup = groupName;
        Save();
        Repaint();
    }

    private List<BookmarkItem> GetFilteredItems()
    {
        var items = _data.items.AsEnumerable();

        if (_selectedGroup != "全部")
        {
            items = items.Where(i => i.group == _selectedGroup);
        }

        if (!string.IsNullOrEmpty(_searchFilter))
        {
            string filter = _searchFilter.ToLower();
            items = items.Where(i =>
                i.name.ToLower().Contains(filter) ||
                i.type.ToLower().Contains(filter) ||
                i.path.ToLower().Contains(filter));
        }

        return items.ToList();
    }
    #endregion

    #region 资源操作
    private void OpenBookmarkItem(BookmarkItem item)
    {
        if (item.isSceneObject)
        {
            // 场景对象：通过 GlobalObjectId 查找并选中（持久化 ID，场景重载后仍有效）
            var obj = ResolveSceneObject(item);
            if (obj != null)
            {
                item.useCount++;
                item.lastUseTicks = DateTime.Now.Ticks;
                Save();

                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            else
            {
                Debug.LogWarning($"[收藏夹] 场景对象已不存在: {item.name}");
            }
        }
        else
        {
            // 资源文件
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.path);
            if (obj != null)
            {
                // 增加使用次数
                item.useCount++;
                item.lastUseTicks = DateTime.Now.Ticks;
                Save();

                AssetDatabase.OpenAsset(obj);
                EditorGUIUtility.PingObject(obj);
            }
            else
            {
                Debug.LogWarning($"[收藏夹] 无法打开资源: {item.path}");
            }
        }
    }

    /// <summary>
    /// 解析场景对象：优先用 GlobalObjectId，失败时用层级路径回退查找
    /// </summary>
    private GameObject ResolveSceneObject(BookmarkItem item)
    {
        // 优先使用 GlobalObjectId（持久化 ID）
        if (!string.IsNullOrEmpty(item.globalObjectId))
        {
            if (GlobalObjectId.TryParse(item.globalObjectId, out var gid))
            {
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                if (obj != null)
                    return obj as GameObject;
            }
        }

        // 回退：通过场景路径 + 层级路径查找
        if (!string.IsNullOrEmpty(item.scenePath) && !string.IsNullOrEmpty(item.path))
        {
            // 在所有加载的场景中查找
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.path == item.scenePath)
                {
                    var rootObjects = scene.GetRootGameObjects();
                    foreach (var root in rootObjects)
                    {
                        if (root.name == item.path.Split('/')[0])
                        {
                            var found = root.transform.Find(string.Join("/", item.path.Split('/').Skip(1)));
                            if (found != null)
                                return found.gameObject;
                        }
                    }
                }
            }

            // 如果场景未加载，尝试在所有已加载场景中按名称查找
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var found = FindByNamePath(root.transform, item.path);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 按层级名称路径查找 GameObject
    /// </summary>
    private GameObject FindByNamePath(Transform root, string namePath)
    {
        string[] parts = namePath.Split('/');
        if (parts.Length == 0) return null;
        if (root.name != parts[0]) return null;

        if (parts.Length == 1) return root.gameObject;

        Transform current = root;
        for (int i = 1; i < parts.Length; i++)
        {
            var child = current.Find(parts[i]);
            if (child == null) return null;
            current = child;
        }
        return current.gameObject;
    }

    private void PingAsset(BookmarkItem item)
    {
        if (item.isSceneObject)
        {
            // 场景对象：定位到 Hierarchy
            var obj = ResolveSceneObject(item);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            else
            {
                Debug.LogWarning($"[收藏夹] 场景对象已不存在: {item.name}");
            }
        }
        else
        {
            // 资源文件：定位到 Project 窗口
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.path);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }

    private void CopyPath(BookmarkItem item)
    {
        if (item.isSceneObject)
        {
            EditorGUIUtility.systemCopyBuffer = $"{item.scenePath}/{item.name}";
            Debug.Log($"[收藏夹] 已复制路径: {item.scenePath}/{item.name}");
        }
        else
        {
            EditorGUIUtility.systemCopyBuffer = item.path;
            Debug.Log($"[收藏夹] 已复制路径: {item.path}");
        }
    }
    #endregion

    #region 工具方法
    private string GetAssetIcon(string typeName)
    {
        switch (typeName)
        {
            case "GameObject":          return "🎮";
            case "Scene":               return "🎬";
            case "Material":            return "🎨";
            case "Texture2D":
            case "Sprite":              return "🖼";
            case "AudioClip":           return "🔊";
            case "AnimationClip":
            case "AnimatorController":  return "🎞";
            case "ScriptableObject":    return "📋";
            case "MonoScript":          return "📜";
            case "Shader":
            case "ShaderGraph":         return "💎";
            case "Prefab":              return "📦";
            case "Font":                return "🔤";
            case "Mesh":                return "🔷";
            case "PhysicMaterial":
            case "PhysicsMaterial2D":   return "⚡";
            case "Canvas":
            case "RectTransform":       return "🖥";
            default:                    return "📄";
        }
    }

    private Color GetTypeColor(string typeName)
    {
        switch (typeName)
        {
            case "GameObject":          return ClrCatPrefab;
            case "Scene":               return ClrCatScene;
            case "Material":            return ClrCatMaterial;
            case "Texture2D":
            case "Sprite":              return ClrCatDefault;
            case "AudioClip":           return ClrCatAudio;
            case "AnimationClip":
            case "AnimatorController":  return ClrCatAudio;
            case "ScriptableObject":    return ClrCatScript;
            case "MonoScript":          return ClrCatScript;
            case "Shader":
            case "ShaderGraph":         return ClrCatMaterial;
            case "Prefab":              return ClrCatPrefab;
            default:                    return ClrTextDim;
        }
    }

    private Color GetGroupColor(string groupName)
    {
        int hash = groupName.GetHashCode();
        float h = (hash % 360) / 360f;
        if (h < 0) h += 1f;
        return Color.HSVToRGB(h, 0.45f, 0.70f);
    }

    private string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var parts = path.Split('/');
        if (parts.Length <= 2) return path;
        return $"…/{parts[parts.Length - 2]}/{parts[parts.Length - 1]}";
    }
    #endregion

    #region 自动分组
    /// <summary>
    /// 根据自动分组模式解析资源应归属的分组名
    /// </summary>
    private string ResolveAutoGroup(UnityEngine.Object obj, string path)
    {
        switch (_autoGroupMode)
        {
            case AutoGroupMode.ByTypeCategory:
                return GetTypeCategoryName(obj.GetType().Name);
            case AutoGroupMode.ByType:
                return obj.GetType().Name;
            case AutoGroupMode.ByDirectory:
                return GetDirectoryGroup(path);
            default:
                return "默认";
        }
    }

    /// <summary>
    /// 获取资源类型的语义分类名
    /// </summary>
    private string GetTypeCategoryName(string typeName)
    {
        switch (typeName)
        {
            case "Texture2D":
            case "Sprite":
            case "RenderTexture":
                return "🖼 贴图";
            case "AudioClip":
            case "AudioMixer":
                return "🔊 音频";
            case "AnimationClip":
            case "AnimatorController":
            case "AnimatorOverrideController":
            case "RuntimeAnimatorController":
                return "🎞 动画";
            case "Material":
            case "Shader":
            case "ShaderGraph":
            case "ShaderVariantCollection":
                return "🎨 材质 & Shader";
            case "MonoScript":
            case "ScriptableObject":
                return "📜 脚本";
            case "Prefab":
            case "GameObject":
                return "📦 预制体";
            case "Scene":
                return "🎬 场景";
            case "Font":
            case "TextAsset":
                return "🔤 文本";
            case "Mesh":
            case "SkinnedMeshRenderer":
                return "🔷 模型";
            case "PhysicMaterial":
            case "PhysicsMaterial2D":
                return "⚙ 物理";
            case "Canvas":
            case "RectTransform":
                return "🖥 UI";
            default:
                return "📄 其他";
        }
    }

    /// <summary>
    /// 获取资源所在目录作为分组名
    /// </summary>
    private string GetDirectoryGroup(string path)
    {
        if (string.IsNullOrEmpty(path)) return "根目录";
        var parts = path.Split('/');
        // Assets/X/Y/file.asset → 取前两级目录 Assets/X
        if (parts.Length >= 3)
            return $"{parts[0]}/{parts[1]}";
        if (parts.Length >= 2)
            return $"{parts[0]}";
        return "根目录";
    }

    /// <summary>
    /// 获取自动分组模式的显示标签
    /// </summary>
    private string GetAutoGroupLabel(AutoGroupMode mode)
    {
        switch (mode)
        {
            case AutoGroupMode.ByTypeCategory: return "按类型分类";
            case AutoGroupMode.ByType:         return "按具体类型";
            case AutoGroupMode.ByDirectory:    return "按目录";
            default:                           return "手动分组";
        }
    }

    /// <summary>
    /// 获取排序模式的显示标签
    /// </summary>
    private string GetSortModeLabel(SortMode mode)
    {
        switch (mode)
        {
            case SortMode.MostUsed:     return "最常用";
            case SortMode.RecentlyUsed: return "最近用";
            default:                    return "默认";
        }
    }

    /// <summary>
    /// 显示排序模式选择菜单
    /// </summary>
    private void ShowSortModeMenu()
    {
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("默认排序"), _sortMode == SortMode.Default,
            () => SetSortMode(SortMode.Default));
        menu.AddItem(new GUIContent("按使用次数"), _sortMode == SortMode.MostUsed,
            () => SetSortMode(SortMode.MostUsed));
        menu.AddItem(new GUIContent("最近使用"), _sortMode == SortMode.RecentlyUsed,
            () => SetSortMode(SortMode.RecentlyUsed));

        menu.ShowAsContext();
    }

    /// <summary>
    /// 切换排序模式
    /// </summary>
    private void SetSortMode(SortMode mode)
    {
        _sortMode = mode;
        _data.sortMode = (int)mode;
        Save();
        Repaint();
    }

    /// <summary>
    /// 切换自动分组开关
    /// </summary>
    private void ToggleAutoGroup()
    {
        if (_autoGroupMode == AutoGroupMode.None)
        {
            // 开启 - 使用默认的按类型分类
            SetAutoGroupMode(AutoGroupMode.ByTypeCategory);
        }
        else
        {
            // 关闭
            SetAutoGroupMode(AutoGroupMode.None);
        }
    }

    /// <summary>
    /// 切换自动分组模式
    /// </summary>
    private void SetAutoGroupMode(AutoGroupMode mode)
    {
        _autoGroupMode = mode;
        _data.autoGroupMode = (int)mode;
        Save();
        Repaint();
    }

    /// <summary>
    /// 重新整理所有收藏项到自动分组
    /// </summary>
    private void RegroupAllItems()
    {
        if (_autoGroupMode == AutoGroupMode.None)
        {
            EditorUtility.DisplayDialog("重新分组", "请先开启自动分组模式。", "确定");
            return;
        }

        if (!EditorUtility.DisplayDialog("重新整理收藏",
            $"将所有收藏按「{GetAutoGroupLabel(_autoGroupMode)}」重新分组？\n\n此操作会覆盖现有分组。", "确定", "取消"))
            return;

        int regrouped = 0;
        foreach (var item in _data.items)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.path);
            if (obj != null)
            {
                string newGroup = ResolveAutoGroup(obj, item.path);
                if (item.group != newGroup)
                {
                    item.group = newGroup;
                    regrouped++;
                }
                // 更新类型名（可能已变化）
                item.type = obj.GetType().Name;
            }
        }

        // 重建分组列表
        RebuildGroupList();

        Save();
        Repaint();
        Debug.Log($"[收藏夹] 已重新分组 {regrouped} 项");
    }

    /// <summary>
    /// 根据当前收藏项重建分组列表（清理空分组）
    /// </summary>
    private void RebuildGroupList()
    {
        var usedGroups = new HashSet<string>(_data.items.Select(i => i.group));
        usedGroups.Add("默认"); // 始终保留默认分组
        _data.groups = usedGroups.OrderBy(g => g).ToList();
        // 确保 "默认" 排在最前
        _data.groups.Remove("默认");
        _data.groups.Insert(0, "默认");
    }
    #endregion

    #region 持久化
    private void Save()
    {
        string json = JsonUtility.ToJson(_data, true);
        EditorPrefs.SetString(PREFS_KEY, json);
    }

    private void Load()
    {
        string json = EditorPrefs.GetString(PREFS_KEY, "");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                _data = JsonUtility.FromJson<BookmarkData>(json);
                if (_data == null) _data = new BookmarkData();
                if (_data.groups == null) _data.groups = new List<string> { "默认" };
                if (_data.items == null) _data.items = new List<BookmarkItem>();

                if (!_data.groups.Contains("默认"))
                {
                    _data.groups.Insert(0, "默认");
                }

                // 恢复自动分组模式
                _autoGroupMode = (AutoGroupMode)Mathf.Clamp(_data.autoGroupMode, 0, Enum.GetValues(typeof(AutoGroupMode)).Length - 1);

                // 恢复排序模式
                _sortMode = (SortMode)Mathf.Clamp(_data.sortMode, 0, Enum.GetValues(typeof(SortMode)).Length - 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[收藏夹] 加载数据失败: {e.Message}");
                _data = new BookmarkData();
                _autoGroupMode = AutoGroupMode.None;
                _sortMode = SortMode.Default;
            }
        }
        else
        {
            _data = new BookmarkData();
            _autoGroupMode = AutoGroupMode.None;
            _sortMode = SortMode.Default;
        }
    }
    #endregion
}
#endif
