#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 左侧面板绘制
/// 包含 Logo、搜索框、分类文件夹列表（支持拖拽排序）、工具项（支持拖拽切换分类）、右键菜单
/// </summary>
public partial class UnityToolsHub
{
    #region 常量
    private const float LeftPanelWidth = 248f;
    private const float CategoryHeaderHeight = 34f;
    private const float ToolItemHeight = 30f;
    private const float SearchResultItemHeight = 44f;
    private const float SplitterWidth = 1f;
    private const float RightPadding = 16f;

    // ── 缓存样式（避免每帧 OnGUI 分配）──
    private static GUIStyle _cachedCreateBtnLabel;
    private static GUIStyle _cachedHiddenBtnLabel;
    private static GUIStyle _cachedAddCatBtnLabel;
    private static GUIStyle _cachedSidebarBrand;
    private static GUIStyle _cachedSidebarMeta;
    private static GUIStyle _cachedSidebarCategory;
    private static GUIStyle _cachedSidebarCount;
    private static GUIStyle _cachedSidebarTool;
    private static GUIStyle _cachedSidebarToolSelected;
    private static GUIStyle CachedCreateBtnLabel
        => _cachedCreateBtnLabel ?? (_cachedCreateBtnLabel = new GUIStyle(Styles.ToolItem));
    private static GUIStyle CachedHiddenBtnLabel
        => _cachedHiddenBtnLabel ?? (_cachedHiddenBtnLabel = new GUIStyle(Styles.ToolItem) { fontSize = 11 });
    private static GUIStyle CachedAddCatBtnLabel
        => _cachedAddCatBtnLabel ?? (_cachedAddCatBtnLabel = new GUIStyle(Styles.ToolItem) { fontSize = 11 });
    private static GUIStyle CachedSidebarBrand
        => _cachedSidebarBrand ?? (_cachedSidebarBrand = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            normal = { textColor = Theme.ClrTextBright }
        });
    private static GUIStyle CachedSidebarMeta
        => _cachedSidebarMeta ?? (_cachedSidebarMeta = new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = Theme.ClrTextDim }
        });
    private static GUIStyle CachedSidebarCategory
        => _cachedSidebarCategory ?? (_cachedSidebarCategory = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = Theme.ClrText }
        });
    private static GUIStyle CachedSidebarCount
        => _cachedSidebarCount ?? (_cachedSidebarCount = new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Theme.ClrTextDim }
        });
    private static GUIStyle CachedSidebarTool
        => _cachedSidebarTool ?? (_cachedSidebarTool = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(18, 6, 0, 0),
            clipping = TextClipping.Clip,
            normal = { textColor = Theme.ClrText }
        });
    private static GUIStyle CachedSidebarToolSelected
        => _cachedSidebarToolSelected ?? (_cachedSidebarToolSelected = new GUIStyle(CachedSidebarTool)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = Theme.ClrTextBright }
        });

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterLeftPanelCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            _cachedCreateBtnLabel = null;
            _cachedHiddenBtnLabel = null;
            _cachedAddCatBtnLabel = null;
            _cachedSidebarBrand = null;
            _cachedSidebarMeta = null;
            _cachedSidebarCategory = null;
            _cachedSidebarCount = null;
            _cachedSidebarTool = null;
            _cachedSidebarToolSelected = null;
        };
    }
    #endregion

    #region 拖放处理
    /// <summary>
    /// 处理拖放：检测鼠标按下/拖动/释放，更新拖放状态。
    /// 在 OnGUI 中 DrawLeftPanel 之前调用。
    /// </summary>
    private void HandleToolDragEvents()
    {
        var e = Event.current;

        // ── 鼠标按下：记录拖动起点（待确认）──
        if (e.type == EventType.MouseDown && e.button == 0 && !_isDragActive)
        {
            // 工具项和分类标题的拖动由各自区域检测，这里只处理全局状态
        }

        // ── 鼠标拖动中：更新悬停目标、幽灵位置 ──
        if (e.type == EventType.MouseDrag && _dragPending)
        {
            // _dragStartMousePos 在滚动区内通过 Event.current.mousePosition 获取
            // e.mousePosition 在滚动区外，两者空间可能不同，但距离判断不需要精确
            float dist = Vector2.Distance(e.mousePosition, _dragStartMousePos);
            if (dist > DragThreshold)
            {
                _isDragActive = true;
                _dragPending = false;
                e.Use();
            }
        }

        if (_isDragActive && (e.type == EventType.MouseDrag || e.type == EventType.Repaint))
        {
            _dragGhostRect = new Rect(e.mousePosition.x - 60, e.mousePosition.y - 10, 120, 20);
            Repaint();
        }

        // ── 鼠标释放：仅重置待确认状态，真正的 drop 在 DrawLeftPanel 滚动区内处理 ──
        if (e.type == EventType.MouseUp && _dragPending && !_isDragActive)
        {
            // 仅 _dragPending 但未激活 → 普通点击，不消费事件
            _dragType = DragType.None;
            _dragToolTypeName = null;
            _dragSourceCategory = null;
            _dragCategoryName = null;
            _dragPending = false;
            Repaint();
        }

        // drag 中的 MouseUp 不在这里处理（坐标空间问题），在 DrawLeftPanel 滚动区内处理

        // ── 按 Escape 取消拖动 ──
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && _isDragActive)
        {
            ResetDragState();
            e.Use();
            Repaint();
        }
    }

    /// <summary>根据鼠标 Y 坐标确定分类插入索引</summary>
    private int FindCategoryInsertIndex(Vector2 mousePos)
    {
        if (_cachedCategoryHeaders == null || _cachedCategoryHeaders.Count == 0) return -1;

        var sortedHeaders = _cachedCategoryHeaders.OrderBy(kv => kv.Value.y).ToList();
        for (int i = 0; i < sortedHeaders.Count; i++)
        {
            var header = sortedHeaders[i];
            float midY = header.Value.y + header.Value.height * 0.5f;
            if (mousePos.y < midY)
                return i;
        }
        return sortedHeaders.Count; // 插入到最后
    }

    /// <summary>重置拖放状态</summary>
    private void ResetDragState()
    {
        _dragType = DragType.None;
        _dragToolTypeName = null;
        _dragSourceCategory = null;
        _dragCategoryName = null;
        _dragCategorySourceIndex = -1;
        _isDragActive = false;
        _dragPending = false;
        _dragGhostRect = Rect.zero;
    }

    // ── 分类头矩形缓存（DrawLeftPanel 中填充，拖放中读取）──
    private Dictionary<string, Rect> _cachedCategoryHeaders;
    private Dictionary<float, Rect> _categoryHeaderRects;
    #endregion

    #region 左侧面板
    private void DrawLeftPanel()
    {
        // 左侧背景
        EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, position.height), Theme.ClrLeftBg);

        // ── 处理拖放事件 ──
        HandleToolDragEvents();

        // ── 底部区域（固定在窗口底部，绝对定位）─────────────
        const float BottomHeight = 88f; // 底部区域高度
        var bottomRect = new Rect(0, position.height - BottomHeight, LeftPanelWidth, BottomHeight);

        // ── 中间内容区（顶部 + ScrollView）───────────────
        float topHeight = 112f;
        float svHeight = position.height - topHeight - BottomHeight;
        svHeight = Mathf.Max(svHeight, 100f);

        EditorGUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth), GUILayout.Height(topHeight + svHeight));

        // ── Logo 区域 ──────────────────────────────────────
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(12);
        var brandRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
        GUI.Label(brandRect, "Tools Hub", CachedSidebarBrand);
        GUI.Label(new Rect(brandRect.xMax - 88, brandRect.y, 88, brandRect.height),
            $"{_totalToolCount} 个工具", CachedSidebarMeta);
        if (GUI.Button(brandRect, GUIContent.none, GUIStyle.none))
        {
            _selectedTool = null;
            _selectedCategory = null;
            _showCreateForm = false;
            _showAddToolPanel = false;
            _showThirdPartyManager = false;
            _showHiddenManager = false;
            _rightScroll = Vector2.zero;
        }
        GUILayout.Space(12);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // ── 搜索框（圆角背景 + 占位符）────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(8);

        GUI.SetNextControlName("SearchField");
        var searchRect = GUILayoutUtility.GetRect(LeftPanelWidth - 24, 30);
        // 搜索框背景
        Drawing.DrawRoundedRect(searchRect, Theme.ClrSearchBg, 6f);
        DrawBorderRect(searchRect, new Color(1f, 1f, 1f, 0.07f));
        // 搜索图标（手动绘制，TextField 用 plain style 避免重复图标）
        var searchIconRect = new Rect(searchRect.x + 8, searchRect.y + 7, 16, 16);
        var oldColor = GUI.color;
        GUI.color = Theme.ClrTextDim;
        var searchIcon = EditorGUIUtility.IconContent("Search Icon");
        if (searchIcon != null && searchIcon.image != null)
            GUI.DrawTexture(searchIconRect, searchIcon.image, ScaleMode.ScaleToFit);
        GUI.color = oldColor;
        // 输入区域（用 plain TextField 避免自带搜索图标重复）
        var inputRect = new Rect(searchRect.x + 27, searchRect.y + 3, searchRect.width - 51, searchRect.height - 6);
        _searchText = GUI.TextField(inputRect, _searchText, EditorStyles.textField);

        // 占位符
        if (string.IsNullOrEmpty(_searchText) && GUI.GetNameOfFocusedControl() != "SearchField")
        {
            GUI.Label(inputRect, "搜索工具...", Styles.SearchPlaceholder);
        }

        if (!string.IsNullOrEmpty(_searchText))
        {
            var cancelRect = new Rect(searchRect.xMax - 18, searchRect.y + 5, 14, 14);
            var cancelHover = cancelRect.Contains(Event.current.mousePosition);
            if (cancelHover) GUI.color = Theme.ClrTextBright;
            if (GUI.Button(cancelRect, "✕", EditorStyles.miniLabel))
            {
                _searchText = "";
                GUI.FocusControl(null);
            }
            GUI.color = oldColor;
        }

        GUILayout.Space(12);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        // ── 排序切换 + 折叠/展开 ─────────────────────────
        {
            float toolBarH = 24;
            var toolBarRect = GUILayoutUtility.GetRect(LeftPanelWidth - 20, toolBarH);
            toolBarRect.xMin += 12;
            toolBarRect.xMax -= 12;

            GUI.Label(new Rect(toolBarRect.x, toolBarRect.y, 58, toolBarH), "工具分类", CachedSidebarCategory);

            float foldBtnSize = 22;
            var expandRect = new Rect(toolBarRect.xMax - foldBtnSize, toolBarRect.y + 1, foldBtnSize, foldBtnSize);
            var collapseRect = new Rect(expandRect.x - foldBtnSize - 4, toolBarRect.y + 1, foldBtnSize, foldBtnSize);
            var sortRect = new Rect(collapseRect.x - 80, toolBarRect.y + 1, 72, foldBtnSize);

            bool sortHover = sortRect.Contains(Event.current.mousePosition);
            Drawing.DrawRoundedRect(sortRect, sortHover ? Theme.ClrItemHover : Theme.ClrSearchBg, 4f);
            DrawBorderRect(sortRect, Theme.ClrDivider);
            string sortLabel = _sortMode == SortMode.ByRecent ? "最近" :
                _sortMode == SortMode.ByMostUsed ? "常用" : "名称";
            GUI.Label(sortRect, sortLabel + "  ▾", Styles.SortButton);
            if (GUI.Button(sortRect, GUIContent.none, GUIStyle.none))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("按名称排序"), _sortMode == SortMode.ByName,
                    () => { SetSortMode(SortMode.ByName); Repaint(); });
                menu.AddItem(new GUIContent("按最近使用"), _sortMode == SortMode.ByRecent,
                    () => { SetSortMode(SortMode.ByRecent); Repaint(); });
                menu.AddItem(new GUIContent("按使用次数"), _sortMode == SortMode.ByMostUsed,
                    () => { SetSortMode(SortMode.ByMostUsed); Repaint(); });
                menu.DropDown(sortRect);
            }

            bool collapseHover = collapseRect.Contains(Event.current.mousePosition);
            bool expandHover = expandRect.Contains(Event.current.mousePosition);
            Drawing.DrawRoundedRect(collapseRect, collapseHover ? Theme.ClrItemHover : Color.clear, 4f);
            Drawing.DrawRoundedRect(expandRect, expandHover ? Theme.ClrItemHover : Color.clear, 4f);
            GUI.Label(collapseRect, "−", Styles.FoldButton);
            GUI.Label(expandRect, "+", Styles.FoldButton);
            if (GUI.Button(collapseRect, GUIContent.none, GUIStyle.none))
            {
                CollapseAllCategories();
                Repaint();
            }
            if (GUI.Button(expandRect, GUIContent.none, GUIStyle.none))
            {
                ExpandAllCategories();
                Repaint();
            }
        }

        // ── 分隔线 ──────────────────────────────────────
        EditorGUILayout.Space(3);
        {
            var sepRect = GUILayoutUtility.GetRect(LeftPanelWidth - 16, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(sepRect, Theme.ClrDivider);
        }
        EditorGUILayout.Space(6);

        // ── 工具列表 ──────────────────────────────────────
        _leftScroll = EditorGUILayout.BeginScrollView(
            _leftScroll,
            false, false,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
            GUI.skin.scrollView,
            GUILayout.Height(svHeight));

        bool hasSearch = !string.IsNullOrEmpty(_searchText);
        const float ScrollbarReserve = 16f;

        // 清空分类头矩形缓存
        _cachedCategoryHeaders = new Dictionary<string, Rect>();
        _categoryHeaderRects = new Dictionary<float, Rect>();
        bool isFirstCategory = true;

        foreach (var category in _categories)
        {
            if (_hiddenItems.IsCategoryHidden(category.name)) continue;

            var filtered = hasSearch
                ? category.tools.Where(t =>
                    !_hiddenItems.IsToolHidden(t.typeName) &&
                    (t.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     t.description.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (t.tags != null && t.tags.Any(tag => tag.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)))
                  ).ToList()
                : category.tools;

            if (!hasSearch)
                filtered = filtered.Where(t => !_hiddenItems.IsToolHidden(t.typeName)).ToList();

            // 空分类也显示（但搜索模式下隐藏无匹配的分类）
            if (hasSearch && filtered.Count == 0) continue;

            // 分类之间留出稳定的呼吸空间，避免连续色块造成视觉噪音。
            if (!isFirstCategory)
                EditorGUILayout.Space(6);
            isFirstCategory = false;

            // ── 分类标题（文件夹式）───────────────────
            if (!hasSearch)
            {
                var rawCatHeaderRect = GUILayoutUtility.GetRect(
                    LeftPanelWidth - ScrollbarReserve, CategoryHeaderHeight,
                    GUILayout.ExpandWidth(true));
                var catHeaderRect = new Rect(rawCatHeaderRect.x + 6, rawCatHeaderRect.y,
                    rawCatHeaderRect.width - 12, rawCatHeaderRect.height);

                // 缓存分类头矩形（用于拖放检测）
                _cachedCategoryHeaders[category.name] = catHeaderRect;
                _categoryHeaderRects[catHeaderRect.y] = catHeaderRect;

                bool isCatHover = catHeaderRect.Contains(Event.current.mousePosition);
                bool isDropTarget = _isDragActive && _dragType == DragType.Tool
                    && catHeaderRect.Contains(Event.current.mousePosition);
                bool isCategorySelected = _selectedCategory == category && _selectedTool == null;
                Color categoryBg = isCategorySelected
                    ? new Color(category.accent.r, category.accent.g, category.accent.b, 0.16f)
                    : (isCatHover ? Theme.ClrItemHover : Theme.ClrGroupBoxBg);
                Drawing.DrawRoundedRect(catHeaderRect, categoryBg, 5f);
                if (isDropTarget)
                {
                    EditorGUI.DrawRect(catHeaderRect, new Color(category.accent.r, category.accent.g, category.accent.b, 0.22f));
                }

                // ── 拖放：分类拖动中显示插入指示线 ──
                if (_isDragActive && _dragType == DragType.Category && _dragCategoryName != category.name)
                {
                    bool isAbove = catHeaderRect.Contains(Event.current.mousePosition)
                        && Event.current.mousePosition.y < catHeaderRect.y + catHeaderRect.height * 0.5f;
                    bool isBelow = catHeaderRect.Contains(Event.current.mousePosition)
                        && Event.current.mousePosition.y >= catHeaderRect.y + catHeaderRect.height * 0.5f;
                    if (isAbove)
                    {
                        EditorGUI.DrawRect(new Rect(catHeaderRect.x, catHeaderRect.y - 1, catHeaderRect.width, 2), Theme.ClrAccent);
                    }
                    else if (isBelow)
                    {
                        EditorGUI.DrawRect(new Rect(catHeaderRect.x, catHeaderRect.yMax - 1, catHeaderRect.width, 2), Theme.ClrAccent);
                    }
                }

                // 右键菜单
                var catEvt = Event.current;
                if (catEvt.type == EventType.MouseDown && catEvt.button == 1
                    && catHeaderRect.Contains(catEvt.mousePosition))
                {
                    ShowCategoryContextMenu(category);
                    catEvt.Use();
                }

                // 左键：点击折叠 / 开始拖动分类
                if (catEvt.type == EventType.MouseDown && catEvt.button == 0
                    && catHeaderRect.Contains(catEvt.mousePosition))
                {
                    GUI.FocusControl(null);

                    // 左侧标记区用于拖动分类。
                    if (catEvt.mousePosition.x < catHeaderRect.x + 24)
                    {
                        _dragType = DragType.Category;
                        _dragCategoryName = category.name;
                        _dragCategorySourceIndex = _categories.IndexOf(category);
                        _dragPending = true;
                        _dragStartMousePos = catEvt.mousePosition;
                        catEvt.Use();
                    }
                    else
                    {
                        // 普通点击折叠
                        category.expanded = !category.expanded;
                        catEvt.Use();
                    }
                }

                // 短色条只作为分类识别，不再贯穿整行。
                var colorBar = new Rect(catHeaderRect.x + 6, catHeaderRect.y + 10, 2, 14);
                EditorGUI.DrawRect(colorBar, category.accent);

                // 折叠箭头
                Drawing.DrawFoldoutArrow(new Rect(catHeaderRect.x + 12, catHeaderRect.y, 12, catHeaderRect.height), category.expanded);

                // 分类名
                var labelRect = new Rect(catHeaderRect.x + 30, catHeaderRect.y,
                    catHeaderRect.width - 68, catHeaderRect.height);
                CachedSidebarCategory.normal.textColor = (isCatHover || isDropTarget || isCategorySelected)
                    ? Theme.ClrTextBright
                    : Theme.ClrText;
                GUI.Label(labelRect, category.name, CachedSidebarCategory);

                var countRect = new Rect(catHeaderRect.xMax - 34, catHeaderRect.y + 7, 26, 20);
                GUI.Label(countRect, filtered.Count.ToString(), CachedSidebarCount);

                EditorGUILayout.Space(3);

                if (!category.expanded) continue;
            }

            // ── 工具项（支持拖动到其他分类）─────────────
            foreach (var tool in filtered)
            {
                bool isSelected = _selectedTool == tool;
                var style = isSelected ? CachedSidebarToolSelected : CachedSidebarTool;
                float itemHeight = hasSearch ? SearchResultItemHeight : ToolItemHeight;
                var effectiveShortcut = GetEffectiveShortcut(tool.typeName);
                float shortcutWidth = effectiveShortcut.IsValid
                    ? Styles.Shortcut.CalcSize(new GUIContent(effectiveShortcut.ToString())).x + 10f
                    : 0f;

                var rawRect = GUILayoutUtility.GetRect(
                    LeftPanelWidth - ScrollbarReserve, itemHeight,
                    GUILayout.ExpandWidth(true));
                // 左缩进，与分类标题形成稳定层级。
                var itemRect = new Rect(rawRect.x + 14, rawRect.y, rawRect.width - 22, rawRect.height);

                // 拖动中：当前工具项半透明
                bool isDraggingThisTool = _isDragActive && _dragType == DragType.Tool && _dragToolTypeName == tool.typeName;
                if (isDraggingThisTool)
                {
                    Drawing.DrawRoundedRect(itemRect, new Color(1f, 1f, 1f, 0.10f), 4f);
                }

                // hover 高亮（非拖动、非选中时）
                bool isHover = !isDraggingThisTool && !isSelected && itemRect.Contains(Event.current.mousePosition)
                    && Event.current.type != EventType.MouseDown;
                if (isHover)
                {
                    Drawing.DrawRoundedRect(itemRect, new Color(1f, 1f, 1f, 0.045f), 4f);
                }

                // 选中态背景 + 左侧色条
                if (isSelected)
                {
                    Drawing.DrawRoundedRect(itemRect,
                        new Color(category.accent.r, category.accent.g, category.accent.b, 0.16f), 4f);
                    var selBar = new Rect(itemRect.x + 5, itemRect.y + 8, 2, itemRect.height - 16);
                    EditorGUI.DrawRect(selBar, category.accent);
                }

                // 右键菜单
                var toolEvt = Event.current;
                if (toolEvt.type == EventType.MouseDown && toolEvt.button == 1
                    && itemRect.Contains(toolEvt.mousePosition))
                {
                    ShowToolContextMenu(tool);
                    toolEvt.Use();
                }

                // 左键点击选中 / 开始拖动
                if (toolEvt.type == EventType.MouseDown && toolEvt.button == 0
                    && itemRect.Contains(toolEvt.mousePosition))
                {
                    GUI.FocusControl(null);

                    if (!string.IsNullOrEmpty(tool.typeName))
                    {
                        // 双击直接打开；单击仍进入下方的拖动/选中流程。
                        if (toolEvt.clickCount >= 2)
                        {
                            _dragType = DragType.None;
                            _dragToolTypeName = null;
                            _dragSourceCategory = null;
                            _dragPending = false;
                            _isDragActive = false;
                            _selectedTool = tool;
                            _selectedCategory = category;
                            _showCreateForm = false;
                            _showAddToolPanel = false;
                            _showThirdPartyManager = false;
                            _showHiddenManager = false;
                            _rightScroll = Vector2.zero;
                            RecordToolUsage(tool);
                            OpenToolWindow(tool.typeName);
                            toolEvt.Use();
                            continue;
                        }

                        // 记录拖动起始状态（待确认）
                        _dragType = DragType.Tool;
                        _dragToolTypeName = tool.typeName;
                        _dragSourceCategory = category.name;
                        _dragPending = true;
                        _dragStartMousePos = toolEvt.mousePosition;
                        // 不 Use，让后续 MouseDrag 和 MouseUp 处理
                    }
                    else
                    {
                        // 无 typeName 的工具直接选中
                        _selectedTool = tool;
                        _selectedCategory = category;
                        _showCreateForm = false;
                        _showAddToolPanel = false;
                        _showThirdPartyManager = false;
                        _showHiddenManager = false;
                        _rightScroll = Vector2.zero;
                        RecordToolUsage(tool);
                        toolEvt.Use();
                    }
                }

                // 在非拖动状态下，普通点击选中
                if (!_isDragActive && !_dragPending && toolEvt.type == EventType.MouseUp
                    && itemRect.Contains(toolEvt.mousePosition)
                    && Vector2.Distance(toolEvt.mousePosition, _dragStartMousePos) < DragThreshold
                    && !string.IsNullOrEmpty(tool.typeName))
                {
                    _selectedTool = tool;
                    _selectedCategory = category;
                    _showCreateForm = false;
                    _showAddToolPanel = false;
                    _showThirdPartyManager = false;
                    _showHiddenManager = false;
                    _rightScroll = Vector2.zero;
                    RecordToolUsage(tool);
                }

                // 搜索结果使用双行布局：第一行工具名，第二行分类标签，避免与快捷键重叠。
                var titleRect = hasSearch
                    ? new Rect(itemRect.x, itemRect.y + 2, itemRect.width - shortcutWidth - 4, 20)
                    : itemRect;

                // 绘制工具名称
                if (!isDraggingThisTool)
                {
                    GUI.Label(titleRect, tool.name, style);
                }

                // 搜索模式下显示分类标签
                if (hasSearch && !isDraggingThisTool)
                {
                    Styles.CategoryTagSearch.normal.textColor = category.accent;
                    var catTagContent = new GUIContent(category.name);
                    var catTagSize = Styles.CategoryTagSearch.CalcSize(catTagContent);
                    var catTagRect = new Rect(itemRect.x + 18, itemRect.yMax - 17, catTagSize.x + 8, 13);
                    Drawing.DrawRoundedRect(catTagRect,
                        new Color(category.accent.r, category.accent.g, category.accent.b, 0.10f), 3f);
                    GUI.Label(catTagRect, catTagContent, Styles.CategoryTagSearch);
                }

                // 右侧显示快捷键
                if (effectiveShortcut.IsValid)
                {
                    var kbWidth = shortcutWidth;
                    var kbRect = new Rect(itemRect.xMax - kbWidth - 5,
                        itemRect.y + (hasSearch ? 4 : (itemRect.height - 16) * 0.5f), kbWidth, 16);
                    if (kbRect.xMax > itemRect.xMax - 2)
                        kbRect.x = itemRect.xMax - kbWidth - 2;
                    EditorGUI.DrawRect(kbRect, Theme.ClrTagBg);
                    GUI.Label(kbRect, effectiveShortcut.ToString(), Styles.Shortcut);
                }
            }

            EditorGUILayout.Space(2);
        }

        if (hasSearch)
        {
            int matchCount = _categories.Sum(category => category.tools.Count(tool =>
                !_hiddenItems.IsToolHidden(tool.typeName) &&
                (tool.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 tool.description.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (tool.tags != null && tool.tags.Any(tag =>
                     tag.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)))));
            if (matchCount == 0)
            {
                EditorGUILayout.Space(18);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("未找到匹配的工具", Styles.EmptyHint);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        // ── 在滚动区内处理拖放释放（坐标空间一致）──
        if (_isDragActive && Event.current.type == EventType.MouseUp)
        {
            // 直接遍历缓存的分类头矩形做 hit test
            Vector2 dropPos = Event.current.mousePosition;

            if (_dragType == DragType.Tool && !string.IsNullOrEmpty(_dragToolTypeName))
            {
                foreach (var kv in _cachedCategoryHeaders)
                {
                    if (kv.Value.Contains(dropPos) && kv.Key != _dragSourceCategory)
                    {
                        MoveToolToCategory(_dragToolTypeName, kv.Key);
                        break;
                    }
                }
            }
            else if (_dragType == DragType.Category && !string.IsNullOrEmpty(_dragCategoryName))
            {
                int targetIndex = FindCategoryInsertIndex(dropPos);
                if (targetIndex >= 0 && targetIndex != _dragCategorySourceIndex)
                {
                    ReorderCategory(_dragCategoryName, targetIndex);
                }
            }
            ResetDragState();
            Event.current.Use();
            Repaint();
        }


        EditorGUILayout.EndScrollView();

        // ── 拖动中的分类插入提示（在滚动区末尾）──
        if (_isDragActive && _dragType == DragType.Category)
        {
            var tipRect = GUILayoutUtility.GetRect(LeftPanelWidth - ScrollbarReserve, 20);
            GUI.Label(tipRect, "<color=#5C5C63><size=9>  ↕ 拖动分类到目标位置</size></color>", Styles.Version);
        }

        EditorGUILayout.EndVertical(); // 结束中间内容区

        // ── 底部区域（绝对定位，始终可见）────────────────
        int hiddenCount = _hiddenItems.hiddenTools.Count + _hiddenItems.hiddenCategories.Count;
        GUI.BeginGroup(bottomRect);

        EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, BottomHeight), Theme.ClrToolbarBg);

        // 顶部分隔线
        EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, 1), Theme.ClrDivider);

        // ── +工具 / +分类 并排按钮 ──
        int btnPadding = 8;
        int btnGap = 4;
        float halfW = (LeftPanelWidth - btnPadding * 2 - btnGap) / 2f;
        var toolBtnRect = new Rect(btnPadding, 10, halfW, 26);
        var catBtnRect = new Rect(btnPadding + halfW + btnGap, 10, halfW, 26);

        // +工具
        bool toolActive = _showCreateForm || _showAddToolPanel;
        bool toolHover = toolBtnRect.Contains(Event.current.mousePosition);
        var toolBtnBg = toolActive
            ? new Color(Theme.ClrAccent.r, Theme.ClrAccent.g, Theme.ClrAccent.b, 0.28f)
            : (toolHover ? Theme.ClrItemHover : Theme.ClrGroupBoxBg);
        Drawing.DrawRoundedRect(toolBtnRect, toolBtnBg, 5f);
        DrawBorderRect(toolBtnRect, toolActive ? Theme.ClrAccentDim : Theme.ClrDivider);
        Styles.MiniLabelBoldCenter.normal.textColor = toolActive ? Theme.ClrTextBright : Theme.ClrText;
        GUI.Label(toolBtnRect, "＋ 工具", Styles.MiniLabelBoldCenter);
        if (GUI.Button(toolBtnRect, "", GUIStyle.none))
        {
            if (toolActive) { _showCreateForm = false; _showAddToolPanel = false; _showThirdPartyManager = false; }
            else { _showCreateForm = true; _showAddToolPanel = false; _showThirdPartyManager = false; _selectedTool = null; _selectedCategory = null; _showHiddenManager = false; }
            GUI.FocusControl(null);
        }

        // +分类
        bool catActive = _showNewCategoryDialog;
        bool catHover = catBtnRect.Contains(Event.current.mousePosition);
        var catBtnBg = catActive
            ? new Color(Theme.ClrAccent.r, Theme.ClrAccent.g, Theme.ClrAccent.b, 0.28f)
            : (catHover ? Theme.ClrItemHover : Theme.ClrGroupBoxBg);
        Drawing.DrawRoundedRect(catBtnRect, catBtnBg, 5f);
        DrawBorderRect(catBtnRect, catActive ? Theme.ClrAccentDim : Theme.ClrDivider);
        Styles.MiniLabelBoldCenter.normal.textColor = catActive ? Theme.ClrTextBright : Theme.ClrText;
        GUI.Label(catBtnRect, "＋ 分类", Styles.MiniLabelBoldCenter);
        if (GUI.Button(catBtnRect, "", GUIStyle.none))
        {
            _showNewCategoryDialog = !_showNewCategoryDialog;
            if (_showNewCategoryDialog) { _showCreateForm = false; _showAddToolPanel = false; _showThirdPartyManager = false; _selectedTool = null; _showHiddenManager = false; }
            GUI.FocusControl(null);
        }

        // 管理隐藏项按钮
        var hiddenBtnRect = new Rect(btnPadding, 42, LeftPanelWidth - btnPadding * 2, 24);
        bool hiddenHover = hiddenBtnRect.Contains(Event.current.mousePosition);
        var hiddenBg = _showHiddenManager
            ? new Color(Theme.ClrAccent.r, Theme.ClrAccent.g, Theme.ClrAccent.b, 0.12f)
            : (hiddenHover ? Theme.ClrHover : Color.clear);
        if (hiddenBg.a > 0f)
            Drawing.DrawRoundedRect(hiddenBtnRect, hiddenBg, 5f);
        string hiddenLabel = hiddenCount > 0 ? $"⚙  设置 ({hiddenCount} 项隐藏)" : "⚙  设置";
        Styles.MiniLabelBoldCenter.normal.textColor = _showHiddenManager ? Theme.ClrTextBright : Theme.ClrTextDim;
        Styles.MiniLabelBoldCenter.fontStyle = _showHiddenManager ? FontStyle.Bold : FontStyle.Normal;
        GUI.Label(hiddenBtnRect, hiddenLabel, Styles.MiniLabelBoldCenter);
        if (GUI.Button(hiddenBtnRect, "", GUIStyle.none))
        {
            _showHiddenManager = !_showHiddenManager;
            if (_showHiddenManager) { _showCreateForm = false; _showAddToolPanel = false; _showThirdPartyManager = false; _selectedTool = null; _selectedCategory = null; }
            GUI.FocusControl(null);
        }

        // 版本信息
        var verRect = new Rect(12, 68, LeftPanelWidth - 24, 16);
        string hiddenHint = hiddenCount > 0 ? $" · 隐藏 {hiddenCount} 项" : "";
        GUI.Label(verRect, $"<size=10><color=#5C5C63>UnityToolsHub v1.1 · {_totalToolCount} 个工具{hiddenHint}</color></size>",
            Styles.Version);

        GUI.EndGroup();

        // 拖动预览与对话框由 UnityToolsHub.OnGUI 统一在最上层绘制。
    }


    /// <summary>绘制矩形边框</summary>
    private static void DrawBorderRect(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
    }

    #region 新建分类对话框
    private void DrawNewCategoryDialog()
    {
        // 遮罩
        var maskRect = new Rect(0, 0, position.width, position.height);
        EditorGUI.DrawRect(maskRect, new Color(0, 0, 0, 0.45f));

        // 对话框
        float w = 300, h = 170;
        float x = (position.width - w) * 0.5f;
        float y = (position.height - h) * 0.5f;
        var dialogRect = new Rect(x, y, w, h);

        // 外层阴影
        EditorGUI.DrawRect(new Rect(x + 2, y + 2, w, h), new Color(0, 0, 0, 0.3f));

        // 背景
        EditorGUI.DrawRect(dialogRect, new Color(0.20f, 0.20f, 0.20f, 1f));
        DrawBorderRect(dialogRect, new Color(0.35f, 0.35f, 0.35f, 1f));

        // 顶部彩色条
        EditorGUI.DrawRect(new Rect(x, y, w, 3), Theme.ClrAccent);

        // 标题
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            normal = { textColor = Theme.ClrTextBright }
        };
        GUI.Label(new Rect(x + 16, y + 14, w - 32, 22), "创建新分类", titleStyle);

        // 分隔线
        EditorGUI.DrawRect(new Rect(x + 16, y + 40, w - 32, 1), Theme.ClrDivider);

        // 图标字段
        GUI.Label(new Rect(x + 16, y + 52, 60, 20), "图标", EditorStyles.miniLabel);
        var iconFieldRect = new Rect(x + 16, y + 68, 50, 22);
        EditorGUI.DrawRect(iconFieldRect, new Color(0.12f, 0.12f, 0.12f, 1f));
        DrawBorderRect(iconFieldRect, new Color(0.3f, 0.3f, 0.3f, 1f));
        _newCategoryIconInput = EditorGUI.TextField(new Rect(iconFieldRect.x + 4, iconFieldRect.y + 1, 42, 20), _newCategoryIconInput, EditorStyles.miniTextField);

        // 名称字段
        GUI.Label(new Rect(x + 80, y + 52, 60, 20), "分类名称", EditorStyles.miniLabel);
        var nameFieldRect = new Rect(x + 80, y + 68, w - 96, 22);
        EditorGUI.DrawRect(nameFieldRect, new Color(0.12f, 0.12f, 0.12f, 1f));
        DrawBorderRect(nameFieldRect, new Color(0.3f, 0.3f, 0.3f, 1f));
        _newCategoryNameInput = EditorGUI.TextField(new Rect(nameFieldRect.x + 4, nameFieldRect.y + 1, nameFieldRect.width - 8, 20), _newCategoryNameInput, EditorStyles.miniTextField);

        // 提示
        GUI.Label(new Rect(x + 16, y + 96, w - 32, 16),
            "<size=9><color=#666666>选择一个 emoji 作为图标，输入分类名称</color></size>",
            new GUIStyle(EditorStyles.label) { richText = true, fontSize = 9 });

        // 分隔线
        EditorGUI.DrawRect(new Rect(x + 16, y + h - 44, w - 32, 1), Theme.ClrDivider);

        // 按钮
        var cancelRect = new Rect(x + w - 120, y + h - 36, 50, 24);
        var okRect = new Rect(x + w - 62, y + h - 36, 50, 24);

        if (GUI.Button(cancelRect, "取消", Styles.BtnFlat))
        {
            _showNewCategoryDialog = false;
            _newCategoryNameInput = "";
            _newCategoryIconInput = "📁";
        }
        if (GUI.Button(okRect, "创建", Styles.BtnPrimary))
        {
            if (!string.IsNullOrEmpty(_newCategoryNameInput))
            {
                CreateNewCategory(_newCategoryNameInput, _newCategoryIconInput);
                _showNewCategoryDialog = false;
                _newCategoryNameInput = "";
                _newCategoryIconInput = "📁";
                Repaint();
            }
        }

        // Enter 确认
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
            && !string.IsNullOrEmpty(_newCategoryNameInput))
        {
            CreateNewCategory(_newCategoryNameInput, _newCategoryIconInput);
            _showNewCategoryDialog = false;
            _newCategoryNameInput = "";
            _newCategoryIconInput = "📁";
            Event.current.Use();
            Repaint();
        }

        // Escape 取消
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            _showNewCategoryDialog = false;
            _newCategoryNameInput = "";
            _newCategoryIconInput = "📁";
            Event.current.Use();
            Repaint();
        }
    }
    #endregion

    #region 重命名分类对话框
    private void DrawRenameCategoryDialog()
    {
        var maskRect = new Rect(0, 0, position.width, position.height);
        EditorGUI.DrawRect(maskRect, new Color(0, 0, 0, 0.4f));

        float w = 280, h = 110;
        float x = (position.width - w) * 0.5f;
        float y = (position.height - h) * 0.5f;
        var dialogRect = new Rect(x, y, w, h);
        EditorGUI.DrawRect(dialogRect, new Color(0.18f, 0.18f, 0.18f, 1f));
        DrawBorderRect(dialogRect, Theme.ClrSplitter);

        GUI.Label(new Rect(x + 12, y + 8, w - 24, 20), $"重命名「{_renameCategoryOldName}」", EditorStyles.boldLabel);

        GUI.Label(new Rect(x + 12, y + 38, 50, 18), "新名称：", EditorStyles.label);
        _renameCategoryNewName = EditorGUI.TextField(new Rect(x + 68, y + 36, w - 80, 20), _renameCategoryNewName);

        var okRect = new Rect(x + w - 130, y + h - 36, 56, 24);
        var cancelRect = new Rect(x + w - 66, y + h - 36, 56, 24);

        if (GUI.Button(okRect, "确认", Styles.BtnPrimary))
        {
            if (!string.IsNullOrEmpty(_renameCategoryNewName) && _renameCategoryNewName != _renameCategoryOldName)
            {
                RenameCategory(_renameCategoryOldName, _renameCategoryNewName);
            }
            _showRenameCategoryDialog = false;
            _renameCategoryNewName = "";
            Repaint();
        }
        if (GUI.Button(cancelRect, "取消", Styles.BtnFlat))
        {
            _showRenameCategoryDialog = false;
            _renameCategoryNewName = "";
        }

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
            && !string.IsNullOrEmpty(_renameCategoryNewName) && _renameCategoryNewName != _renameCategoryOldName)
        {
            RenameCategory(_renameCategoryOldName, _renameCategoryNewName);
            _showRenameCategoryDialog = false;
            _renameCategoryNewName = "";
            Event.current.Use();
            Repaint();
        }
    }
    #endregion

    #region 删除分类确认对话框
    private void DrawDeleteCategoryConfirm()
    {
        var maskRect = new Rect(0, 0, position.width, position.height);
        EditorGUI.DrawRect(maskRect, new Color(0, 0, 0, 0.4f));

        float w = 280, h = 100;
        float x = (position.width - w) * 0.5f;
        float y = (position.height - h) * 0.5f;
        var dialogRect = new Rect(x, y, w, h);
        EditorGUI.DrawRect(dialogRect, new Color(0.18f, 0.18f, 0.18f, 1f));
        DrawBorderRect(dialogRect, Theme.ClrSplitter);

        GUI.Label(new Rect(x + 12, y + 8, w - 24, 36),
            $"确定要删除分类「{_deleteCategoryTargetName}」吗？\n其中的工具将移至第一个分类。", EditorStyles.wordWrappedLabel);

        var okRect = new Rect(x + w - 130, y + h - 36, 56, 24);
        var cancelRect = new Rect(x + w - 66, y + h - 36, 56, 24);

        if (GUI.Button(okRect, "删除", Styles.BtnPrimary))
        {
            DeleteCategory(_deleteCategoryTargetName);
            _showDeleteCategoryConfirm = false;
            _deleteCategoryTargetName = "";
            Repaint();
        }
        if (GUI.Button(cancelRect, "取消", Styles.BtnFlat))
        {
            _showDeleteCategoryConfirm = false;
            _deleteCategoryTargetName = "";
        }
    }
    #endregion

    #region 右键菜单
    /// <summary>工具项右键菜单</summary>
    private void ShowToolContextMenu(ToolEntry tool)
    {
        var menu = new GenericMenu();
        if (string.IsNullOrEmpty(tool.typeName))
        {
            menu.AddDisabledItem(new GUIContent("此工具无法隐藏"));
            menu.AddDisabledItem(new GUIContent("此工具无法删除"));
        }
        else
        {
            bool isHidden = _hiddenItems.IsToolHidden(tool.typeName);
            menu.AddItem(new GUIContent(isHidden ? "取消隐藏" : "隐藏此工具"), false, () =>
            {
                ToggleToolHidden(tool.typeName);
                Repaint();
            });

            // 移动到子菜单
            if (_categories.Count > 1)
            {
                foreach (var cat in _categories)
                {
                    if (cat.name == tool.category) continue;
                    string catNameCapture = cat.name;
                    string toolCapture = tool.typeName;
                    bool isOriginal = cat.name == tool.originalCategory;
                    string label = isOriginal ? $"移动到/{cat.name}  ★" : $"移动到/{cat.name}";
                    menu.AddItem(new GUIContent(label), false, () =>
                    {
                        MoveToolToCategory(toolCapture, catNameCapture);
                        Repaint();
                    });
                }
            }

            menu.AddSeparator("");
            // 还原到默认分类（仅当工具已被移动到非原始分类时显示）
            bool isMoved = !string.IsNullOrEmpty(tool.originalCategory) && tool.originalCategory != tool.category;
            if (isMoved)
            {
                menu.AddItem(new GUIContent($"还原到默认分类（{tool.originalCategory}）"), false, () =>
                {
                    RestoreToolToDefaultCategory(tool.typeName);
                    Repaint();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("已在默认分类"));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除工具"), false, () =>
            {
                DeleteToolFile(tool);
                Repaint();
            });
        }
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("管理隐藏项..."), false, () =>
        {
            _showHiddenManager = true;
            _showCreateForm = false;
            _showThirdPartyManager = false;
            _selectedTool = null;
            _selectedCategory = null;
        });
        menu.ShowAsContext();
    }

    /// <summary>分类右键菜单</summary>
    private void ShowCategoryContextMenu(CategoryNode category)
    {
        var menu = new GenericMenu();
        bool isHidden = _hiddenItems.IsCategoryHidden(category.name);
        menu.AddItem(new GUIContent(isHidden ? "取消隐藏分类" : "隐藏此分类"), false, () =>
        {
            ToggleCategoryHidden(category.name);
            Repaint();
        });

        // 重命名（所有分类都可重命名）
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("重命名..."), false, () =>
        {
            _showRenameCategoryDialog = true;
            _renameCategoryOldName = category.name;
            _renameCategoryNewName = category.name;
            _showCreateForm = false;
            _showNewCategoryDialog = false;
            _selectedTool = null;
            Repaint();
        });

        // 删除（仅自定义分类可删除）
        if (!IsDefaultCategory(category.name))
        {
            menu.AddItem(new GUIContent("删除分类"), false, () =>
            {
                _showDeleteCategoryConfirm = true;
                _deleteCategoryTargetName = category.name;
                Repaint();
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("删除分类（默认分类不可删除）"));
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("管理隐藏项..."), false, () =>
        {
            _showHiddenManager = true;
            _showCreateForm = false;
            _showThirdPartyManager = false;
            _selectedTool = null;
            _selectedCategory = null;
        });
        menu.ShowAsContext();
    }
    #endregion

    #region 分隔线
    private void DrawSplitter()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(SplitterWidth));
        var splitRect = GUILayoutUtility.GetRect(SplitterWidth, position.height);
        EditorGUI.DrawRect(splitRect, Theme.ClrSplitter);
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region 拖动幽灵矩形
    private void DrawDragGhost()
    {
        if (_isDragActive && _dragType == DragType.Tool && !string.IsNullOrEmpty(_dragToolTypeName))
        {
            var mousePos = Event.current.mousePosition;
            var ghostRect = new Rect(mousePos.x + 12, mousePos.y - 10, 140, 22);
            EditorGUI.DrawRect(ghostRect, new Color(Theme.ClrItemBg.r, Theme.ClrItemBg.g, Theme.ClrItemBg.b, 0.85f));
            GUI.Label(ghostRect, $"  {_dragToolTypeName.Split('.').Last()}", new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                fontSize = 11,
                clipping = TextClipping.Overflow
            });
            DrawBorderRect(ghostRect, Theme.ClrAccent);
        }
    }
    #endregion
}
#endregion
#endif
