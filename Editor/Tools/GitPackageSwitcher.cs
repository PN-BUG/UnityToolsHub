using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Git 包 ↔ 本地包 一键切换工具
///
/// 功能：
///   1. 自动扫描 manifest.json 中的 Git 依赖包
///   2. 支持 Git ↔ 本地模式一键切换
///   3. 本地模式下仍可从 Git 拉取最新代码
///   4. 支持自定义本地目录名和 Git 分支
/// </summary>
[ToolInfo("Git 包切换器", "包管理工具",
    Description = "在 Git 包和本地包之间一键切换。\n" +
                  "本地包默认放在 Packages 目录下，切换后仍支持从 Git 拉取更新。",
    Icon = "📦", Tags = new[] { "Git", "包管理", "依赖", "切换", "本地" })]
public class GitPackageSwitcher : ToolEditorWindow
{
    // ═══════════════════════════════════════════════════════════════
    //  常量与配置
    // ═══════════════════════════════════════════════════════════════

    protected override string ToolTitle => "Git 包切换器";
    protected override string ToolIcon  => "📦";
    protected override bool ShowStatusBar => true;

    private const string ManifestRelativePath = "Packages/manifest.json";

    // ═══════════════════════════════════════════════════════════════
    //  数据结构
    // ═══════════════════════════════════════════════════════════════

    private class PackageInfo
    {
        public string packageName;
        public string gitUrl;
        public string localFolder;
        public bool isLocal;
        public bool isGit;
        public bool isSelected;
        public string branch;
        public string subPath;
    }

    // ═══════════════════════════════════════════════════════════════
    //  状态 & 缓存
    // ═══════════════════════════════════════════════════════════════

    private List<PackageInfo> _packages = new List<PackageInfo>();
    private Vector2 _scrollPos;
    private bool _isProcessing;
    private string _statusMessage = "";
    private string _manifestPath;
    private string _projectRoot;

    private bool _showAddForm;
    private string _newGitUrl = "";
    private string _newPackageName = "";
    private string _newLocalFolder = "";

    // 目录扫描
    private string _scanDirectory = "";
    private List<ScannedPackage> _scannedPackages = new List<ScannedPackage>();
    private bool _showScanResults;

    // PackageCache 检测
    private List<ScannedPackage> _cachePackages = new List<ScannedPackage>();
    private bool _showCacheResults;

    // 过滤模式
    private enum PackageFilter { All, Git, Local }
    private PackageFilter _filter = PackageFilter.All;

    // 样式缓存
    private GUIStyle _cardStyle;
    private GUIStyle _tagStyle;
    private GUIStyle _coloredBtnStyle;

    private class ScannedPackage
    {
        public string name;
        public string displayName;
        public string folderPath;
        public string folderName;
        public bool isSelected;
    }

    // ═══════════════════════════════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════════════════════════════

    protected override void OnToolEnable()
    {
        _projectRoot = Path.GetDirectoryName(Application.dataPath);
        _manifestPath = Path.Combine(_projectRoot, ManifestRelativePath);
        RefreshPackageList();
    }

    protected override void DrawStatusBarContent()
    {
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            GUILayout.Label(_statusMessage, StLabelDim);
        }
        else
        {
            int gitCount = _packages.Count(p => p.isGit);
            int localCount = _packages.Count(p => p.isLocal);
            GUILayout.Label(
                $"共 {_packages.Count} 个 Git 包 | {gitCount} 个 Git 模式 | {localCount} 个本地模式",
                StLabelDim);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  主绘制
    // ═══════════════════════════════════════════════════════════════

    protected override void DrawToolContent()
    {
        EnsureStyles();

        DrawToolbarArea();
        DrawScanSection();
        DrawCacheSection();
        DrawStatusArea();

        if (_packages.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        DrawPackageList();
        DrawBatchActions();
    }

    private void EnsureStyles()
    {
        if (_cardStyle == null)
        {
            _cardStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 2, 2),
            };
        }
        if (_tagStyle == null)
        {
            _tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                padding = new RectOffset(6, 6, 1, 1),
                alignment = TextAnchor.MiddleCenter,
            };
        }
        if (_coloredBtnStyle == null)
        {
            // 透明背景样式，让 DrawRect 的颜色能正常显示
            _coloredBtnStyle = new GUIStyle
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white, background = null },
                hover = { textColor = Color.white, background = null },
                active = { textColor = new Color(0.85f, 0.85f, 0.85f), background = null },
                padding = new RectOffset(6, 6, 4, 4),
            };
        }
    }

    /// <summary>绘制带颜色的按钮（透明背景，颜色由 DrawRect 提供）</summary>
    private bool DrawColoredBtn(string text, Color normal, Color hover, params GUILayoutOption[] options)
    {
        var rect = GUILayoutUtility.GetRect(GUIContent.none, _coloredBtnStyle, options);
        bool isHover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, isHover ? hover : normal);
        return GUI.Button(rect, text, _coloredBtnStyle);
    }

    private bool BtnSuccess(string text, params GUILayoutOption[] opts) => DrawColoredBtn(text, ClrBtnSuccess, ClrBtnSuccessHov, opts);
    private bool BtnPrimary(string text, params GUILayoutOption[] opts) => DrawColoredBtn(text, ClrBtnNormal, ClrBtnHover, opts);
    private bool BtnWarn(string text, params GUILayoutOption[] opts)    => DrawColoredBtn(text, ClrBtnWarn, ClrBtnWarnHov, opts);
    private bool BtnDanger(string text, params GUILayoutOption[] opts)  => DrawColoredBtn(text, ClrBtnDanger, ClrBtnDangerHov, opts);

    // ─── 顶部工具栏 ───────────────────────────────────────────

    private void DrawToolbarArea()
    {
        // 第一行：主要操作按钮
        EditorGUILayout.BeginHorizontal();
        {
            if (BtnSuccess("🔄 刷新列表", GUILayout.Width(100)))
                RefreshPackageList();

            GUILayout.Space(6);

            if (BtnSuccess("➕ 添加 Git 包", GUILayout.Width(110)))
                _showAddForm = !_showAddForm;

            GUILayout.Space(6);

            if (BtnWarn("📂 扫描目录", GUILayout.Width(100)))
                PickAndScanDirectory();

            GUILayout.Space(6);

            if (BtnDanger("🔍 检测缓存", GUILayout.Width(100)))
                ScanPackageCache();

            GUILayout.FlexibleSpace();

            if (DrawFlatButton("全选", GUILayout.Width(50)))
                _packages.ForEach(p => p.isSelected = true);

            GUILayout.Space(4);

            if (DrawFlatButton("全不选", GUILayout.Width(60)))
                _packages.ForEach(p => p.isSelected = false);
        }
        EditorGUILayout.EndHorizontal();

        if (_showAddForm)
            DrawAddGitPackageForm();

        EditorGUILayout.Space(4);
        DrawDivider();
    }

    // ─── 状态提示 ─────────────────────────────────────────────

    private void DrawStatusArea()
    {
        if (_isProcessing)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("正在处理中，请稍候...", MessageType.Warning);
        }
    }

    // ─── 目录扫描 ────────────────────────────────────────────

    private void PickAndScanDirectory()
    {
        string dir = EditorUtility.OpenFolderPanel("选择要扫描的目录", _projectRoot, "");
        if (string.IsNullOrEmpty(dir)) return;

        _scanDirectory = dir;
        ScanDirectoryForPackages(dir);
    }

    private void ScanDirectoryForPackages(string directoryPath)
    {
        _scannedPackages.Clear();
        _showScanResults = true;

        if (!Directory.Exists(directoryPath))
        {
            ShowStatus($"目录不存在: {directoryPath}", MessageType.Warning);
            return;
        }

        try
        {
            // 扫描目录下所有包含 package.json 的子目录
            foreach (var subDir in Directory.GetDirectories(directoryPath))
            {
                string packageJsonPath = Path.Combine(subDir, "package.json");
                if (!File.Exists(packageJsonPath)) continue;

                string folderName = Path.GetFileName(subDir);
                var pkg = new ScannedPackage
                {
                    folderPath = subDir,
                    folderName = folderName,
                    isSelected = true,
                };

                // 尝试读取 package.json 获取包名
                try
                {
                    string json = File.ReadAllText(packageJsonPath);
                    var nameMatch = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    if (nameMatch.Success)
                    {
                        pkg.name = nameMatch.Groups[1].Value;
                        var displayMatch = Regex.Match(json, "\"displayName\"\\s*:\\s*\"([^\"]+)\"");
                        pkg.displayName = displayMatch.Success ? displayMatch.Groups[1].Value : pkg.name;
                    }
                    else
                    {
                        pkg.name = folderName;
                        pkg.displayName = folderName;
                    }
                }
                catch
                {
                    pkg.name = folderName;
                    pkg.displayName = folderName;
                }

                _scannedPackages.Add(pkg);
            }

            ShowStatus($"扫描完成，发现 {_scannedPackages.Count} 个包", MessageType.Info);
        }
        catch (Exception ex)
        {
            ShowStatus("扫描失败: " + ex.Message, MessageType.Error);
            Debug.LogException(ex);
        }
    }

    private void DrawScanSection()
    {
        if (!_showScanResults || _scannedPackages.Count == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(_cardStyle);
        {
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label($"📂 {_scanDirectory}", StTitle);
                GUILayout.FlexibleSpace();
                if (DrawFlatButton("✕ 关闭", GUILayout.Width(60)))
                {
                    _showScanResults = false;
                    _scannedPackages.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"发现 {_scannedPackages.Count} 个包含 package.json 的包:", StSubtitle);

            foreach (var pkg in _scannedPackages)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    pkg.isSelected = EditorGUILayout.Toggle(pkg.isSelected, GUILayout.Width(20));

                    EditorGUILayout.BeginVertical();
                    {
                        EditorGUILayout.LabelField(
                            string.IsNullOrEmpty(pkg.displayName) ? pkg.name : $"{pkg.displayName} ({pkg.name})",
                            StLabel);
                        EditorGUILayout.LabelField($"📁 {pkg.folderName}", StLabelDim);
                    }
                    EditorGUILayout.EndVertical();

                    GUILayout.FlexibleSpace();

                    // 检查是否已在 manifest 中
                    bool alreadyExists = _packages.Any(p => p.packageName == pkg.name);
                    if (alreadyExists)
                    {
                        GUI.enabled = false;
                        DrawFlatButton("已存在", GUILayout.Width(70));
                        GUI.enabled = true;
                    }
                    else
                    {
                        if (BtnSuccess("➕ 添加为本地包", GUILayout.Width(120)))
                            AddLocalPackage(pkg);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            {
                var selected = _scannedPackages.Where(s => s.isSelected && !_packages.Any(ep => ep.packageName == s.name)).ToList();
                if (selected.Count > 0)
                {
                    if (BtnSuccess($"批量添加 {selected.Count} 个", GUILayout.Width(140)))
                    {
                        foreach (var pkg in selected)
                            AddLocalPackage(pkg);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        DrawDivider();
    }

    private void AddLocalPackage(ScannedPackage scanned)
    {
        if (string.IsNullOrEmpty(scanned.name)) return;

        // 计算相对于项目根目录的路径
        string relativePath = GetRelativePath(_projectRoot, scanned.folderPath);
        string fileRef = $"file:{relativePath.Replace('\\', '/')}";

        AddGitPackage(fileRef, scanned.name, scanned.folderName);
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        basePath = basePath.Replace('\\', '/').TrimEnd('/');
        fullPath = fullPath.Replace('\\', '/').TrimEnd('/');

        if (!basePath.EndsWith("/")) basePath += "/";
        if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return fullPath.Substring(basePath.Length);

        return fullPath;
    }

    // ─── PackageCache 检测 ──────────────────────────────────────

    /// <summary>扫描 Library/PackageCache 检测 Unity 缓存的 Git 包</summary>
    private void ScanPackageCache()
    {
        _cachePackages.Clear();
        _showCacheResults = true;

        string cacheDir = Path.Combine(_projectRoot, "Library", "PackageCache");
        if (!Directory.Exists(cacheDir))
        {
            ShowStatus("Library/PackageCache 目录不存在，请先打开 Unity 让它解析包", MessageType.Warning);
            return;
        }

        try
        {
            // 读取 packages-lock.json 获取 git URL 信息
            var lockGitUrls = ReadLockFileGitUrls();

            foreach (var subDir in Directory.GetDirectories(cacheDir))
            {
                string folderName = Path.GetFileName(subDir);

                // Git 缓存包的目录名格式: com.xxx.yyy@commit_hash
                int atIndex = folderName.LastIndexOf('@');
                if (atIndex <= 0) continue;

                string packageName = folderName.Substring(0, atIndex);
                string commitHash = folderName.Substring(atIndex + 1);

                // 确认是包目录（有 package.json）
                string packageJsonPath = Path.Combine(subDir, "package.json");
                if (!File.Exists(packageJsonPath)) continue;

                var pkg = new ScannedPackage
                {
                    name = packageName,
                    folderPath = subDir,
                    folderName = folderName,
                    isSelected = true,
                };

                // 读取 displayName
                try
                {
                    string json = File.ReadAllText(packageJsonPath);
                    var displayMatch = Regex.Match(json, "\"displayName\"\\s*:\\s*\"([^\"]+)\"");
                    pkg.displayName = displayMatch.Success ? displayMatch.Groups[1].Value : packageName;
                }
                catch
                {
                    pkg.displayName = packageName;
                }

                // 尝试从 packages-lock.json 获取 git URL
                string gitUrl = null;
                if (lockGitUrls.TryGetValue(packageName, out var lockUrl))
                    gitUrl = lockUrl;

                // 检查是否有 .git 目录
                string gitConfigDir = Path.Combine(subDir, ".git");
                bool hasGitDir = Directory.Exists(gitConfigDir);

                // 附加信息
                pkg.displayName = $"{pkg.displayName}  [{commitHash.Substring(0, Math.Min(8, commitHash.Length))}]" +
                    (hasGitDir ? " [有.git]" : "") +
                    (!string.IsNullOrEmpty(gitUrl) ? "\n    " + gitUrl : "");

                _cachePackages.Add(pkg);
            }

            ShowStatus($"PackageCache 检测完成，发现 {_cachePackages.Count} 个 Git 缓存包", MessageType.Info);
        }
        catch (Exception ex)
        {
            ShowStatus("扫描 PackageCache 失败: " + ex.Message, MessageType.Error);
            Debug.LogException(ex);
        }
    }

    /// <summary>从 packages-lock.json 读取所有 git URL</summary>
    private Dictionary<string, string> ReadLockFileGitUrls()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string lockPath = Path.Combine(_projectRoot, "Packages", "packages-lock.json");
        if (!File.Exists(lockPath)) return result;

        try
        {
            string lockJson = File.ReadAllText(lockPath);
            // 匹配 "com.xxx": { ... "url": "https://..." ... } 模式
            var pkgRegex = new Regex("\"([^\"]+)\"\\s*:\\s*\\{[^}]*\"url\"\\s*:\\s*\"([^\"]+)\"");
            var matches = pkgRegex.Matches(lockJson);
            foreach (Match m in matches)
            {
                string name = m.Groups[1].Value;
                string url = m.Groups[2].Value;
                if (IsGitUrl(url))
                    result[name] = url;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"读取 packages-lock.json 失败: {ex.Message}");
        }

        return result;
    }

    private void DrawCacheSection()
    {
        if (!_showCacheResults || _cachePackages.Count == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(_cardStyle);
        {
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label($"🔍 Library/PackageCache 检测结果", StTitle);
                GUILayout.FlexibleSpace();
                if (DrawFlatButton("✕ 关闭", GUILayout.Width(60)))
                {
                    _showCacheResults = false;
                    _cachePackages.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"发现 {_cachePackages.Count} 个 Git 缓存包:", StSubtitle);

            foreach (var pkg in _cachePackages)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    pkg.isSelected = EditorGUILayout.Toggle(pkg.isSelected, GUILayout.Width(20));

                    EditorGUILayout.BeginVertical();
                    {
                        EditorGUILayout.LabelField(pkg.name, StLabel);
                        EditorGUILayout.LabelField($"📁 {pkg.folderName}", StLabelDim);
                        // 显示附加信息（git URL 等）
                        string extra = pkg.displayName;
                        if (extra.Contains("\n"))
                        {
                            string urlPart = extra.Substring(extra.IndexOf('\n') + 1).Trim();
                            if (!string.IsNullOrEmpty(urlPart))
                                EditorGUILayout.LabelField(urlPart, StLabelDim);
                        }
                    }
                    EditorGUILayout.EndVertical();

                    GUILayout.FlexibleSpace();

                    // 检查是否已在 manifest 中
                    bool alreadyExists = _packages.Any(p => p.packageName == pkg.name);
                    if (alreadyExists)
                    {
                        GUI.enabled = false;
                        DrawFlatButton("已在 manifest", GUILayout.Width(90));
                        GUI.enabled = true;
                    }
                    else
                    {
                        if (BtnSuccess("➕ 添加到 manifest", GUILayout.Width(130)))
                            AddCachePackageToManifest(pkg);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            // 批量操作
            var selected = _cachePackages.Where(s => s.isSelected && !_packages.Any(ep => ep.packageName == s.name)).ToList();
            if (selected.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                {
                    if (BtnSuccess($"批量添加 {selected.Count} 个到 manifest", GUILayout.Width(200)))
                    {
                        foreach (var pkg in selected)
                            AddCachePackageToManifest(pkg);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();

        DrawDivider();
    }

    /// <summary>将 PackageCache 中的 git 包添加到 manifest.json</summary>
    private void AddCachePackageToManifest(ScannedPackage cached)
    {
        if (string.IsNullOrEmpty(cached.name)) return;

        // 从 packages-lock.json 查找 git URL
        var lockUrls = ReadLockFileGitUrls();
        if (!lockUrls.TryGetValue(cached.name, out string gitUrl) || string.IsNullOrEmpty(gitUrl))
        {
            ShowStatus($"无法找到 {cached.name} 的 Git URL（packages-lock.json 中无记录）", MessageType.Warning);
            return;
        }

        AddGitPackage(gitUrl, cached.name, "");
    }

    // ─── 空状态 ───────────────────────────────────────────────

    private void DrawEmptyState()
    {
        EditorGUILayout.Space(20);
        EditorGUILayout.BeginVertical(_cardStyle);
        {
            GUILayout.Label("📭 未发现 Git 包", StTitle);
            EditorGUILayout.Space(4);
            GUILayout.Label(
                "manifest.json 中没有以 https:// 或 git@ 开头的依赖包。\n" +
                "你可以通过上方「添加 Git 包」按钮手动添加。",
                StBody);
        }
        EditorGUILayout.EndVertical();
    }

    // ─── 包列表 ───────────────────────────────────────────────

    private void DrawPackageList()
    {
        // ── 过滤标签栏 ──
        EditorGUILayout.BeginHorizontal();
        {
            GUILayout.Label("分类:", StLabel, GUILayout.Width(36));

            int gitCount = _packages.Count(p => p.isGit);
            int localCount = _packages.Count(p => p.isLocal);

            if (_filter == PackageFilter.All)
                DrawTag($"📦 全部 ({_packages.Count})", ClrAccent);
            else if (DrawFlatButton($"📦 全部 ({_packages.Count})", GUILayout.Width(90)))
                _filter = PackageFilter.All;

            GUILayout.Space(4);

            if (_filter == PackageFilter.Git)
                DrawTag($"🔀 Git ({gitCount})", ClrCatOrange);
            else if (DrawFlatButton($"🔀 Git ({gitCount})", GUILayout.Width(80)))
                _filter = PackageFilter.Git;

            GUILayout.Space(4);

            if (_filter == PackageFilter.Local)
                DrawTag($"📂 本地 ({localCount})", ClrCatGreen);
            else if (DrawFlatButton($"📂 本地 ({localCount})", GUILayout.Width(90)))
                _filter = PackageFilter.Local;

            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(6);

        // ── 按过滤分组显示 ──
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        var gitPackages = _packages.Where(p => p.isGit).ToList();
        var localPackages = _packages.Where(p => p.isLocal).ToList();

        if (gitPackages.Count > 0 && _filter != PackageFilter.Local)
        {
            DrawSection($"Git 模式 ({gitPackages.Count})", ClrCatOrange);
            foreach (var pkg in gitPackages)
                DrawPackageRow(pkg);
        }

        if (localPackages.Count > 0 && _filter != PackageFilter.Git)
        {
            EditorGUILayout.Space(4);
            DrawSection($"本地模式 ({localPackages.Count})", ClrCatGreen);
            foreach (var pkg in localPackages)
                DrawPackageRow(pkg);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawPackageRow(PackageInfo pkg)
    {
        EditorGUILayout.BeginVertical(_cardStyle);
        {
            EditorGUILayout.BeginHorizontal();
            {
                // 选中框
                pkg.isSelected = EditorGUILayout.Toggle(pkg.isSelected, GUILayout.Width(20));

                // 包名 & 状态标签
                EditorGUILayout.BeginVertical();
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        GUILayout.Label(pkg.packageName, StTitle);

                        if (pkg.isLocal)
                            DrawStatusTag("本地", ClrSuccess);
                        else if (pkg.isGit)
                            DrawStatusTag("Git", ClrAccent);

                        GUILayout.FlexibleSpace();
                    }
                    EditorGUILayout.EndHorizontal();

                    if (!string.IsNullOrEmpty(pkg.gitUrl))
                        GUILayout.Label(pkg.gitUrl, StLabelDim);

                    if (pkg.isLocal)
                    {
                        string localPath = Path.Combine("Packages", pkg.localFolder);
                        bool exists = Directory.Exists(Path.Combine(_projectRoot, "Packages", pkg.localFolder));
                        string status = exists ? "✅ 目录存在" : "❌ 目录缺失";
                        GUILayout.Label($"本地路径: {localPath}  ({status})", StLabelDim);
                    }

                    if (!string.IsNullOrEmpty(pkg.branch))
                        GUILayout.Label($"分支: {pkg.branch}", StLabelDim);
                }
                EditorGUILayout.EndVertical();

                // 操作按钮
                DrawPackageActions(pkg);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    private void DrawPackageActions(PackageInfo pkg)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(210));
        {
            if (pkg.isGit)
            {
                if (BtnSuccess("📥 切换到本地", GUILayout.Width(200), GUILayout.Height(24)))
                    SwitchToLocal(pkg);

                GUILayout.Space(4);

                if (BtnDanger("🗑 从 manifest 移除", GUILayout.Width(200), GUILayout.Height(22)))
                    RemoveFromManifest(pkg);
            }
            else if (pkg.isLocal)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    if (BtnPrimary("📤 切回 Git", GUILayout.Width(96), GUILayout.Height(24)))
                        SwitchToGit(pkg);

                    GUILayout.Space(4);

                    if (BtnWarn("🔄 Git 更新", GUILayout.Width(96), GUILayout.Height(24)))
                        PullFromGit(pkg);
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                {
                    if (BtnPrimary("📂 打开目录", GUILayout.Width(96), GUILayout.Height(22)))
                    {
                        string localPath = Path.Combine(_projectRoot, "Packages", pkg.localFolder);
                        if (Directory.Exists(localPath))
                            EditorUtility.RevealInFinder(localPath);
                        else
                            ShowStatus($"目录不存在: {localPath}", MessageType.Warning);
                    }

                    GUILayout.Space(4);

                    if (BtnDanger("🗑 删除本地", GUILayout.Width(96), GUILayout.Height(22)))
                        DeleteLocalPackage(pkg);
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawStatusTag(string text, Color color)
    {
        var oldColor = GUI.color;
        GUI.color = color;
        GUILayout.Label(text, _tagStyle, GUILayout.Width(40));
        GUI.color = oldColor;
    }

    // ─── 批量操作 ─────────────────────────────────────────────

    private void DrawBatchActions()
    {
        var selected = _packages.Where(p => p.isSelected).ToList();
        if (selected.Count == 0) return;

        EditorGUILayout.Space(4);
        DrawDivider();
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        {
            GUILayout.Label($"已选 {selected.Count} 个包", StLabel, GUILayout.Width(100));

            GUILayout.Space(8);

            if (BtnSuccess("📥 批量切换到本地", GUILayout.Width(140)))
                SwitchBatchToLocal(selected);

            GUILayout.Space(6);

            if (BtnPrimary("📤 批量切回 Git", GUILayout.Width(130)))
                SwitchBatchToGit(selected);

            GUILayout.Space(6);

            if (BtnWarn("🔄 批量 Git 更新", GUILayout.Width(130)))
                PullBatchFromGit(selected);
        }
        EditorGUILayout.EndHorizontal();
    }

    // ─── 手动添加表单 ─────────────────────────────────────────

    private void DrawAddGitPackageForm()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(_cardStyle);
        {
            GUILayout.Label("手动添加 Git 包", StTitle);
            EditorGUILayout.Space(4);

            _newGitUrl = EditorGUILayout.TextField("Git URL", _newGitUrl);
            _newPackageName = EditorGUILayout.TextField("包名 (com.xxx.yyy)", _newPackageName);
            _newLocalFolder = EditorGUILayout.TextField("本地文件夹名", _newLocalFolder);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            {
                if (BtnSuccess("确认添加", GUILayout.Width(100)))
                    AddGitPackage(_newGitUrl, _newPackageName, _newLocalFolder);
                if (DrawFlatButton("取消", GUILayout.Width(60)))
                    ClearAddForm();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }

    private void ClearAddForm()
    {
        _showAddForm = false;
        _newGitUrl = "";
        _newPackageName = "";
        _newLocalFolder = "";
    }

    // ═══════════════════════════════════════════════════════════════
    //  核心逻辑
    // ═══════════════════════════════════════════════════════════════

    /// <summary>刷新包列表</summary>
    private void RefreshPackageList()
    {
        _packages.Clear();
        _statusMessage = "";

        if (!File.Exists(_manifestPath))
        {
            ShowStatus("找不到 manifest.json: " + _manifestPath, MessageType.Error);
            return;
        }

        try
        {
            string json = File.ReadAllText(_manifestPath);
            var manifest = ParseManifest(json);
            if (manifest == null)
            {
                ShowStatus("manifest.json 解析失败", MessageType.Error);
                return;
            }

            // 收集所有本地包目录名
            var localDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string packagesDir = Path.Combine(_projectRoot, "Packages");
            if (Directory.Exists(packagesDir))
            {
                foreach (var dir in Directory.GetDirectories(packagesDir))
                {
                    localDirs.Add(Path.GetFileName(dir));
                }
            }

            foreach (var kv in manifest)
            {
                string pkgName = kv.Key;
                string value = kv.Value;

                // 跳过 Unity 内置模块
                if (pkgName.StartsWith("com.unity.modules.")) continue;

                var info = new PackageInfo
                {
                    packageName = pkgName,
                    isSelected = false,
                };

                if (IsGitUrl(value))
                {
                    info.gitUrl = value;
                    info.isGit = true;
                    ParseGitUrl(value, info);

                    // 检查是否有对应的本地目录
                    string localFolder = GuessLocalFolder(pkgName, info);
                    info.localFolder = localFolder;

                    if (localDirs.Contains(localFolder))
                    {
                        // 本地目录存在但 manifest 里是 Git → 仍然是 Git 模式
                        // 用户可以手动切换
                    }
                }
                else if (IsLocalRef(value))
                {
                    // 已经是本地引用
                    info.isLocal = true;
                    info.localFolder = ParseLocalFolder(value);
                    info.gitUrl = TryFindGitUrlFromLock(pkgName);

                    // Fallback: 从 .git/config 读取远程 URL
                    if (string.IsNullOrEmpty(info.gitUrl))
                    {
                        string localDir = Path.Combine(_projectRoot, "Packages", info.localFolder);
                        info.gitUrl = TryReadGitConfig(localDir);
                    }

                    // Fallback: 从元数据文件读取
                    if (string.IsNullOrEmpty(info.gitUrl))
                        info.gitUrl = TryReadGitMeta(info.localFolder);

                    if (!string.IsNullOrEmpty(info.gitUrl))
                        ParseGitUrl(info.gitUrl, info);
                }
                else
                {
                    // Registry 版本包，跳过
                    continue;
                }

                _packages.Add(info);
            }

            // ── 补充扫描 Packages/ 目录中未在 manifest 声明的本地包 ──
            // Unity 会自动发现 Packages/ 下的本地包，无需在 manifest 中声明
            var knownNames = new HashSet<string>(_packages.Select(p => p.packageName), StringComparer.OrdinalIgnoreCase);
            string packagesDir2 = Path.Combine(_projectRoot, "Packages");
            if (Directory.Exists(packagesDir2))
            {
                foreach (var dir in Directory.GetDirectories(packagesDir2))
                {
                    string folderName = Path.GetFileName(dir);
                    string pjPath = Path.Combine(dir, "package.json");
                    if (!File.Exists(pjPath)) continue;

                    // 读取包名
                    string pkgName = folderName;
                    try
                    {
                        string pj = File.ReadAllText(pjPath);
                        var nameMatch = Regex.Match(pj, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                        if (nameMatch.Success) pkgName = nameMatch.Groups[1].Value;
                    }
                    catch { }

                    if (knownNames.Contains(pkgName)) continue;

                    var info = new PackageInfo
                    {
                        packageName = pkgName,
                        localFolder = folderName,
                        isLocal = true,
                        isSelected = false,
                    };

                    // 尝试从 packages-lock.json 获取 git URL
                    info.gitUrl = TryFindGitUrlFromLock(pkgName);
                    if (!string.IsNullOrEmpty(info.gitUrl))
                        ParseGitUrl(info.gitUrl, info);

                    // 如果 lock 文件没有，尝试从 .git/config 读取远程 URL
                    if (string.IsNullOrEmpty(info.gitUrl))
                    {
                        info.gitUrl = TryReadGitConfig(dir);
                        if (!string.IsNullOrEmpty(info.gitUrl))
                            ParseGitUrl(info.gitUrl, info);
                    }

                    // Fallback: 从元数据文件读取
                    if (string.IsNullOrEmpty(info.gitUrl))
                    {
                        info.gitUrl = TryReadGitMeta(folderName);
                        if (!string.IsNullOrEmpty(info.gitUrl))
                            ParseGitUrl(info.gitUrl, info);
                    }

                    _packages.Add(info);
                }
            }

            int gitCount = _packages.Count(p => p.isGit);
            int localCount = _packages.Count(p => p.isLocal);
            ShowStatus($"已加载 {_packages.Count} 个包（{gitCount} Git / {localCount} 本地）", MessageType.Info);
        }
        catch (Exception ex)
        {
            ShowStatus("读取 manifest.json 失败: " + ex.Message, MessageType.Error);
            Debug.LogException(ex);
        }
    }

    /// <summary>切换 Git 包到本地模式</summary>
    private void SwitchToLocal(PackageInfo pkg)
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            string packagesDir = Path.Combine(_projectRoot, "Packages");
            string localPath = Path.Combine(packagesDir, pkg.localFolder);

            // 1. 如果本地目录不存在，先 git clone
            if (!Directory.Exists(localPath))
            {
                ShowStatus($"正在克隆 {pkg.packageName} ...", MessageType.Info);
                Repaint();

                string cloneUrl = pkg.gitUrl;
                // 去掉 ?path=xxx 后缀用于 clone
                int pathIdx = cloneUrl.IndexOf("?path=");
                if (pathIdx > 0) cloneUrl = cloneUrl.Substring(0, pathIdx);
                // 去掉 #branch 后缀
                int hashIdx = cloneUrl.IndexOf("#");
                string branch = pkg.branch;
                if (hashIdx > 0)
                {
                    branch = cloneUrl.Substring(hashIdx + 1);
                    cloneUrl = cloneUrl.Substring(0, hashIdx);
                }

                if (!RunGitCommand($"clone --depth 1 -b {branch} \"{cloneUrl}\" \"{localPath}\"", _projectRoot))
                {
                    ShowStatus($"克隆失败: {pkg.packageName}，请检查网络和 Git 配置", MessageType.Error);
                    _isProcessing = false;
                    return;
                }

                // 如果有子目录，需要调整
                if (!string.IsNullOrEmpty(pkg.subPath))
                {
                    // Git URL 带 ?path=xxx 的情况
                    // Unity 会自动处理子目录，本地模式下需要把 package.json 所在的实际目录作为引用路径
                    // 这种情况暂时保留目录结构，用户可自行调整
                }
            }

            // 2. 修改 manifest.json
            string manifest = File.ReadAllText(_manifestPath);
            string oldValue = $"\"{pkg.packageName}\": \"{pkg.gitUrl}\"";
            string newValue = $"\"{pkg.packageName}\": \"file:{pkg.localFolder}\"";

            // 保存 Git 元数据，便于后续恢复 Git URL
            SaveGitMeta(pkg.localFolder, pkg.gitUrl, pkg.branch);

            if (manifest.Contains(oldValue))
            {
                manifest = manifest.Replace(oldValue, newValue);
                File.WriteAllText(_manifestPath, manifest);
                ShowStatus($"✅ {pkg.packageName} 已切换到本地模式", MessageType.Info);
            }
            else
            {
                ShowStatus($"manifest 中未找到匹配条目，尝试精确解析...", MessageType.Warning);
                // 使用更精确的替换
                manifest = ReplaceManifestEntry(manifest, pkg.packageName, $"file:{pkg.localFolder}");
                File.WriteAllText(_manifestPath, manifest);
                ShowStatus($"✅ {pkg.packageName} 已切换到本地模式（精确替换）", MessageType.Info);
            }

            RefreshPackageList();
        }
        catch (Exception ex)
        {
            ShowStatus("切换失败: " + ex.Message, MessageType.Error);
            Debug.LogException(ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>切换本地包回 Git 模式</summary>
    private void SwitchToGit(PackageInfo pkg)
    {
        if (_isProcessing) return;

        if (string.IsNullOrEmpty(pkg.gitUrl))
        {
            ShowStatus($"{pkg.packageName} 没有记录 Git URL，无法切回", MessageType.Warning);
            return;
        }

        _isProcessing = true;

        try
        {
            string manifest = File.ReadAllText(_manifestPath);
            string oldValue = $"\"{pkg.packageName}\": \"file:{pkg.localFolder}\"";
            string newValue = $"\"{pkg.packageName}\": \"{pkg.gitUrl}\"";

            if (manifest.Contains(oldValue))
            {
                manifest = manifest.Replace(oldValue, newValue);
            }
            else
            {
                manifest = ReplaceManifestEntry(manifest, pkg.packageName, pkg.gitUrl);
            }

            File.WriteAllText(_manifestPath, manifest);
            ShowStatus($"✅ {pkg.packageName} 已切回 Git 模式", MessageType.Info);

            RefreshPackageList();
        }
        catch (Exception ex)
        {
            ShowStatus("切换失败: " + ex.Message, MessageType.Error);
            Debug.LogException(ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>从 Git 拉取最新代码到本地目录</summary>
    private void PullFromGit(PackageInfo pkg)
    {
        if (_isProcessing) return;

        if (string.IsNullOrEmpty(pkg.gitUrl))
        {
            ShowStatus($"{pkg.packageName} 没有记录 Git URL，无法拉取", MessageType.Warning);
            return;
        }

        string localPath = Path.Combine(_projectRoot, "Packages", pkg.localFolder);
        if (!Directory.Exists(localPath))
        {
            ShowStatus($"本地目录不存在: {localPath}", MessageType.Warning);
            return;
        }

        // 检查是否是 git 仓库
        string gitDir = Path.Combine(localPath, ".git");
        if (!Directory.Exists(gitDir))
        {
            ShowStatus($"{pkg.localFolder} 不是 Git 仓库，无法拉取更新", MessageType.Warning);
            return;
        }

        _isProcessing = true;

        try
        {
            ShowStatus($"正在拉取 {pkg.packageName} 最新代码...", MessageType.Info);
            Repaint();

            if (RunGitCommand("pull", localPath))
            {
                ShowStatus($"✅ {pkg.packageName} 已更新到最新版本", MessageType.Info);
            }
            else
            {
                ShowStatus($"拉取失败: {pkg.packageName}，可能存在冲突", MessageType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowStatus("拉取失败: " + ex.Message, MessageType.Error);
            Debug.LogException(ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    // ─── 批量操作 ─────────────────────────────────────────────

    /// <summary>从 manifest.json 中移除包条目（不删除本地文件）</summary>
    private void RemoveFromManifest(PackageInfo pkg)
    {
        if (!EditorUtility.DisplayDialog("确认移除",
            $"确定要从 manifest.json 中移除 {pkg.packageName} 吗？\n\n这只会移除 manifest 中的引用，不会删除本地文件。",
            "移除", "取消"))
            return;

        try
        {
            string manifest = File.ReadAllText(_manifestPath);
            string entryPattern = $"\"{Regex.Escape(pkg.packageName)}\"\\s*:\\s*\"[^\"]*\"\\s*,?";
            manifest = Regex.Replace(manifest, entryPattern, "");
            manifest = Regex.Replace(manifest, @",\s*,", ",");
            File.WriteAllText(_manifestPath, manifest);

            ShowStatus($"✅ 已从 manifest 移除: {pkg.packageName}", MessageType.Info);
            RefreshPackageList();
        }
        catch (Exception ex)
        {
            ShowStatus($"移除失败: {ex.Message}", MessageType.Error);
            Debug.LogException(ex);
        }
    }

    /// <summary>删除本地包目录（清除只读属性后删除）</summary>
    private void DeleteLocalPackage(PackageInfo pkg)
    {
        string localPath = Path.Combine(_projectRoot, "Packages", pkg.localFolder);

        if (!Directory.Exists(localPath))
        {
            ShowStatus($"目录不存在: {localPath}", MessageType.Warning);
            return;
        }

        if (!EditorUtility.DisplayDialog("确认删除",
            $"确定要删除本地包目录吗？\n\n{pkg.localFolder}\n{localPath}\n\n此操作不可撤销。",
            "删除", "取消"))
            return;

        _isProcessing = true;
        try
        {
            // git clone 会在 .git 下创建只读文件，先清除只读属性再删除
            ClearReadOnlyAttributes(localPath);

            Directory.Delete(localPath, true);
            ShowStatus($"✅ 已删除本地包目录: {pkg.localFolder}", MessageType.Info);

            // 如果 manifest 中有 file: 引用，同时移除
            if (!string.IsNullOrEmpty(pkg.packageName))
            {
                string manifest = File.ReadAllText(_manifestPath);
                string entryPattern = $"\"{Regex.Escape(pkg.packageName)}\"\\s*:\\s*\"[^\"]*\"\\s*,?";
                manifest = Regex.Replace(manifest, entryPattern, "");
                // 清理可能残留的连续逗号
                manifest = Regex.Replace(manifest, @",\s*,", ",");
                File.WriteAllText(_manifestPath, manifest);
            }

            RefreshPackageList();
        }
        catch (Exception ex)
        {
            ShowStatus($"删除失败: {ex.Message}", MessageType.Error);
            Debug.LogException(ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>递归清除目录下所有文件和子目录的只读属性</summary>
    private static void ClearReadOnlyAttributes(string dirPath)
    {
        // 先递归处理子目录
        foreach (var subDir in Directory.GetDirectories(dirPath))
            ClearReadOnlyAttributes(subDir);

        // 清除文件只读属性
        foreach (var file in Directory.GetFiles(dirPath))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }

        // 清除目录自身的只读属性
        var dirAttrs = File.GetAttributes(dirPath);
        if ((dirAttrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(dirPath, dirAttrs & ~FileAttributes.ReadOnly);
    }

    private void SwitchBatchToLocal(List<PackageInfo> packages)
    {
        foreach (var pkg in packages)
        {
            if (pkg.isGit)
                SwitchToLocal(pkg);
        }
    }

    private void SwitchBatchToGit(List<PackageInfo> packages)
    {
        foreach (var pkg in packages)
        {
            if (pkg.isLocal)
                SwitchToGit(pkg);
        }
    }

    private void PullBatchFromGit(List<PackageInfo> packages)
    {
        foreach (var pkg in packages)
        {
            if (pkg.isLocal)
                PullFromGit(pkg);
        }
    }

    /// <summary>手动添加 Git 包</summary>
    private void AddGitPackage(string gitUrl, string packageName, string localFolder)
    {
        if (string.IsNullOrEmpty(gitUrl) || string.IsNullOrEmpty(packageName))
        {
            ShowStatus("Git URL 和包名不能为空", MessageType.Warning);
            return;
        }

        if (string.IsNullOrEmpty(localFolder))
        {
            localFolder = packageName;
        }

        // 检查是否已存在
        if (_packages.Any(p => p.packageName == packageName))
        {
            ShowStatus($"包 {packageName} 已存在于列表中", MessageType.Warning);
            return;
        }

        // 添加到 manifest.json
        try
        {
            string manifest = File.ReadAllText(_manifestPath);

            // 在 dependencies 的 } 之前插入
            int lastBrace = manifest.LastIndexOf('}');
            if (lastBrace > 0)
            {
                string entry = $"    \"{packageName}\": \"{gitUrl}\",\n";
                manifest = manifest.Insert(lastBrace, entry);
                File.WriteAllText(_manifestPath, manifest);
            }

            _newGitUrl = "";
            _newPackageName = "";
            _newLocalFolder = "";
            _showAddForm = false;

            ShowStatus($"✅ 已添加 {packageName} 到 manifest.json", MessageType.Info);
            RefreshPackageList();
        }
        catch (Exception ex)
        {
            ShowStatus("添加失败: " + ex.Message, MessageType.Error);
            Debug.LogException(ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════════════════════════

    /// <summary>判断是否为 Git URL</summary>
    private static bool IsGitUrl(string value)
    {
        return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断是否为本地引用</summary>
    private static bool IsLocalRef(string value)
    {
        return value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>解析 Git URL，提取分支和子目录</summary>
    private static void ParseGitUrl(string url, PackageInfo info)
    {
        // 格式: https://github.com/user/repo.git?path=/sub/dir#branch
        string working = url;

        // 提取 #branch
        int hashIdx = working.IndexOf('#');
        if (hashIdx > 0)
        {
            info.branch = working.Substring(hashIdx + 1);
            working = working.Substring(0, hashIdx);
        }
        else
        {
            info.branch = "main";
        }

        // 提取 ?path=
        int pathIdx = working.IndexOf("?path=");
        if (pathIdx > 0)
        {
            info.subPath = working.Substring(pathIdx + 6).TrimStart('/');
            working = working.Substring(0, pathIdx);
        }

        // 清理 URL 用于显示
        info.gitUrl = url;
    }

    /// <summary>猜测本地文件夹名</summary>
    private static string GuessLocalFolder(string packageName, PackageInfo info)
    {
        // 优先用包名的最后部分
        string[] parts = packageName.Split('.');
        if (parts.Length >= 2)
            return parts[parts.Length - 1];

        return packageName;
    }

    /// <summary>从本地引用解析文件夹名</summary>
    private static string ParseLocalFolder(string value)
    {
        // file:xxx 或 file:Packages/xxx
        string folder = value.Substring(5); // 去掉 "file:"
        // 取最后一段
        if (folder.Contains("/"))
            folder = folder.Substring(folder.LastIndexOf('/') + 1);
        if (folder.Contains("\\"))
            folder = folder.Substring(folder.LastIndexOf('\\') + 1);
        return folder;
    }

    /// <summary>从 .git/config 读取远程 URL</summary>
    private static string TryReadGitConfig(string localDir)
    {
        string gitConfig = Path.Combine(localDir, ".git", "config");
        if (!File.Exists(gitConfig)) return null;

        try
        {
            string cfg = File.ReadAllText(gitConfig);
            var urlMatch = Regex.Match(cfg, "url\\s*=\\s*(.+)");
            if (urlMatch.Success)
                return urlMatch.Groups[1].Value.Trim();
        }
        catch { }

        return null;
    }

    /// <summary>保存 Git 元数据到本地包目录</summary>
    private void SaveGitMeta(string localFolder, string gitUrl, string branch)
    {
        try
        {
            string metaPath = Path.Combine(_projectRoot, "Packages", localFolder, ".gitswitcher.json");
            string json = $"{{\"gitUrl\":\"{gitUrl}\",\"branch\":\"{branch ?? "main"}\"}}";
            File.WriteAllText(metaPath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"保存 Git 元数据失败: {ex.Message}");
        }
    }

    /// <summary>从本地包目录读取 Git 元数据</summary>
    private string TryReadGitMeta(string localFolder)
    {
        try
        {
            string metaPath = Path.Combine(_projectRoot, "Packages", localFolder, ".gitswitcher.json");
            if (!File.Exists(metaPath)) return null;

            string json = File.ReadAllText(metaPath);
            var urlMatch = Regex.Match(json, "\"gitUrl\"\\s*:\\s*\"([^\"]+)\"");
            return urlMatch.Success ? urlMatch.Groups[1].Value : null;
        }
        catch { return null; }
    }

    /// <summary>从 packages-lock.json 尝试查找原始 Git URL</summary>
    private string TryFindGitUrlFromLock(string packageName)
    {
        string lockPath = Path.Combine(_projectRoot, "Packages", "packages-lock.json");
        if (!File.Exists(lockPath)) return null;

        try
        {
            string lockJson = File.ReadAllText(lockPath);
            // 简单文本搜索
            string pattern = $"\"{packageName}\"";
            int idx = lockJson.IndexOf(pattern);
            if (idx < 0) return null;

            // 搜索 url 字段
            string segment = lockJson.Substring(idx, Math.Min(1000, lockJson.Length - idx));
            var urlMatch = Regex.Match(segment, "\"url\"\\s*:\\s*\"([^\"]+)\"");
            if (urlMatch.Success)
                return urlMatch.Groups[1].Value;

            // 搜索 repository 字段
            var repoMatch = Regex.Match(segment, "\"repository\"\\s*:\\s*\"([^\"]+)\"");
            if (repoMatch.Success)
                return repoMatch.Groups[1].Value;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"读取 packages-lock.json 失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>精确替换 manifest 中某个包的引用值</summary>
    private static string ReplaceManifestEntry(string manifest, string packageName, string newValue)
    {
        // 匹配 "com.xxx.yyy": "任意内容"
        string pattern = $"\"{Regex.Escape(packageName)}\"\\s*:\\s*\"[^\"]*\"";
        string replacement = $"\"{packageName}\": \"{newValue}\"";
        return Regex.Replace(manifest, pattern, replacement);
    }

    /// <summary>解析 manifest.json（简单 JSON 解析，避免依赖）</summary>
    private static Dictionary<string, string> ParseManifest(string json)
    {
        var result = new Dictionary<string, string>();

        int depth;

        // 简化处理：直接解析 dependencies 块
        int depStart = json.IndexOf("\"dependencies\"");
        if (depStart < 0) return result;

        int blockStart = json.IndexOf('{', depStart);
        if (blockStart < 0) return result;

        // 找到对应的闭合 }
        depth = 0;
        int blockEnd = -1;
        for (int i = blockStart; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    blockEnd = i;
                    break;
                }
            }
        }

        if (blockEnd < 0) return result;

        string depBlock = json.Substring(blockStart, blockEnd - blockStart + 1);
        var depRegex = new Regex("\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
        var depMatches = depRegex.Matches(depBlock);

        foreach (Match m in depMatches)
        {
            result[m.Groups[1].Value] = m.Groups[2].Value;
        }

        return result;
    }

    /// <summary>执行 Git 命令</summary>
    private static bool RunGitCommand(string arguments, string workingDirectory)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(psi))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Debug.LogError($"Git 命令失败: git {arguments}\n{stderr}");
                    return false;
                }

                if (!string.IsNullOrEmpty(stdout))
                    Debug.Log($"Git 输出: {stdout}");

                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Git 命令执行异常: {ex.Message}");
            return false;
        }
    }

    private void ShowStatus(string message, MessageType type = MessageType.Info)
    {
        _statusMessage = message;
        Repaint();
    }
}
