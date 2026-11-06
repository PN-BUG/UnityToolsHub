#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>框架文档中心。正文为仓库内 Markdown；编辑草稿跨域重载保留。</summary>
[ToolInfo("框架文档中心", "AI工具", Description = "离线阅读、全文搜索和编辑框架 Markdown 文档；显式保存写入文档文件，支持冲突检测。",
    Icon = "📖", Tags = new[] { "文档", "Markdown", "编辑", "帮助", "框架" }, Priority = -90)]
public sealed class FrameworkDocumentationWindow : EditorWindow
{
    public enum ThemeMode { FollowUnity, Light, Dark }
    private const string ThemePreferenceKey = "UnityFramework.Documentation.Theme";
    private ThemeMode themeMode;
    private bool appliedDarkTheme;
    private ToolbarMenu themeMenu;
    public ThemeMode CurrentTheme => themeMode;
    public bool UsesDarkTheme => themeMode == ThemeMode.Dark || (themeMode == ThemeMode.FollowUnity && EditorGUIUtility.isProSkin);
    [SerializeField] private string documentRoot;
    [SerializeField] private string selectedPath;
    [SerializeField] private string buffer;
    [SerializeField] private string diskContent;
    [SerializeField] private string search = "";
    [SerializeField] private bool editMode;
    private List<FrameworkDocumentation.Page> pages = new List<FrameworkDocumentation.Page>();
    private FrameworkMarkdownView markdown;
    private ScrollView navigation;
    private ScrollView reader;
    private TextField editor;
    private Label heading;
    private Label status;
    private Button save;
    private ToolbarToggle editToggle;

    [Serializable]
    private sealed class Draft
    {
        public string root;
        public string path;
        public string text;
        public string expected;
    }

    private static string DraftKey => "UnityFramework.Documentation.Draft." + Application.dataPath;

    [MenuItem("UnityFramework/文档中心", false, -99)]
    public static void ShowWindow()
    {
        var window = GetWindow<FrameworkDocumentationWindow>("框架文档中心");
        window.minSize = new Vector2(860, 540);
        window.Show();
    }

    private void OnEnable()
    {
        themeMode = (ThemeMode)Mathf.Clamp(EditorPrefs.GetInt(ThemePreferenceKey, 0), 0, 2);
        saveChangesMessage = "文档有未保存的更改。保存到 Markdown 文件后，可通过框架仓库提交更新。";
        if (string.IsNullOrEmpty(selectedPath))
        {
            string json = SessionState.GetString(DraftKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                var draft = JsonUtility.FromJson<Draft>(json);
                documentRoot = draft.root;
                selectedPath = draft.path;
                buffer = draft.text;
                diskContent = draft.expected;
                editMode = true;
            }
        }
        hasUnsavedChanges = !string.IsNullOrEmpty(selectedPath) && buffer != diskContent;
    }

    private void OnDisable()
    {
        markdown?.Dispose();
    }

    private void OnInspectorUpdate()
    {
        if (themeMenu != null && themeMode == ThemeMode.FollowUnity && appliedDarkTheme != UsesDarkTheme)
            ApplyTheme();
    }

    /// <summary>仅更改文档窗口的外观及本机 EditorPrefs；不修改 Unity 全局主题或文档正文。</summary>
    public void SetThemeMode(ThemeMode mode)
    {
        if (!Enum.IsDefined(typeof(ThemeMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        themeMode = mode;
        EditorPrefs.SetInt(ThemePreferenceKey, (int)mode);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        appliedDarkTheme = UsesDarkTheme;
        rootVisualElement.EnableInClassList("fw-docs-manual", themeMode != ThemeMode.FollowUnity);
        rootVisualElement.EnableInClassList("fw-docs-dark", appliedDarkTheme);
        rootVisualElement.EnableInClassList("fw-docs-light", !appliedDarkTheme);
        if (themeMenu != null)
            themeMenu.text = themeMode == ThemeMode.FollowUnity
                ? "外观：跟随 Unity（" + (appliedDarkTheme ? "夜间" : "日间") + "）"
                : themeMode == ThemeMode.Light ? "外观：日间" : "外观：夜间";
        if (markdown != null && reader != null && !editMode && !string.IsNullOrEmpty(selectedPath))
        {
            var offset = reader.scrollOffset;
            markdown.Render(reader, buffer, documentRoot, selectedPath, Navigate, appliedDarkTheme);
            reader.schedule.Execute(() => reader.scrollOffset = offset);
        }
    }

    public void CreateGUI()
    {
        rootVisualElement.Clear();
        try
        {
            // 重新定位包，避免移动工程后使用旧的绝对路径。
            documentRoot = FrameworkDocumentation.GetDocumentationRoot();
            pages = FrameworkDocumentation.ReadPages(documentRoot);
            markdown = new FrameworkMarkdownView();
            BuildInterface();
            if (string.IsNullOrEmpty(selectedPath))
            {
                var first = pages.FirstOrDefault(p => p.RelativePath == "index.md") ?? pages.FirstOrDefault();
                if (first != null)
                    LoadPage(first.RelativePath);
            }
            else
                DrawPage();
            DrawNavigation();
        }
        catch (Exception ex)
        {
            rootVisualElement.Add(new HelpBox(ex.Message, HelpBoxMessageType.Error));
        }
    }

    private void BuildInterface()
    {
        string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(
            (Path.GetDirectoryName(scriptPath) + "/FrameworkDocumentationTheme.uss").Replace('\\', '/'));
        if (styles != null && !rootVisualElement.styleSheets.Contains(styles))
            rootVisualElement.styleSheets.Add(styles);
        var toolbar = new Toolbar();
        toolbar.Add(new Label("UnityFramework 文档中心") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 6, marginRight = 16 } });
        toolbar.Add(new ToolbarButton(RefreshPages) { text = "刷新" });
        toolbar.Add(new ToolbarButton(CreatePage) { text = "新建文档" });
        toolbar.Add(new ToolbarButton(() => EditorUtility.RevealInFinder(documentRoot)) { text = "打开文档目录" });
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1;
        toolbar.Add(spacer);
        themeMenu = new ToolbarMenu { tooltip = "切换日间/夜间外观，默认跟随 Unity；只影响文档中心。" };
        foreach (ThemeMode mode in Enum.GetValues(typeof(ThemeMode)))
        {
            var option = mode;
            string label = mode == ThemeMode.FollowUnity ? "跟随 Unity" : mode == ThemeMode.Light ? "日间" : "夜间";
            themeMenu.menu.AppendAction(label, _ => SetThemeMode(option),
                _ => themeMode == option ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
        }
        toolbar.Add(themeMenu);
        rootVisualElement.Add(toolbar);
        var split = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);
        split.style.flexGrow = 1;
        split.style.flexShrink = 1;
        split.style.flexBasis = 0;
        split.style.minHeight = 0;
        var sidebar = new VisualElement();
        sidebar.style.minWidth = 180;
        sidebar.style.minHeight = 0;
        var searchField = new ToolbarSearchField();
        searchField.value = search;
        searchField.style.marginTop = searchField.style.marginBottom = 8;
        searchField.RegisterValueChangedCallback(evt => { search = evt.newValue; DrawNavigation(); });
        sidebar.Add(searchField);
        navigation = new ScrollView();
        navigation.style.flexGrow = 1;
        navigation.style.flexShrink = 1;
        navigation.style.minHeight = 0;
        sidebar.Add(navigation);
        split.Add(sidebar);
        var content = new VisualElement();
        content.style.minWidth = 460;
        content.style.minHeight = 0;
        var actions = new Toolbar();
        editToggle = new ToolbarToggle { text = "编辑 Markdown", value = editMode };
        editToggle.RegisterValueChangedCallback(evt => { editMode = evt.newValue; DrawPage(); });
        actions.Add(editToggle);
        save = new ToolbarButton(() => TrySave()) { text = "保存  Ctrl/Cmd+S" };
        actions.Add(save);
        actions.Add(new ToolbarButton(ReloadPage) { text = "重新载入" });
        actions.Add(new ToolbarButton(OpenExternal) { text = "外部编辑" });
        actions.Add(new ToolbarButton(() => EditorGUIUtility.systemCopyBuffer = buffer ?? "") { text = "复制正文" });
        content.Add(actions);
        heading = new Label();
        heading.style.marginLeft = 16;
        heading.style.marginTop = heading.style.marginBottom = 8;
        heading.style.whiteSpace = WhiteSpace.Normal;
        content.Add(heading);
        reader = new ScrollView();
        reader.style.flexGrow = 1;
        reader.style.flexShrink = 1;
        reader.style.flexBasis = 0;
        reader.style.minHeight = 0;
        reader.style.paddingLeft = reader.style.paddingRight = 20;
        reader.style.paddingBottom = 24;
        content.Add(reader);
        editor = new TextField { multiline = true, isDelayed = false, verticalScrollerVisibility = ScrollerVisibility.Auto };
        editor.style.whiteSpace = WhiteSpace.Normal;
        editor.style.flexGrow = 1;
        editor.style.flexShrink = 1;
        editor.style.flexBasis = 0;
        editor.style.marginLeft = editor.style.marginRight = 12;
        editor.style.marginBottom = 12;
        editor.style.minHeight = 0;
        editor.RegisterValueChangedCallback(evt =>
        {
            buffer = evt.newValue;
            UpdateDirty();
        });
        content.Add(editor);
        split.Add(content);
        rootVisualElement.Add(split);
        status = new Label();
        status.style.paddingLeft = 10;
        status.style.paddingTop = status.style.paddingBottom = 6;
        status.style.whiteSpace = WhiteSpace.Normal;
        rootVisualElement.Add(status);
        rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.S && (evt.ctrlKey || evt.commandKey))
            {
                TrySave();
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);
        ApplyTheme();
    }

    private void DrawNavigation()
    {
        navigation.Clear();
        var visible = pages.Where(p => string.IsNullOrWhiteSpace(search)
            || (p.Title + "\n" + p.RelativePath + "\n" + (p.RelativePath == selectedPath ? buffer : p.Content))
                .IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        navigation.Add(new Label($"{visible.Count} / {pages.Count} 篇文档") { style = { marginLeft = 10, marginBottom = 10 } });
        foreach (var group in visible.GroupBy(p => p.RelativePath.StartsWith("Legacy/") ? "历史资料 / " + (Path.GetDirectoryName(p.RelativePath)?.Replace('\\', '/').Substring(6) ?? "") : p.RelativePath.StartsWith("Guides/") ? "当前使用指南" : "入口与维护"))
        {
            navigation.Add(new Label(group.Key.TrimEnd('/', ' ')) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 8, marginTop = 10, marginBottom = 5 } });
            foreach (var page in group)
            {
                string path = page.RelativePath;
                var button = new Button(() => Navigate(path)) { text = page.Title, tooltip = path };
                button.style.whiteSpace = WhiteSpace.Normal;
                button.style.minHeight = 28;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (path == selectedPath)
                    button.style.unityFontStyleAndWeight = FontStyle.Bold;
                navigation.Add(button);
            }
        }
    }

    private void Navigate(string path)
    {
        if (path == selectedPath || !CanLeave())
            return;
        try { LoadPage(path); DrawNavigation(); }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void LoadPage(string path)
    {
        string text = FrameworkDocumentation.LoadDocument(documentRoot, path);
        selectedPath = path;
        diskContent = buffer = text;
        SessionState.EraseString(DraftKey);
        hasUnsavedChanges = false;
        DrawPage();
        reader.scrollOffset = Vector2.zero;
    }

    private void DrawPage()
    {
        heading.text = selectedPath ?? "请选择或新建文档";
        reader.style.display = editMode ? DisplayStyle.None : DisplayStyle.Flex;
        editor.style.display = editMode ? DisplayStyle.Flex : DisplayStyle.None;
        editor.SetValueWithoutNotify(buffer ?? "");
        editToggle.SetValueWithoutNotify(editMode);
        if (!editMode)
            markdown.Render(reader, buffer, documentRoot, selectedPath ?? "index.md", Navigate, UsesDarkTheme);
        UpdateDirty();
    }

    private void UpdateDirty()
    {
        hasUnsavedChanges = !string.IsNullOrEmpty(selectedPath) && buffer != diskContent;
        save.SetEnabled(hasUnsavedChanges);
        titleContent.text = hasUnsavedChanges ? "框架文档中心 *" : "框架文档中心";
        status.text = hasUnsavedChanges ? "草稿未保存；切换阅读可预览。保存时会检查磁盘冲突。" : "Markdown 保存在框架仓库；更新后请随代码一起提交。";
        if (hasUnsavedChanges)
            SessionState.SetString(DraftKey, JsonUtility.ToJson(new Draft { root = documentRoot, path = selectedPath, text = buffer, expected = diskContent }));
        else
            SessionState.EraseString(DraftKey);
    }

    private bool TrySave()
    {
        if (!hasUnsavedChanges)
            return true;
        try
        {
            FrameworkDocumentation.SaveDocument(documentRoot, selectedPath, buffer, diskContent);
            diskContent = buffer;
            UpdateDirty();
            AssetDatabase.Refresh();
            pages = FrameworkDocumentation.ReadPages(documentRoot);
            DrawNavigation();
            status.text = "已保存：" + selectedPath;
            return true;
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            return false;
        }
    }

    public override void SaveChanges()
    {
        // 保存失败时不清理 dirty 或恢复草稿，关闭后仍可从当前 Editor 会话恢复。
        if (TrySave())
            base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        buffer = diskContent;
        SessionState.EraseString(DraftKey);
        base.DiscardChanges();
    }

    private bool CanLeave()
    {
        if (!hasUnsavedChanges)
            return true;
        int result = EditorUtility.DisplayDialogComplex("文档尚未保存", "保存当前文档后继续？", "保存", "取消", "放弃更改");
        if (result == 0)
            return TrySave();
        if (result == 2)
        {
            DiscardChanges();
            return true;
        }
        return false;
    }

    private void RefreshPages()
    {
        // 刷新目录不替换当前草稿，也不提前更新冲突检测的基线。
        try
        {
            pages = FrameworkDocumentation.ReadPages(documentRoot);
            DrawNavigation();
            status.text = "目录已刷新；当前打开页保持原内容。读取外部更新请点“重新载入”。";
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void ReloadPage()
    {
        if (!string.IsNullOrEmpty(selectedPath) && CanLeave())
        {
            try { LoadPage(selectedPath); }
            catch (Exception ex) { Report(ex.Message); }
        }
    }

    private void OpenExternal()
    {
        if (!string.IsNullOrEmpty(selectedPath) && CanLeave())
            EditorUtility.OpenWithDefaultApp(FrameworkDocumentation.ResolvePath(documentRoot, selectedPath));
    }

    private void CreatePage()
    {
        if (!CanLeave())
            return;
        string full = EditorUtility.SaveFilePanel("新建 Markdown 文档", documentRoot, "新文档", "md");
        if (string.IsNullOrEmpty(full))
            return;
        try
        {
            string prefix = Path.GetFullPath(documentRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(full);
            if (!path.StartsWith(prefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("请将新文档保存在框架 Documentation 目录内。");
            string relative = path.Substring(prefix.Length).Replace('\\', '/');
            FrameworkDocumentation.SaveDocument(documentRoot, relative, "# " + Path.GetFileNameWithoutExtension(full) + "\n\n", null);
            AssetDatabase.Refresh();
            pages = FrameworkDocumentation.ReadPages(documentRoot);
            editMode = true;
            LoadPage(relative);
            DrawNavigation();
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void Report(string message)
    {
        if (status != null)
            status.text = message;
        EditorUtility.DisplayDialog("文档中心", message, "确定");
    }
}
#endif
