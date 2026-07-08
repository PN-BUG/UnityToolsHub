using UnityEditor;
using UnityEngine;

/// <summary>
/// 样式展示面板 — 集中预览 ToolEditorWindow 提供的所有 UI 样式。
/// 通过菜单 UnityToolsHub/样式模板 打开，或从 Hub 面板进入。
/// </summary>
[ToolInfo("样式模板", "编辑器工具",
    Description = "展示 ToolEditorWindow 基类提供的所有 UI 样式与控件",
    Icon = "🎨",
    Tags = new[] { "样式", "模板", "UI" },
    Priority = -100)]
public class StyleShowcase : ToolEditorWindow
{
    // ── 配置 ────────────────────────────────────────────
    protected override string ToolTitle => "样式模板 · 展示面板";
    protected override string ToolIcon => "🎨";
    protected override bool ShowSearchBar => false;
    protected override bool ShowStatusBar => true;

    private float _demoProgress = 0.65f;
    private bool _demoToggle1 = true;
    private bool _demoToggle2 = false;
    private int _selectedItem = 0;
    private string _searchDemo = "";

    // ═══════════════════════════════════════════════════
    //  主内容
    // ═══════════════════════════════════════════════════

    protected override void DrawToolContent()
    {
        // ── 顶部渐变条 ────────────────────────────────
        DrawGradientBar(ClrAccent, ClrSuccess);

        EditorGUILayout.Space(4);

        // ── 介绍 ──────────────────────────────────────
        DrawTitle("ToolEditorWindow 样式模板");
        DrawSubtitle("以下展示基类提供的所有 UI 样式与控件");
        DrawBody("所有工具面板继承 <b>ToolEditorWindow</b> 即可使用以下统一风格控件。");

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 1. 按钮 ───────────────────────────────────
        DrawSection("按钮 Buttons", ClrAccent);

        EditorGUILayout.BeginHorizontal();
        if (DrawPrimaryButton("主按钮 Primary")) Debug.Log("[StyleShowcase] Primary clicked");
        if (DrawSuccessButton("成功按钮 Success")) Debug.Log("[StyleShowcase] Success clicked");
        if (DrawWarnButton("警告按钮 Warn")) Debug.Log("[StyleShowcase] Warn clicked");
        if (DrawDangerButton("危险按钮 Danger")) Debug.Log("[StyleShowcase] Danger clicked");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (DrawFlatButton("扁平按钮 Flat")) Debug.Log("[StyleShowcase] Flat clicked");
        if (DrawBackButton()) Debug.Log("[StyleShowcase] Back clicked");
        if (DrawIconButton("⚙️", "设置")) Debug.Log("[StyleShowcase] Icon clicked");
        if (DrawIconButton("📋", "复制")) Debug.Log("[StyleShowcase] Copy clicked");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 2. 标签 & 快捷键 ─────────────────────────
        DrawSection("标签 & 快捷键 Tags & KeyCaps", ClrCatPurple);

        EditorGUILayout.BeginHorizontal();
        DrawTag("默认标签");
        DrawTag("绿色", ClrSuccess);
        DrawTag("橙色", ClrWarning);
        DrawTag("红色", ClrError);
        DrawTag("蓝", ClrInfo);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        DrawTags(new[] { "Unity", "编辑器", "C#", "工具" });

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        DrawLabelDim("快捷键示例：");
        DrawKeyCap("Ctrl");
        GUILayout.Label("+", StLabelDim);
        DrawKeyCap("S");
        GUILayout.Label("  →  保存", StLabelDim);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 3. 文本层级 ──────────────────────────────
        DrawSection("文本层级 Typography", ClrCatTeal);

        DrawTitle("大标题 Title · 16px Bold");
        DrawSubtitle("副标题 Subtitle · 12px Dim");
        DrawBody("正文 Body · 12px <b>支持富文本</b> 和 <color=#5AB0F0>颜色标记</color>。");
        DrawLabelDim("暗淡标签 LabelDim · 11px");
        DrawLabelSmall("小标签 LabelSmall · 10px");

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 4. 分段 & 容器 ────────────────────────────
        DrawSection("分段 & 容器 Sections & Containers", ClrCatOrange);

        DrawSection("这是一个分段标题");
        DrawLabelDim("左侧带 3px 色条装饰，可自定义颜色。");

        EditorGUILayout.Space(4);

        DrawSection("自定义颜色分段", ClrCatPink);
        DrawLabelDim("使用 DrawSection(\"标题\", ClrCatPink)");

        EditorGUILayout.Space(4);

        // ── 帮助框 ────────────────────────────────────
        DrawToolHelpBox("ℹ️  这是一个 Info 帮助框，用于显示提示信息。");
        DrawToolHelpBox("⚠️  这是一个 Warning 帮助框，用于显示警告。", MessageType.Warning);
        DrawToolHelpBox("❌  这是一个 Error 帮助框，用于显示错误。", MessageType.Error);

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 5. 卡片 ───────────────────────────────────
        DrawSection("卡片 Cards", ClrCatGreen);

        EditorGUILayout.BeginHorizontal();
        // 卡片 1
        BeginCard(60, GUILayout.Width(position.width / 3 - 20));
        DrawLabelDim("卡片内容");
        DrawBody("使用 BeginCard / EndCard");
        EndCard();

        // 卡片 2
        GUILayout.Space(8);
        BeginCard(60, GUILayout.Width(position.width / 3 - 20));
        DrawLabelDim("带顶部色条");
        DrawBody("蓝色 Accent 装饰条");
        EndCard();

        // 卡片 3
        GUILayout.Space(8);
        BeginCard(60, GUILayout.Width(position.width / 3 - 20));
        DrawLabelDim("可嵌套内容");
        DrawBody("任意 DrawXxx 方法");
        EndCard();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 6. 统计卡片 ──────────────────────────────
        DrawSection("统计卡片 Stat Cards", ClrAccent);

        EditorGUILayout.BeginHorizontal();
        DrawStatCard("总文件数", "1,234", ClrAccent,
            GUILayout.Width(position.width / 4 - 16));
        GUILayout.Space(8);
        DrawStatCard("已处理", "987", ClrSuccess,
            GUILayout.Width(position.width / 4 - 16));
        GUILayout.Space(8);
        DrawStatCard("警告数", "12", ClrWarning,
            GUILayout.Width(position.width / 4 - 16));
        GUILayout.Space(8);
        DrawStatCard("错误数", "3", ClrError,
            GUILayout.Width(position.width / 4 - 16));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 7. 进度条 ────────────────────────────────
        DrawSection("进度条 Progress Bars", ClrCatTeal);

        DrawProgressBar(_demoProgress, $"处理进度  {_demoProgress:P0}", ClrAccent);
        EditorGUILayout.Space(2);
        DrawProgressBar(1.0f, "已完成 100%", ClrSuccess);
        EditorGUILayout.Space(2);
        DrawProgressBar(0.3f, "警告进度 30%", ClrWarning);
        EditorGUILayout.Space(2);
        DrawProgressBar(0.1f, "错误进度 10%", ClrError);

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("-", GUILayout.Width(28)))
            _demoProgress = Mathf.Max(0, _demoProgress - 0.05f);
        GUILayout.Label($"{_demoProgress:P0}", StLabelDim, GUILayout.Width(50));
        if (GUILayout.Button("+", GUILayout.Width(28)))
            _demoProgress = Mathf.Min(1, _demoProgress + 0.05f);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 8. 分组框 ────────────────────────────────
        DrawSection("分组框 Group Boxes", ClrCatPurple);

        BeginGroupBox("分组标题 A");
        DrawLabelDim("分组框带标题头 + 缩进内容区。");
        DrawBody("适用于将相关设置项归类展示。");
        EndGroupBox();

        BeginGroupBox("分组标题 B");
        DrawLabelDim("嵌套使用也不会冲突。");
        DrawToggle("开关示例", _demoToggle1);
        EndGroupBox();

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 9. 列表 & 选择 ───────────────────────────
        DrawSection("列表 & 选择 Lists & Selection", ClrCatOrange);

        string[] items = { "AssetBookmarks", "UsingManager", "FileRenameTool", "EncodingConverter", "FontReplacer" };
        for (int i = 0; i < items.Length; i++)
        {
            if (DrawSelectableItem(items[i], _selectedItem == i))
                _selectedItem = i;
        }

        EditorGUILayout.Space(4);

        DrawLabelDim("可删除列表项：");
        DrawRemovableListItem("临时文件.txt", () => Debug.Log("[StyleShowcase] Remove clicked"));

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 10. 开关 ─────────────────────────────────
        DrawSection("开关 Toggles", ClrCatGreen);

        _demoToggle1 = DrawToggle("启用自动刷新", _demoToggle1);
        _demoToggle2 = DrawToggle("显示隐藏文件", _demoToggle2);
        DrawToggle("只读模式（禁用）", false);

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 11. 搜索框 ──────────────────────────────
        DrawSection("搜索框 Search Field", ClrCatTeal);

        _searchDemo = DrawSearchField(_searchDemo, "输入关键词搜索...");
        if (!string.IsNullOrEmpty(_searchDemo))
            DrawLabelDim($"当前搜索：{_searchDemo}");

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 12. 拖拽区域 ────────────────────────────
        DrawSection("拖拽区域 Drop Area", ClrCatPink);

        DrawDropArea("拖拽文件到此处");

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 13. 颜色方块 ────────────────────────────
        DrawSection("颜色方块 Color Swatches", ClrCatYellow);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("主色板：", StLabel, GUILayout.Width(60));
        DrawColorSwatch(ClrAccent);
        DrawColorSwatch(ClrSuccess);
        DrawColorSwatch(ClrWarning);
        DrawColorSwatch(ClrError);
        DrawColorSwatch(ClrInfo);
        GUILayout.Space(12);
        EditorGUILayout.LabelField("分类色：", StLabel, GUILayout.Width(60));
        DrawColorSwatch(ClrCatDefault);
        DrawColorSwatch(ClrCatGreen);
        DrawColorSwatch(ClrCatOrange);
        DrawColorSwatch(ClrCatPurple);
        DrawColorSwatch(ClrCatRed);
        DrawColorSwatch(ClrCatTeal);
        DrawColorSwatch(ClrCatPink);
        DrawColorSwatch(ClrCatYellow);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawDivider();

        // ── 14. 分割线 ──────────────────────────────
        DrawSection("分割线 Dividers", ClrTextDim);

        DrawLabelDim("标准分割线（1px）：");
        DrawDivider();
        DrawLabelDim("粗分割线（3px）：");
        DrawDivider(3f);
        DrawLabelDim("渐变装饰条：");
        DrawGradientBar(ClrAccent, ClrSuccess, 6f);

        EditorGUILayout.Space(10);

        // ── 底部说明 ─────────────────────────────────
        DrawToolHelpBox(
            "💡 以上所有样式均由 ToolEditorWindow 基类提供。" +
            "新工具只需继承 ToolEditorWindow，即可直接使用这些统一风格的控件。" +
            "参考 _NewToolTemplate.cs.txt 模板快速开始。");

        EditorGUILayout.Space(8);
    }

    // ═══════════════════════════════════════════════════
    //  状态栏
    // ═══════════════════════════════════════════════════

    protected override void DrawStatusBarContent()
    {
        DrawStatusText("ToolEditorWindow 样式模板 v1.0  ·  ", ClrTextDim);
        DrawStatusText("🎨 展示所有内置控件", ClrAccent);
    }
}


