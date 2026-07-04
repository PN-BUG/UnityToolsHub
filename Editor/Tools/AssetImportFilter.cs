#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资产导入过滤工具 —— 可忽略指定文件（按文件名关键词或扩展名）
/// </summary>
[ToolInfo("资产导入过滤", "资产工具",
    Description = "按文件名关键词或扩展名忽略指定文件的导入。\n\n仅作用于新导入/重新导入的资产，对已有资产无影响。支持指定过滤目录和自定义忽略规则。",
    Icon = "📦", Tags = new[] { "导入过滤", "关键词", "扩展名" })]
public class AssetImportFilter : EditorWindow
{
    // ── 持久化 Key ──────────────────────────────────────────────
    private const string KeyIgnoreKeywords  = "AssetImportFilter_Keywords";
    private const string KeyIgnoreExtensions = "AssetImportFilter_Extensions";
    private const string KeyEnabled          = "AssetImportFilter_Enabled";
    private const string KeyFilterDirectories = "AssetImportFilter_FilterDirectories";
    private const string IgnoredAssetsFolder = "Assets/IgnoredAssets";

    // ── 内部状态 ────────────────────────────────────────────────
    private static List<string> _ignoreKeywords   = new List<string>();
    private static List<string> _ignoreExtensions = new List<string>();
    private static List<string> _filterDirectories = new List<string>();
    private static bool         _enabled;
    private static bool         _loaded;

    private string _newKeyword   = "";
    private string _newExtension = "";
    private string _newDirectory = "";
    private Vector2 _scroll;

    // ── 菜单入口 ────────────────────────────────────────────────
    [MenuItem("UnityToolsHub/资产导入过滤工具")]
    public static void ShowWindow()
    {
        GetWindow<AssetImportFilter>("资产导入过滤");
    }

    // ── 初始化（从 EditorPrefs 加载） ───────────────────────────
    private void OnEnable()
    {
        LoadPrefs();
    }

    private static void LoadPrefs()
    {
        _enabled           = EditorPrefs.GetBool(KeyEnabled, false);
        _ignoreKeywords    = ParseList(EditorPrefs.GetString(KeyIgnoreKeywords, ""));
        _ignoreExtensions  = ParseList(EditorPrefs.GetString(KeyIgnoreExtensions, ""));
        _filterDirectories = ParseList(EditorPrefs.GetString(KeyFilterDirectories, ""));
        _loaded            = true;
    }

    // ── GUI ─────────────────────────────────────────────────────
    private void OnGUI()
    {
        EditorGUILayout.Space(4);
        GUILayout.Label("资产导入过滤工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("启用后，导入资产时满足忽略规则的文件将被自动标记为不可导入（TextAsset 后缀 .ignore）。\n对已有资产无影响，仅作用于新导入/重新导入的资产。", MessageType.Info);

        EditorGUILayout.Space(4);

        bool newEnabled = EditorGUILayout.Toggle("启用过滤", _enabled);
        if (newEnabled != _enabled)
        {
            _enabled = newEnabled;
            Save();
        }

        EditorGUILayout.Space(6);

        // ── 目录列表 ─────────────────────────────────────────
        GUILayout.Label("过滤目录（仅在这些目录下生效，留空则作用于整个 Assets）", EditorStyles.boldLabel);
        DrawList(_filterDirectories, "目录");

        EditorGUILayout.BeginHorizontal();
        _newDirectory = EditorGUILayout.TextField(_newDirectory);
        if (GUILayout.Button("添加", GUILayout.Width(50)) && !string.IsNullOrWhiteSpace(_newDirectory))
        {
            string dir = NormalizeDirectory(_newDirectory);
            if (!string.IsNullOrEmpty(dir) && !_filterDirectories.Contains(dir))
            {
                _filterDirectories.Add(dir);
                Save();
            }
            _newDirectory = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // ── 关键词列表 ─────────────────────────────────────────
        GUILayout.Label("忽略关键词（文件名包含以下任意词时忽略）", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(160));
        DrawList(_ignoreKeywords, "关键词");
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        _newKeyword = EditorGUILayout.TextField(_newKeyword);
        if (GUILayout.Button("添加", GUILayout.Width(50)) && !string.IsNullOrWhiteSpace(_newKeyword))
        {
            string kw = _newKeyword.Trim();
            if (!_ignoreKeywords.Contains(kw))
            {
                _ignoreKeywords.Add(kw);
                Save();
            }
            _newKeyword = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // ── 扩展名列表 ─────────────────────────────────────────
        GUILayout.Label("忽略扩展名（如 .psd  .tmp  .bak）", EditorStyles.boldLabel);
        DrawList(_ignoreExtensions, "扩展名");

        EditorGUILayout.BeginHorizontal();
        _newExtension = EditorGUILayout.TextField(_newExtension);
        if (GUILayout.Button("添加", GUILayout.Width(50)) && !string.IsNullOrWhiteSpace(_newExtension))
        {
            string ext = NormalizeExt(_newExtension);
            if (!_ignoreExtensions.Contains(ext))
            {
                _ignoreExtensions.Add(ext);
                Save();
            }
            _newExtension = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(12);

        // ── 手动扫描 ───────────────────────────────────────────
        if (GUILayout.Button("扫描 Assets 并列出匹配文件（不修改）"))
        {
            ScanAndLog();
        }

        if (GUILayout.Button("扫描 Assets 并处理匹配文件（修改）"))
        {
            ScanAndModify();
        }
    }

    // ── 绘制可删除列表 ──────────────────────────────────────────
    private void DrawList(List<string> list, string label)
    {
        if (list.Count == 0)
        {
            EditorGUILayout.LabelField($"（暂无{label}）", EditorStyles.miniLabel);
            return;
        }

        for (int i = list.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(list[i]);
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                list.RemoveAt(i);
                Save();
                
                List<string> ignoredFiles = GetIgnoredAssetFiles();
                if (ignoredFiles.Count > 0)
                {
                    RestoreIgnoredAssetsWindow.Open(ignoredFiles);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // ── 获取被忽略的资产文件列表 ────────────────────────────────
    private static List<string> GetIgnoredAssetFiles()
    {
        var files = new List<string>();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string ignoredDir = Path.GetFullPath(Path.Combine(projectRoot, IgnoredAssetsFolder.Replace('/', Path.DirectorySeparatorChar)));

        if (!Directory.Exists(ignoredDir)) return files;

        string[] allFiles = Directory.GetFiles(ignoredDir, "*", SearchOption.AllDirectories);
        foreach (string file in allFiles)
        {
            if (file.EndsWith(".meta")) continue;
            string assetPath = file.Substring(projectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
            files.Add(assetPath);
        }

        return files;
    }

    // ── 持久化 ──────────────────────────────────────────────────
    private static void Save()
    {
        EditorPrefs.SetBool(KeyEnabled, _enabled);
        EditorPrefs.SetString(KeyIgnoreKeywords,    string.Join("|", _ignoreKeywords));
        EditorPrefs.SetString(KeyIgnoreExtensions,  string.Join("|", _ignoreExtensions));
        EditorPrefs.SetString(KeyFilterDirectories, string.Join("|", _filterDirectories));
    }

    private static List<string> ParseList(string raw)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(raw)) return list;
        foreach (var item in raw.Split('|'))
        {
            string s = item.Trim();
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
        return list;
    }

    private static string NormalizeExt(string ext)
    {
        ext = ext.Trim();
        return ext.StartsWith(".") ? ext.ToLower() : ("." + ext.ToLower());
    }

    private static string NormalizeDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return string.Empty;

        string normalized = directory.Trim().Replace('\\', '/').TrimEnd('/');
        if (!normalized.StartsWith("Assets"))
        {
            normalized = normalized.TrimStart('/');
            normalized = string.IsNullOrEmpty(normalized) ? "Assets" : $"Assets/{normalized}";
        }

        return normalized;
    }

    private static bool IsInFilterDirectories(string assetPath)
    {
        if (_filterDirectories.Count == 0) return true;

        foreach (string dir in _filterDirectories)
        {
            if (assetPath == dir || assetPath.StartsWith(dir + "/"))
                return true;
        }

        return false;
    }

    internal static string GetUniqueDestinationPath(string destDir, string fileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        string destFull = Path.Combine(destDir, baseName + extension);
        if (!File.Exists(destFull)) return destFull;

        string stamp = System.DateTime.Now.ToString("yyyyMMddHHmmssfff");
        int index = 1;
        do
        {
            destFull = Path.Combine(destDir, $"{baseName}_{stamp}_{index}{extension}");
            index++;
        }
        while (File.Exists(destFull));

        return destFull;
    }

    // ── 判断路径是否匹配规则（不受开关影响） ─────────────────────
    private static bool MatchesRules(string assetPath)
    {
        if (_ignoreKeywords.Count == 0 && _ignoreExtensions.Count == 0) return false;
        if (assetPath.StartsWith(IgnoredAssetsFolder + "/")) return false;
        if (!IsInFilterDirectories(assetPath)) return false;

        string fileName = Path.GetFileName(assetPath).ToLower();
        string fileExt  = Path.GetExtension(assetPath).ToLower();

        foreach (string ext in _ignoreExtensions)
            if (fileExt == ext) return true;

        foreach (string kw in _ignoreKeywords)
            if (fileName.Contains(kw.ToLower())) return true;

        return false;
    }

    // ── 判断路径是否应被忽略（受开关影响，供导入器使用） ──────────
    public static bool ShouldIgnore(string assetPath)
    {
        if (!_loaded) LoadPrefs();
        if (!_enabled) return false;
        return MatchesRules(assetPath);
    }

    // ── 手动扫描日志 ────────────────────────────────────────────
    private static void ScanAndLog()
    {
        if (!_loaded) LoadPrefs();

        if (_ignoreKeywords.Count == 0 && _ignoreExtensions.Count == 0)
        {
            Debug.LogWarning("[AssetImportFilter] 当前没有任何忽略规则，请先添加关键词或扩展名。");
            return;
        }

        string[] allAssets = AssetDatabase.GetAllAssetPaths();
        int count = 0;
        foreach (string path in allAssets)
        {
            if (!path.StartsWith("Assets/")) continue;
            if (MatchesRules(path))
            {
                Debug.Log($"[AssetImportFilter] 匹配（将被忽略）: {path}");
                count++;
            }
        }
        Debug.Log($"[AssetImportFilter] 扫描完成，共匹配 {count} 个文件。");
    }

    private static void ScanAndModify()
    {
        if (!_loaded) LoadPrefs();

        if (_ignoreKeywords.Count == 0 && _ignoreExtensions.Count == 0)
        {
            Debug.LogWarning("[AssetImportFilter] 当前没有任何忽略规则，请先添加关键词或扩展名。");
            return;
        }

        string[] allAssets = AssetDatabase.GetAllAssetPaths();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string destDir = Path.GetFullPath(Path.Combine(projectRoot, IgnoredAssetsFolder.Replace('/', Path.DirectorySeparatorChar)));

        int matchCount = 0;
        int movedCount = 0;

        Directory.CreateDirectory(destDir);

        foreach (string path in allAssets)
        {
            if (!path.StartsWith("Assets/")) continue;
            if (path.StartsWith(IgnoredAssetsFolder + "/")) continue;
            if (path.EndsWith(".meta")) continue;
            if (!MatchesRules(path)) continue;

            matchCount++;

            string relativePath = path.Replace('/', Path.DirectorySeparatorChar);
            string srcFull = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
            if (!File.Exists(srcFull)) continue;

            string destFull = GetUniqueDestinationPath(destDir, Path.GetFileName(path));

            File.Move(srcFull, destFull);
            movedCount++;
            Debug.LogWarning($"[AssetImportFilter] 文件已移至 {IgnoredAssetsFolder}/: {Path.GetFileName(path)}");
        }

        AssetDatabase.Refresh();
        Debug.Log($"[AssetImportFilter] 扫描并处理完成，共匹配 {matchCount} 个文件，成功处理 {movedCount} 个文件。目标目录: {IgnoredAssetsFolder}");
    }
}

/// <summary>
/// 恢复被忽略资产的窗口
/// </summary>
public class RestoreIgnoredAssetsWindow : EditorWindow
{
    private List<string> _ignoredFiles = new List<string>();
    private Dictionary<string, bool> _selectionState = new Dictionary<string, bool>();
    private Vector2 _scrollPosition;
    private bool _selectAll = false;

    public static void Open(List<string> ignoredFiles)
    {
        var window = GetWindow<RestoreIgnoredAssetsWindow>("恢复被忽略的资产");
        window.Initialize(ignoredFiles);
        window.minSize = new Vector2(400, 300);
    }

    private void Initialize(List<string> ignoredFiles)
    {
        _ignoredFiles = new List<string>(ignoredFiles);
        _selectionState.Clear();
        foreach (var file in _ignoredFiles)
        {
            _selectionState[file] = false;
        }
        _selectAll = false;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("选择要恢复的文件", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox($"在 Assets/IgnoredAssets 文件夹中共有 {_ignoredFiles.Count} 个被忽略的文件", MessageType.Info);

        EditorGUILayout.Space(8);

        // ── 全选/取消按钮 ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        bool newSelectAll = EditorGUILayout.Toggle("全选", _selectAll, GUILayout.Width(60));
        if (newSelectAll != _selectAll)
        {
            _selectAll = newSelectAll;
            foreach (var file in _ignoredFiles)
            {
                _selectionState[file] = _selectAll;
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // ── 文件列表 ────────────────────────────────────────
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        for (int i = 0; i < _ignoredFiles.Count; i++)
        {
            string file = _ignoredFiles[i];
            EditorGUILayout.BeginHorizontal();

            bool wasSelected = _selectionState[file];
            bool isSelected = EditorGUILayout.Toggle(wasSelected, GUILayout.Width(20));
            if (isSelected != wasSelected)
            {
                _selectionState[file] = isSelected;
            }

            EditorGUILayout.LabelField(file, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(12);

        // ── 操作按钮 ────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("恢复选中文件", GUILayout.Height(30)))
        {
            RestoreSelectedFiles();
        }

        if (GUILayout.Button("取消", GUILayout.Height(30)))
        {
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void RestoreSelectedFiles()
    {
        List<string> filesToRestore = new List<string>();
        foreach (var kvp in _selectionState)
        {
            if (kvp.Value)
            {
                filesToRestore.Add(kvp.Key);
            }
        }

        if (filesToRestore.Count == 0)
        {
            Debug.LogWarning("[AssetImportFilter] 没有选中任何文件");
            return;
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        int restoredCount = 0;

        foreach (string assetPath in filesToRestore)
        {
            string relativePath = assetPath.Replace('/', Path.DirectorySeparatorChar);
            string srcFull = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            if (!File.Exists(srcFull))
            {
                Debug.LogWarning($"[AssetImportFilter] 文件不存在: {assetPath}");
                continue;
            }

            // 提取原始文件名（去掉时间戳等）
            string fileName = Path.GetFileName(assetPath);
            string destDir = Path.GetDirectoryName(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            destDir = destDir.Replace("IgnoredAssets", "").TrimEnd(Path.DirectorySeparatorChar);

            if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
            {
                destDir = Path.Combine(projectRoot, "Assets");
            }

            Directory.CreateDirectory(destDir);

            string destFull = Path.Combine(destDir, fileName);
            // 如果目标文件已存在，添加时间戳
            if (File.Exists(destFull))
            {
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                string stamp = System.DateTime.Now.ToString("yyyyMMddHHmmssfff");
                destFull = Path.Combine(destDir, $"{baseName}_{stamp}{extension}");
            }

            try
            {
                File.Move(srcFull, destFull);
                restoredCount++;
                string restorePath = destFull.Substring(projectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                Debug.Log($"[AssetImportFilter] 文件已恢复: {assetPath} -> {restorePath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AssetImportFilter] 恢复失败 {assetPath}: {ex.Message}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[AssetImportFilter] 共恢复 {restoredCount} 个文件");

        Close();
    }
}

/// <summary>
/// 资产导入后处理器 —— 自动将匹配规则的资产设为不可导入
/// </summary>
public class AssetImportFilterPostprocessor : AssetPostprocessor
{
    private void OnPreprocessAsset()
    {
        if (assetPath.StartsWith("Assets/IgnoredAssets/")) return;
        if (!AssetImportFilter.ShouldIgnore(assetPath)) return;

        // assetPath 形如 "Assets/Foo/bar.txt"，需转为系统路径
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string relativePath = assetPath.Replace('/', Path.DirectorySeparatorChar);
        string srcFull  = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        string destDir  = Path.GetFullPath(Path.Combine(projectRoot, "Assets/IgnoredAssets".Replace('/', Path.DirectorySeparatorChar)));

        Debug.Log($"[AssetImportFilter] OnPreprocessAsset srcFull={srcFull} exists={File.Exists(srcFull)}");

        if (!File.Exists(srcFull)) return;

        Directory.CreateDirectory(destDir);

        string destFull = AssetImportFilter.GetUniqueDestinationPath(destDir, Path.GetFileName(assetPath));

        File.Move(srcFull, destFull);
        Debug.LogWarning($"[AssetImportFilter] 文件已移至 Assets/IgnoredAssets/: {Path.GetFileName(assetPath)}");
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromPaths)
    {
        bool needRefresh = false;
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        foreach (string path in importedAssets)
        {
            if (path.EndsWith(".meta")) continue;
            if (!AssetImportFilter.ShouldIgnore(path)) continue;

            string relativePath = path.Replace('/', Path.DirectorySeparatorChar);
            string metaFull = Path.GetFullPath(Path.Combine(projectRoot, relativePath + ".meta"));
            if (File.Exists(metaFull))
            {
                File.Delete(metaFull);
                needRefresh = true;
                Debug.LogWarning($"[AssetImportFilter] 已删除孤立 .meta: {path}.meta");
            }
        }

        if (needRefresh)
            AssetDatabase.Refresh();
    }
}
#endif
