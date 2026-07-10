#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 右侧面板绘制
/// 包含欢迎页、工具详情页、创建工具表单、隐藏项管理面板
/// </summary>
public partial class UnityToolsHub
{
    // ── 缓存样式（避免每帧 OnGUI 分配）──
    private static GUIStyle _cachedTooltipStyle;
    private static GUIStyle _cachedCenterLabel;
    private static GUIStyle _cachedBtnFlatSmall;
    private static GUIStyle _cachedBtnFlatSmallCenter;
    private static GUIStyle _cachedHintStyle;
    private static GUIStyle _cachedDimLabel;
    private static readonly GUIContent _cachedContent = new GUIContent();

    private static GUIStyle CachedTooltipStyle
        => _cachedTooltipStyle ?? (_cachedTooltipStyle = new GUIStyle()
        {
            fontSize = 9,
            normal = { textColor = new Color(0.4f, 0.4f, 0.4f, 1f) },
            padding = new RectOffset(62, 0, 0, 0)
        });

    private static GUIStyle CachedCenterLabel
        => _cachedCenterLabel ?? (_cachedCenterLabel = new GUIStyle()
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrText }
        });

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterRightPanelCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            _cachedTooltipStyle = null;
            _cachedCenterLabel = null;
            _cachedBtnFlatSmall = null;
            _cachedBtnFlatSmallCenter = null;
            _cachedHintStyle = null;
            _cachedDimLabel = null;
        };
    }

    #region 右侧面板
    private void DrawRightPanel()
    {
        // 右侧背景
        EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(
            new Rect(LeftPanelWidth + SplitterWidth, 0,
                position.width - LeftPanelWidth - SplitterWidth, position.height),
            ClrRightBg);

        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        if (_showHiddenManager)
            DrawHiddenManagerPanel();
        else if (_showCreateForm)
            DrawCreateToolForm();
        else if (_selectedTool == null)
            DrawWelcomePanel();
        else
            DrawToolDetailPanel(_selectedTool);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region 欢迎页
    private void DrawWelcomePanel()
    {
        var area = new Rect(0, 0, position.width - LeftPanelWidth - SplitterWidth, position.height);

        // ── 渐变装饰条 ──────────────────────────────────
        var gradRect = new Rect(area.x, area.y, area.width, 4);
        DrawGradientRect(gradRect, new Color(0.20f, 0.45f, 0.90f), new Color(0.40f, 0.70f, 0.95f));

        // ── 主内容 ──────────────────────────────────────
        float centerY = area.height * 0.28f;

        // 标题
        var titleRect = new Rect(area.x + RightPadding, centerY, area.width - RightPadding * 2, 40);
        GUI.Label(titleRect,
            "<color=#6699FF>Unity</color><color=#EEEEEE>ToolsHub</color>",
            _styleWelcomeTitle);

        // 副标题
        var subRect = new Rect(area.x + RightPadding, centerY + 46, area.width - RightPadding * 2, 22);
        GUI.Label(subRect, "游戏开发工具集", _styleWelcomeSub);

        EditorGUILayout.Space(centerY + 80);

        // ── 统计卡片 ──────────────────────────────────────
        int totalTools = _totalToolCount;
        int totalCategories = _categories.Count;

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        DrawStatCard("工具总数", totalTools.ToString(), ClrAccent,
            GUILayout.Width(110), GUILayout.Height(60));
        GUILayout.Space(20);
        DrawStatCard("分类数", totalCategories.ToString(), new Color(0.35f, 0.75f, 0.45f, 1f),
            GUILayout.Width(110), GUILayout.Height(60));
        GUILayout.Space(20);
        DrawStatCard("快捷键", "Ctrl+Shift+E", new Color(0.85f, 0.55f, 0.40f, 1f),
            GUILayout.Width(130), GUILayout.Height(60));

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(36);

        // ── 提示 ──────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("← 从左侧选择一个工具开始使用", _styleWelcomeSub);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // ── 分类概览 ──────────────────────────────────────
        EditorGUILayout.Space(28);
        DrawCategoryOverview();
    }

    private void DrawStatCard(string label, string value, Color accent, params GUILayoutOption[] options)
    {
        var rect = GUILayoutUtility.GetRect(0, 0, options);
        // 背景
        EditorGUI.DrawRect(rect, ClrCardBg);
        // 顶部色条
        EditorGUI.DrawRect(new Rect(rect.x + 8, rect.y + 2, rect.width - 16, 2), accent);

        // 数值
        var numRect = new Rect(rect.x, rect.y + 10, rect.width, 28);
        GUI.Label(numRect, value, _styleStatNum);
        // 标签
        var lblRect = new Rect(rect.x, rect.y + 38, rect.width, 16);
        GUI.Label(lblRect, label, _styleStatLabel);
    }

    private void DrawCategoryOverview()
    {
        var areaWidth = position.width - LeftPanelWidth - SplitterWidth - RightPadding * 2;
        float cardW = Mathf.Min(160, (areaWidth - 12 * 3) / 4);
        float cardH = 48;
        float spacing = 12;

        int cols = Mathf.Max(1, Mathf.FloorToInt((areaWidth + spacing) / (cardW + spacing)));
        int col = 0;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        foreach (var cat in _categories)
        {
            if (col >= cols)
            {
                col = 0;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(spacing);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(RightPadding);
            }

            var rect = GUILayoutUtility.GetRect(cardW, cardH, GUILayout.Width(cardW));
            EditorGUI.DrawRect(rect, ClrCardBg);

            // 色条
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 6, 3, rect.height - 12), cat.accent);

            // 图标
            var iconRect = new Rect(rect.x + 12, rect.y + 6, 20, 22);
            _styleCatCardIcon.normal.textColor = cat.accent;
            GUI.Label(iconRect, cat.icon, _styleCatCardIcon);

            // 名称
            var nameRect = new Rect(rect.x + 32, rect.y + 6, rect.width - 38, 18);
            GUI.Label(nameRect, cat.name, _styleCatCardName);

            // 数量
            var countRect = new Rect(rect.x + 32, rect.y + 26, rect.width - 38, 14);
            GUI.Label(countRect, $"{cat.tools.Count} 个工具", _styleCatCardCount);

            GUILayout.Space(spacing);
            col++;
        }

        while (col < cols && col > 0)
        {
            GUILayout.Space(cardW + spacing);
            col++;
        }

        EditorGUILayout.EndHorizontal();
    }
    #endregion

    #region 隐藏项管理面板
    private void DrawHiddenManagerPanel()
    {
        var area = new Rect(0, 0, position.width - LeftPanelWidth - SplitterWidth, position.height);
        Color accent = new Color(0.85f, 0.55f, 0.40f, 1f);

        // ── 渐变装饰条 ──────────────────────────────────
        DrawGradientRect(new Rect(0, 0, area.width, 4), accent,
            new Color(0.95f, 0.65f, 0.50f, 1f));

        EditorGUILayout.Space(16);

        // ── 标题区 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        bool shouldReturn = false;
        if (GUILayout.Button("← 返回", _styleBackButton, GUILayout.Width(48)))
        {
            _showHiddenManager = false;
            shouldReturn = true;
        }

        GUILayout.Space(8);
        GUILayout.Label("隐藏项管理", _styleRightTitle);
        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        if (shouldReturn) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.Label("管理被隐藏的分类与工具，可单独恢复或一键全部恢复", _styleRightSubtitle);
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(16);

        // ── 全局操作按钮 ──────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.FlexibleSpace();

        // 一键全部恢复
        var restoreAllContent = new GUIContent("  全部恢复");
        var restoreAllSize = _styleBtnPrimary.CalcSize(restoreAllContent);
        var restoreAllW = Mathf.Max(restoreAllSize.x + 24, 120);
        var restoreAllRect = GUILayoutUtility.GetRect(restoreAllW, 32, GUILayout.Width(restoreAllW), GUILayout.Height(32));
        bool restoreAllHover = restoreAllRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(restoreAllRect, restoreAllHover ? ClrBtnHover : ClrBtnNormal);
        if (GUI.Button(restoreAllRect, restoreAllContent, _styleBtnPrimary))
        {
            if (EditorUtility.DisplayDialog("确认", "确定要恢复所有隐藏项吗？", "确定", "取消"))
            {
                UnhideAllItems();
                DiscoverTools();
            }
        }

        GUILayout.Space(12);

        // 重置使用频率
        var resetUsageContent = new GUIContent("  重置使用频率");
        var resetUsageSize = _styleBtnPrimary.CalcSize(resetUsageContent);
        var resetUsageW = Mathf.Max(resetUsageSize.x + 24, 140);
        var resetUsageRect = GUILayoutUtility.GetRect(resetUsageW, 32, GUILayout.Width(resetUsageW), GUILayout.Height(32));
        bool resetUsageHover = resetUsageRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(resetUsageRect, resetUsageHover ? ClrTagBg : ClrCardBg);
        if (GUI.Button(resetUsageRect, resetUsageContent, new GUIStyle(_styleBtnPrimary)
        {
            normal = { textColor = ClrTextBright }
        }))
        {
            if (EditorUtility.DisplayDialog("确认", "确定要重置所有工具的使用频率统计吗？\n\n这将使排序恢复为默认优先级顺序。", "确定", "取消"))
            {
                ResetAllUsageStats();
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // ── 列表区 ──────────────────────────────────────
        _hiddenMgrScroll = EditorGUILayout.BeginScrollView(_hiddenMgrScroll);

        // ── 隐藏的分类 ──────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

        GUILayout.Label("隐藏的分类", _styleSectionHeader);
        GUILayout.Space(4);

        if (_hiddenItems.hiddenCategories.Count == 0)
        {
            GUILayout.Label("暂无隐藏的分类", _styleEmptyHint);
        }
        else
        {
            foreach (var catName in _hiddenItems.hiddenCategories.ToList())
            {
                DrawHiddenItemRow(catName, "分类", () =>
                {
                    ToggleCategoryHidden(catName);
                    Repaint();
                });
            }
        }

        GUILayout.Space(16);

        // ── 隐藏的工具 ──────────────────────────────────
        GUILayout.Label("隐藏的工具", _styleSectionHeader);
        GUILayout.Space(4);

        if (_hiddenItems.hiddenTools.Count == 0)
        {
            GUILayout.Label("暂无隐藏的工具", _styleEmptyHint);
        }
        else
        {
            foreach (var typeName in _hiddenItems.hiddenTools.ToList())
            {
                // 查找工具显示名
                _toolIndex.TryGetValue(typeName, out var tool);
                string displayName = tool != null ? tool.name : typeName;
                string displayDesc = tool != null ? tool.category : "(类型未找到)";
                DrawHiddenItemRow(displayName, displayDesc, () =>
                {
                    ToggleToolHidden(typeName);
                    Repaint();
                });
            }
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);
        EditorGUILayout.EndScrollView();

        // ── 使用提示 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        EditorGUILayout.HelpBox(
            "提示：\n" +
            "• 在左侧列表的分类标题或工具项上右键，可快速隐藏/取消隐藏。\n" +
            "• 隐藏的项不会显示在左侧列表中（搜索时仍可见）。\n" +
            "• 工具和分类按使用频率排序，使用越多越靠前。",
            MessageType.Info);
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(8);
    }

    /// <summary>绘制隐藏项行（名称 + 描述 + 恢复按钮）</summary>
    private void DrawHiddenItemRow(string name, string desc, Action onRestore)
    {
        var rect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);
        if (hover) EditorGUI.DrawRect(rect, ClrHover);

        // 左侧色点
        var dotRect = new Rect(rect.x + 8, rect.y + rect.height / 2 - 3, 6, 6);
        GUI.color = ClrTextDim;
        GUI.DrawTexture(dotRect, _texWhite, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        // 名称
        var nameRect = new Rect(rect.x + 20, rect.y + 2, rect.width - 120, 18);
        GUI.Label(nameRect, name, _styleHiddenItemName);

        // 描述
        var descRect = new Rect(rect.x + 20, rect.y + 18, rect.width - 120, 12);
        GUI.Label(descRect, desc, _styleHiddenItemDesc);

        // 恢复按钮
        var btnRect = new Rect(rect.xMax - 80, rect.y + 4, 72, 24);
        bool btnHover = btnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(btnRect, btnHover ? ClrBtnHover : ClrCardBg);
        CachedCenterLabel.normal.textColor = btnHover ? Color.white : ClrText;
        GUI.Label(btnRect, "恢复", CachedCenterLabel);
        if (GUI.Button(btnRect, "", GUIStyle.none))
        {
            onRestore?.Invoke();
        }
    }
    #endregion

    #region 创建工具表单
    private void DrawCreateToolForm()
    {
        var area = new Rect(0, 0, position.width - LeftPanelWidth - SplitterWidth, position.height);
        Color accent = new Color(0.35f, 0.75f, 0.45f, 1f);

        // ── 渐变装饰条 ──────────────────────────────────
        DrawGradientRect(new Rect(0, 0, area.width, 4), accent,
            new Color(0.45f, 0.85f, 0.55f, 1f));

        EditorGUILayout.Space(16);

        // ── 标题区 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        bool shouldReturn = false;
        if (GUILayout.Button("← 返回", _styleBackButton, GUILayout.Width(48)))
        {
            _showCreateForm = false;
            shouldReturn = true;
        }

        GUILayout.Space(8);
        GUILayout.Label("创建新工具", _styleRightTitle);
        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        if (shouldReturn) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.Label("根据模板快速创建新的 EditorWindow 工具，保存后自动出现在左侧列表", _styleRightSubtitle);
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(16);

        // ── 表单卡片 ──────────────────────────────────────
        _createScroll = EditorGUILayout.BeginScrollView(_createScroll);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var cardRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(cardRect, ClrCardBg);
        EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y + 8, 3, cardRect.height - 16), accent);

        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        EditorGUILayout.BeginVertical();

        // ── 基本信息 ──────────────────────────────────────
        GUILayout.Label("基本信息", _styleSectionHeader);
        GUILayout.Space(6);

        DrawFormField("工具名称", ref _createToolName, "显示在 Hub 左侧列表中的名称");

        // 类名自动推导
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("类名", GUILayout.Width(60));
        _createClassName = EditorGUILayout.TextField(_createClassName, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        {
            var autoName = DeriveClassName(_createToolName);
            if (_createClassName == _lastAutoClassName && autoName != _lastAutoClassName)
            {
                _createClassName = autoName;
            }
            _lastAutoClassName = autoName;
        }
        GUILayout.Space(1);
        GUILayout.Label("C# 类名（自动从工具名生成，可手动修改）", CachedTooltipStyle);
        GUILayout.Space(4);

        GUILayout.Space(10);

        // ── 目录路径（带浏览按钮）──────────────────────
        GUILayout.Label("保存路径", _styleSectionHeader);
        GUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        _createDirectory = EditorGUILayout.TextField(_createDirectory, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("浏览...", GUILayout.Width(60)))
        {
            var selected = EditorUtility.OpenFolderPanel("选择保存目录", _createDirectory, "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    _createDirectory = "Assets" + selected.Substring(Application.dataPath.Length);
                else
                    _createDirectory = selected;
            }
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(2);
        EditorGUILayout.HelpBox("默认保存到 Assets/Editor/Tools/，可修改为其他目录", MessageType.None);

        GUILayout.Space(10);

        // ── 分类与图标 ────────────────────────────────────
        GUILayout.Label("分类设置", _styleSectionHeader);
        GUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("分类", GUILayout.Width(60));
        var catNames = _categoryColors.Keys.ToList();
        if (!catNames.Contains(_createCategory)) catNames.Add(_createCategory);
        int catIdx = catNames.IndexOf(_createCategory);
        catIdx = EditorGUILayout.Popup(catIdx, catNames.ToArray());
        _createCategory = catNames[catIdx];
        GUILayout.Space(16);
        GUILayout.Label("图标", GUILayout.Width(36));
        _createIcon = EditorGUILayout.TextField(_createIcon, GUILayout.Width(60));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        // ── 描述 ──────────────────────────────────────────
        GUILayout.Label("功能描述", _styleSectionHeader);
        GUILayout.Space(4);
        _createDescription = EditorGUILayout.TextArea(_createDescription,
            new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Height(80));

        GUILayout.Space(10);

        // ── 标签 ──────────────────────────────────────────
        DrawFormField("搜索标签", ref _createTags, "多个标签用逗号分隔，如: 动画, 帧动画, Sprite");

        GUILayout.Space(10);

        // ── 默认快捷键 ────────────────────────────────────
        GUILayout.Label("默认快捷键", _styleSectionHeader);
        GUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("快捷键", GUILayout.Width(60));
        _createShortcut = EditorGUILayout.TextField(_createShortcut, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("可选，如 Ctrl+Shift+T。留空则不设默认快捷键，用户可在工具详情页自行设置。",
            CachedTooltipStyle);
        GUILayout.Space(4);

        EditorGUILayout.EndVertical();
        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // ── 操作按钮 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.FlexibleSpace();

        // 取消按钮
        var cancelContent = new GUIContent("  取消");
        var cancelSize = _styleBtnPrimary.CalcSize(cancelContent);
        var cancelW = Mathf.Max(cancelSize.x + 24, 100);
        var cancelRect = GUILayoutUtility.GetRect(cancelW, 36, GUILayout.Width(cancelW), GUILayout.Height(36));
        bool cancelHover = cancelRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(cancelRect, cancelHover ? ClrTagBg : ClrCardBg);
        if (GUI.Button(cancelRect, cancelContent, _styleBtnPrimary))
        {
            _showCreateForm = false;
        }

        GUILayout.Space(12);

        // 创建按钮
        var createContent = new GUIContent("  创建工具");
        var createBtnSize = _styleBtnPrimary.CalcSize(createContent);
        var createBtnW = Mathf.Max(createBtnSize.x + 32, 140);
        var createBtnH = 36f;
        var createBtnRect2 = GUILayoutUtility.GetRect(createBtnW, createBtnH, GUILayout.Width(createBtnW), GUILayout.Height(createBtnH));
        bool createBtnHover = createBtnRect2.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(createBtnRect2, createBtnHover ? ClrBtnHover : ClrBtnNormal);
        if (GUI.Button(createBtnRect2, createContent, _styleBtnPrimary))
        {
            CreateToolFile();
        }

        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // ── 已有分类参考 ──────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        var refRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(refRect, new Color(0.15f, 0.15f, 0.16f, 0.5f));
        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        EditorGUILayout.BeginVertical();
        GUILayout.Label("已有分类参考", _styleSectionHeader);
        GUILayout.Space(4);
        string allCategories = string.Join("  •  ", _categoryColors.Keys);
        _cachedDimLabel ??= new GUIStyle()
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(0, 0, 0, 0)
        };
        GUILayout.Label(allCategories, _cachedDimLabel);
        EditorGUILayout.EndVertical();
        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);
        EditorGUILayout.EndVertical();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);
        EditorGUILayout.EndScrollView();
    }

    private void DrawFormField(string label, ref string value, string tooltip)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(60));
        value = EditorGUILayout.TextField(value, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label(tooltip, CachedTooltipStyle);
        GUILayout.Space(4);
    }
    #endregion

    #region 工具详情页
    private void DrawToolDetailPanel(ToolEntry tool)
    {
        var area = new Rect(0, 0, position.width - LeftPanelWidth - SplitterWidth, position.height);
        var cat = _selectedCategory;
        Color accent = cat != null ? cat.accent : ClrAccent;

        // ── 渐变头部装饰条 ──────────────────────────────
        var gradRect = new Rect(0, 0, area.width, 4);
        DrawGradientRect(gradRect, accent, new Color(
            Mathf.Min(1f, accent.r + 0.15f),
            Mathf.Min(1f, accent.g + 0.15f),
            Mathf.Min(1f, accent.b + 0.15f), 1f));

        EditorGUILayout.Space(16);

        // ── 返回按钮 + 分类标签 ────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        bool shouldReturn = false;
        if (GUILayout.Button("← 返回", _styleBackButton, GUILayout.Width(48)))
        {
            _selectedTool = null;
            _selectedCategory = null;
            shouldReturn = true;
        }

        GUILayout.Space(8);

        // 分类色点 + 分类名
        var catDotRect = GUILayoutUtility.GetRect(8, 8, GUILayout.Width(8));
        catDotRect.y += 3;
        GUI.color = accent;
        GUI.DrawTexture(catDotRect, _texWhite, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        GUILayout.Space(4);
        GUILayout.Label(tool.category, _styleRightSubtitle);

        var detailShortcut = GetEffectiveShortcut(tool.typeName);
        if (detailShortcut.IsValid)
        {
            var kbStr = detailShortcut.ToString();
            var kbContent = new GUIContent(kbStr);
            var kbSize = _styleShortcut.CalcSize(kbContent);
            GUILayout.FlexibleSpace();
            var kbRect = GUILayoutUtility.GetRect(kbSize.x + 16, 18, GUILayout.Width(kbSize.x + 16));
            EditorGUI.DrawRect(kbRect, ClrTagBg);
            GUI.Label(kbRect, kbStr, _styleShortcut);
        }

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        if (shouldReturn) return;

        EditorGUILayout.Space(6);

        // ── 工具名称 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.Label(tool.name, _styleRightTitle);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(12);

        // ── 功能标签 ──────────────────────────────────────
        if (tool.tags != null && tool.tags.Length > 0)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);

            foreach (var tag in tool.tags)
            {
                var tagContent = new GUIContent(tag);
                var tagSize = _styleTag.CalcSize(tagContent);
                var tagRect = GUILayoutUtility.GetRect(tagSize.x + 16, tagSize.y + 6,
                    GUILayout.Width(tagSize.x + 16));
                EditorGUI.DrawRect(tagRect, ClrTagBg);
                GUI.Label(tagRect, tag, _styleTag);
                GUILayout.Space(6);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(12);
        }

        // ── 描述卡片 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var descRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(descRect, ClrCardBg);

        // 卡片左侧色条
        EditorGUI.DrawRect(new Rect(descRect.x, descRect.y + 8, 3, descRect.height - 16), accent);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("功能说明", _styleSectionHeader);
        GUILayout.Space(2);
        GUILayout.Label(tool.description, _styleDescription);
        EditorGUILayout.EndVertical();

        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);

        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // ── 主操作按钮 ────────────────────────────────────
        if (!string.IsNullOrEmpty(tool.typeName))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);
            GUILayout.FlexibleSpace();

            var btnContent = new GUIContent($"  打开 {tool.name}");
            var btnSize = _styleBtnPrimary.CalcSize(btnContent);
            var btnW = Mathf.Max(btnSize.x + 32, 200);
            var btnH = 40f;
            var btnRect = GUILayoutUtility.GetRect(btnW, btnH, GUILayout.Width(btnW), GUILayout.Height(btnH));

            bool isHover = btnRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(btnRect, isHover ? ClrBtnHover : ClrBtnNormal);

            if (GUI.Button(btnRect, btnContent, _styleBtnPrimary))
            {
                RecordToolUsage(tool);
                OpenToolWindow(tool.typeName);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(RightPadding);
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);
            EditorGUILayout.HelpBox("此工具为菜单项集合，请通过 UnityToolsHub 菜单访问。", MessageType.Info);
            GUILayout.Space(RightPadding);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(20);

        // ── 快捷操作 ──────────────────────────────────────
        DrawQuickActionsSection(tool, accent);
    }

    private void DrawQuickActionsSection(ToolEntry tool, Color accent)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var sectionRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(sectionRect, new Color(0.15f, 0.15f, 0.16f, 0.5f));

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("快捷操作", _styleSectionHeader);
        GUILayout.Space(6);

        DrawFlatButton("打开此工具", () =>
        {
            RecordToolUsage(tool);
            OpenToolWindow(tool.typeName);
        });

        if (tool.typeName == "UnityFrameworkInitWindow" || tool.typeName == "EditorWindowLauncher")
        {
            DrawFlatButton("打开面板管理器", () => OpenToolWindow("EditorWindowLauncher"));
        }

        if (tool.typeName == "AssetImportFilter")
        {
            DrawFlatButton("打开面板管理器", () => OpenToolWindow("EditorWindowLauncher"));
        }

        EditorGUILayout.EndVertical();

        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);

        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        // ── 快捷键设置区域 ──────────────────────────────
        DrawShortcutSettingsSection(tool, accent);
    }

    private void DrawFlatButton(string text, Action onClick)
    {
        var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);

        if (hover)
            EditorGUI.DrawRect(rect, ClrHover);

        // 左侧色点
        var dotRect = new Rect(rect.x + 4, rect.y + rect.height / 2 - 3, 6, 6);
        GUI.color = ClrAccent;
        GUI.DrawTexture(dotRect, _texWhite, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        var labelRect = new Rect(rect.x + 16, rect.y, rect.width - 16, rect.height);
        GUI.Label(labelRect, text, _styleBtnFlat);

        if (GUI.Button(rect, "", GUIStyle.none))
        {
            onClick?.Invoke();
        }
    }
    #endregion

    #region 快捷键设置 UI
    /// <summary>快捷键设置区域（录制、显示、清除）</summary>
    private void DrawShortcutSettingsSection(ToolEntry tool, Color accent)
    {
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var sectionRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(sectionRect, new Color(0.15f, 0.15f, 0.16f, 0.5f));

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("⌨  快捷键设置", _styleSectionHeader);
        GUILayout.Space(4);

        var currentShortcut = GetEffectiveShortcut(tool.typeName);
        bool isRecordingThis = _isRecordingShortcut && _recordingForTypeName == tool.typeName;

        if (isRecordingThis)
        {
            DrawRecordingPrompt(accent);
        }
        else if (currentShortcut.IsValid)
        {
            DrawShortcutDisplay(currentShortcut, accent, tool.typeName);
        }
        else
        {
            DrawSetShortcutButton(tool.typeName, accent);
        }

        EditorGUILayout.EndVertical();

        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);

        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();
    }

    private static GUIStyle CachedBtnFlatSmall
        => _cachedBtnFlatSmall ?? (_cachedBtnFlatSmall = new GUIStyle(Styles.BtnFlat) { fontSize = 10 });

    private static GUIStyle CachedBtnFlatSmallCenter
        => _cachedBtnFlatSmallCenter ?? (_cachedBtnFlatSmallCenter = new GUIStyle(Styles.BtnFlat)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter
        });

    /// <summary>绘制"录制中"提示</summary>
    private void DrawRecordingPrompt(Color accent)
    {
        float pulse = (Mathf.Sin((float)(EditorApplication.timeSinceStartup * 4.0)) + 1f) * 0.5f;
        var promptRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));

        var borderColor = Color.Lerp(accent, new Color(accent.r, accent.g, accent.b, 0.3f), pulse);
        EditorGUI.DrawRect(new Rect(promptRect.x, promptRect.y, promptRect.width, 2), borderColor);
        EditorGUI.DrawRect(new Rect(promptRect.x, promptRect.y + promptRect.height - 2, promptRect.width, 2), borderColor);
        EditorGUI.DrawRect(new Rect(promptRect.x, promptRect.y, 2, promptRect.height), borderColor);
        EditorGUI.DrawRect(new Rect(promptRect.x + promptRect.width - 2, promptRect.y, 2, promptRect.height), borderColor);

        var labelRect = new Rect(promptRect.x + 12, promptRect.y + 4, promptRect.width - 24, promptRect.height - 8);
        GUI.Label(labelRect, "⏺ 正在录制快捷键...  请按下组合键\n   按 Esc 取消", new GUIStyle()
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = accent },
            richText = true,
            wordWrap = true
        });

        var cancelRect = new Rect(promptRect.xMax - 60, promptRect.y + 6, 48, 28);
        bool cancelHover = cancelRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(cancelRect, cancelHover ? new Color(0.6f, 0.2f, 0.2f, 0.8f) : new Color(0.4f, 0.15f, 0.15f, 0.6f));
        GUI.Label(cancelRect, "取消", new GUIStyle()
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        });
        if (GUI.Button(cancelRect, "", GUIStyle.none))
        {
            CancelRecording();
        }
    }

    private static GUIStyle CachedDimSmall
        => _cachedDimLabel ?? (_cachedDimLabel = new GUIStyle()
        {
            fontSize = 10,
            normal = { textColor = ClrTextDim },
            wordWrap = true
        });

    /// <summary>绘制已设置的快捷键（含重新设置和清除按钮）</summary>
    private void DrawShortcutDisplay(ShortcutBinding binding, Color accent, string typeName)
    {
        EditorGUILayout.BeginHorizontal();

        DrawKeyCaps(binding, accent);

        GUILayout.Space(12);

        EditorGUILayout.BeginVertical();
        GUILayout.Space(4);
        GUILayout.Label("在 Hub 面板中按下此快捷键可快速选中该工具", CachedDimSmall);
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        // 重新设置按钮
        var resetContent = new GUIContent("  重新设置");
        var resetSize = _styleShortcut.CalcSize(resetContent);
        var resetW = resetSize.x + 16;
        var resetRect = GUILayoutUtility.GetRect(resetW, 24, GUILayout.Width(resetW));
        bool resetHover = resetRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(resetRect, resetHover ? ClrHover : new Color(0, 0, 0, 0));
        GUI.Label(resetRect, resetContent, CachedBtnFlatSmallCenter);
        if (GUI.Button(resetRect, GUIContent.none, _styleInvisibleBtn))
        {
            StartRecording(typeName);
        }

        GUILayout.Space(8);

        // 清除按钮
        var clearContent = new GUIContent("  清除");
        var clearSize = _styleShortcut.CalcSize(clearContent);
        var clearW = clearSize.x + 16;
        var clearRect = GUILayoutUtility.GetRect(clearW, 24, GUILayout.Width(clearW));
        bool clearHover = clearRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(clearRect, clearHover ? new Color(0.6f, 0.2f, 0.2f, 0.3f) : new Color(0, 0, 0, 0));
        CachedBtnFlatSmallCenter.normal.textColor = new Color(0.85f, 0.35f, 0.35f);
        GUI.Label(clearRect, clearContent, CachedBtnFlatSmallCenter);
        if (GUI.Button(clearRect, GUIContent.none, _styleInvisibleBtn))
        {
            ClearShortcut(typeName);
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>绘制"设置快捷键"按钮</summary>
    private void DrawSetShortcutButton(string typeName, Color accent)
    {
        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("暂未设置快捷键", new GUIStyle()
        {
            fontSize = 11,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(0, 0, 6, 0)
        });

        GUILayout.FlexibleSpace();

        var btnContent = new GUIContent("  ⌨  设置快捷键");
        var btnSize = _styleShortcut.CalcSize(btnContent);
        var btnW = btnSize.x + 24;
        var btnRect = GUILayoutUtility.GetRect(btnW, 28, GUILayout.Width(btnW));
        bool btnHover = btnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(btnRect, btnHover ? ClrBtnHover : ClrBtnNormal);
        _cachedHintStyle ??= new GUIStyle(Styles.BtnFlat)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
            hover = { textColor = Color.white }
        };
        GUI.Label(btnRect, btnContent, _cachedHintStyle);
        if (GUI.Button(btnRect, "", GUIStyle.none))
        {
            StartRecording(typeName);
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>绘制快捷键键帽（视觉样式：圆角方块模拟键盘按键）</summary>
    private void DrawKeyCaps(ShortcutBinding binding, Color accent)
    {
        var keys = new string[4];
        int keyCount = 0;
        if (binding.ctrl)  keys[keyCount++] = "Ctrl";
        if (binding.alt)   keys[keyCount++] = "Alt";
        if (binding.shift) keys[keyCount++] = "Shift";
        keys[keyCount++] = ShortcutBinding.KeyDisplay(binding.key);

        float totalWidth = 0f;
        var keyWidths = new float[keyCount];

        for (int i = 0; i < keyCount; i++)
        {
            var content = new GUIContent(keys[i]);
            var size = _styleKeyCap.CalcSize(content);
            float w = size.x + 12;
            keyWidths[i] = w;
            totalWidth += w;
        }
        totalWidth += (keyCount - 1) * 4;

        var startRect = GUILayoutUtility.GetRect(totalWidth, 28, GUILayout.Width(totalWidth));
        float x = startRect.x;

        for (int i = 0; i < keyCount; i++)
        {
            var keyRect = new Rect(x, startRect.y + 2, keyWidths[i], 24);

            var bgColor = i < keyCount - 1 ? ClrTagBg : accent;
            EditorGUI.DrawRect(keyRect, bgColor);
            EditorGUI.DrawRect(new Rect(keyRect.x, keyRect.y + keyRect.height - 1, keyRect.width, 1),
                new Color(0, 0, 0, 0.4f));
            GUI.Label(keyRect, keys[i], _styleKeyCap);

            x += keyWidths[i] + 4;
        }
    }
    #endregion
}
#endif
