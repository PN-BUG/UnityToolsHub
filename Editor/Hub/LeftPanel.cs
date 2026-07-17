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
    private const float LeftPanelWidth = 230f;
    private const float CategoryHeaderHeight = 30f;
    private const float ToolItemHeight = 26f;
    private const float SplitterWidth = 1f;
    private const float RightPadding = 16f;

    // ── 缓存样式（避免每帧 OnGUI 分配）──
    private static GUIStyle _cachedCreateBtnLabel;
    private static GUIStyle _cachedHiddenBtnLabel;
    private static GUIStyle _cachedAddCatBtnLabel;
    private static GUIStyle CachedCreateBtnLabel
        => _cachedCreateBtnLabel ?? (_cachedCreateBtnLabel = new GUIStyle(Styles.ToolItem));
    private static GUIStyle CachedHiddenBtnLabel
        => _cachedHiddenBtnLabel ?? (_cachedHiddenBtnLabel = new GUIStyle(Styles.ToolItem) { fontSize = 11 });
    private static GUIStyle CachedAddCatBtnLabel
        => _cachedAddCatBtnLabel ?? (_cachedAddCatBtnLabel = new GUIStyle(Styles.ToolItem) { fontSize = 11 });

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterLeftPanelCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            _cachedCreateBtnLabel = null;
            _cachedHiddenBtnLabel = null;
            _cachedAddCatBtnLabel = null;
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
        _hoveredCategoryName = null;
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
        EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, position.height), ClrLeftBg);

        // ── 处理拖放事件 ──
        HandleToolDragEvents();

        // ── 底部区域（固定在窗口底部，绝对定位）─────────────
        const float BottomHeight = 90f; // 底部区域高度
        var bottomRect = new Rect(0, position.height - BottomHeight, LeftPanelWidth, BottomHeight);

        // ── 中间内容区（顶部 + ScrollView）───────────────
        float topHeight = 70f;
        float svHeight = position.height - topHeight - BottomHeight;
        svHeight = Mathf.Max(svHeight, 100f);

        EditorGUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth), GUILayout.Height(topHeight + svHeight));

        // ── Logo 区域 ──────────────────────────────────────
        EditorGUILayout.Space(8);
        var logoRect = EditorGUILayout.BeginHorizontal();
        logoRect.xMin += 12;
        GUI.Label(logoRect, "<color=#6699FF><b>Unity</b></color><color=#CCCCCC>Framework</color>",
            _styleLogo);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // ── 搜索框 ────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(8);

        GUI.SetNextControlName("SearchField");
        var searchRect = GUILayoutUtility.GetRect(LeftPanelWidth - 20, 24);
        _searchText = EditorGUI.TextField(searchRect, _searchText, EditorStyles.toolbarSearchField);

        if (!string.IsNullOrEmpty(_searchText))
        {
            var cancelRect = new Rect(searchRect.xMax - 14, searchRect.y + 4, 12, 12);
            if (GUI.Button(cancelRect, "", GUI.skin.GetStyle("ToolbarSeachCancelButton")))
            {
                _searchText = "";
                GUI.FocusControl(null);
            }
        }

        GUILayout.Space(8);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        // ── 排序切换 + 折叠/展开 ─────────────────────────
        {
            float toolBarH = 20;
            var toolBarRect = GUILayoutUtility.GetRect(LeftPanelWidth - 20, toolBarH);
            toolBarRect.xMin += 10;

            // 排序按钮（左侧）
            float sortAreaW = toolBarRect.width - 44;
            float btnW = sortAreaW / 3f;
            var modes = new[] { SortMode.ByName, SortMode.ByRecent, SortMode.ByMostUsed };
            var labels = new[] { "名称", "最近使用", "最常使用" };
            for (int i = 0; i < 3; i++)
            {
                var btnRect = new Rect(toolBarRect.x + btnW * i, toolBarRect.y, btnW, toolBarH);
                bool isActive = _sortMode == modes[i];
                if (isActive)
                {
                    EditorGUI.DrawRect(new Rect(btnRect.x + 2, btnRect.y + toolBarH - 2, btnRect.width - 4, 2), ClrAccent);
                }
                var s = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = isActive ? ClrTextBright : ClrTextDim },
                    fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                    fontSize = 10
                };
                GUI.Label(btnRect, labels[i], s);
                if (GUI.Button(btnRect, "", GUIStyle.none))
                {
                    SetSortMode(modes[i]);
                    Repaint();
                }
            }

            // 折叠/展开按钮（右侧）
            float foldBtnSize = 18;
            float foldBtnY = toolBarRect.y + 1;
            var collapseRect = new Rect(toolBarRect.xMax - foldBtnSize * 2 - 2, foldBtnY, foldBtnSize, foldBtnSize);
            var expandRect = new Rect(toolBarRect.xMax - foldBtnSize, foldBtnY, foldBtnSize, foldBtnSize);

            var foldStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ClrTextDim },
                hover = { textColor = ClrText },
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            var collapseHover = collapseRect.Contains(Event.current.mousePosition);
            var expandHover = expandRect.Contains(Event.current.mousePosition);
            if (collapseHover) EditorGUI.DrawRect(collapseRect, ClrHover);
            if (expandHover) EditorGUI.DrawRect(expandRect, ClrHover);
            GUI.Label(collapseRect, "⊟", foldStyle);
            GUI.Label(expandRect, "⊞", foldStyle);
            if (GUI.Button(collapseRect, "", GUIStyle.none))
            {
                CollapseAllCategories();
                Repaint();
            }
            if (GUI.Button(expandRect, "", GUIStyle.none))
            {
                ExpandAllCategories();
                Repaint();
            }
        }

        // ── 分隔线 ──────────────────────────────────────
        EditorGUILayout.Space(2);
        {
            var sepRect = GUILayoutUtility.GetRect(LeftPanelWidth - 16, 1);
            sepRect.xMin += 8;
            EditorGUI.DrawRect(sepRect, ClrDivider);
        }
        EditorGUILayout.Space(4);

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
            if (!hasSearch && _hiddenItems.IsCategoryHidden(category.name)) continue;

            var filtered = hasSearch
                ? category.tools.Where(t =>
                    t.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.description.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (t.tags != null && t.tags.Any(tag => tag.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                  ).ToList()
                : category.tools;

            if (!hasSearch)
                filtered = filtered.Where(t => !_hiddenItems.IsToolHidden(t.typeName)).ToList();

            // 空分类也显示（但搜索模式下隐藏无匹配的分类）
            if (hasSearch && filtered.Count == 0) continue;

            // 分类间分隔线（GUILayout 定位，与分类头对齐）
            if (!isFirstCategory)
            {
                var lineRect = GUILayoutUtility.GetRect(LeftPanelWidth - ScrollbarReserve, 2, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(new Rect(lineRect.x + 16, lineRect.y, lineRect.width - 32, 1), ClrDivider);
            }
            isFirstCategory = false;

            // ── 分类标题（文件夹式）───────────────────
            if (!hasSearch)
            {
                var catHeaderRect = GUILayoutUtility.GetRect(
                    LeftPanelWidth - ScrollbarReserve, CategoryHeaderHeight,
                    GUILayout.ExpandWidth(true));

                // 缓存分类头矩形（用于拖放检测）
                _cachedCategoryHeaders[category.name] = catHeaderRect;
                _categoryHeaderRects[catHeaderRect.y] = catHeaderRect;

                // ── 分类头背景（始终有底色，区分内容区）──
                EditorGUI.DrawRect(catHeaderRect, new Color(0.15f, 0.15f, 0.15f, 1f));

                bool isCatHover = catHeaderRect.Contains(Event.current.mousePosition);
                bool isDropTarget = _isDragActive && _dragType == DragType.Tool
                    && catHeaderRect.Contains(Event.current.mousePosition);
                if (isDropTarget)
                {
                    EditorGUI.DrawRect(catHeaderRect, new Color(category.accent.r, category.accent.g, category.accent.b, 0.22f));
                }
                else if (isCatHover && Event.current.type != EventType.MouseDown)
                {
                    EditorGUI.DrawRect(catHeaderRect, new Color(1f, 1f, 1f, 0.08f));
                }

                // 分类头底部分隔线
                EditorGUI.DrawRect(new Rect(catHeaderRect.x, catHeaderRect.yMax - 1, catHeaderRect.width, 1), ClrDivider);

                // ── 拖放：分类拖动中显示插入指示线 ──
                if (_isDragActive && _dragType == DragType.Category && _dragCategoryName != category.name)
                {
                    bool isAbove = catHeaderRect.Contains(Event.current.mousePosition)
                        && Event.current.mousePosition.y < catHeaderRect.y + catHeaderRect.height * 0.5f;
                    bool isBelow = catHeaderRect.Contains(Event.current.mousePosition)
                        && Event.current.mousePosition.y >= catHeaderRect.y + catHeaderRect.height * 0.5f;
                    if (isAbove)
                    {
                        EditorGUI.DrawRect(new Rect(catHeaderRect.x, catHeaderRect.y - 1, catHeaderRect.width, 2), ClrAccent);
                    }
                    else if (isBelow)
                    {
                        EditorGUI.DrawRect(new Rect(catHeaderRect.x, catHeaderRect.yMax - 1, catHeaderRect.width, 2), ClrAccent);
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

                    // 如果点击在色条（左侧 16px）区域，开始拖动分类
                    if (catEvt.mousePosition.x < catHeaderRect.x + 16)
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

                // 分类色条（更粗，带圆角感）
                var colorBar = new Rect(catHeaderRect.x + 1, catHeaderRect.y + 5, 3, catHeaderRect.height - 10);
                EditorGUI.DrawRect(colorBar, category.accent);

                // 折叠箭头
                var arrowColor = category.expanded ? ClrText : ClrTextDim;
                DrawFoldoutArrow(new Rect(catHeaderRect.x + 7, catHeaderRect.y, 12, catHeaderRect.height), category.expanded);

                // 分类名
                var labelRect = new Rect(catHeaderRect.x + 20, catHeaderRect.y, catHeaderRect.width - 20, catHeaderRect.height);
                string customTag = !IsDefaultCategory(category.name) ? " <color=#888888>[自定义]</color>" : "";
                string countTag = filtered.Count > 0 ? $"  <color=#777777>{filtered.Count}</color>" : "";
                var headerLabelStyle = new GUIStyle(_styleCategoryHeader);
                if (isCatHover || isDropTarget)
                {
                    headerLabelStyle.normal.textColor = ClrTextBright;
                }
                GUI.Label(labelRect, $"{category.icon}  {category.name}{customTag}{countTag}", headerLabelStyle);

                EditorGUILayout.Space(1);

                if (!category.expanded) continue;
            }

            // ── 工具项（支持拖动到其他分类）─────────────
            foreach (var tool in filtered)
            {
                bool isSelected = _selectedTool == tool;
                var style = isSelected ? _styleToolItemSelected : _styleToolItem;

                var rawRect = GUILayoutUtility.GetRect(
                    LeftPanelWidth - ScrollbarReserve, ToolItemHeight,
                    GUILayout.ExpandWidth(true));
                // 左缩进，与分类标题形成层级
                var itemRect = new Rect(rawRect.x + 14, rawRect.y, rawRect.width - 14, rawRect.height);

                // 拖动中：当前工具项半透明
                bool isDraggingThisTool = _isDragActive && _dragType == DragType.Tool && _dragToolTypeName == tool.typeName;
                if (isDraggingThisTool)
                {
                    EditorGUI.DrawRect(itemRect, new Color(1f, 1f, 1f, 0.15f));
                }

                // hover 高亮（非拖动、非选中时）
                bool isHover = !isDraggingThisTool && !isSelected && itemRect.Contains(Event.current.mousePosition)
                    && Event.current.type != EventType.MouseDown;
                if (isHover)
                {
                    EditorGUI.DrawRect(itemRect, ClrHover);
                }

                // 选中态背景 + 左侧色条
                if (isSelected)
                {
                    EditorGUI.DrawRect(itemRect, ClrSelection);
                    var selBar = new Rect(itemRect.x, itemRect.y + 4, 3, itemRect.height - 8);
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
                    _showHiddenManager = false;
                    _rightScroll = Vector2.zero;
                    RecordToolUsage(tool);
                }

                // 绘制工具名称
                if (!isDraggingThisTool)
                {
                    GUI.Label(itemRect, tool.name, style);
                }

                // 搜索模式下显示分类标签
                if (hasSearch && !isDraggingThisTool)
                {
                    var catTagStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = category.accent },
                        fontStyle = FontStyle.Italic,
                        fontSize = 9,
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(4, 4, 0, 0)
                    };
                    var catTagContent = new GUIContent(category.name);
                    var catTagSize = catTagStyle.CalcSize(catTagContent);
                    var catTagRect = new Rect(itemRect.x + 8, itemRect.yMax - 14, catTagSize.x + 8, 12);
                    EditorGUI.DrawRect(catTagRect, new Color(category.accent.r, category.accent.g, category.accent.b, 0.1f));
                    GUI.Label(catTagRect, catTagContent, catTagStyle);
                }

                // 右侧显示快捷键
                var effectiveShortcut = GetEffectiveShortcut(tool.typeName);
                if (effectiveShortcut.IsValid)
                {
                    var kbContent = new GUIContent(effectiveShortcut.ToString());
                    var kbSize = _styleShortcut.CalcSize(kbContent);
                    var kbWidth = kbSize.x + 10;
                    var kbRect = new Rect(itemRect.xMax - kbWidth - 4, itemRect.y + 6, kbWidth, 16);
                    if (kbRect.xMax > itemRect.xMax - 2)
                        kbRect.x = itemRect.xMax - kbWidth - 2;
                    EditorGUI.DrawRect(kbRect, ClrTagBg);
                    GUI.Label(kbRect, effectiveShortcut.ToString(), _styleShortcut);
                }
            }

            EditorGUILayout.Space(4);
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
            GUI.Label(tipRect, "<color=#888888><size=9>  ↕ 拖动分类到目标位置</size></color>", _styleVersion);
        }

        EditorGUILayout.EndVertical(); // 结束中间内容区

        // ── 底部区域（绝对定位，始终可见）────────────────
        int hiddenCount = _hiddenItems.hiddenTools.Count + _hiddenItems.hiddenCategories.Count;
        GUI.BeginGroup(bottomRect);

        // 顶部分隔线
        EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, 1), ClrDivider);

        // ── +工具 / +分类 并排按钮 ──
        int btnPadding = 8;
        int btnGap = 4;
        float halfW = (LeftPanelWidth - btnPadding * 2 - btnGap) / 2f;
        var toolBtnRect = new Rect(btnPadding, 10, halfW, 26);
        var catBtnRect  = new Rect(btnPadding + halfW + btnGap, 10, halfW, 26);

        // +工具
        bool toolActive = _showCreateForm || _showAddToolPanel;
        bool toolHover = toolBtnRect.Contains(Event.current.mousePosition);
        var toolBtnBg = toolActive ? new Color(ClrAccent.r, ClrAccent.g, ClrAccent.b, 0.3f) : (toolHover ? ClrHover : ClrBtnNormal);
        EditorGUI.DrawRect(toolBtnRect, toolBtnBg);
        DrawBorderRect(toolBtnRect, toolActive ? ClrAccent : new Color(0.35f, 0.35f, 0.35f, 1f));
        var toolLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = toolActive ? ClrTextBright : ClrText }
        };
        GUI.Label(toolBtnRect, "＋ 工具", toolLabelStyle);
        if (GUI.Button(toolBtnRect, "", GUIStyle.none))
        {
            if (toolActive) { _showCreateForm = false; _showAddToolPanel = false; }
            else { _showCreateForm = true; _showAddToolPanel = false; _selectedTool = null; _selectedCategory = null; _showHiddenManager = false; }
            GUI.FocusControl(null);
        }

        // +分类
        bool catActive = _showNewCategoryDialog;
        bool catHover = catBtnRect.Contains(Event.current.mousePosition);
        var catBtnBg = catActive ? new Color(ClrAccent.r, ClrAccent.g, ClrAccent.b, 0.3f) : (catHover ? ClrHover : ClrBtnNormal);
        EditorGUI.DrawRect(catBtnRect, catBtnBg);
        DrawBorderRect(catBtnRect, catActive ? ClrAccent : new Color(0.35f, 0.35f, 0.35f, 1f));
        var catLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = catActive ? ClrTextBright : ClrText }
        };
        GUI.Label(catBtnRect, "＋ 分类", catLabelStyle);
        if (GUI.Button(catBtnRect, "", GUIStyle.none))
        {
            _showNewCategoryDialog = !_showNewCategoryDialog;
            if (_showNewCategoryDialog) { _showCreateForm = false; _showAddToolPanel = false; _selectedTool = null; _showHiddenManager = false; }
            GUI.FocusControl(null);
        }

        // 管理隐藏项按钮
        var hiddenBtnRect = new Rect(btnPadding, 42, LeftPanelWidth - btnPadding * 2, 22);
        bool hiddenHover = hiddenBtnRect.Contains(Event.current.mousePosition);
        var hiddenBg = _showHiddenManager ? new Color(ClrAccent.r, ClrAccent.g, ClrAccent.b, 0.2f) : (hiddenHover ? ClrHover : new Color(0, 0, 0, 0));
        EditorGUI.DrawRect(hiddenBtnRect, hiddenBg);
        string hiddenLabel = hiddenCount > 0 ? $"⚙  管理隐藏项 ({hiddenCount})" : "⚙  管理隐藏项";
        var hiddenLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = _showHiddenManager ? ClrTextBright : ClrTextDim },
            fontStyle = _showHiddenManager ? FontStyle.Bold : FontStyle.Normal
        };
        GUI.Label(hiddenBtnRect, hiddenLabel, hiddenLabelStyle);
        if (GUI.Button(hiddenBtnRect, "", GUIStyle.none))
        {
            _showHiddenManager = !_showHiddenManager;
            if (_showHiddenManager) { _showCreateForm = false; _showAddToolPanel = false; _selectedTool = null; _selectedCategory = null; }
            GUI.FocusControl(null);
        }

        // 版本信息
        var verRect = new Rect(12, 68, LeftPanelWidth - 16, 16);
        string hiddenHint = hiddenCount > 0 ? $" · 隐藏 {hiddenCount} 项" : "";
        GUI.Label(verRect, $"<size=10><color=#444444>UnityToolsHub v1.0 · {_totalToolCount} 个工具{hiddenHint}</color></size>",
            _styleVersion);

        GUI.EndGroup();

        // ── 绘制拖动幽灵矩形（最后绘制，覆盖在最上层）──
        DrawDragGhost();

        // ── 新建分类对话框 ──
        if (_showNewCategoryDialog)
        {
            DrawNewCategoryDialog();
        }

        // ── 重命名分类对话框 ──
        if (_showRenameCategoryDialog)
        {
            DrawRenameCategoryDialog();
        }

        // ── 删除确认对话框 ──
        if (_showDeleteCategoryConfirm)
        {
            DrawDeleteCategoryConfirm();
        }
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
        EditorGUI.DrawRect(new Rect(x, y, w, 3), ClrAccent);

        // 标题
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            normal = { textColor = ClrTextBright }
        };
        GUI.Label(new Rect(x + 16, y + 14, w - 32, 22), "创建新分类", titleStyle);

        // 分隔线
        EditorGUI.DrawRect(new Rect(x + 16, y + 40, w - 32, 1), ClrDivider);

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
        EditorGUI.DrawRect(new Rect(x + 16, y + h - 44, w - 32, 1), ClrDivider);

        // 按钮
        var cancelRect = new Rect(x + w - 120, y + h - 36, 50, 24);
        var okRect = new Rect(x + w - 62, y + h - 36, 50, 24);

        if (GUI.Button(cancelRect, "取消", _styleBtnFlat))
        {
            _showNewCategoryDialog = false;
            _newCategoryNameInput = "";
            _newCategoryIconInput = "📁";
        }
        if (GUI.Button(okRect, "创建", _styleBtnPrimary))
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
        DrawBorderRect(dialogRect, ClrSplitter);

        GUI.Label(new Rect(x + 12, y + 8, w - 24, 20), $"重命名「{_renameCategoryOldName}」", EditorStyles.boldLabel);

        GUI.Label(new Rect(x + 12, y + 38, 50, 18), "新名称：", EditorStyles.label);
        _renameCategoryNewName = EditorGUI.TextField(new Rect(x + 68, y + 36, w - 80, 20), _renameCategoryNewName);

        var okRect = new Rect(x + w - 130, y + h - 36, 56, 24);
        var cancelRect = new Rect(x + w - 66, y + h - 36, 56, 24);

        if (GUI.Button(okRect, "确认", _styleBtnPrimary))
        {
            if (!string.IsNullOrEmpty(_renameCategoryNewName) && _renameCategoryNewName != _renameCategoryOldName)
            {
                RenameCategory(_renameCategoryOldName, _renameCategoryNewName);
            }
            _showRenameCategoryDialog = false;
            _renameCategoryNewName = "";
            Repaint();
        }
        if (GUI.Button(cancelRect, "取消", _styleBtnFlat))
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
        DrawBorderRect(dialogRect, ClrSplitter);

        GUI.Label(new Rect(x + 12, y + 8, w - 24, 36),
            $"确定要删除分类「{_deleteCategoryTargetName}」吗？\n其中的工具将移至第一个分类。", EditorStyles.wordWrappedLabel);

        var okRect = new Rect(x + w - 130, y + h - 36, 56, 24);
        var cancelRect = new Rect(x + w - 66, y + h - 36, 56, 24);

        if (GUI.Button(okRect, "删除", _styleBtnPrimary))
        {
            DeleteCategory(_deleteCategoryTargetName);
            _showDeleteCategoryConfirm = false;
            _deleteCategoryTargetName = "";
            Repaint();
        }
        if (GUI.Button(cancelRect, "取消", _styleBtnFlat))
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
        EditorGUI.DrawRect(splitRect, ClrSplitter);
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
            EditorGUI.DrawRect(ghostRect, new Color(0.15f, 0.15f, 0.15f, 0.85f));
            GUI.Label(ghostRect, $"  {_dragToolTypeName.Split('.').Last()}", new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                fontSize = 11,
                clipping = TextClipping.Overflow
            });
            DrawBorderRect(ghostRect, ClrAccent);
        }
    }
    #endregion
}
#endregion
#endif
