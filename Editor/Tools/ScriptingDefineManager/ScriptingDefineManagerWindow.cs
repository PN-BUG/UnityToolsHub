using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 管理 Assets/Scripts 中使用的自定义条件编译符号。
/// </summary>
[ToolInfo("自定义宏管理", "代码工具",
    Description = "扫描条件编译宏，支持按目录查看、切换启用状态、备注及忽略规则配置。",
    Icon = "⚙",
    Tags = new[] { "宏定义", "条件编译", "代码" },
    Priority = 30)]
public sealed class ScriptingDefineManagerWindow : EditorWindow
{
    private static readonly Regex DirectiveRegex = new Regex(@"^\s*#\s*(if|elif)\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex SymbolRegex = new Regex(@"\b[A-Z][A-Z0-9_]*\b", RegexOptions.Compiled);
    private static readonly HashSet<string> Keywords = new HashSet<string>
    {
        "TRUE", "FALSE", "AND", "OR", "NOT"
    };

    private readonly List<DefineInfo> defines = new List<DefineInfo>();
    private Vector2 scrollPosition;
    private Vector2 folderScrollPosition;
    private string searchText = string.Empty;
    private string selectedScanFolder = string.Empty;
    private bool settingsExpanded;

    [MenuItem("UnityToolsHub/自定义宏管理", priority = 130)]
    private static void Open()
    {
        GetWindow<ScriptingDefineManagerWindow>("自定义宏管理");
    }

    private void OnEnable()
    {
        RefreshDefines();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawScanSettings();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "Scan folders: " + string.Join(", ", ScriptingDefineNotes.instance.GetScanFolders()) +
            ". Shows custom conditional-compilation symbols for " + GetBuildTargetGroup() + ". Changing a symbol recompiles scripts.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        DrawDirectorySidebar();
        EditorGUILayout.BeginVertical();
        if (defines.Count == 0)
        {
            EditorGUILayout.HelpBox("No custom symbols were found in the selected scan folder.", MessageType.None);
        }
        else
        {
            var enabledDefines = GetEnabledDefines();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var define in defines)
            {
                if (!string.IsNullOrWhiteSpace(searchText) &&
                    define.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0 &&
                    define.Note.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                DrawDefine(define, enabledDefines);
            }
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("刷新扫描", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            RefreshDefines();
        }

        GUILayout.FlexibleSpace();
        searchText = GUILayout.TextField(searchText, GUI.skin.FindStyle("ToolbarSearchTextField"), GUILayout.Width(200));
        if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(22)))
        {
            searchText = string.Empty;
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawScanSettings()
    {
        settingsExpanded = EditorGUILayout.Foldout(settingsExpanded, "扫描设置", true);
        if (!settingsExpanded)
        {
            return;
        }

        var settings = ScriptingDefineNotes.instance;
        settings.EnsureDefaults();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("扫描目录", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("填写项目内相对目录，例如 Assets/Scripts 或 Assets/Game。", MessageType.None);
        DrawStringList(settings.GetScanFolders(), "添加扫描目录", settings.SaveSettings);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("忽略宏", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("填写宏名可精确忽略；以 * 结尾可按前缀忽略，例如 DEBUG_*。Unity 内置宏始终会自动忽略。", MessageType.None);
        DrawStringList(settings.GetIgnoredSymbols(), "添加忽略宏", settings.SaveSettings);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("忽略文件夹名", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("路径中包含这些文件夹名的脚本不会参与扫描。可精确匹配，或以 * 结尾按前缀匹配，例如 ThirdParty*。", MessageType.None);
        DrawStringList(settings.GetIgnoredFolderNames(), "添加忽略文件夹", settings.SaveSettings);
        EditorGUILayout.EndVertical();
    }

    private void DrawDirectorySidebar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(190), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("扫描目录", EditorStyles.boldLabel);
        folderScrollPosition = EditorGUILayout.BeginScrollView(folderScrollPosition);
        DrawDirectoryButton("全部目录", string.Empty);
        foreach (var folder in ScriptingDefineNotes.instance.GetScanFolders().Where(folder => !string.IsNullOrWhiteSpace(folder)))
        {
            DrawDirectoryButton(folder, folder);
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawDirectoryButton(string label, string folder)
    {
        var isSelected = string.Equals(selectedScanFolder, folder, StringComparison.OrdinalIgnoreCase);
        var previousColor = GUI.backgroundColor;
        if (isSelected)
        {
            GUI.backgroundColor = new Color(0.45f, 0.7f, 1f);
        }
        if (GUILayout.Button(label, EditorStyles.miniButton))
        {
            selectedScanFolder = folder;
            RefreshDefines();
        }
        GUI.backgroundColor = previousColor;
    }

    private static void DrawStringList(List<string> values, string addButtonText, Action save)
    {
        var removeIndex = -1;
        for (var index = 0; index < values.Count; index++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var value = EditorGUILayout.TextField(values[index]);
            if (EditorGUI.EndChangeCheck())
            {
                values[index] = value.Trim();
                save();
            }
            if (GUILayout.Button("移除", GUILayout.Width(48)))
            {
                removeIndex = index;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (removeIndex >= 0)
        {
            values.RemoveAt(removeIndex);
            save();
        }
        if (GUILayout.Button(addButtonText, GUILayout.Width(100)))
        {
            values.Add(string.Empty);
            save();
        }
    }

    private void DrawDefine(DefineInfo define, HashSet<string> enabledDefines)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        var isEnabled = enabledDefines.Contains(define.Name);
        EditorGUI.BeginChangeCheck();
        var newEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(18));
        if (EditorGUI.EndChangeCheck())
        {
            SetDefineEnabled(define.Name, newEnabled);
            enabledDefines = GetEnabledDefines();
        }

        EditorGUILayout.LabelField(define.Name, EditorStyles.boldLabel, GUILayout.Width(220));
        GUILayout.Label(isEnabled ? "已启用" : "已关闭", isEnabled ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel, GUILayout.Width(48));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"{define.Files.Count} 个文件", EditorStyles.miniLabel, GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        var note = EditorGUILayout.TextField("备注", define.Note);
        if (EditorGUI.EndChangeCheck())
        {
            define.Note = note;
            ScriptingDefineNotes.instance.SetNote(define.Name, note);
        }

        define.LocationsExpanded = EditorGUILayout.Foldout(define.LocationsExpanded, $"使用位置 ({define.Files.Count})", true);
        if (define.LocationsExpanded)
        {
            foreach (var location in define.Files)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(location, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("定位", GUILayout.Width(48)))
                {
                    OpenScriptAtLocation(location);
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();
    }

    private static void OpenScriptAtLocation(string location)
    {
        var separatorIndex = location.LastIndexOf(':');
        if (separatorIndex <= 0 || !int.TryParse(location.Substring(separatorIndex + 1), out var lineNumber))
        {
            Debug.LogWarning("无法解析脚本位置：" + location);
            return;
        }

        var assetPath = location.Substring(0, separatorIndex);
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
        if (script == null)
        {
            Debug.LogWarning("未找到脚本：" + assetPath);
            return;
        }

        AssetDatabase.OpenAsset(script, lineNumber);
    }

    private void RefreshDefines()
    {
        defines.Clear();
        var symbols = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var projectDirectory = Directory.GetCurrentDirectory();
        var scanFolders = ScriptingDefineNotes.instance.GetScanFolders()
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Where(folder => string.IsNullOrEmpty(selectedScanFolder) || string.Equals(folder, selectedScanFolder, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var scanFolder in scanFolders)
        {
            var absoluteFolder = Path.GetFullPath(Path.Combine(projectDirectory, scanFolder));
            if (!IsProjectSubdirectory(projectDirectory, absoluteFolder) || !Directory.Exists(absoluteFolder))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(absoluteFolder, "*.cs", SearchOption.AllDirectories))
            {
                if (ScriptingDefineNotes.instance.IsInIgnoredFolder(path))
                {
                    continue;
                }

                var relativePath = path.Replace('\\', '/');
                var projectIndex = relativePath.IndexOf(projectDirectory.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase);
                if (projectIndex >= 0)
                {
                    relativePath = relativePath.Substring(projectIndex + projectDirectory.Length + 1);
                }

                var lines = File.ReadAllLines(path);
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var directive = DirectiveRegex.Match(lines[lineIndex]);
                    if (!directive.Success)
                    {
                        continue;
                    }

                    foreach (Match match in SymbolRegex.Matches(directive.Groups[2].Value))
                    {
                        var symbol = match.Value;
                        if (IsBuiltInSymbol(symbol) || ScriptingDefineNotes.instance.IsIgnored(symbol))
                        {
                            continue;
                        }

                        if (!symbols.TryGetValue(symbol, out var locations))
                        {
                            locations = new HashSet<string>();
                            symbols.Add(symbol, locations);
                        }
                        locations.Add($"{relativePath}:{lineIndex + 1}");
                    }
                }
            }
        }

        foreach (var pair in symbols.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            defines.Add(new DefineInfo(pair.Key, ScriptingDefineNotes.instance.GetNote(pair.Key), pair.Value.OrderBy(value => value).ToList()));
        }
        Repaint();
    }

    private static bool IsProjectSubdirectory(string projectDirectory, string candidateDirectory)
    {
        var projectRoot = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidateDirectory.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuiltInSymbol(string symbol)
    {
        return Keywords.Contains(symbol) ||
               symbol.StartsWith("UNITY_", StringComparison.Ordinal) ||
               symbol.StartsWith("ENABLE_", StringComparison.Ordinal) ||
               symbol.StartsWith("DISABLE_", StringComparison.Ordinal) ||
               symbol.StartsWith("NET_", StringComparison.Ordinal) ||
               symbol.StartsWith("CSHARP_", StringComparison.Ordinal) ||
               symbol.StartsWith("PLATFORM_", StringComparison.Ordinal) ||
               symbol.StartsWith("DOTNET_", StringComparison.Ordinal);
    }

    private static BuildTargetGroup GetBuildTargetGroup()
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        return group == BuildTargetGroup.Unknown ? BuildTargetGroup.Standalone : group;
    }

    private static HashSet<string> GetEnabledDefines()
    {
        var rawDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(GetBuildTargetGroup());
        return new HashSet<string>(rawDefines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    }

    private static void SetDefineEnabled(string define, bool enabled)
    {
        var buildTargetGroup = GetBuildTargetGroup();
        var enabledDefines = GetEnabledDefines();
        if (enabled)
        {
            enabledDefines.Add(define);
        }
        else
        {
            enabledDefines.Remove(define);
        }

        PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, string.Join(";", enabledDefines.OrderBy(value => value, StringComparer.Ordinal)));
    }

    private sealed class DefineInfo
    {
        public readonly string Name;
        public readonly List<string> Files;
        public string Note;
        public bool LocationsExpanded;

        public DefineInfo(string name, string note, List<string> files)
        {
            Name = name;
            Note = note;
            Files = files;
        }
    }
}

[FilePath("ProjectSettings/ScriptingDefineNotes.asset", FilePathAttribute.Location.ProjectFolder)]
internal sealed class ScriptingDefineNotes : ScriptableSingleton<ScriptingDefineNotes>
{
    [Serializable]
    private sealed class NoteEntry
    {
        public string define;
        [TextArea] public string note;
    }

    [SerializeField] private List<NoteEntry> entries = new List<NoteEntry>();
    [SerializeField] private List<string> scanFolders = new List<string>();
    [SerializeField] private List<string> ignoredSymbols = new List<string>();
    [SerializeField] private List<string> ignoredFolderNames = new List<string>();
    [SerializeField] private bool defaultIgnoreFoldersInitialized;

    public void EnsureDefaults()
    {
        var hasChanges = false;
        if (scanFolders == null)
        {
            scanFolders = new List<string>();
            hasChanges = true;
        }
        if (ignoredSymbols == null)
        {
            ignoredSymbols = new List<string>();
            hasChanges = true;
        }
        if (ignoredFolderNames == null)
        {
            ignoredFolderNames = new List<string>();
            hasChanges = true;
        }
        if (scanFolders.Count == 0)
        {
            scanFolders.Add("Assets");
            hasChanges = true;
        }
        if (!defaultIgnoreFoldersInitialized)
        {
            var commonIgnoredFolders = new[]
            {
                "Plugins", "ThirdParty", "ThirdParty*", "External", "Vendor",
                "Samples", "Sample", "Examples", "Example", "Demo", "Demos",
                "Tests", "Test", "Editor Default Resources"
            };
            foreach (var folder in commonIgnoredFolders)
            {
                if (!ignoredFolderNames.Contains(folder))
                {
                    ignoredFolderNames.Add(folder);
                }
            }
            defaultIgnoreFoldersInitialized = true;
            hasChanges = true;
        }
        if (hasChanges)
        {
            Save(true);
        }
    }

    public List<string> GetScanFolders()
    {
        EnsureDefaults();
        return scanFolders;
    }

    public List<string> GetIgnoredSymbols()
    {
        EnsureDefaults();
        return ignoredSymbols;
    }

    public List<string> GetIgnoredFolderNames()
    {
        EnsureDefaults();
        return ignoredFolderNames;
    }

    public bool IsIgnored(string symbol)
    {
        return MatchesAnyRule(symbol, GetIgnoredSymbols());
    }

    public bool IsInIgnoredFolder(string filePath)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directoryPath))
        {
            return false;
        }

        return directoryPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(folderName => MatchesAnyRule(folderName, GetIgnoredFolderNames()));
    }

    private static bool MatchesAnyRule(string value, IEnumerable<string> rules)
    {
        return rules.Any(rule =>
        {
            rule = rule.Trim();
            if (string.IsNullOrEmpty(rule))
            {
                return false;
            }
            if (rule.EndsWith("*", StringComparison.Ordinal))
            {
                return value.StartsWith(rule.Substring(0, rule.Length - 1), StringComparison.Ordinal);
            }
            return string.Equals(value, rule, StringComparison.Ordinal);
        });
    }

    public void SaveSettings()
    {
        Save(true);
    }

    public string GetNote(string define)
    {
        var entry = entries.Find(item => item.define == define);
        return entry == null ? string.Empty : entry.note ?? string.Empty;
    }

    public void SetNote(string define, string note)
    {
        var entry = entries.Find(item => item.define == define);
        if (entry == null)
        {
            entry = new NoteEntry { define = define };
            entries.Add(entry);
        }
        entry.note = note;
        Save(true);
    }
}
