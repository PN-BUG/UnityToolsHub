#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 左侧面板绘制
/// 包含 Logo、搜索框、分类列表、工具项、右键菜单
/// </summary>
public partial class UnityToolsHub
{
    #region 常量
    private const float LeftPanelWidth = 230f;
    private const float CategoryHeaderHeight = 28f;
    private const float ToolItemHeight = 30f;
    private const float SplitterWidth = 1f;
    private const float RightPadding = 16f;

    // ── 缓存样式（避免每帧 OnGUI 分配）──
    private static GUIStyle _cachedCreateBtnLabel;
    private static GUIStyle _cachedHiddenBtnLabel;
    private static GUIStyle CachedCreateBtnLabel
        => _cachedCreateBtnLabel ?? (_cachedCreateBtnLabel = new GUIStyle(Styles.ToolItem));
    private static GUIStyle CachedHiddenBtnLabel
        => _cachedHiddenBtnLabel ?? (_cachedHiddenBtnLabel = new GUIStyle(Styles.ToolItem) { fontSize = 11 });

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterLeftPanelCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            _cachedCreateBtnLabel = null;
            _cachedHiddenBtnLabel = null;
        };
    }
    #endregion

    #region 左侧面板
    private void DrawLeftPanel()
    {
        // 左侧背景
        EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, position.height), ClrLeftBg);

        // ── 底部区域（固定在窗口底部，绝对定位）─────────────
        const float BottomHeight = 76f;
        var bottomRect = new Rect(0, position.height - BottomHeight, LeftPanelWidth, BottomHeight);

        // ── 中间内容区（顶部 + ScrollView）───────────────
        // 顶部高度：logo(8+20+4) + search(24+8+6) ≈ 70
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
        EditorGUILayout.Space(6);

        // ── 工具列表 ──────────────────────────────────────
        _leftScroll = EditorGUILayout.BeginScrollView(
            _leftScroll,
            false,                              // alwaysShowHorizontal
            false,                              // alwaysShowVertical
            GUIStyle.none,                      // 水平滚动条样式（隐藏）
            GUI.skin.verticalScrollbar,         // 垂直滚动条样式
            GUI.skin.scrollView,
            GUILayout.Height(svHeight));

        bool hasSearch = !string.IsNullOrEmpty(_searchText);

        // 预留垂直滚动条宽度（约 15px），避免快捷键被遮挡
        const float ScrollbarReserve = 16f;

        foreach (var category in _categories)
        {
            // 跳过隐藏的分类（搜索模式下仍显示，方便用户找到后取消隐藏）
            if (!hasSearch && _hiddenItems.IsCategoryHidden(category.name)) continue;

            var filtered = hasSearch
                ? category.tools.Where(t =>
                    t.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.description.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (t.tags != null && t.tags.Any(tag => tag.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                  ).ToList()
                : category.tools;

            // 非搜索模式下过滤隐藏工具
            if (!hasSearch)
                filtered = filtered.Where(t => !_hiddenItems.IsToolHidden(t.typeName)).ToList();

            if (filtered.Count == 0) continue;

            // ── 分类标题（手动绘制折叠箭头 + 文字）──────
            if (!hasSearch)
            {
                var catHeaderRect = GUILayoutUtility.GetRect(
                    LeftPanelWidth - ScrollbarReserve, CategoryHeaderHeight,
                    GUILayout.ExpandWidth(true));

                // 右键菜单：隐藏/取消隐藏分类
                var catEvt = Event.current;
                if (catEvt.type == EventType.MouseDown && catEvt.button == 1
                    && catHeaderRect.Contains(catEvt.mousePosition))
                {
                    ShowCategoryContextMenu(category);
                    catEvt.Use();
                }

                // 整行可点击折叠（左键）
                if (GUI.Button(catHeaderRect, GUIContent.none, GUIStyle.none))
                    category.expanded = !category.expanded;

                // 分类色条
                var colorBar = new Rect(catHeaderRect.x, catHeaderRect.y + 4, 3, catHeaderRect.height - 8);
                EditorGUI.DrawRect(colorBar, category.accent);

                // 折叠箭头（手动三角形）
                DrawFoldoutArrow(new Rect(catHeaderRect.x + 7, catHeaderRect.y, 12, catHeaderRect.height), category.expanded);

                // 分类名
                var labelRect = new Rect(catHeaderRect.x + 8, catHeaderRect.y, catHeaderRect.width - 8, catHeaderRect.height);
                GUI.Label(labelRect, $"{category.icon}  {category.name}  <color=#666666>({filtered.Count})</color>", _styleCategoryHeader);

                EditorGUILayout.Space(2);

                if (!category.expanded) continue;
            }

            // ── 工具项 ────────────────────────────────────
            foreach (var tool in filtered)
            {
                bool isSelected = _selectedTool == tool;
                var style = isSelected ? _styleToolItemSelected : _styleToolItem;

                var itemRect = GUILayoutUtility.GetRect(
                    LeftPanelWidth - ScrollbarReserve, ToolItemHeight,
                    GUILayout.ExpandWidth(true));

                // 选中态左侧色条
                if (isSelected)
                {
                    var selBar = new Rect(itemRect.x, itemRect.y + 3, 3, itemRect.height - 6);
                    EditorGUI.DrawRect(selBar, category.accent);
                }

                // 右键菜单：隐藏/取消隐藏工具
                var toolEvt = Event.current;
                if (toolEvt.type == EventType.MouseDown && toolEvt.button == 1
                    && itemRect.Contains(toolEvt.mousePosition))
                {
                    ShowToolContextMenu(tool);
                    toolEvt.Use();
                }

                // 左键点击选中
                if (GUI.Button(itemRect, tool.name, style))
                {
                    _selectedTool = tool;
                    _selectedCategory = category;
                    _rightScroll = Vector2.zero;
                    GUI.FocusControl(null);
                    RecordToolUsage(tool);
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

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical(); // 结束中间内容区

        // ── 底部区域（绝对定位，始终可见）────────────────
        int hiddenCount = _hiddenItems.hiddenTools.Count + _hiddenItems.hiddenCategories.Count;
        GUI.BeginGroup(bottomRect);

        // 创建新工具按钮
        var createBtnRect = new Rect(8, 2, LeftPanelWidth - 16, 28);
        bool createHover = createBtnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(createBtnRect, _showCreateForm ? ClrSelection : (createHover ? ClrHover : new Color(0, 0, 0, 0)));
        EditorGUI.DrawRect(new Rect(createBtnRect.x, createBtnRect.y + 4, 3, createBtnRect.height - 8),
            new Color(0.35f, 0.75f, 0.45f, 1f));
        CachedCreateBtnLabel.normal.textColor = _showCreateForm ? ClrTextBright : ClrText;
        CachedCreateBtnLabel.fontStyle = _showCreateForm ? FontStyle.Bold : FontStyle.Normal;
        GUI.Label(createBtnRect, "  ＋  创建新工具", CachedCreateBtnLabel);
        if (GUI.Button(createBtnRect, "", GUIStyle.none))
        {
            _showCreateForm = !_showCreateForm;
            if (_showCreateForm) { _selectedTool = null; _selectedCategory = null; _showHiddenManager = false; }
            GUI.FocusControl(null);
        }

        // 管理隐藏项按钮
        var hiddenBtnRect = new Rect(8, 32, LeftPanelWidth - 16, 24);
        bool hiddenBtnHover = hiddenBtnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(hiddenBtnRect, _showHiddenManager ? ClrSelection : (hiddenBtnHover ? ClrHover : new Color(0, 0, 0, 0)));
        EditorGUI.DrawRect(new Rect(hiddenBtnRect.x, hiddenBtnRect.y + 4, 3, hiddenBtnRect.height - 8),
            new Color(0.85f, 0.55f, 0.40f, 1f));
        string hiddenLabel = hiddenCount > 0 ? $"  ⚙  管理隐藏项 ({hiddenCount})" : "  ⚙  管理隐藏项";
        CachedHiddenBtnLabel.normal.textColor = _showHiddenManager ? ClrTextBright : ClrTextDim;
        CachedHiddenBtnLabel.fontStyle = _showHiddenManager ? FontStyle.Bold : FontStyle.Normal;
        GUI.Label(hiddenBtnRect, hiddenLabel, CachedHiddenBtnLabel);
        if (GUI.Button(hiddenBtnRect, "", GUIStyle.none))
        {
            _showHiddenManager = !_showHiddenManager;
            if (_showHiddenManager) { _showCreateForm = false; _selectedTool = null; _selectedCategory = null; }
            GUI.FocusControl(null);
        }

        // 版本信息
        var verRect = new Rect(12, 58, LeftPanelWidth - 16, 16);
        string hiddenHint = hiddenCount > 0 ? $" · 隐藏 {hiddenCount} 项" : "";
        GUI.Label(verRect, $"<size=10><color=#444444>UnityToolsHub v1.0 · {_totalToolCount} 个工具{hiddenHint}</color></size>",
            _styleVersion);

        GUI.EndGroup();
    }

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
}
#endif
