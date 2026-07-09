#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

/// <summary>
/// UnityFramework 主页入口 — 框架仪表盘
/// 汇总展示框架信息、快捷入口、Runtime 模块概览、文档链接。
/// 继承 ToolEditorWindow 复用深色主题与样式系统。
/// </summary>
[ToolInfo("框架主页", "框架初始化",
    Description = "UnityFramework 主页入口 — 框架信息概览、快捷入口、模块状态与文档链接。",
    Icon = "🏠", Tags = new[] { "主页", "入口", "概览" })]
public class UnityFrameworkHomeWindow : ToolEditorWindow
{
    private const string PackageAssetPath = "Assets/UnityFramework";

    private static readonly ModuleInfo[] RuntimeModules =
    {
        new ModuleInfo("输入系统",   "InputSystem",     "InputControl / InputSystemModule",         ClrCatTeal),
        new ModuleInfo("UI 管理系统", "UISystem",        "UIManager / UIBase / UI 组件库",            ClrCatPurple),
        new ModuleInfo("事件系统",   "ZEventSystem",    "EventCenter / 事件订阅与分发",              ClrCatOrange),
        new ModuleInfo("音频系统",   "AudioSystem",     "AudioManager / 音频播放与控制",             ClrCatGreen),
        new ModuleInfo("对象池",     "Pool",            "PoolManager / 组件对象池",                  ClrCatDefault),
        new ModuleInfo("数据系统",   "DataSystem",      "Excel 导入 / 静态配置 / 动态数据",          ClrCatYellow),
        new ModuleInfo("场景切换",   "SceneTransition", "场景过渡动画与交互控制",                    ClrCatPink),
        new ModuleInfo("资源加载",   "ResourceLoad",    "AddressableTool / 可寻址资源封装",          ClrCatRed),
    };

    private static readonly QuickEntry[] QuickEntries =
    {
        new QuickEntry("初始化设置", "⚙", "项目初始化工具：目录模板、依赖包、项目设置",
            "Tools/项目初始化工具", ClrCatGreen),
        new QuickEntry("模块管理", "📦", "启用/禁用框架模块、配置实现",
            "UnityFramework/模块管理", ClrCatPurple),
        new QuickEntry("工具集主面板", "🛠", "打开 UnityToolsHub 工具集 (Ctrl+Shift+E)",
            "UnityToolsHub/主面板 %#e", ClrCatDefault),
        new QuickEntry("项目打包", "📦", "构建与打包工具",
            "UnityFramework/项目打包工具", ClrCatOrange),
    };

    private UnityEditor.PackageManager.PackageInfo _packageInfo;
    private string _version = "—";
    private string _description = "游戏开发框架";
    private string _docUrl = "";
    private string _repoUrl = "";

    protected override string ToolTitle => "UnityFramework 主页";
    protected override string ToolIcon => "🏠";

    [MenuItem("UnityFramework/主页", false, -100)]
    [MenuItem("UnityToolsHub/UnityFramework 主页")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<UnityFrameworkHomeWindow>(true, "UnityFramework 主页", true);
        wnd.minSize = new Vector2(560, 600);
        wnd.Show();
    }

    protected override void OnToolEnable()
    {
        LoadPackageInfo();
    }

    protected override void DrawToolContent()
    {
        DrawGradientBar(ClrAccent, ClrBtnHover);
        GUILayout.Space(4);

        DrawFrameworkHeader();
        GUILayout.Space(16);

        DrawQuickAccessSection();
        GUILayout.Space(16);

        DrawRuntimeModuleSection();
        GUILayout.Space(16);

        DrawDocumentationSection();
        GUILayout.Space(8);

        DrawFooter();
    }

    #region 框架头部

    private void DrawFrameworkHeader()
    {
        var rect = BeginCard(0, GUILayout.ExpandWidth(true));
        GUILayout.Space(10);

        // Logo + 版本
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(4);
        StTitle.fontSize = 24;
        StTitle.normal.textColor = ClrTextBright;
        GUILayout.Label("<color=#6699FF><b>Unity</b></color>Framework", StTitle);
        StTitle.fontSize = 16;
        GUILayout.FlexibleSpace();
        StLabelDim.fontSize = 11;
        GUILayout.Label($"v{_version}", StLabelDim);
        StLabelDim.fontSize = 11;
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6);

        // 描述
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(4);
        StBody.normal.textColor = ClrText;
        GUILayout.Label(_description, StBody);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);
        EndCard();
    }

    #endregion

    #region 快捷入口

    private void DrawQuickAccessSection()
    {
        DrawSection("快捷入口", ClrAccent);

        float cardWidth = (position.width - ContentPadding * 2 - 12) * 0.5f;
        for (int i = 0; i < QuickEntries.Length; i += 2)
        {
            EditorGUILayout.BeginHorizontal();
            DrawQuickEntryCard(QuickEntries[i], cardWidth);
            GUILayout.Space(12);
            if (i + 1 < QuickEntries.Length)
                DrawQuickEntryCard(QuickEntries[i + 1], cardWidth);
            else
                GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (i + 2 < QuickEntries.Length)
                GUILayout.Space(8);
        }
    }

    private void DrawQuickEntryCard(QuickEntry entry, float width)
    {
        var rect = GUILayoutUtility.GetRect(width, 64, GUILayout.Width(width), GUILayout.Height(64));
        bool hover = rect.Contains(Event.current.mousePosition);
        bool clicked = Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition);

        EditorGUI.DrawRect(rect, hover ? ClrItemHover : ClrCardBg);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 6, 3, rect.height - 12), entry.accent);

        // 图标
        StIconLabel.fontSize = 20;
        StIconLabel.normal.textColor = entry.accent;
        var iconRect = new Rect(rect.x + 10, rect.y + 8, 28, 28);
        GUI.Label(iconRect, entry.icon, StIconLabel);
        StIconLabel.fontSize = 16;

        // 标题
        StLabel.fontSize = 13;
        StLabel.fontStyle = FontStyle.Bold;
        StLabel.normal.textColor = ClrTextBright;
        var nameRect = new Rect(rect.x + 42, rect.y + 6, rect.width - 50, 20);
        GUI.Label(nameRect, entry.name, StLabel);
        StLabel.fontSize = 12;
        StLabel.fontStyle = FontStyle.Normal;

        // 描述
        StLabelSmall.normal.textColor = ClrTextDim;
        var descRect = new Rect(rect.x + 42, rect.y + 28, rect.width - 50, 30);
        GUI.Label(descRect, entry.description, StLabelSmall);

        if (clicked)
        {
            Event.current.Use();
            if (!EditorApplication.ExecuteMenuItem(entry.menuPath))
                Debug.LogWarning($"[UnityFramework Home] 无法执行菜单: {entry.menuPath}");
        }
    }

    #endregion

    #region Runtime 模块概览

    private void DrawRuntimeModuleSection()
    {
        DrawSection("Runtime 模块", ClrCatGreen);

        for (int i = 0; i < RuntimeModules.Length; i++)
        {
            var mod = RuntimeModules[i];
            var rect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            bool hover = rect.Contains(Event.current.mousePosition);
            if (hover) EditorGUI.DrawRect(rect, ClrItemHover);

            // 色条
            EditorGUI.DrawRect(new Rect(rect.x + 4, rect.y + 6, 3, rect.height - 12), mod.accent);

            // 名称
            StLabel.fontSize = 12;
            StLabel.fontStyle = FontStyle.Bold;
            StLabel.normal.textColor = ClrText;
            GUI.Label(new Rect(rect.x + 14, rect.y + 4, 100, 22), mod.displayName, StLabel);
            StLabel.fontStyle = FontStyle.Normal;

            // 文件夹名
            StLabelSmall.normal.textColor = ClrTextDim;
            GUI.Label(new Rect(rect.x + 120, rect.y + 4, 100, 22), mod.folderName, StLabelSmall);

            // 描述
            StLabelSmall.normal.textColor = ClrTextDim;
            GUI.Label(new Rect(rect.x + 220, rect.y + 4, rect.width - 230, 22), mod.description, StLabelSmall);

            if (i + 1 < RuntimeModules.Length)
                GUILayout.Space(2);
        }
    }

    #endregion

    #region 文档链接

    private void DrawDocumentationSection()
    {
        DrawSection("文档与链接", ClrCatPurple);

        // 飞书文档
        if (!string.IsNullOrEmpty(_docUrl))
            DrawLinkRow("📖", "飞书文档", _docUrl);

        // Gitee 仓库
        if (!string.IsNullOrEmpty(_repoUrl))
            DrawLinkRow("🔗", "Gitee 仓库", _repoUrl);
    }

    private void DrawLinkRow(string icon, string label, string url)
    {
        var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);
        bool clicked = Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition);

        if (hover) EditorGUI.DrawRect(rect, ClrItemHover);

        StIconLabel.fontSize = 14;
        StIconLabel.normal.textColor = ClrAccent;
        GUI.Label(new Rect(rect.x + 8, rect.y, 24, rect.height), icon, StIconLabel);
        StIconLabel.fontSize = 16;

        StLabel.fontSize = 12;
        StLabel.normal.textColor = hover ? ClrBtnHover : ClrText;
        GUI.Label(new Rect(rect.x + 32, rect.y, 100, rect.height), label, StLabel);

        StLabelSmall.normal.textColor = ClrTextDim;
        var urlRect = new Rect(rect.x + 120, rect.y, rect.width - 130, rect.height);
        GUI.Label(urlRect, url, StLabelSmall);

        if (clicked)
        {
            Event.current.Use();
            Application.OpenURL(url);
        }
    }

    #endregion

    #region 底部

    private void DrawFooter()
    {
        EditorGUILayout.Space(8);
        DrawDivider();
        StLabelSmall.normal.textColor = ClrTextDim;
        StLabelSmall.alignment = TextAnchor.MiddleCenter;
        GUILayout.Label($"UnityFramework v{_version}  ·  by zko", StLabelSmall);
        StLabelSmall.alignment = TextAnchor.MiddleLeft;
    }

    #endregion

    #region 包信息读取

    private void LoadPackageInfo()
    {
        try
        {
            _packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(PackageAssetPath);
            if (_packageInfo != null)
            {
                _version = _packageInfo.version;
                _description = _packageInfo.description;
                _docUrl = _packageInfo.author?.url ?? "";
                _repoUrl = "https://gitee.com/heqian1002/unity-framework";
            }
        }
        catch
        {
            // 回退：直接读取 package.json
            TryReadPackageJsonDirectly();
        }

        if (string.IsNullOrEmpty(_docUrl))
            _docUrl = "https://m01nl7uomo.feishu.cn/wiki/VnDQwoVdfinn68kWLXacqKIQnGd";
    }

    private void TryReadPackageJsonDirectly()
    {
        string jsonPath = Path.Combine(Application.dataPath, "UnityFramework/package.json");
        if (!File.Exists(jsonPath)) return;

        try
        {
            string json = File.ReadAllText(jsonPath);
            _version = ExtractJsonValue(json, "version");
            _description = ExtractJsonValue(json, "description");
        }
        catch { }
    }

    private static string ExtractJsonValue(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "—";
        idx = json.IndexOf(':', idx + pattern.Length);
        if (idx < 0) return "—";
        idx = json.IndexOf('"', idx + 1);
        if (idx < 0) return "—";
        int end = json.IndexOf('"', idx + 1);
        if (end < 0) return "—";
        return json.Substring(idx + 1, end - idx - 1);
    }

    #endregion

    #region 数据结构

    private struct ModuleInfo
    {
        public readonly string displayName;
        public readonly string folderName;
        public readonly string description;
        public readonly Color accent;

        public ModuleInfo(string displayName, string folderName, string description, Color accent)
        {
            this.displayName = displayName;
            this.folderName = folderName;
            this.description = description;
            this.accent = accent;
        }
    }

    private struct QuickEntry
    {
        public readonly string name;
        public readonly string icon;
        public readonly string description;
        public readonly string menuPath;
        public readonly Color accent;

        public QuickEntry(string name, string icon, string description, string menuPath, Color accent)
        {
            this.name = name;
            this.icon = icon;
            this.description = description;
            this.menuPath = menuPath;
            this.accent = accent;
        }
    }

    #endregion
}
#endif
