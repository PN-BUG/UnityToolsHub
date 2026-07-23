#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    private static bool _isDragOverPanel;

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
            normal = { textColor = Theme.ClrText }
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
            Theme.ClrRightBg);

        // ── 第三方管理面板不使用 ScrollView（内部自带滚动）──
        if (_showThirdPartyManager && !_showHiddenManager)
        {
            DrawAddToolToggleBar();
            DrawThirdPartyManagerPanel();
        }
        // ── 添加工具面板不使用 ScrollView，否则 Layout 阶段会吞掉拖放事件 ──
        else if (_showAddToolPanel && !_showHiddenManager)
        {
            // 无 ScrollView 路径：toggle 栏 + 添加工具面板，确保拖放事件不被拦截
            DrawAddToolToggleBar();
            DrawAddToolPanel();
        }
        else
        {
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            if (_showHiddenManager)
                DrawHiddenManagerPanel();
            else if (_showCreateForm)
                DrawCreateOrAddToolPanel();
            else if (_selectedTool == null)
                DrawWelcomePanel();
            else
                DrawToolDetailPanel(_selectedTool);

            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Unity 最简原生拖放方案，无条件在 OnGUI 最前面执行。
    /// </summary>
    private void HandleDragAndDrop()
    {
        var evt = Event.current;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var csFiles = DragAndDrop.paths
                    .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (csFiles.Count > 0)
                {
                    _pendingDropPaths = new List<string>(csFiles);
                    _showAddToolPanel = true;
                    _showCreateForm = false;
                    _showThirdPartyManager = false;
                    _showHiddenManager = false;
                    Debug.Log($"[ToolsHub] 拖入 {csFiles.Count} 个 .cs 文件");
                }
            }
            evt.Use();
        }
    }

    /// <summary>
    /// 处理拖放的文件列表，解析 EditorWindow 子类并加入候选
    /// </summary>
    private void ProcessDroppedFiles(List<string> paths)
    {
        int totalCount = 0;
        int rejectedCs = 0;
        int addedCount = 0;

        foreach (var dragged in paths)
        {
            if (!dragged.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            totalCount++;

            string relPath = dragged;
            if (dragged.StartsWith(Application.dataPath))
                relPath = "Assets" + dragged.Substring(Application.dataPath.Length);
            relPath = relPath.Replace('\\', '/');

            string absPath = Path.GetFullPath(dragged);
            if (!File.Exists(absPath)) { rejectedCs++; continue; }
            string src = File.ReadAllText(absPath);

            if (src.Contains("[ToolInfo")) { rejectedCs++; continue; }

            var match = Regex.Match(src,
                @"(?:public|internal|private|protected)?\s*(?:partial\s+)?class\s+(\w+)\s*:\s*(\w[\w.,\s]*)");
            if (!match.Success) { rejectedCs++; continue; }

            string className = match.Groups[1].Value;
            string baseClasses = match.Groups[2].Value.Trim();
            bool isEditorWindow = baseClasses.Split(',').Any(b => b.Trim() == "EditorWindow");
            if (!isEditorWindow) { rejectedCs++; continue; }

            var nsMatch = Regex.Match(src, @"namespace\s+([\w.]+)");
            string ns = nsMatch.Success ? nsMatch.Groups[1].Value : "";

            bool alreadyExists = _addToolCandidates.Any(c =>
                c.className == className && c.filePath == relPath);
            if (alreadyExists) continue;

            _addToolCandidates.Add(new AddToolCandidate
            {
                filePath = relPath,
                absPath = absPath,
                className = className,
                baseClass = "EditorWindow",
                namespaceName = ns,
                fullTypeName = string.IsNullOrEmpty(ns) ? className : $"{ns}.{className}",
                existingDescription = ExtractFileHeaderComment(src)
            });
            _addToolSelectedIndex = _addToolCandidates.Count - 1;
            FillAddToolFormFromCandidate(_addToolSelectedIndex);
            addedCount++;
        }

        if (addedCount > 0)
            _addToolScanError = "";
        else if (totalCount == 0)
            _addToolScanError = "未拖入 .cs 文件";
        else if (rejectedCs > 0)
            _addToolScanError = $"拖入 {totalCount} 个 .cs 文件均不符合要求（非 EditorWindow 子类或已有 [ToolInfo]）";
    }
    #endregion

    #region 创建/添加工具统一入口（含顶部 toggle）
    /// <summary>
    /// 创建工具与添加工具共用右侧面板，顶部 toggle 切换
    /// </summary>
    private void DrawCreateOrAddToolPanel()
    {
        // ── 顶部 toggle 栏 ─────────────────────────────
        DrawAddToolToggleBar();

        // ── 分隔线 ─────────────────────────────────────
        var divRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(divRect, Theme.ClrDivider);

        // ── 内容区 ─────────────────────────────────────
        if (_showCreateForm)
            DrawCreateToolForm();
        else if (_showAddToolPanel)
            DrawAddToolPanel();
    }

    /// <summary>
    /// 绘制创建/添加/第三方工具的顶部 toggle 栏（独立方法，供 ScrollView 和非 ScrollView 路径共用）
    /// </summary>
    private void DrawAddToolToggleBar()
    {
        var toggleBarRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(toggleBarRect, new Color(0.13f, 0.13f, 0.14f, 1f));

        float toggleW = 108f;
        float gap = 4f;
        float toggleH = 26f;
        float toggleY = toggleBarRect.y + (toggleBarRect.height - toggleH) / 2f;
        float totalW = toggleW * 3 + gap * 2;
        float startX = toggleBarRect.x + (toggleBarRect.width - totalW) / 2f;

        // ── 创建工具 toggle ──
        var createRect = new Rect(startX, toggleY, toggleW, toggleH);
        bool createActive = _showCreateForm && !_showThirdPartyManager;
        bool createHover = createRect.Contains(Event.current.mousePosition);
        Color createAccent = new Color(0.35f, 0.75f, 0.45f, 1f);
        if (createActive)
        {
            EditorGUI.DrawRect(createRect, Theme.ClrSelection);
            EditorGUI.DrawRect(new Rect(createRect.x, createRect.yMax - 2, createRect.width, 2), createAccent);
        }
        else if (createHover)
        {
            EditorGUI.DrawRect(createRect, Theme.ClrHover);
        }
        Styles.MiniLabelBoldCenter.fontStyle = createActive ? FontStyle.Bold : FontStyle.Normal;
        Styles.MiniLabelBoldCenter.normal.textColor = createActive ? Theme.ClrTextBright : Theme.ClrTextDim;
        GUI.Label(createRect, "＋ 创建工具", Styles.MiniLabelBoldCenter);
        if (GUI.Button(createRect, "", GUIStyle.none))
        {
            _showCreateForm = true;
            _showAddToolPanel = false;
            _showThirdPartyManager = false;
            _showHiddenManager = false;
            _selectedTool = null;
            _rightScroll = Vector2.zero;
        }

        // ── 添加工具 toggle ──
        var addRect = new Rect(startX + toggleW + gap, toggleY, toggleW, toggleH);
        bool addActive = _showAddToolPanel && !_showThirdPartyManager;
        bool addHover = addRect.Contains(Event.current.mousePosition);
        Color addAccent = new Color(0.40f, 0.65f, 0.90f, 1f);
        if (addActive)
        {
            EditorGUI.DrawRect(addRect, Theme.ClrSelection);
            EditorGUI.DrawRect(new Rect(addRect.x, addRect.yMax - 2, addRect.width, 2), addAccent);
        }
        else if (addHover)
        {
            EditorGUI.DrawRect(addRect, Theme.ClrHover);
        }
        Styles.MiniLabelBoldCenter.fontStyle = addActive ? FontStyle.Bold : FontStyle.Normal;
        Styles.MiniLabelBoldCenter.normal.textColor = addActive ? Theme.ClrTextBright : Theme.ClrTextDim;
        GUI.Label(addRect, "⊕ 添加工具", Styles.MiniLabelBoldCenter);
        if (GUI.Button(addRect, "", GUIStyle.none))
        {
            _showAddToolPanel = true;
            _showCreateForm = false;
            _showThirdPartyManager = false;
            _showHiddenManager = false;
            _selectedTool = null;
            _rightScroll = Vector2.zero;
        }

        // ── 第三方工具 toggle ──
        var tpRect = new Rect(startX + (toggleW + gap) * 2, toggleY, toggleW, toggleH);
        bool tpActive = _showThirdPartyManager;
        bool tpHover = tpRect.Contains(Event.current.mousePosition);
        Color tpAccent = new Color(0.80f, 0.55f, 0.90f, 1f);
        if (tpActive)
        {
            EditorGUI.DrawRect(tpRect, Theme.ClrSelection);
            EditorGUI.DrawRect(new Rect(tpRect.x, tpRect.yMax - 2, tpRect.width, 2), tpAccent);
        }
        else if (tpHover)
        {
            EditorGUI.DrawRect(tpRect, Theme.ClrHover);
        }
        Styles.MiniLabelBoldCenter.fontStyle = tpActive ? FontStyle.Bold : FontStyle.Normal;
        Styles.MiniLabelBoldCenter.normal.textColor = tpActive ? Theme.ClrTextBright : Theme.ClrTextDim;
        int tpCount = _thirdPartyRegistry.tools.Count;
        GUI.Label(tpRect, tpCount > 0 ? $"📦 第三方 ({tpCount})" : "📦 第三方工具", Styles.MiniLabelBoldCenter);
        if (GUI.Button(tpRect, "", GUIStyle.none))
        {
            _showThirdPartyManager = true;
            _showCreateForm = false;
            _showAddToolPanel = false;
            _showHiddenManager = false;
            _selectedTool = null;
            _rightScroll = Vector2.zero;
        }
    }
    #endregion

    #region 欢迎页
    private void DrawWelcomePanel()
    {
        var area = new Rect(0, 0, position.width - LeftPanelWidth - SplitterWidth, position.height);

        // ── 渐变装饰条（更宽，带光泽过渡）──────────────────
        var gradRect = new Rect(area.x, area.y, area.width, 5);
        Drawing.DrawGradientRect(gradRect, new Color(0.345f, 0.569f, 0.910f), new Color(0.400f, 0.529f, 0.729f));

        // ── 主内容 ──────────────────────────────────────
        float centerY = area.height * 0.22f;

        // 标题
        var titleRect = new Rect(area.x + RightPadding, centerY, area.width - RightPadding * 2, 42);
        GUI.Label(titleRect,
            "<color=#5891E8>Unity</color><color=#F2F2F9>ToolsHub</color>",
            Styles.WelcomeTitle);

        // 副标题
        var subRect = new Rect(area.x + RightPadding, centerY + 48, area.width - RightPadding * 2, 22);
        GUI.Label(subRect, "游戏开发工具集 · 一站式编辑器扩展管理", Styles.WelcomeSub);

        EditorGUILayout.Space(centerY + 82);

        // ── 统计卡片 ──────────────────────────────────────
        int totalTools = _totalToolCount;
        int totalCategories = _categories.Count;

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        DrawStatCard("工具总数", totalTools.ToString(), Theme.ClrAccent,
            GUILayout.Width(110), GUILayout.Height(64));
        GUILayout.Space(20);
        DrawStatCard("分类数", totalCategories.ToString(), Palette.Success,
            GUILayout.Width(110), GUILayout.Height(64));
        GUILayout.Space(20);
        DrawStatCard("快捷键", "Ctrl+Shift+E", Palette.Warning,
            GUILayout.Width(150), GUILayout.Height(64));

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(36);

        // ── 提示 ──────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("← 从左侧选择一个工具开始使用", Styles.WelcomeSub);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // ── 仓库链接 ──────────────────────────────────────
        EditorGUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        var repoStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            richText = true,
            fontSize = 11,
            normal = { textColor = Theme.ClrTextDim },
            hover = { textColor = Theme.ClrAccent }
        };
        var repoRect = GUILayoutUtility.GetRect(new GUIContent("📦 GitHub 仓库"), repoStyle);
        bool repoHover = repoRect.Contains(Event.current.mousePosition);
        if (repoHover)
        {
            repoStyle.normal.textColor = Theme.ClrAccent;
            EditorGUI.DrawRect(new Rect(repoRect.x, repoRect.yMax - 1, repoRect.width, 1), Theme.ClrAccent);
        }
        GUI.Label(repoRect, "📦 GitHub 仓库", repoStyle);
        if (repoHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            Application.OpenURL("https://github.com/PN-BUG/UnityToolsHub");
            Event.current.Use();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // ── 分类概览 ──────────────────────────────────────
        EditorGUILayout.Space(28);
        DrawCategoryOverview();
    }

    private void DrawStatCard(string label, string value, Color accent, params GUILayoutOption[] options)
    {
        var rect = GUILayoutUtility.GetRect(0, 64, options);
        // 圆角背景
        Drawing.DrawRoundedRect(rect, Theme.ClrCardBg, 8f);
        // 顶部色条
        var barRect = new Rect(rect.x + 10, rect.y + 3, rect.width - 20, 3);
        EditorGUI.DrawRect(barRect, accent);

        // 数值 — 长文本用更小字号
        bool isLongText = value.Length > 5;
        Styles.StatNum.fontSize = isLongText ? 15 : 22;
        Styles.StatNum.normal.textColor = accent;
        var numRect = new Rect(rect.x + 4, rect.y + 12, rect.width - 8, 30);
        GUI.Label(numRect, value, Styles.StatNum);
        // 恢复字号
        Styles.StatNum.fontSize = 22;
        // 标签
        var lblRect = new Rect(rect.x, rect.y + 42, rect.width, 16);
        GUI.Label(lblRect, label, Styles.StatLabel);
    }

    private void DrawCategoryOverview()
    {
        var areaWidth = position.width - LeftPanelWidth - SplitterWidth - RightPadding * 2;
        float cardW = Mathf.Min(170, (areaWidth - 12 * 3) / 4);
        float cardH = 52;
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
            bool hover = rect.Contains(Event.current.mousePosition);

            // 圆角背景
            Drawing.DrawRoundedRect(rect, hover ? Palette.ItemHover : Theme.ClrCardBg, 6f);

            // 左侧色条
            var barRect = new Rect(rect.x + 6, rect.y + 8, 3, rect.height - 16);
            EditorGUI.DrawRect(barRect, cat.accent);

            // 图标
            var iconRect = new Rect(rect.x + 14, rect.y + 7, 20, 22);
            Styles.CatCardIcon.normal.textColor = cat.accent;
            GUI.Label(iconRect, cat.icon, Styles.CatCardIcon);

            // 名称
            var nameRect = new Rect(rect.x + 34, rect.y + 7, rect.width - 40, 18);
            GUI.Label(nameRect, cat.name, Styles.CatCardName);

            // 数量
            var countRect = new Rect(rect.x + 34, rect.y + 27, rect.width - 40, 14);
            GUI.Label(countRect, $"{cat.tools.Count} 个工具", Styles.CatCardCount);

            // 点击选中分类
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _selectedCategory = cat;
                _selectedTool = null;
                _showCreateForm = false;
                _showAddToolPanel = false;
                _showThirdPartyManager = false;
                _showHiddenManager = false;
                Event.current.Use();
            }

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
        Drawing.DrawGradientRect(new Rect(0, 0, area.width, 4), accent,
            new Color(0.95f, 0.65f, 0.50f, 1f));

        EditorGUILayout.Space(16);

        // ── 标题区 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        bool shouldReturn = false;
        if (GUILayout.Button("← 返回", Styles.BackButton, GUILayout.Width(48)))
        {
            _showHiddenManager = false;
            shouldReturn = true;
        }

        GUILayout.Space(8);
        GUILayout.Label("隐藏项管理", Styles.RightTitle);
        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        if (shouldReturn) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.Label("管理被隐藏的分类与工具，可单独恢复或一键全部恢复", Styles.RightSubtitle);
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(16);

        // ── 全局操作按钮 ──────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.FlexibleSpace();

        // 一键全部恢复
        var restoreAllContent = new GUIContent("  全部恢复");
        var restoreAllSize = Styles.BtnPrimary.CalcSize(restoreAllContent);
        var restoreAllW = Mathf.Max(restoreAllSize.x + 24, 120);
        var restoreAllRect = GUILayoutUtility.GetRect(restoreAllW, 32, GUILayout.Width(restoreAllW), GUILayout.Height(32));
        bool restoreAllHover = restoreAllRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(restoreAllRect, restoreAllHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal);
        if (GUI.Button(restoreAllRect, restoreAllContent, Styles.BtnPrimary))
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
        var resetUsageSize = Styles.BtnPrimary.CalcSize(resetUsageContent);
        var resetUsageW = Mathf.Max(resetUsageSize.x + 24, 140);
        var resetUsageRect = GUILayoutUtility.GetRect(resetUsageW, 32, GUILayout.Width(resetUsageW), GUILayout.Height(32));
        bool resetUsageHover = resetUsageRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(resetUsageRect, resetUsageHover ? Theme.ClrTagBg : Theme.ClrCardBg);
        if (GUI.Button(resetUsageRect, resetUsageContent, new GUIStyle(Styles.BtnPrimary)
        {
            normal = { textColor = Theme.ClrTextBright }
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

        GUILayout.Label("隐藏的分类", Styles.SectionHeader);
        GUILayout.Space(4);

        if (_hiddenItems.hiddenCategories.Count == 0)
        {
            GUILayout.Label("暂无隐藏的分类", Styles.EmptyHint);
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
        GUILayout.Label("隐藏的工具", Styles.SectionHeader);
        GUILayout.Space(4);

        if (_hiddenItems.hiddenTools.Count == 0)
        {
            GUILayout.Label("暂无隐藏的工具", Styles.EmptyHint);
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
        if (hover) EditorGUI.DrawRect(rect, Theme.ClrHover);

        // 左侧色点
        var dotRect = new Rect(rect.x + 8, rect.y + rect.height / 2 - 3, 6, 6);
        GUI.color = Theme.ClrTextDim;
        GUI.DrawTexture(dotRect, Styles.TexWhite, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        // 名称
        var nameRect = new Rect(rect.x + 20, rect.y + 2, rect.width - 120, 18);
        GUI.Label(nameRect, name, Styles.HiddenItemName);

        // 描述
        var descRect = new Rect(rect.x + 20, rect.y + 18, rect.width - 120, 12);
        GUI.Label(descRect, desc, Styles.HiddenItemDesc);

        // 恢复按钮
        var btnRect = new Rect(rect.xMax - 80, rect.y + 4, 72, 24);
        bool btnHover = btnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(btnRect, btnHover ? Theme.ClrBtnHover : Theme.ClrCardBg);
        CachedCenterLabel.normal.textColor = btnHover ? Color.white : Theme.ClrText;
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
        Color accent = new Color(0.35f, 0.75f, 0.45f, 1f);

        // ── 表单卡片 ──────────────────────────────────────
        _createScroll = EditorGUILayout.BeginScrollView(_createScroll);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var cardRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(cardRect, Theme.ClrCardBg);
        EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y + 8, 3, cardRect.height - 16), accent);

        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        EditorGUILayout.BeginVertical();

        // ── 基本信息 ──────────────────────────────────────
        GUILayout.Label("基本信息", Styles.SectionHeader);
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
        GUILayout.Label("保存路径", Styles.SectionHeader);
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
        GUILayout.Label("分类设置", Styles.SectionHeader);
        GUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("分类", GUILayout.Width(60));
        var catNames = Theme.CategoryColors.Keys.ToList();
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
        GUILayout.Label("功能描述", Styles.SectionHeader);
        GUILayout.Space(4);
        _createDescription = EditorGUILayout.TextArea(_createDescription,
            new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Height(80));

        GUILayout.Space(10);

        // ── 标签 ──────────────────────────────────────────
        DrawFormField("搜索标签", ref _createTags, "多个标签用逗号分隔，如: 动画, 帧动画, Sprite");

        GUILayout.Space(10);

        // ── 默认快捷键 ────────────────────────────────────
        GUILayout.Label("默认快捷键", Styles.SectionHeader);
        GUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("快捷键", GUILayout.Width(60));
        _createShortcut = EditorGUILayout.TextField(_createShortcut, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("可选，如 Ctrl+Shift+T。留空则不设默认快捷键，用户可在工具详情页自行设置。",
            CachedTooltipStyle);
        GUILayout.Space(4);

        GUILayout.Space(10);

        // ── 作者信息 ──────────────────────────────────────
        GUILayout.Label("作者信息", Styles.SectionHeader);
        GUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("作者", GUILayout.Width(60));
        _createAuthor = EditorGUILayout.TextField(_createAuthor, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("工具作者名称（可选）", CachedTooltipStyle);
        GUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("作者链接", GUILayout.Width(60));
        _createAuthorLink = EditorGUILayout.TextField(_createAuthorLink, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("作者主页/仓库 URL（可选），点击可跳转", CachedTooltipStyle);
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
        var cancelSize = Styles.BtnPrimary.CalcSize(cancelContent);
        var cancelW = Mathf.Max(cancelSize.x + 24, 100);
        var cancelRect = GUILayoutUtility.GetRect(cancelW, 36, GUILayout.Width(cancelW), GUILayout.Height(36));
        bool cancelHover = cancelRect.Contains(Event.current.mousePosition);
        Drawing.DrawRoundedRect(cancelRect, cancelHover ? Palette.ItemHover : Theme.ClrCardBg, 6f);
        if (GUI.Button(cancelRect, cancelContent, Styles.BtnPrimary))
        {
            _showCreateForm = false;
        }

        GUILayout.Space(12);

        // 创建按钮
        var createContent = new GUIContent("  创建工具");
        var createBtnSize = Styles.BtnPrimary.CalcSize(createContent);
        var createBtnW = Mathf.Max(createBtnSize.x + 32, 140);
        var createBtnH = 36f;
        var createBtnRect2 = GUILayoutUtility.GetRect(createBtnW, createBtnH, GUILayout.Width(createBtnW), GUILayout.Height(createBtnH));
        bool createBtnHover = createBtnRect2.Contains(Event.current.mousePosition);
        Drawing.DrawRoundedRect(createBtnRect2, createBtnHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal, 6f);
        if (GUI.Button(createBtnRect2, createContent, Styles.BtnPrimary))
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
        GUILayout.Label("已有分类参考", Styles.SectionHeader);
        GUILayout.Space(4);
        string allCategories = string.Join("  •  ", Theme.CategoryColors.Keys);
        _cachedDimLabel ??= new GUIStyle()
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = Theme.ClrTextDim },
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
        Color accent = cat != null ? cat.accent : Theme.ClrAccent;

        // ── 渐变头部装饰条 ──────────────────────────────
        var gradRect = new Rect(0, 0, area.width, 4);
        Drawing.DrawGradientRect(gradRect, accent, new Color(
            Mathf.Min(1f, accent.r + 0.15f),
            Mathf.Min(1f, accent.g + 0.15f),
            Mathf.Min(1f, accent.b + 0.15f), 1f));

        EditorGUILayout.Space(16);

        // ── 返回按钮 + 分类标签 ────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        bool shouldReturn = false;
        if (GUILayout.Button("← 返回", Styles.BackButton, GUILayout.Width(48)))
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
        GUI.DrawTexture(catDotRect, Styles.TexWhite, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        GUILayout.Space(4);
        GUILayout.Label(tool.category, Styles.RightSubtitle);

        var detailShortcut = GetEffectiveShortcut(tool.typeName);
        if (detailShortcut.IsValid)
        {
            var kbStr = detailShortcut.ToString();
            var kbContent = new GUIContent(kbStr);
            var kbSize = Styles.Shortcut.CalcSize(kbContent);
            GUILayout.FlexibleSpace();
            var kbRect = GUILayoutUtility.GetRect(kbSize.x + 16, 18, GUILayout.Width(kbSize.x + 16));
            EditorGUI.DrawRect(kbRect, Theme.ClrTagBg);
            GUI.Label(kbRect, kbStr, Styles.Shortcut);
        }

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        if (shouldReturn) return;

        EditorGUILayout.Space(6);

        // ── 工具名称 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.Label(tool.name, Styles.RightTitle);
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
                var tagSize = Styles.Tag.CalcSize(tagContent);
                var tagRect = GUILayoutUtility.GetRect(tagSize.x + 16, tagSize.y + 6,
                    GUILayout.Width(tagSize.x + 16));
                EditorGUI.DrawRect(tagRect, Theme.ClrTagBg);
                GUI.Label(tagRect, tag, Styles.Tag);
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
        Drawing.DrawRoundedRect(descRect, Theme.ClrCardBg, 8f);

        // 卡片左侧色条
        EditorGUI.DrawRect(new Rect(descRect.x + 2, descRect.y + 8, 3, descRect.height - 16), accent);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("功能说明", Styles.SectionHeader);
        GUILayout.Space(2);
        GUILayout.Label(tool.description, Styles.Description);
        EditorGUILayout.EndVertical();

        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);

        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // ── 脚本信息 + 作者 ──────────────────────────────
        DrawScriptInfoSection(tool, accent);

        // ── 主操作按钮 ────────────────────────────────────
        if (!string.IsNullOrEmpty(tool.typeName))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);
            GUILayout.FlexibleSpace();

            var btnContent = new GUIContent($"  打开 {tool.name}");
            var btnSize = Styles.BtnPrimary.CalcSize(btnContent);
            var btnW = Mathf.Max(btnSize.x + 32, 200);
            var btnH = 40f;
            var btnRect = GUILayoutUtility.GetRect(btnW, btnH, GUILayout.Width(btnW), GUILayout.Height(btnH));

            bool isHover = btnRect.Contains(Event.current.mousePosition);
            Drawing.DrawRoundedRect(btnRect, isHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal, 6f);

            if (GUI.Button(btnRect, btnContent, Styles.BtnPrimary))
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

    /// <summary>脚本信息区：脚本名称 + 打开脚本按钮 + 作者/链接</summary>
    private void DrawScriptInfoSection(ToolEntry tool, Color accent)
    {
        // 查找脚本路径（缓存到 tool.scriptPath）
        if (string.IsNullOrEmpty(tool.scriptPath) && !string.IsNullOrEmpty(tool.typeName))
            tool.scriptPath = FindScriptPathForType(tool.typeName);

        bool hasScript = !string.IsNullOrEmpty(tool.scriptPath);
        bool hasAuthor = !string.IsNullOrEmpty(tool.author);

        if (!hasScript && !hasAuthor) return;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var sectionRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        Drawing.DrawRoundedRect(sectionRect, new Color(0.14f, 0.14f, 0.17f, 0.6f), 8f);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("📋 脚本信息", Styles.SectionHeader);
        GUILayout.Space(6);

        // 脚本名称 + 打开按钮
        if (hasScript)
        {
            EditorGUILayout.BeginHorizontal();

            // 脚本文件名
            string fileName = System.IO.Path.GetFileName(tool.scriptPath);
            GUILayout.Label("📄", GUILayout.Width(20));
            var scriptLabelStyle = new GUIStyle()
            {
                fontSize = 11,
                normal = { textColor = Theme.ClrText },
                richText = true,
                wordWrap = false
            };
            GUILayout.Label(fileName, scriptLabelStyle, GUILayout.ExpandWidth(true));

            // 打开脚本按钮
            var openContent = new GUIContent("  打开脚本");
            var openSize = Styles.Shortcut.CalcSize(openContent);
            var openW = openSize.x + 16;
            var openRect = GUILayoutUtility.GetRect(openW, 24, GUILayout.Width(openW));
            bool openHover = openRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(openRect, openHover ? Theme.ClrBtnHover : Theme.ClrCardBg);
            CachedBtnFlatSmallCenter.normal.textColor = openHover ? Color.white : Theme.ClrText;
            GUI.Label(openRect, openContent, CachedBtnFlatSmallCenter);
            if (GUI.Button(openRect, "", GUIStyle.none))
            {
                OpenScriptFile(tool.scriptPath);
            }

            EditorGUILayout.EndHorizontal();

            // 脚本完整路径（灰色小字）
            GUILayout.Space(2);
            GUILayout.Label(tool.scriptPath, new GUIStyle()
            {
                fontSize = 9,
                normal = { textColor = Theme.ClrTextDim },
                wordWrap = false
            });
        }

        // 作者 + 链接
        if (hasAuthor)
        {
            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("✍ 作者:", new GUIStyle()
            {
                fontSize = 11,
                normal = { textColor = Theme.ClrTextDim }
            });

            if (!string.IsNullOrEmpty(tool.authorLink))
            {
                // 可点击的作者名
                var authorContent = new GUIContent(tool.author);
                var authorStyle = new GUIStyle()
                {
                    fontSize = 11,
                    normal = { textColor = Theme.ClrAccent },
                    hover = { textColor = new Color(Theme.ClrAccent.r + 0.1f, Theme.ClrAccent.g + 0.1f, Theme.ClrAccent.b + 0.1f) }
                };
                var authorSize = authorStyle.CalcSize(authorContent);
                var authorRect = GUILayoutUtility.GetRect(authorSize.x, 20, GUILayout.Width(authorSize.x));
                bool authorHover = authorRect.Contains(Event.current.mousePosition);
                if (authorHover)
                    EditorGUI.DrawRect(new Rect(authorRect.x, authorRect.yMax - 1, authorRect.width, 1), Theme.ClrAccent);
                GUI.Label(authorRect, tool.author, authorStyle);
                if (GUI.Button(authorRect, "", GUIStyle.none))
                {
                    Application.OpenURL(tool.authorLink);
                }

                GUILayout.Space(8);
                // 链接图标
                var linkContent = new GUIContent("🔗");
                var linkStyle = new GUIStyle()
                {
                    fontSize = 11,
                    normal = { textColor = Theme.ClrAccentDim }
                };
                GUILayout.Label(linkContent, linkStyle);
            }
            else
            {
                GUILayout.Label(tool.author, new GUIStyle()
                {
                    fontSize = 11,
                    normal = { textColor = Theme.ClrText }
                });
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();

        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);

        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);
    }

    private void DrawQuickActionsSection(ToolEntry tool, Color accent)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var sectionRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        Drawing.DrawRoundedRect(sectionRect, new Color(0.14f, 0.14f, 0.17f, 0.6f), 8f);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("快捷操作", Styles.SectionHeader);
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
        var rect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);

        if (hover)
            Drawing.DrawRoundedRect(rect, Theme.ClrHover, 4f);

        // 左侧色点
        var dotRect = new Rect(rect.x + 6, rect.y + rect.height / 2 - 3, 6, 6);
        GUI.color = Theme.ClrAccent;
        GUI.DrawTexture(dotRect, Styles.TexWhite, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        var labelRect = new Rect(rect.x + 16, rect.y, rect.width - 16, rect.height);
        GUI.Label(labelRect, text, Styles.BtnFlat);

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
        Drawing.DrawRoundedRect(sectionRect, new Color(0.14f, 0.14f, 0.17f, 0.6f), 8f);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);

        EditorGUILayout.BeginVertical();
        GUILayout.Label("⌨  快捷键设置", Styles.SectionHeader);
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
            normal = { textColor = Theme.ClrTextDim },
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
        var resetSize = Styles.Shortcut.CalcSize(resetContent);
        var resetW = resetSize.x + 16;
        var resetRect = GUILayoutUtility.GetRect(resetW, 24, GUILayout.Width(resetW));
        bool resetHover = resetRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(resetRect, resetHover ? Theme.ClrHover : new Color(0, 0, 0, 0));
        GUI.Label(resetRect, resetContent, CachedBtnFlatSmallCenter);
        if (GUI.Button(resetRect, GUIContent.none, Styles.InvisibleBtn))
        {
            StartRecording(typeName);
        }

        GUILayout.Space(8);

        // 清除按钮
        var clearContent = new GUIContent("  清除");
        var clearSize = Styles.Shortcut.CalcSize(clearContent);
        var clearW = clearSize.x + 16;
        var clearRect = GUILayoutUtility.GetRect(clearW, 24, GUILayout.Width(clearW));
        bool clearHover = clearRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(clearRect, clearHover ? new Color(0.6f, 0.2f, 0.2f, 0.3f) : new Color(0, 0, 0, 0));
        CachedBtnFlatSmallCenter.normal.textColor = new Color(0.85f, 0.35f, 0.35f);
        GUI.Label(clearRect, clearContent, CachedBtnFlatSmallCenter);
        if (GUI.Button(clearRect, GUIContent.none, Styles.InvisibleBtn))
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
            normal = { textColor = Theme.ClrTextDim },
            padding = new RectOffset(0, 0, 6, 0)
        });

        GUILayout.FlexibleSpace();

        var btnContent = new GUIContent("  ⌨  设置快捷键");
        var btnSize = Styles.Shortcut.CalcSize(btnContent);
        var btnW = btnSize.x + 24;
        var btnRect = GUILayoutUtility.GetRect(btnW, 28, GUILayout.Width(btnW));
        bool btnHover = btnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(btnRect, btnHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal);
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
            var size = Styles.KeyCap.CalcSize(content);
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

            var bgColor = i < keyCount - 1 ? Theme.ClrTagBg : accent;
            EditorGUI.DrawRect(keyRect, bgColor);
            EditorGUI.DrawRect(new Rect(keyRect.x, keyRect.y + keyRect.height - 1, keyRect.width, 1),
                new Color(0, 0, 0, 0.4f));
            GUI.Label(keyRect, keys[i], Styles.KeyCap);

            x += keyWidths[i] + 4;
        }
    }
    #endregion

    #region 添加工具面板（导入已有编辑器扩展）
    private void DrawAddToolPanel()
    {
        Color accent = new Color(0.40f, 0.65f, 0.90f, 1f);

        EditorGUILayout.Space(12);

        // ── 说明文字 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.Label("扫描指定目录或拖入 .cs 文件，找出未注册的 EditorWindow 扩展并添加 [ToolInfo] 特性", Styles.RightSubtitle);
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // ── 处理待处理的拖放文件（在 ScrollView 之外已拦截）──────
        if (_pendingDropPaths.Count > 0)
        {
            ProcessDroppedFiles(_pendingDropPaths);
            _pendingDropPaths.Clear();
        }

        // ── 拖放区域（纯视觉提示，事件在 HandleDragAndDrop 处理）──
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        // 检测是否有活跃的拖拽操作
        var dropAreaRect = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
        bool isDragOver = _isDragOverPanel && dropAreaRect.Contains(Event.current.mousePosition);

        // 背景 + 边框效果
        var borderColor = isDragOver ? accent : new Color(0.3f, 0.3f, 0.32f, 1f);
        var bgColor = isDragOver ? new Color(accent.r, accent.g, accent.b, 0.08f) : new Color(0.13f, 0.13f, 0.14f, 0.6f);
        EditorGUI.DrawRect(dropAreaRect, bgColor);
        EditorGUI.DrawRect(new Rect(dropAreaRect.x, dropAreaRect.y, dropAreaRect.width, 1), borderColor);
        EditorGUI.DrawRect(new Rect(dropAreaRect.x, dropAreaRect.yMax - 1, dropAreaRect.width, 1), borderColor);
        EditorGUI.DrawRect(new Rect(dropAreaRect.x, dropAreaRect.y, 1, dropAreaRect.height), borderColor);
        EditorGUI.DrawRect(new Rect(dropAreaRect.xMax - 1, dropAreaRect.y, 1, dropAreaRect.height), borderColor);

        Styles.MiniLabelBoldCenter.normal.textColor = isDragOver ? Theme.ClrTextBright : Theme.ClrTextDim;
        GUI.Label(dropAreaRect, isDragOver ? "释放以导入文件..." : "📂  拖放 .cs 文件到此处（EditorWindow 子类）", Styles.MiniLabelBoldCenter);

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // ── 扫描目录输入 ────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var scanCardRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(scanCardRect, Theme.ClrCardBg);
        EditorGUI.DrawRect(new Rect(scanCardRect.x, scanCardRect.y + 6, 3, scanCardRect.height - 12), accent);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        EditorGUILayout.BeginVertical();

        GUILayout.Label("扫描目录", Styles.SectionHeader);
        GUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        _addToolScanDir = EditorGUILayout.TextField(_addToolScanDir, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("浏览...", GUILayout.Width(60)))
        {
            var selected = EditorUtility.OpenFolderPanel("选择扫描目录", _addToolScanDir, "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    _addToolScanDir = "Assets" + selected.Substring(Application.dataPath.Length);
                else
                    _addToolScanDir = selected;
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        _addToolScanRecursive = EditorGUILayout.Toggle("递归子目录", _addToolScanRecursive, GUILayout.Width(120));

        GUILayout.FlexibleSpace();

        // 扫描按钮
        var scanContent = new GUIContent("  🔍 扫描");
        var scanSize = Styles.BtnPrimary.CalcSize(scanContent);
        var scanW = Mathf.Max(scanSize.x + 24, 100);
        var scanRect = GUILayoutUtility.GetRect(scanW, 28, GUILayout.Width(scanW), GUILayout.Height(28));
        bool scanHover = scanRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(scanRect, scanHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal);
        if (GUI.Button(scanRect, scanContent, Styles.BtnPrimary))
        {
            ScanDirectoryForNonHubTools(_addToolScanDir, _addToolScanRecursive);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);
        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // ── 扫描错误提示 ────────────────────────────────
        if (!string.IsNullOrEmpty(_addToolScanError))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);
            EditorGUILayout.HelpBox(_addToolScanError, MessageType.Warning);
            GUILayout.Space(RightPadding);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        // ── 扫描结果列表 ────────────────────────────────
        if (_addToolCandidates.Count > 0)
        {
            DrawAddToolCandidateList(accent);
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            string hint = string.IsNullOrEmpty(_addToolScanError)
                ? "点击「扫描」或拖放 .cs 文件查找可添加的编辑器扩展"
                : _addToolScanError;
            Color hintColor = string.IsNullOrEmpty(_addToolScanError) ? Theme.ClrTextDim : new Color(0.85f, 0.75f, 0.45f, 1f);
            GUILayout.Label(hint, new GUIStyle()
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = hintColor }
            });
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.Space(RightPadding);
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>绘制候选列表 + 选中后的导入表单</summary>
    private void DrawAddToolCandidateList(Color accent)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

        // ── 结果标题 ────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"发现 {_addToolCandidates.Count} 个未注册的编辑器扩展", Styles.SectionHeader);
        GUILayout.FlexibleSpace();

        // 一键全部添加按钮（快速模式：类名作工具名，"编辑器工具"作分类）
        var quickAddContent = new GUIContent("  全部快速添加");
        var quickAddSize = Styles.BtnPrimary.CalcSize(quickAddContent);
        var quickAddW = Mathf.Max(quickAddSize.x + 20, 110);
        var quickAddRect = GUILayoutUtility.GetRect(quickAddW, 24, GUILayout.Width(quickAddW), GUILayout.Height(24));
        bool quickAddHover = quickAddRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(quickAddRect, quickAddHover ? Theme.ClrBtnHover : Theme.ClrCardBg);
        CachedCenterLabel.normal.textColor = quickAddHover ? Color.white : Theme.ClrText;
        GUI.Label(quickAddRect, quickAddContent, CachedCenterLabel);
        if (GUI.Button(quickAddRect, "", GUIStyle.none))
        {
            if (EditorUtility.DisplayDialog("确认批量添加",
                $"将为 {_addToolCandidates.Count} 个扩展自动添加 [ToolInfo] 特性。\n\n工具名 = 类名，分类 = 编辑器工具。\n\n是否继续？",
                "确定", "取消"))
            {
                int success = 0;
                foreach (var c in _addToolCandidates)
                {
                    string name = DeriveToolNameFromClass(c.className);
                    if (AddToolInfoToScript(c, name, "编辑器工具",
                        c.existingDescription ?? $"{name} 编辑器扩展",
                        "⚙", null, "", "", "", false))
                        success++;
                }
                Debug.Log($"[UnityToolsHub] 批量添加完成：成功 {success}/{_addToolCandidates.Count}");
                _addToolCandidates.Clear();
                _addToolSelectedIndex = -1;
                DiscoverTools();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // ── 候选列表 ────────────────────────────────────
        _addToolScroll = EditorGUILayout.BeginScrollView(_addToolScroll,
            GUILayout.Height(Mathf.Min(_addToolCandidates.Count * 36f + 8f, 200f)));

        for (int i = 0; i < _addToolCandidates.Count; i++)
        {
            var c = _addToolCandidates[i];
            bool isSelected = i == _addToolSelectedIndex;

            var rowRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            bool rowHover = rowRect.Contains(Event.current.mousePosition);

            // 背景
            if (isSelected)
                EditorGUI.DrawRect(rowRect, Theme.ClrSelection);
            else if (rowHover)
                EditorGUI.DrawRect(rowRect, Theme.ClrHover);

            // 选中色条
            if (isSelected)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y + 4, 3, rowRect.height - 8), accent);

            // 类名
            var nameRect = new Rect(rowRect.x + 10, rowRect.y + 2, rowRect.width - 200, 18);
            GUI.Label(nameRect, c.className, isSelected ? Styles.ToolItemSelected : Styles.ToolItem);

            // 文件路径
            var pathRect = new Rect(rowRect.x + 10, rowRect.y + 18, rowRect.width - 200, 12);
            GUI.Label(pathRect, c.filePath, new GUIStyle()
            {
                fontSize = 9,
                normal = { textColor = Theme.ClrTextDim }
            });

            // 基类标签
            var baseTagContent = new GUIContent(c.baseClass);
            var baseTagSize = Styles.Tag.CalcSize(baseTagContent);
            var baseTagRect = new Rect(rowRect.xMax - baseTagSize.x - 20, rowRect.y + 7, baseTagSize.x + 12, 18);
            EditorGUI.DrawRect(baseTagRect, Theme.ClrTagBg);
            GUI.Label(baseTagRect, c.baseClass, Styles.Tag);

            // 点击选中
            if (GUI.Button(rowRect, "", GUIStyle.none))
            {
                FillAddToolFormFromCandidate(i);
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        // ── 选中后的导入表单 ────────────────────────────
        if (_addToolSelectedIndex >= 0 && _addToolSelectedIndex < _addToolCandidates.Count)
        {
            EditorGUILayout.Space(12);
            DrawAddToolForm();
        }
    }

    /// <summary>绘制导入工具的参数填写表单</summary>
    private void DrawAddToolForm()
    {
        var candidate = _addToolCandidates[_addToolSelectedIndex];
        Color accent = new Color(0.40f, 0.65f, 0.90f, 1f);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var cardRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(cardRect, Theme.ClrCardBg);
        EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y + 8, 3, cardRect.height - 16), accent);

        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        EditorGUILayout.BeginVertical();

        // ── 表单标题 ────────────────────────────────────
        GUILayout.Label($"为 「{candidate.className}」 填写工具信息", Styles.SectionHeader);
        GUILayout.Space(8);

        // ── 工具名称 ────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("工具名称", GUILayout.Width(70));
        _addToolName = EditorGUILayout.TextField(_addToolName, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("显示在 Hub 左侧列表中的名称", CachedTooltipStyle);
        GUILayout.Space(4);

        // ── 类名（只读）────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("类名", GUILayout.Width(70));
        GUI.enabled = false;
        EditorGUILayout.TextField(candidate.className, GUILayout.ExpandWidth(true));
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("自动从源文件读取，不可修改", CachedTooltipStyle);
        GUILayout.Space(4);

        // ── 分类 + 图标 ────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("分类", GUILayout.Width(70));
        var catNames = Theme.CategoryColors.Keys.ToList();
        if (!catNames.Contains(_addToolCategory)) catNames.Add(_addToolCategory);
        int catIdx = catNames.IndexOf(_addToolCategory);
        catIdx = EditorGUILayout.Popup(catIdx, catNames.ToArray());
        _addToolCategory = catNames[catIdx];
        GUILayout.Space(16);
        GUILayout.Label("图标", GUILayout.Width(36));
        _addToolIcon = EditorGUILayout.TextField(_addToolIcon, GUILayout.Width(60));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6);

        // ── 功能描述 ────────────────────────────────────
        GUILayout.Label("功能描述", Styles.SectionHeader);
        GUILayout.Space(4);
        _addToolDescription = EditorGUILayout.TextArea(_addToolDescription,
            new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Height(60));

        GUILayout.Space(6);

        // ── 搜索标签 ────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("搜索标签", GUILayout.Width(70));
        _addToolTags = EditorGUILayout.TextField(_addToolTags, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("多个标签用逗号分隔", CachedTooltipStyle);
        GUILayout.Space(4);

        // ── 快捷键 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("快捷键", GUILayout.Width(70));
        _addToolShortcut = EditorGUILayout.TextField(_addToolShortcut, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("可选，如 Ctrl+Shift+T，留空则不设", CachedTooltipStyle);
        GUILayout.Space(4);

        GUILayout.Space(10);

        // ── 作者信息 ────────────────────────────────────
        GUILayout.Label("作者信息", Styles.SectionHeader);
        GUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("作者", GUILayout.Width(70));
        _addToolAuthor = EditorGUILayout.TextField(_addToolAuthor, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("工具作者名称（可选）", CachedTooltipStyle);
        GUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("作者链接", GUILayout.Width(70));
        _addToolAuthorLink = EditorGUILayout.TextField(_addToolAuthorLink, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("作者主页/仓库 URL（可选），点击可跳转", CachedTooltipStyle);
        GUILayout.Space(4);

        GUILayout.Space(10);

        // ── 第三方工具 ──────────────────────────────────
        GUILayout.Label("安全设置", Styles.SectionHeader);
        GUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        _addToolIsThirdParty = EditorGUILayout.Toggle(_addToolIsThirdParty, GUILayout.Width(20));
        GUILayout.Label("标记为第三方工具", new GUIStyle()
        {
            fontSize = 12,
            normal = { textColor = Theme.ClrText }
        });
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(1);
        GUILayout.Label("第三方工具默认禁用（不显示在分类列表中），需在管理面板手动启用", CachedTooltipStyle);
        GUILayout.Space(4);

        EditorGUILayout.EndVertical();
        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        // ── 操作按钮 ────────────────────────────────────
        EditorGUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.FlexibleSpace();

        // 取消按钮
        var cancelContent = new GUIContent("  取消");
        var cancelSize = Styles.BtnPrimary.CalcSize(cancelContent);
        var cancelW = Mathf.Max(cancelSize.x + 24, 100);
        var cancelRect = GUILayoutUtility.GetRect(cancelW, 36, GUILayout.Width(cancelW), GUILayout.Height(36));
        bool cancelHover = cancelRect.Contains(Event.current.mousePosition);
        Drawing.DrawRoundedRect(cancelRect, cancelHover ? Palette.ItemHover : Theme.ClrCardBg, 6f);
        if (GUI.Button(cancelRect, cancelContent, Styles.BtnPrimary))
        {
            _addToolSelectedIndex = -1;
        }

        GUILayout.Space(12);

        // 添加按钮
        var addContent = new GUIContent("  添加工具");
        var addBtnSize = Styles.BtnPrimary.CalcSize(addContent);
        var addBtnW = Mathf.Max(addBtnSize.x + 32, 140);
        var addBtnRect = GUILayoutUtility.GetRect(addBtnW, 36, GUILayout.Width(addBtnW), GUILayout.Height(36));
        bool addBtnHover = addBtnRect.Contains(Event.current.mousePosition);
        Drawing.DrawRoundedRect(addBtnRect, addBtnHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal, 6f);
        if (GUI.Button(addBtnRect, addContent, Styles.BtnPrimary))
        {
            // 验证
            if (string.IsNullOrWhiteSpace(_addToolName))
            {
                EditorUtility.DisplayDialog("提示", "请输入工具名称", "确定");
            }
            else
            {
                // 处理标签
                string[] tags = null;
                if (!string.IsNullOrWhiteSpace(_addToolTags))
                {
                    tags = _addToolTags.Split(',')
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToArray();
                }

                bool success = AddToolInfoToScript(
                    candidate,
                    _addToolName.Trim(),
                    _addToolCategory,
                    _addToolDescription.Trim(),
                    _addToolIcon.Trim(),
                    tags,
                    _addToolShortcut.Trim(),
                    _addToolAuthor.Trim(),
                    _addToolAuthorLink.Trim(),
                    _addToolIsThirdParty);

                if (success)
                {
                    // 从候选列表移除
                    _addToolCandidates.RemoveAt(_addToolSelectedIndex);
                    _addToolSelectedIndex = -1;
                    _addToolName = "";
                    _addToolClassName = "";
                    _addToolDescription = "";
                    _addToolAuthor = "";
                    _addToolAuthorLink = "";
                    _addToolIsThirdParty = false;

                    // 刷新工具发现
                    DiscoverTools();
                }
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(12);

        // ── 文件路径参考 ────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        var refRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(refRect, new Color(0.15f, 0.15f, 0.16f, 0.5f));
        GUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        GUILayout.Label($"文件路径：{candidate.filePath}", new GUIStyle()
        {
            fontSize = 10,
            normal = { textColor = Theme.ClrTextDim }
        });
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(6);
        EditorGUILayout.EndVertical();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(16);
    }
    #endregion

    #region 第三方工具管理面板
    /// <summary>第三方工具管理面板（类似 Unity Package Manager 的左右分栏布局）</summary>
    private void DrawThirdPartyManagerPanel()
    {
        Color accent = new Color(0.80f, 0.55f, 0.90f, 1f);

        EditorGUILayout.Space(6);

        // ── 顶部工具栏：导入按钮 + 导入表单 ──────────────────
        DrawThirdPartyToolbar(accent);

        // 导入中状态
        if (_isImporting)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);
            EditorGUILayout.HelpBox(_importStatus, MessageType.Info);
            GUILayout.Space(RightPadding);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }
        else if (!string.IsNullOrEmpty(_importStatus))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);
            EditorGUILayout.HelpBox(_importStatus, MessageType.Info);
            GUILayout.Space(RightPadding);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        if (_thirdPartyRegistry.tools.Count == 0)
        {
            // ── 空状态 ──
            EditorGUILayout.Space(30);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("📦", new GUIStyle() { fontSize = 48, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Theme.ClrTextDim } }, GUILayout.Width(80), GUILayout.Height(60));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("暂无第三方工具", new GUIStyle()
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Theme.ClrTextDim }
            });
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("通过上方按钮从本地文件夹或 Git URL 导入第三方工具", new GUIStyle()
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Theme.ClrTextDim }
            });
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            return;
        }

        // ── 左右分栏布局 ──
        float statsBarHeight = 28f;
        float toolbarHeight = _showImportForm ? 160f : 44f;
        float contentHeight = (position.height - toolbarHeight - 60) - statsBarHeight;
        contentHeight = Mathf.Max(contentHeight, 200f);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        // ── 左侧：工具列表 ──
        float listWidth = 220f;
        EditorGUILayout.BeginVertical(GUILayout.Width(listWidth), GUILayout.Height(contentHeight));

        var listHeaderRect = GUILayoutUtility.GetRect(listWidth, 24);
        EditorGUI.DrawRect(listHeaderRect, new Color(0.13f, 0.13f, 0.14f, 1f));
        GUI.Label(new Rect(listHeaderRect.x + 10, listHeaderRect.y, listHeaderRect.width - 20, listHeaderRect.height),
            "工具列表", new GUIStyle()
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Theme.ClrTextBright }
            });

        _thirdPartyListScroll = EditorGUILayout.BeginScrollView(_thirdPartyListScroll);

        for (int i = 0; i < _thirdPartyRegistry.tools.Count; i++)
        {
            var state = _thirdPartyRegistry.tools[i];
            bool isSelected = i == _thirdPartySelectedIndex;

            var rowRect = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            bool rowHover = rowRect.Contains(Event.current.mousePosition);

            if (isSelected)
            {
                EditorGUI.DrawRect(rowRect, Theme.ClrSelection);
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y + 4, 3, rowRect.height - 8), accent);
            }
            else if (rowHover)
            {
                EditorGUI.DrawRect(rowRect, Theme.ClrHover);
            }

            // 状态指示灯
            var dotRect = new Rect(rowRect.x + 8, rowRect.y + 8, 8, 8);
            Color dotColor = state.isEnabled ? new Color(0.35f, 0.85f, 0.45f, 1f) : new Color(0.85f, 0.35f, 0.35f, 1f);
            EditorGUI.DrawRect(dotRect, dotColor);

            // 工具名
            var nameRect = new Rect(rowRect.x + 22, rowRect.y + 4, rowRect.width - 30, 18);
            GUI.Label(nameRect, state.toolName, isSelected ? Styles.ToolItemSelected : Styles.ToolItem);

            // 来源标签
            string sourceLabel = "";
            Color sourceColor = Theme.ClrTextDim;
            if (state.importSource == "git") { sourceLabel = "Git"; sourceColor = new Color(0.40f, 0.65f, 0.90f, 1f); }
            else if (state.importSource == "local") { sourceLabel = "本地"; sourceColor = new Color(0.35f, 0.75f, 0.45f, 1f); }
            else if (state.importSource == "manual") { sourceLabel = "手动"; sourceColor = Theme.ClrTextDim; }

            if (!string.IsNullOrEmpty(sourceLabel))
            {
                var srcContent = new GUIContent(sourceLabel);
                var srcSize = Styles.Tag.CalcSize(srcContent);
                var srcRect = new Rect(rowRect.xMax - srcSize.x - 20, rowRect.y + 5, srcSize.x + 12, 16);
                EditorGUI.DrawRect(srcRect, new Color(sourceColor.r, sourceColor.g, sourceColor.b, 0.15f));
                var srcStyle = new GUIStyle(Styles.Tag) { normal = { textColor = sourceColor }, fontSize = 9 };
                GUI.Label(srcRect, sourceLabel, srcStyle);
            }

            // 作者
            var authorRect = new Rect(rowRect.x + 22, rowRect.y + 22, rowRect.width - 30, 12);
            GUI.Label(authorRect, string.IsNullOrEmpty(state.author) ? "(未知作者)" : state.author, new GUIStyle()
            {
                fontSize = 9,
                normal = { textColor = Theme.ClrTextDim }
            });

            if (GUI.Button(rowRect, "", GUIStyle.none))
            {
                _thirdPartySelectedIndex = i;
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ── 分隔线 ──
        var splitterRect = GUILayoutUtility.GetRect(1, contentHeight, GUILayout.Width(1));
        EditorGUI.DrawRect(splitterRect, Theme.ClrDivider);

        // ── 右侧：详情 ──
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(contentHeight));

        _thirdPartyMgrScroll = EditorGUILayout.BeginScrollView(_thirdPartyMgrScroll);

        if (_thirdPartySelectedIndex >= 0 && _thirdPartySelectedIndex < _thirdPartyRegistry.tools.Count)
        {
            DrawThirdPartyToolDetails(_thirdPartyRegistry.tools[_thirdPartySelectedIndex], accent);
        }
        else
        {
            GUILayout.Space(60);
            GUILayout.Label("← 从左侧选择一个工具查看详情", new GUIStyle()
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Theme.ClrTextDim }
            });
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        // ── 底部统计栏 ──
        EditorGUILayout.Space(4);
        var statsRect = GUILayoutUtility.GetRect(0, statsBarHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(statsRect, new Color(0.13f, 0.13f, 0.14f, 1f));
        EditorGUI.DrawRect(new Rect(statsRect.x, statsRect.y, statsRect.width, 1), Theme.ClrDivider);

        int total = _thirdPartyRegistry.tools.Count;
        int enabled = _thirdPartyRegistry.EnabledCount;
        int disabled = total - enabled;

        var statsStyle = new GUIStyle()
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Theme.ClrTextDim },
            richText = true
        };
        GUI.Label(new Rect(statsRect.x + RightPadding, statsRect.y, statsRect.width - RightPadding * 2, statsBarHeight),
            $"共 {total} 个第三方工具  ·  <color=#5BB56B>已启用 {enabled}</color>  ·  <color=#D9534F>已禁用 {disabled}</color>",
            statsStyle);
    }

    /// <summary>顶部工具栏：导入按钮 + 展开式导入表单</summary>
    private void DrawThirdPartyToolbar(Color accent)
    {
        // 按钮栏
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        // ＋ 从本地导入
        var localBtnContent = new GUIContent("  ＋ 从本地导入");
        var localBtnSize = Styles.BtnPrimary.CalcSize(localBtnContent);
        var localBtnW = Mathf.Max(localBtnSize.x + 20, 120);
        var localBtnRect = GUILayoutUtility.GetRect(localBtnW, 28, GUILayout.Width(localBtnW), GUILayout.Height(28));
        bool localHover = localBtnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(localBtnRect, localHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal);
        if (GUI.Button(localBtnRect, localBtnContent, Styles.BtnPrimary))
        {
            _showImportForm = !_showImportForm || _importSourceIndex != 0;
            _importSourceIndex = 0;
            _importStatus = "";
        }

        GUILayout.Space(8);

        // ＋ 从 Git 导入
        var gitBtnContent = new GUIContent("  ＋ 从 Git 导入");
        var gitBtnSize = Styles.BtnPrimary.CalcSize(gitBtnContent);
        var gitBtnW = Mathf.Max(gitBtnSize.x + 20, 120);
        var gitBtnRect = GUILayoutUtility.GetRect(gitBtnW, 28, GUILayout.Width(gitBtnW), GUILayout.Height(28));
        bool gitHover = gitBtnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(gitBtnRect, gitHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal);
        if (GUI.Button(gitBtnRect, gitBtnContent, Styles.BtnPrimary))
        {
            _showImportForm = !_showImportForm || _importSourceIndex != 1;
            _importSourceIndex = 1;
            _importStatus = "";
        }

        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        // ── 导入表单 ──
        if (_showImportForm)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(RightPadding);

            var formRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            Drawing.DrawRoundedRect(formRect, Theme.ClrCardBg, 8f);
            EditorGUI.DrawRect(new Rect(formRect.x + 2, formRect.y + 8, 3, formRect.height - 16), accent);

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(14);
            EditorGUILayout.BeginVertical();

            if (_importSourceIndex == 0)
            {
                // 本地导入表单
                GUILayout.Label("从本地导入", Styles.SectionHeader);
                GUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("路径", GUILayout.Width(50));
                _importLocalPath = EditorGUILayout.TextField(_importLocalPath, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("浏览...", GUILayout.Width(60)))
                {
                    var selected = EditorUtility.OpenFolderPanel("选择工具包目录", "", "");
                    if (!string.IsNullOrEmpty(selected))
                        _importLocalPath = selected;
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(1);
                GUILayout.Label("选择含 package.json 的 UPM 包目录或含 .cs 文件的目录", CachedTooltipStyle);
                GUILayout.Space(4);
            }
            else
            {
                // Git 导入表单
                GUILayout.Label("从 Git 导入", Styles.SectionHeader);
                GUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Git URL", GUILayout.Width(60));
                _importGitUrl = EditorGUILayout.TextField(_importGitUrl, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(1);
                GUILayout.Label("如 https://github.com/user/repo.git 或 git@github.com:user/repo.git", CachedTooltipStyle);
                GUILayout.Space(4);
            }

            // 通用字段
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("工具名", GUILayout.Width(60));
            _importToolName = EditorGUILayout.TextField(_importToolName, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(1);
            GUILayout.Label("可选，留空则自动从路径/URL 推导", CachedTooltipStyle);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("作者", GUILayout.Width(60));
            _importAuthor = EditorGUILayout.TextField(_importAuthor, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("作者链接", GUILayout.Width(60));
            _importAuthorLink = EditorGUILayout.TextField(_importAuthorLink, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // 取消
            var cancelContent = new GUIContent("  取消");
            var cancelSize = Styles.BtnPrimary.CalcSize(cancelContent);
            var cancelW = Mathf.Max(cancelSize.x + 20, 80);
            var cancelRect = GUILayoutUtility.GetRect(cancelW, 28, GUILayout.Width(cancelW), GUILayout.Height(28));
            bool cancelHover = cancelRect.Contains(Event.current.mousePosition);
            Drawing.DrawRoundedRect(cancelRect, cancelHover ? Palette.ItemHover : Theme.ClrCardBg, 4f);
            if (GUI.Button(cancelRect, cancelContent, Styles.BtnPrimary))
            {
                _showImportForm = false;
                _importStatus = "";
            }

            GUILayout.Space(8);

            // 导入
            string importLabel = _isImporting ? "  导入中..." : "  导入";
            var importContent = new GUIContent(importLabel);
            var importSize = Styles.BtnPrimary.CalcSize(importContent);
            var importW = Mathf.Max(importSize.x + 20, 100);
            var importRect = GUILayoutUtility.GetRect(importW, 28, GUILayout.Width(importW), GUILayout.Height(28));
            bool importHover = importRect.Contains(Event.current.mousePosition);
            Drawing.DrawRoundedRect(importRect, importHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal, 4f);
            GUI.enabled = !_isImporting;
            if (GUI.Button(importRect, importContent, Styles.BtnPrimary))
            {
                _importStatus = "";
                if (_importSourceIndex == 0)
                    ImportThirdPartyFromLocal(_importLocalPath, _importToolName, _importAuthor, _importAuthorLink);
                else
                    ImportThirdPartyFromGit(_importGitUrl, _importToolName, _importAuthor, _importAuthorLink);
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            EditorGUILayout.EndVertical();
            GUILayout.Space(RightPadding);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4);
    }

    /// <summary>绘制第三方工具详情面板</summary>
    private void DrawThirdPartyToolDetails(ThirdPartyToolState state, Color accent)
    {
        EditorGUILayout.Space(12);

        // ── 工具名称 + 状态标签 ──
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.Label(state.toolName, Styles.RightTitle);

        // 来源标签
        string sourceLabel = "";
        Color sourceColor = Theme.ClrTextDim;
        if (state.importSource == "git") { sourceLabel = "Git"; sourceColor = new Color(0.40f, 0.65f, 0.90f, 1f); }
        else if (state.importSource == "local") { sourceLabel = "本地"; sourceColor = new Color(0.35f, 0.75f, 0.45f, 1f); }
        else if (state.importSource == "manual") { sourceLabel = "手动"; sourceColor = Theme.ClrTextDim; }

        if (!string.IsNullOrEmpty(sourceLabel))
        {
            var srcContent = new GUIContent(sourceLabel);
            var srcSize = Styles.Tag.CalcSize(srcContent);
            var srcRect = GUILayoutUtility.GetRect(srcSize.x + 16, srcSize.y + 6, GUILayout.Width(srcSize.x + 16));
            EditorGUI.DrawRect(srcRect, new Color(sourceColor.r, sourceColor.g, sourceColor.b, 0.2f));
            var srcStyle = new GUIStyle(Styles.Tag) { normal = { textColor = sourceColor } };
            GUI.Label(srcRect, sourceLabel, srcStyle);
            GUILayout.Space(4);
        }

        // 状态标签
        var statusContent = new GUIContent(state.isEnabled ? "已启用" : "已禁用");
        var statusSize = Styles.Tag.CalcSize(statusContent);
        var statusRect = GUILayoutUtility.GetRect(statusSize.x + 16, statusSize.y + 6,
            GUILayout.Width(statusSize.x + 16));
        Color statusColor = state.isEnabled ? new Color(0.2f, 0.5f, 0.3f, 0.6f) : new Color(0.5f, 0.2f, 0.2f, 0.6f);
        EditorGUI.DrawRect(statusRect, statusColor);
        GUI.Label(statusRect, state.isEnabled ? "已启用" : "已禁用", Styles.Tag);

        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(12);

        // ── 信息卡片 ──
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);

        var cardRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        Drawing.DrawRoundedRect(cardRect, Theme.ClrCardBg, 8f);
        EditorGUI.DrawRect(new Rect(cardRect.x + 2, cardRect.y + 8, 3, cardRect.height - 16), accent);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        EditorGUILayout.BeginVertical();

        // 作者
        GUILayout.Label("作者", Styles.SectionHeader);
        GUILayout.Space(2);
        if (!string.IsNullOrEmpty(state.authorLink))
        {
            EditorGUILayout.BeginHorizontal();
            var authorStyle = new GUIStyle()
            {
                fontSize = 12,
                normal = { textColor = Theme.ClrAccent },
                hover = { textColor = new Color(Theme.ClrAccent.r + 0.1f, Theme.ClrAccent.g + 0.1f, Theme.ClrAccent.b + 0.1f) }
            };
            var authorContent = new GUIContent(state.author);
            var authorSize = authorStyle.CalcSize(authorContent);
            var authorRect = GUILayoutUtility.GetRect(authorSize.x, 20, GUILayout.Width(authorSize.x));
            bool authorHover = authorRect.Contains(Event.current.mousePosition);
            if (authorHover)
                EditorGUI.DrawRect(new Rect(authorRect.x, authorRect.yMax - 1, authorRect.width, 1), Theme.ClrAccent);
            GUI.Label(authorRect, state.author, authorStyle);
            if (GUI.Button(authorRect, "", GUIStyle.none))
                Application.OpenURL(state.authorLink);

            GUILayout.Space(8);
            GUILayout.Label("🔗", new GUIStyle() { fontSize = 11, normal = { textColor = Theme.ClrAccentDim } });
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label(string.IsNullOrEmpty(state.author) ? "(未设置)" : state.author, Styles.Description);
        }

        GUILayout.Space(8);

        // 来源信息
        if (!string.IsNullOrEmpty(state.importSource))
        {
            GUILayout.Label("导入来源", Styles.SectionHeader);
            GUILayout.Space(2);
            if (state.importSource == "git" && !string.IsNullOrEmpty(state.gitUrl))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(state.gitUrl, new GUIStyle()
                {
                    fontSize = 11,
                    normal = { textColor = Theme.ClrText },
                    wordWrap = false
                }, GUILayout.ExpandWidth(true));

                // 打开链接按钮
                var openContent = new GUIContent("  打开");
                var openSize = Styles.Shortcut.CalcSize(openContent);
                var openW = openSize.x + 16;
                var openRect = GUILayoutUtility.GetRect(openW, 22, GUILayout.Width(openW));
                bool openHover = openRect.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(openRect, openHover ? Theme.ClrBtnHover : Theme.ClrCardBg);
                CachedBtnFlatSmallCenter.normal.textColor = openHover ? Color.white : Theme.ClrText;
                GUI.Label(openRect, openContent, CachedBtnFlatSmallCenter);
                if (GUI.Button(openRect, "", GUIStyle.none))
                    Application.OpenURL(state.gitUrl);
                EditorGUILayout.EndHorizontal();
            }
            else if (state.importSource == "local" && !string.IsNullOrEmpty(state.packagePath))
            {
                GUILayout.Label(state.packagePath, new GUIStyle()
                {
                    fontSize = 11,
                    normal = { textColor = Theme.ClrText },
                    wordWrap = false
                });
            }
            else
            {
                GUILayout.Label("手动添加", Styles.Description);
            }
            GUILayout.Space(8);
        }

        // 安装路径
        if (!string.IsNullOrEmpty(state.installPath))
        {
            GUILayout.Label("安装路径", Styles.SectionHeader);
            GUILayout.Space(2);
            GUILayout.Label(state.installPath, new GUIStyle()
            {
                fontSize = 10,
                normal = { textColor = Theme.ClrTextDim },
                wordWrap = false
            });
            GUILayout.Space(8);
        }

        // 分类
        GUILayout.Label("分类", Styles.SectionHeader);
        GUILayout.Space(2);
        GUILayout.Label(string.IsNullOrEmpty(state.category) ? "(未设置)" : state.category, Styles.Description);

        GUILayout.Space(8);

        // 描述
        GUILayout.Label("功能描述", Styles.SectionHeader);
        GUILayout.Space(2);
        GUILayout.Label(string.IsNullOrEmpty(state.description) ? "(无描述)" : state.description, Styles.Description);

        GUILayout.Space(8);

        // 脚本路径
        if (!string.IsNullOrEmpty(state.scriptPath))
        {
            GUILayout.Label("脚本路径", Styles.SectionHeader);
            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(state.scriptPath, new GUIStyle()
            {
                fontSize = 10,
                normal = { textColor = Theme.ClrTextDim },
                wordWrap = false
            }, GUILayout.ExpandWidth(true));

            var openContent = new GUIContent("  打开");
            var openSize = Styles.Shortcut.CalcSize(openContent);
            var openW = openSize.x + 16;
            var openRect = GUILayoutUtility.GetRect(openW, 22, GUILayout.Width(openW));
            bool openHover = openRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(openRect, openHover ? Theme.ClrBtnHover : Theme.ClrCardBg);
            CachedBtnFlatSmallCenter.normal.textColor = openHover ? Color.white : Theme.ClrText;
            GUI.Label(openRect, openContent, CachedBtnFlatSmallCenter);
            if (GUI.Button(openRect, "", GUIStyle.none))
                OpenScriptFile(state.scriptPath);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(4);
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);
        EditorGUILayout.EndVertical();

        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // ── 操作按钮 ──
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        GUILayout.FlexibleSpace();

        // 启用/禁用按钮
        string toggleLabel = state.isEnabled ? "  禁用工具" : "  启用工具";
        var toggleContent = new GUIContent(toggleLabel);
        var toggleSize = Styles.BtnPrimary.CalcSize(toggleContent);
        var toggleW = Mathf.Max(toggleSize.x + 32, 140);
        var toggleRect = GUILayoutUtility.GetRect(toggleW, 36, GUILayout.Width(toggleW), GUILayout.Height(36));
        bool toggleHover = toggleRect.Contains(Event.current.mousePosition);
        Color toggleBg = state.isEnabled
            ? (toggleHover ? new Color(0.6f, 0.2f, 0.2f, 0.8f) : new Color(0.4f, 0.15f, 0.15f, 0.6f))
            : (toggleHover ? Theme.ClrBtnHover : Theme.ClrBtnNormal);
        Drawing.DrawRoundedRect(toggleRect, toggleBg, 6f);
        if (GUI.Button(toggleRect, toggleContent, Styles.BtnPrimary))
        {
            ToggleThirdPartyToolEnabled(state.typeName);
            Repaint();
        }

        GUILayout.Space(12);

        // 卸载按钮
        string uninstallLabel = state.importSource == "git" || state.importSource == "local" ? "  卸载" : "  移除记录";
        var uninstallContent = new GUIContent(uninstallLabel);
        var uninstallSize = Styles.BtnPrimary.CalcSize(uninstallContent);
        var uninstallW = Mathf.Max(uninstallSize.x + 24, 120);
        var uninstallRect = GUILayoutUtility.GetRect(uninstallW, 36, GUILayout.Width(uninstallW), GUILayout.Height(36));
        bool uninstallHover = uninstallRect.Contains(Event.current.mousePosition);
        Drawing.DrawRoundedRect(uninstallRect, uninstallHover ? Palette.ItemHover : Theme.ClrCardBg, 6f);
        if (GUI.Button(uninstallRect, uninstallContent, new GUIStyle(Styles.BtnPrimary)
        {
            normal = { textColor = Theme.ClrTextBright }
        }))
        {
            string action = state.importSource == "git" || state.importSource == "local" ? "卸载" : "移除";
            string extraHint = state.importSource == "git" || state.importSource == "local"
                ? "\n\n卸载将从 Unity Package Manager 中移除此包。"
                : "\n\n此操作仅移除 Hub 的管理记录，不会删除脚本文件。";

            if (EditorUtility.DisplayDialog($"确认{action}",
                $"确定要{action}「{state.toolName}」吗？{extraHint}",
                "确定", "取消"))
            {
                UninstallThirdPartyTool(state);
                _thirdPartySelectedIndex = -1;
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // ── 安全提示 ──
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RightPadding);
        if (!state.isEnabled)
        {
            EditorGUILayout.HelpBox(
                "此工具当前已禁用。\n" +
                "• 禁用状态下不会显示在 Hub 分类列表中\n" +
                "• 无法通过 Hub 打开\n" +
                "• 点击「启用工具」可启用",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "此工具已启用，正常显示在分类列表中。\n" +
                "如需暂时停用，点击「禁用工具」。",
                MessageType.Info);
        }
        GUILayout.Space(RightPadding);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(16);
    }
    #endregion
}
#endif
