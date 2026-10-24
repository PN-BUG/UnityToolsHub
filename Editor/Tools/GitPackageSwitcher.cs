using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityPackageCreator.Editor;
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
                  "新工作副本默认放在 LocalPackages 目录下，支持后台切换和 Git 更新。",
    Icon = "📦", Tags = new[] { "Git", "包管理", "依赖", "切换", "本地" }, ShowInUnityMenuByDefault = true)]
public partial class GitPackageSwitcher : ToolEditorWindow
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
        public bool isAsset;
        public string branch;
        public string subPath;
        public string repositoryPath;
        public GitRepositoryStatus gitStatus;
        public bool checkingStatus;
    }

    // ═══════════════════════════════════════════════════════════════
    //  状态 & 缓存
    // ═══════════════════════════════════════════════════════════════

    private List<PackageInfo> _packages = new List<PackageInfo>();
    private bool _isProcessing;
    [SerializeField] private string _statusMessage = "";
    [SerializeField] private MessageType _statusType = MessageType.Info;
    [SerializeField] private bool _showStatusDetails;
    private string _manifestPath;
    private string _projectRoot;

    private sealed class PackageOperation
    {
        public string message;
        public Func<CancellationToken, string> run;
        public Action<string> complete;
    }

    private readonly Queue<PackageOperation> _packageOperations = new Queue<PackageOperation>();
    private PackageOperation _activeOperation;
    private Task<string> _operationTask;
    private CancellationTokenSource _operationCancellation;
    private double _operationStartedAt, _lastOperationRepaint;
    private bool _reloadLocked, _packagesChanged;
    private int _operationSucceeded, _operationFailed;
    private string _lastOperationError;

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
    private GUIStyle _packageNameStyle;
    private GUIStyle _metaStyle;
    private GUIStyle _segmentStyle;
    private GUIStyle _selectedCountStyle;
    private GUIStyle _checkMarkStyle;
    private GUIStyle _headingStyle;

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
        titleContent = new GUIContent("Git 包切换器");
        minSize = new Vector2(460, 420);
        _projectRoot = Path.GetDirectoryName(Application.dataPath);
        _manifestPath = Path.Combine(_projectRoot, ManifestRelativePath);
        RefreshPackageList(string.IsNullOrEmpty(_statusMessage));
        EditorApplication.update += PollGitStatus;
        EditorApplication.update += PollPackageOperation;
    }

    protected override void OnToolDisable()
    {
        EditorApplication.update -= PollGitStatus;
        EditorApplication.update -= PollPackageOperation;
        _gitStatusCancellation?.Cancel();
        _operationCancellation?.Cancel();
        _packageOperations.Clear();
        if (_reloadLocked)
        {
            EditorApplication.UnlockReloadAssemblies();
            _reloadLocked = false;
        }
    }

    private void OnFocus()
    {
        if (!string.IsNullOrEmpty(_projectRoot) && _gitStatusJobs.Count == 0 && !_isProcessing)
            StartGitStatusRefresh(false);
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

        DrawStatusArea();
        using (new EditorGUI.DisabledScope(_isProcessing))
        {
            DrawToolbarArea();
            DrawGitClientSettings();
            DrawViewToggles();
            DrawBatchActions();
            DrawScanSection();
            DrawCacheSection();

            if (_packages.Count == 0)
            {
                DrawEmptyState();
                return;
            }

            DrawPackageList();
            // 空余高度留在列表之后，不分配给包卡片。
            GUILayout.FlexibleSpace();
        }
    }

    private void EnsureStyles()
    {
        if (_cardStyle == null)
        {
            _cardStyle = new GUIStyle("box")
            {
                padding = new RectOffset(14, 12, 10, 10),
                margin = new RectOffset(0, 0, 3, 3),
                normal = { background = null },
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
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ClrTextBright, background = null },
                hover = { textColor = Color.white, background = null },
                active = { textColor = new Color(0.85f, 0.85f, 0.85f), background = null },
                padding = new RectOffset(6, 6, 4, 4),
            };
        }
        if (_packageNameStyle == null)
        {
            _packageNameStyle = new GUIStyle(StTitle)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                fixedHeight = 22,
            };
        }
        if (_metaStyle == null)
        {
            _metaStyle = new GUIStyle(StLabelDim)
            {
                fontSize = 11,
                clipping = TextClipping.Clip,
                fixedHeight = 18,
                normal = { textColor = Color.Lerp(ClrTextDim, ClrTextBright, 0.25f) },
            };
        }
        if (_segmentStyle == null)
        {
            _segmentStyle = new GUIStyle
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ClrTextDim },
                hover = { textColor = ClrTextBright },
                active = { textColor = Color.white },
            };
        }
        if (_selectedCountStyle == null)
        {
            _selectedCountStyle = new GUIStyle(StLabel)
            {
                alignment = TextAnchor.MiddleRight,
            };
        }
        if (_checkMarkStyle == null)
        {
            _checkMarkStyle = new GUIStyle
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
        }
        if (_headingStyle == null)
            _headingStyle = new GUIStyle(StTitle) { fontSize = 17, fixedHeight = 24 };
    }

    /// <summary>绘制带颜色的按钮（透明背景，颜色由 DrawRect 提供）</summary>
    private bool DrawColoredBtn(string text, Color normal, Color hover, params GUILayoutOption[] options)
        => DrawColoredBtn(new GUIContent(text), normal, hover, options);

    private bool DrawColoredBtn(GUIContent text, Color normal, Color hover, params GUILayoutOption[] options)
    {
        var rect = GUILayoutUtility.GetRect(GUIContent.none, _coloredBtnStyle, options);
        return DrawColoredBtn(rect, text, normal, hover);
    }

    private bool DrawColoredBtn(Rect rect, GUIContent text, Color normal, Color hover)
    {
        bool isHover = GUI.enabled && rect.Contains(Event.current.mousePosition);
        // 将高饱和语义色混入深色卡片背景，只保留轻量色彩提示。
        // 悬停时提高混合比例，并用底部细线强化交互反馈。
        float blend = isHover ? 0.70f : 0.52f;
        Color semantic = isHover ? hover : normal;
        Color background = Color.Lerp(ClrCardBg, semantic, blend);
        if (!GUI.enabled) background = Color.Lerp(ClrCardBg, background, 0.35f);
        Drawing.DrawRoundedRect(rect, background, 4f);

        Color accent = Color.Lerp(background, semantic, isHover ? 0.85f : 0.65f);
        accent.a = 0.9f;
        EditorGUI.DrawRect(new Rect(rect.x + 3, rect.yMax - 2, rect.width - 6, 2), accent);

        _coloredBtnStyle.normal.textColor = !GUI.enabled ? ClrTextDim : isHover ? Color.white : ClrTextBright;
        return GUI.Button(rect, text, _coloredBtnStyle);
    }

    private bool BtnSuccess(string text, params GUILayoutOption[] opts) => DrawColoredBtn(text, ClrBtnSuccess, ClrBtnSuccessHov, opts);
    private bool BtnPrimary(string text, params GUILayoutOption[] opts) => DrawColoredBtn(text, ClrBtnNormal, ClrBtnHover, opts);
    private bool BtnWarn(string text, params GUILayoutOption[] opts)    => DrawColoredBtn(text, ClrBtnWarn, ClrBtnWarnHov, opts);
    private bool BtnDanger(string text, params GUILayoutOption[] opts)  => DrawColoredBtn(text, ClrBtnDanger, ClrBtnDangerHov, opts);

    private bool DrawPackageToggle(bool value, string tooltip = "选择此包")
    {
        var layoutRect = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20), GUILayout.Height(20));
        var rect = new Rect(layoutRect.x + 2, layoutRect.y + 2, 16, 16);
        bool hover = rect.Contains(Event.current.mousePosition);

        Color border = value ? ClrAccent : (hover ? ClrTextDim : ClrDivider);
        Color background = value
            ? Color.Lerp(ClrCardBg, ClrAccent, hover ? 0.85f : 0.70f)
            : (hover ? ClrItemHover : ClrItemBg);
        Drawing.DrawRoundedRect(rect, border, 3f);
        Drawing.DrawRoundedRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), background, 2f);

        if (value)
            GUI.Label(rect, "✓", _checkMarkStyle);

        if (GUI.Button(rect, new GUIContent("", tooltip), GUIStyle.none))
            value = !value;
        return value;
    }

    // ─── 顶部工具栏 ───────────────────────────────────────────

    private void DrawToolbarArea()
    {
        var toolbarRect = EditorGUILayout.BeginVertical(_cardStyle);
        Drawing.DrawRoundedRect(toolbarRect, ClrToolbarBg, 6f);
        GUILayout.Label("Git 包切换器", _headingStyle);
        GUILayout.Label("管理包来源，在 Git 依赖与本地工作副本之间切换", _metaStyle);
        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        if (BtnPrimary("刷新列表", GUILayout.Width(78), GUILayout.Height(28))) RefreshPackageList();
        GUILayout.Space(6);
        if (BtnSuccess("添加 Git 包", GUILayout.Width(96), GUILayout.Height(28))) _showAddForm = !_showAddForm;
        GUILayout.Space(6);
        using (new EditorGUI.DisabledScope(_gitStatusJobs.Count > 0))
            if (BtnPrimary("检查远端", GUILayout.Width(86), GUILayout.Height(28))) StartGitStatusRefresh(true);
        GUILayout.Space(6);
        if (BtnPrimary("更多工具 ▾", GUILayout.Width(96), GUILayout.Height(28)))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("扫描本地目录…"), false, PickAndScanDirectory);
            menu.AddItem(new GUIContent("检测 PackageCache"), false, ScanPackageCache);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Git 客户端设置"), _showGitClientSettings, () => _showGitClientSettings = !_showGitClientSettings);
            menu.ShowAsContext();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.Label(_gitStatusJobs.Count > 0 ? $"后台检查中 · 剩余 {_gitStatusJobs.Count} 组仓库" : "返回窗口自动刷新本地状态 · 远端检查只更新状态", _metaStyle);
        if (_showAddForm) DrawAddGitPackageForm();
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(6);
    }
    private void DrawViewToggles()
    {
        int gitCount = _packages.Count(p => p.isGit), localCount = _packages.Count(p => p.isLocal);
        int selectedCount = _packages.Count(p => p.isSelected);
        var rect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
        Drawing.DrawRoundedRect(rect, ClrToolbarBg, 6f);
        float x = rect.x + 10;
        if (DrawFilterSegment(new Rect(x, rect.y + 7, 72, 26), $"全部 {_packages.Count}", _filter == PackageFilter.All, ClrAccent)) _filter = PackageFilter.All;
        x += 76;
        if (DrawFilterSegment(new Rect(x, rect.y + 7, 64, 26), $"Git {gitCount}", _filter == PackageFilter.Git, ClrCatOrange)) _filter = PackageFilter.Git;
        x += 68;
        if (DrawFilterSegment(new Rect(x, rect.y + 7, 64, 26), $"本地 {localCount}", _filter == PackageFilter.Local, ClrCatGreen)) _filter = PackageFilter.Local;
        _selectedCountStyle.normal.textColor = selectedCount > 0 ? ClrCatPurple : ClrTextDim;
        GUI.Label(new Rect(rect.xMax - 156, rect.y + 9, 54, 22), $"已选 {selectedCount}", _selectedCountStyle);
        if (GUI.Button(new Rect(rect.xMax - 94, rect.y + 7, 32, 26), "全选", _segmentStyle)) _packages.ForEach(p => p.isSelected = true);
        if (GUI.Button(new Rect(rect.xMax - 58, rect.y + 7, 48, 26), "清空", _segmentStyle)) _packages.ForEach(p => p.isSelected = false);
        EditorGUILayout.Space(8);
    }
    // ─── 状态提示 ─────────────────────────────────────────────

    private void DrawStatusArea()
    {
        if (string.IsNullOrEmpty(_statusMessage) || (!_showStatusDetails && !_isProcessing)) return;
        EditorGUILayout.Space(4);
        string message = _statusMessage;
        if (_isProcessing && _activeOperation != null)
        {
            message += $"\n已用时 {(int)(EditorApplication.timeSinceStartup - _operationStartedAt)} 秒";
            if (_packageOperations.Count > 0) message += $" · 等待 {_packageOperations.Count} 个操作";
        }
        EditorGUILayout.HelpBox(message, _statusType);
        if (_isProcessing && _operationCancellation != null)
            using (new EditorGUI.DisabledScope(_operationCancellation.IsCancellationRequested))
                if (GUILayout.Button("取消剩余操作", GUILayout.Width(110)))
                {
                    _packageOperations.Clear();
                    _operationCancellation.Cancel();
                    ShowStatus("正在取消，请稍候…已完成的操作会保留。", MessageType.Warning);
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
            // 包可能直接位于所选目录，也可能嵌套在 Assets 的多级子目录中。
            foreach (var packageJsonPath in Directory.GetFiles(directoryPath, "package.json", SearchOption.AllDirectories))
            {
                string subDir = Path.GetDirectoryName(packageJsonPath);
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
            Debug.LogError(PackageReferenceUtility.Redact(ex.ToString()));
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
                    pkg.isSelected = DrawPackageToggle(pkg.isSelected);

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

        try
        {
            string fileRef = PackageReferenceUtility.PrepareLocalPackageReference(scanned.folderPath, _projectRoot);
            AddGitPackage(fileRef, scanned.name, scanned.folderName);
        }
        catch (Exception ex) { ShowStatus(ex.Message, MessageType.Error); }
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        if (!PackageReferenceUtility.IsWithin(basePath, fullPath))
            throw new ArgumentException("包位于项目外，请先通过本地导入复制到 LocalPackages。");
        return PackageReferenceUtility.RelativePath(basePath, fullPath);
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
                    (!string.IsNullOrEmpty(gitUrl) ? "\n    " + PackageReferenceUtility.Redact(gitUrl) : "");

                _cachePackages.Add(pkg);
            }

            ShowStatus($"PackageCache 检测完成，发现 {_cachePackages.Count} 个 Git 缓存包", MessageType.Info);
        }
        catch (Exception ex)
        {
            ShowStatus("扫描 PackageCache 失败: " + ex.Message, MessageType.Error);
            Debug.LogError(PackageReferenceUtility.Redact(ex.ToString()));
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
            Debug.LogWarning(PackageReferenceUtility.Redact($"读取 packages-lock.json 失败: {ex.Message}"));
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
                    pkg.isSelected = DrawPackageToggle(pkg.isSelected);

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
        // ── 按过滤分组显示 ──
        // ToolEditorWindow 已提供整体滚动，避免嵌套滚动挤压列表高度。

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

        if ((_filter == PackageFilter.Git && gitPackages.Count == 0)
            || (_filter == PackageFilter.Local && localPackages.Count == 0))
            EditorGUILayout.HelpBox("当前分类下没有包，可切换到其他分类查看。", MessageType.Info);
    }

    private bool DrawFilterSegment(Rect rect, string text, bool active, Color accent)
    {
        bool hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Color background = active ? Color.Lerp(ClrToolbarBg, accent, 0.22f) : hover ? ClrItemHover : ClrToolbarBg;
        Drawing.DrawRoundedRect(rect, background, 4);
        if (active) EditorGUI.DrawRect(new Rect(rect.x + 6, rect.yMax - 2, rect.width - 12, 2), accent);
        _segmentStyle.normal.textColor = active ? ClrTextBright : ClrTextDim;
        return GUI.Button(rect, text, _segmentStyle);
    }

    private void DrawPackageRow(PackageInfo pkg)
    {
        const float padding = 12;
        string branch = pkg.isGit && string.IsNullOrEmpty(pkg.branch) ? "远端默认分支" : pkg.gitStatus?.branch ?? pkg.branch;
        float estimatedWidth = Mathf.Max(180, position.width - ContentPadding * 2 - 18 - padding * 2 - 26);
        float badgeHeight = GetGitStatusHeight(pkg, estimatedWidth);
        float height = padding * 2 + 22 + (!string.IsNullOrEmpty(pkg.gitUrl) ? 20 : 0)
            + (pkg.isLocal ? 20 : 0) + (!string.IsNullOrEmpty(branch) ? 18 : 0) + badgeHeight + 12 + 28;
        var rect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        var content = new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2, rect.height - padding * 2);
        var selectRect = new Rect(rect.x, rect.y, rect.width, rect.height - 48);
        Color modeColor = pkg.isGit ? ClrCatOrange : ClrCatGreen;
        bool hover = GUI.enabled && selectRect.Contains(Event.current.mousePosition);
        Color background = pkg.isSelected ? Color.Lerp(ClrCardBg, ClrAccent, 0.12f) : hover ? ClrItemHover : ClrCardBg;
        Drawing.DrawRoundedRect(rect, background, 6);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 8, 3, rect.height - 16), modeColor);
        if (pkg.isSelected)
        {
            EditorGUI.DrawRect(new Rect(rect.x + 3, rect.y, rect.width - 3, 1), ClrAccentDim);
            EditorGUI.DrawRect(new Rect(rect.x + 3, rect.yMax - 1, rect.width - 3, 1), ClrAccentDim);
        }
        if (GUI.enabled)
        {
            EditorGUIUtility.AddCursorRect(selectRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && selectRect.Contains(Event.current.mousePosition))
            {
                pkg.isSelected = !pkg.isSelected;
                Event.current.Use();
                Repaint();
            }
        }

        var check = new Rect(content.x, content.y + 3, 16, 16);
        Drawing.DrawRoundedRect(check, pkg.isSelected ? ClrAccent : ClrDivider, 3);
        if (pkg.isSelected) GUI.Label(check, "✓", _checkMarkStyle);
        else EditorGUI.DrawRect(new Rect(check.x + 1, check.y + 1, 14, 14), background);
        GUI.Label(check, new GUIContent("", "选择此包以进行批量操作"));
        GUI.Label(new Rect(content.x + 26, content.y, content.width - 84, 22), new GUIContent(pkg.packageName, pkg.packageName), _packageNameStyle);
        var mode = new Rect(content.xMax - 48, content.y + 2, 48, 18);
        Drawing.DrawRoundedRect(mode, new Color(modeColor.r, modeColor.g, modeColor.b, 0.14f), 4);
        _tagStyle.normal.textColor = modeColor;
        GUI.Label(mode, pkg.isGit ? "Git" : pkg.isAsset ? "Assets" : "本地", _tagStyle);

        float y = content.y + 26;
        if (!string.IsNullOrEmpty(pkg.gitUrl))
        {
            string url = PackageReferenceUtility.Redact(pkg.gitUrl);
            GUI.Label(new Rect(content.x + 26, y, content.width - 26, 18), new GUIContent(url, url), _metaStyle);
            y += 20;
        }
        if (pkg.isLocal)
        {
            string path = GetPackageRelativePath(pkg).Replace('\\', '/');
            bool exists = Directory.Exists(GetPackageDirectory(pkg));
            GUI.Label(new Rect(content.x + 26, y, content.width - 90, 18), new GUIContent("路径  " + path, GetPackageDirectory(pkg)), _metaStyle);
            var state = new Rect(content.xMax - 60, y, 60, 18);
            _tagStyle.normal.textColor = exists ? ClrTextDim : ClrError;
            GUI.Label(state, exists ? "目录存在" : "目录缺失", _tagStyle);
            y += 20;
        }
        if (!string.IsNullOrEmpty(branch))
        {
            GUI.Label(new Rect(content.x + 26, y, content.width - 26, 18), new GUIContent("分支  " + branch, pkg.gitStatus?.upstream ?? branch), _metaStyle);
            y += 18;
        }
        DrawGitStatus(pkg, new Rect(content.x + 26, y, content.width - 26, badgeHeight));
        float actionY = rect.yMax - padding - 28;
        EditorGUI.DrawRect(new Rect(content.x, actionY - 7, content.width, 1), ClrDivider);
        DrawPackageActions(pkg, new Rect(content.x, actionY, content.width, 28));
        GUILayout.Space(8);
    }

    private void DrawPackageActions(PackageInfo pkg, Rect rect)
    {
        float x = rect.x;
        string primary = pkg.isGit ? "切换到本地" : pkg.isAsset ? "Git 更新" : "切回 Git";
        bool noGitUrl = !pkg.isGit && !pkg.isAsset && string.IsNullOrEmpty(pkg.gitUrl);
        using (new EditorGUI.DisabledScope(noGitUrl))
        if (DrawColoredBtn(new Rect(x, rect.y, 112, rect.height), new GUIContent(primary, noGitUrl ? "未记录此包的 Git 地址，无法切回 Git。" : primary), ClrBtnNormal, ClrBtnHover))
        {
            if (pkg.isGit) SwitchToLocal(pkg);
            else if (pkg.isAsset) PullFromGit(pkg);
            else SwitchToGit(pkg);
        }
        x += 118;
        if (!pkg.isAsset)
        {
            if (DrawColoredBtn(new Rect(x, rect.y, 86, rect.height), new GUIContent("Git 更新"), ClrItemBg, ClrItemHover))
            {
                if (pkg.isGit) UpdateGitPackage(pkg);
                else PullFromGit(pkg);
            }
            x += 92;
        }
        if (pkg.isLocal && rect.width >= 340)
        {
            if (DrawColoredBtn(new Rect(x, rect.y, 84, rect.height), new GUIContent("打开目录"), ClrItemBg, ClrItemHover)) OpenPackageDirectory(pkg);
            x += 90;
        }
        EnsureGitClientSettings();
        if (rect.xMax - x >= 146)
        {
            string client = string.IsNullOrEmpty(_gitClientPath) ? "Git 客户端" : Path.GetFileNameWithoutExtension(_gitClientPath);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(pkg.repositoryPath)))
                if (DrawColoredBtn(new Rect(x, rect.y, 106, rect.height), new GUIContent(client, pkg.repositoryPath ?? "请先切换到本地工作副本"), ClrItemBg, ClrItemHover)) OpenPackageInGitClient(pkg);
        }
        var more = new Rect(rect.xMax - 32, rect.y, 32, rect.height);
        if (DrawColoredBtn(more, new GUIContent("•••", "更多包操作"), ClrItemBg, ClrItemHover))
        {
            var menu = new GenericMenu();
            if (pkg.isLocal) menu.AddItem(new GUIContent("打开包目录"), false, () => OpenPackageDirectory(pkg));
            if (!string.IsNullOrEmpty(pkg.repositoryPath)) menu.AddItem(new GUIContent("在 Git 客户端中打开"), false, () => OpenPackageInGitClient(pkg));
            else menu.AddDisabledItem(new GUIContent("在 Git 客户端中打开（无工作副本）"));
            if (pkg.isGit || (pkg.isLocal && !pkg.isAsset))
            {
                menu.AddSeparator("");
                if (pkg.isGit) menu.AddItem(new GUIContent("从 manifest 移除…"), false, () => RemoveFromManifest(pkg));
                else menu.AddItem(new GUIContent("删除本地目录…"), false, () => DeleteLocalPackage(pkg));
            }
            menu.DropDown(more);
        }
    }
    // ─── 批量操作 ─────────────────────────────────────────────

    private void DrawBatchActions()
    {
        var selected = _packages.Where(p => p.isSelected).ToList();
        if (selected.Count == 0) return;
        var rect = GUILayoutUtility.GetRect(0, 48, GUILayout.ExpandWidth(true));
        Drawing.DrawRoundedRect(rect, Color.Lerp(ClrToolbarBg, ClrAccent, 0.08f), 6);
        GUI.Label(new Rect(rect.x + 12, rect.y + 14, 64, 20), "批量操作", _metaStyle);
        float x = rect.x + 84;
        using (new EditorGUI.DisabledScope(!selected.Any(p => p.isGit)))
            if (DrawColoredBtn(new Rect(x, rect.y + 10, 96, 28), new GUIContent("切换到本地", "将已选 Git 包切换为本地引用"), ClrBtnNormal, ClrBtnHover)) SwitchBatchToLocal(selected);
        x += 102;
        using (new EditorGUI.DisabledScope(!selected.Any(p => p.isLocal && !p.isAsset && !string.IsNullOrEmpty(p.gitUrl))))
            if (DrawColoredBtn(new Rect(x, rect.y + 10, 86, 28), new GUIContent("切回 Git", "将记录了远端地址的本地包切回 Git"), ClrItemBg, ClrItemHover)) SwitchBatchToGit(selected);
        x += 92;
        using (new EditorGUI.DisabledScope(!selected.Any(p => p.isLocal && !string.IsNullOrEmpty(p.repositoryPath))))
            if (DrawColoredBtn(new Rect(x, rect.y + 10, 80, 28), new GUIContent("更新本地", "从 Git 拉取已选本地工作副本的更新"), ClrItemBg, ClrItemHover)) PullBatchFromGit(selected);
        GUILayout.Space(8);
    }
    // ─── 手动添加表单 ─────────────────────────────────────────

    private void DrawAddGitPackageForm()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(_cardStyle);
        {
            GUILayout.Label("手动添加 Git 包", StTitle);
            EditorGUILayout.Space(4);

            GUILayout.Label("Git URL", _metaStyle);
            _newGitUrl = EditorGUILayout.TextField(_newGitUrl);
            EditorGUILayout.Space(6);
            GUILayout.Label("包名 (com.xxx.yyy)", _metaStyle);
            _newPackageName = EditorGUILayout.TextField(_newPackageName);
            EditorGUILayout.Space(6);
            GUILayout.Label("本地文件夹名", _metaStyle);
            _newLocalFolder = EditorGUILayout.TextField(_newLocalFolder);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            {
                if (BtnSuccess("确认添加", GUILayout.Width(100), GUILayout.Height(28)))
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
    private void RefreshPackageList() => RefreshPackageList(true);

    private void RefreshPackageList(bool showSummary)
    {
        _packages.Clear();

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
                    string absolutePath = PackageReferenceUtility.ResolveLocalReference(value, _projectRoot);
                    info.isAsset = PackageReferenceUtility.IsWithin(Application.dataPath, absolutePath);
                    info.localFolder = info.isAsset ? GetRelativePath(_projectRoot, absolutePath) : ParseLocalFolder(value);
                    info.gitUrl = info.isAsset ? null : TryReadGitMeta(info.localFolder);
                    if (string.IsNullOrEmpty(info.gitUrl)) info.gitUrl = TryFindGitUrlFromLock(pkgName);

                    // Fallback: 从 .git/config 读取远程 URL
                    if (string.IsNullOrEmpty(info.gitUrl))
                    {
                        string localDir = GetPackageDirectory(info);
                        info.gitUrl = TryReadGitConfig(info.isAsset ? FindGitWorkingDirectory(localDir) : localDir);
                    }

                    // Fallback: 从元数据文件读取
                    if (string.IsNullOrEmpty(info.gitUrl) && !info.isAsset)
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

                    if (knownNames.Contains(pkgName))
                    {
                        // 嵌入包会优先于 manifest 中同名 Git/file 引用，显示实际生效的本地目录。
                        var embedded = _packages.First(p => p.packageName == pkgName);
                        bool sameSource = embedded.isGit || string.Equals(Path.GetFullPath(GetPackageDirectory(embedded)),
                            Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);
                        string referenceUrl = sameSource ? embedded.gitUrl : null;
                        embedded.isGit = false;
                        embedded.isLocal = true;
                        embedded.isAsset = false;
                        embedded.localFolder = folderName;
                        embedded.gitUrl = TryReadGitMeta(folderName) ?? referenceUrl ?? TryReadGitConfig(dir);
                        embedded.branch = embedded.subPath = null;
                        if (!string.IsNullOrEmpty(embedded.gitUrl)) ParseGitUrl(embedded.gitUrl, embedded);
                        continue;
                    }

                    var info = new PackageInfo
                    {
                        packageName = pkgName,
                        localFolder = folderName,
                        isLocal = true,
                        isSelected = false,
                    };

                    // 尝试从 packages-lock.json 获取 git URL
                    info.gitUrl = TryReadGitMeta(folderName) ?? TryFindGitUrlFromLock(pkgName);
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

            // Assets 下的包不在 UPM manifest 或 Packages 目录中。
            string assetsDir = Path.Combine(_projectRoot, "Assets");
            if (Directory.Exists(assetsDir))
            {
                foreach (var pjPath in Directory.GetFiles(assetsDir, "package.json", SearchOption.AllDirectories))
                {
                    string dir = Path.GetDirectoryName(pjPath);
                    if (_packages.Any(p => p.isAsset &&
                        string.Equals(GetPackageDirectory(p), dir, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    string pkgName = Path.GetFileName(dir);
                    try
                    {
                        string pj = File.ReadAllText(pjPath);
                        var nameMatch = Regex.Match(pj, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                        if (nameMatch.Success) pkgName = nameMatch.Groups[1].Value;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(PackageReferenceUtility.Redact($"读取包信息失败: {pjPath} · {ex.Message}"));
                    }

                    var info = new PackageInfo
                    {
                        packageName = pkgName,
                        localFolder = GetRelativePath(_projectRoot, dir),
                        isLocal = true,
                        isAsset = true,
                        isSelected = false,
                        gitUrl = TryReadGitConfig(FindGitWorkingDirectory(dir)),
                    };
                    if (!string.IsNullOrEmpty(info.gitUrl))
                        ParseGitUrl(info.gitUrl, info);
                    _packages.Add(info);
                }
            }

            int gitCount = _packages.Count(p => p.isGit);
            int localCount = _packages.Count(p => p.isLocal);
            if (showSummary)
            {
                ShowStatus($"已加载 {_packages.Count} 个包（{gitCount} Git / {localCount} 本地）", MessageType.Info);
                _showStatusDetails = false;
            }
            foreach (var pkg in _packages) pkg.repositoryPath = FindGitWorkingDirectory(GetPackageDirectory(pkg));
            StartGitStatusRefresh(false, true);
        }
        catch (Exception ex)
        {
            ShowStatus("读取 manifest.json 失败: " + ex.Message, MessageType.Error);
            Debug.LogError(PackageReferenceUtility.Redact(ex.ToString()));
        }
    }

    /// <summary>切换 Git 包到本地模式；克隆在后台执行，提交引用在主线程完成。</summary>
    private void SwitchToLocal(PackageInfo pkg)
    {
        if (!CanUseGitUrl(pkg.gitUrl)) return;
        QueuePackageOperation($"正在将 {pkg.packageName} 切换到本地…",
            token => PrepareLocalPackage(pkg, token), path =>
            {
                pkg.localFolder = PackageReferenceUtility.RelativePath(Path.Combine(_projectRoot, "Packages"), path);
                SaveGitMeta(pkg.localFolder, pkg.gitUrl, pkg.branch);
                WritePackageReference(pkg.packageName, "file:" + pkg.localFolder);
                ShowStatus($"✅ {pkg.packageName} 已切换为本地引用，UPM 将重新解析。");
            });
    }

    /// <summary>不指定 revision 时使用远端默认分支；显式分支和标签才传给 --branch。</summary>
    public static string[] GetCloneArguments(string gitUrl, string destination)
    {
        PackageReferenceUtility.ValidateGitUrl(gitUrl);
        var info = new PackageInfo();
        ParseGitUrl(gitUrl, info);
        string remote = gitUrl.Split('#')[0].Split('?')[0];
        if (remote.StartsWith("git+", StringComparison.OrdinalIgnoreCase)) remote = remote.Substring(4);
        var arguments = new List<string> { "clone", "--depth", "1" };
        if (!string.IsNullOrEmpty(info.branch) && info.branch != "HEAD"
            && !info.branch.StartsWith("refs/tags/", StringComparison.Ordinal)
            && !Regex.IsMatch(info.branch, @"^[0-9a-fA-F]{7,40}$"))
        {
            arguments.Add("--branch");
            arguments.Add(info.branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? info.branch.Substring(11) : info.branch);
        }
        arguments.AddRange(new[] { "--", remote, destination });
        return arguments.ToArray();
    }

    private string PrepareLocalPackage(PackageInfo pkg, CancellationToken token)
    {
        string existing = Path.GetFullPath(GetPackageDirectory(pkg));
        if (Directory.Exists(existing))
        {
            ValidateLocalPackage(existing, pkg.packageName);
            return existing;
        }

        // 使用完整包名，避免 com.vendorA.core 与 com.vendorB.core 相互覆盖。
        // 工作副本留在 Packages 外，切回 Git 后不会被嵌入包优先级覆盖。
        if (!Regex.IsMatch(pkg.packageName, @"^[a-z0-9][a-z0-9._-]*$"))
            throw new ArgumentException("包名不是有效的目录名。");
        string repository = Path.Combine(_projectRoot, "LocalPackages", pkg.packageName);
        string packagePath = Path.GetFullPath(Path.Combine(repository, pkg.subPath ?? ""));
        if (!PackageReferenceUtility.IsWithin(repository, packagePath))
            throw new ArgumentException("Git 包子目录必须位于克隆仓库内。");
        if (Directory.Exists(repository))
        {
            ValidateLocalPackage(packagePath, pkg.packageName);
            return packagePath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(repository));
        string staging = repository + ".clone-" + Guid.NewGuid().ToString("N");
        try
        {
            ExecuteGit(_projectRoot, token, 300, GetCloneArguments(pkg.gitUrl, staging));
            if (!string.IsNullOrEmpty(pkg.branch) && (Regex.IsMatch(pkg.branch, @"^[0-9a-fA-F]{7,40}$")
                || pkg.branch.StartsWith("refs/tags/", StringComparison.Ordinal)))
            {
                ExecuteGit(staging, token, 300, "fetch", "--depth", "1", "--", "origin", pkg.branch);
                ExecuteGit(staging, token, 30, "checkout", "--detach", "FETCH_HEAD");
            }
            ValidateLocalPackage(Path.Combine(staging, pkg.subPath ?? ""), pkg.packageName);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, repository);
            return packagePath;
        }
        finally
        {
            // 仅清理本次克隆创建的临时目录，已有工作副本保持原样。
            if (Directory.Exists(staging))
            {
                ClearReadOnlyAttributes(staging);
                Directory.Delete(staging, true);
            }
        }
    }

    private static void ValidateLocalPackage(string directory, string packageName)
    {
        string manifest = Path.Combine(directory, "package.json");
        if (!File.Exists(manifest)) throw new InvalidDataException("本地目录缺少 package.json：" + directory);
        string actual = JObject.Parse(File.ReadAllText(manifest))["name"]?.ToString();
        if (!string.Equals(actual, packageName, StringComparison.Ordinal))
            throw new InvalidDataException($"本地目录的包名为 {actual}，与 {packageName} 不匹配，不会覆盖。");
    }

    /// <summary>切换本地包回 Git；保留并移出会覆盖 Git 引用的嵌入工作副本。</summary>
    private void SwitchToGit(PackageInfo pkg)
    {
        if (!CanUseGitUrl(pkg.gitUrl)) return;
        QueuePackageOperation($"正在将 {pkg.packageName} 切回 Git…", token => pkg.gitUrl, url =>
        {
            string source = Path.GetFullPath(GetPackageDirectory(pkg));
            string packages = Path.Combine(_projectRoot, "Packages");
            string movedTo = null;
            try
            {
                if (Directory.Exists(source) && string.Equals(Path.GetDirectoryName(source), packages, StringComparison.OrdinalIgnoreCase))
                {
                    ValidateLocalPackage(source, pkg.packageName);
                    if (File.Exists(Path.Combine(source, ".git")))
                        throw new IOException("此嵌入包使用 Git 子模块或 worktree，不能直接移动。请先将其调整为 Packages 外的本地引用，再切回 Git。");
                    movedTo = Path.Combine(_projectRoot, "LocalPackages", Path.GetFileName(source));
                    if (Directory.Exists(movedTo) || File.Exists(movedTo) || File.Exists(movedTo + ".meta"))
                        throw new IOException("保留本地包的目标已存在，请先整理后重试：" + movedTo);
                    Directory.CreateDirectory(Path.GetDirectoryName(movedTo));
                    Directory.Move(source, movedTo);
                    if (File.Exists(source + ".meta")) File.Move(source + ".meta", movedTo + ".meta");
                }
                WritePackageReference(pkg.packageName, url);
                ShowStatus($"✅ {pkg.packageName} 已切换为 Git 引用，UPM 将重新解析。"
                    + (movedTo == null ? "" : "\n本地工作副本已保留在：" + movedTo));
            }
            catch
            {
                if (movedTo != null && Directory.Exists(movedTo) && !Directory.Exists(source))
                {
                    Directory.Move(movedTo, source);
                    if (File.Exists(movedTo + ".meta")) File.Move(movedTo + ".meta", source + ".meta");
                }
                throw;
            }
        });
    }

    private void WritePackageReference(string packageName, string reference)
    {
        string manifest = ReplaceManifestEntry(File.ReadAllText(_manifestPath), packageName, reference);
        File.WriteAllText(_manifestPath, manifest);
        _packagesChanged = true;
    }

    /// <summary>从 Git 拉取最新代码到本地目录。</summary>
    private void PullFromGit(PackageInfo pkg)
    {
        string localPath = GetPackageDirectory(pkg);
        string repository = FindGitWorkingDirectory(localPath);
        if (!Directory.Exists(localPath) || string.IsNullOrEmpty(repository))
        {
            ShowStatus($"{pkg.packageName} 没有可更新的本地 Git 仓库。", MessageType.Warning);
            return;
        }
        if (pkg.isAsset && !string.Equals(localPath, repository, StringComparison.OrdinalIgnoreCase)
            && !EditorUtility.DisplayDialog("确认更新 Git 仓库",
                $"{pkg.packageName} 位于上层 Git 仓库中。\n\n更新将拉取整个仓库：\n{repository}", "更新", "取消"))
            return;
        QueuePackageOperation($"正在拉取 {pkg.packageName} 最新代码…",
            token => ExecuteGit(repository, token, 300, "pull"), output =>
            {
                _packagesChanged = true;
                ShowStatus($"✅ {pkg.packageName} Git 更新完成。\n{output.Trim()}");
            });
    }

    private void QueuePackageOperation(string message, Func<CancellationToken, string> run, Action<string> complete)
    {
        if (!_isProcessing)
        {
            _isProcessing = true;
            _packagesChanged = false;
            _operationSucceeded = _operationFailed = 0;
            _lastOperationError = null;
            _operationCancellation = new CancellationTokenSource();
            _gitStatusCancellation?.Cancel();
            _gitStatusJobs.Clear();
            _checkRemoteAfterLocal = false;
            // 批量操作全部完成后再允许程序集重载，避免首个包变化中断剩余队列。
            EditorApplication.LockReloadAssemblies();
            _reloadLocked = true;
            ContentScroll = Vector2.zero;
            ShowStatus(message);
        }
        _packageOperations.Enqueue(new PackageOperation { message = message, run = run, complete = complete });
    }

    private void PollPackageOperation()
    {
        if (!_isProcessing || _operationCancellation == null) return;
        if (_operationTask == null)
        {
            if (_packageOperations.Count == 0) { FinishPackageOperations(); return; }
            _activeOperation = _packageOperations.Dequeue();
            _operationStartedAt = EditorApplication.timeSinceStartup;
            ShowStatus(_activeOperation.message);
            var operation = _activeOperation;
            var token = _operationCancellation.Token;
            _operationTask = Task.Run(() => operation.run(token), token);
        }
        if (!_operationTask.IsCompleted)
        {
            if (EditorApplication.timeSinceStartup - _lastOperationRepaint > 0.25)
            {
                _lastOperationRepaint = EditorApplication.timeSinceStartup;
                Repaint();
            }
            return;
        }
        try
        {
            string result = _operationTask.GetAwaiter().GetResult();
            _operationCancellation.Token.ThrowIfCancellationRequested();
            _activeOperation.complete(result);
            _operationSucceeded++;
        }
        catch (OperationCanceledException)
        {
            _packageOperations.Clear();
            ShowStatus($"操作已取消；已完成 {_operationSucceeded} 个操作。", MessageType.Warning);
        }
        catch (Exception ex)
        {
            _operationFailed++;
            _lastOperationError = PackageReferenceUtility.Redact(ex.Message);
            ShowStatus("操作失败：" + _lastOperationError, MessageType.Error);
            Debug.LogError(PackageReferenceUtility.Redact(ex.ToString()));
        }
        finally
        {
            _operationTask = null;
            _activeOperation = null;
        }
        if (_packageOperations.Count == 0) FinishPackageOperations();
    }

    private void FinishPackageOperations()
    {
        bool cancelled = _operationCancellation.IsCancellationRequested;
        _isProcessing = false;
        _operationCancellation.Dispose();
        _operationCancellation = null;
        try
        {
            if (_packagesChanged)
            {
                _filter = PackageFilter.All;
                RefreshPackageList(false);
                AssetDatabase.Refresh();
                UnityEditor.PackageManager.Client.Resolve();
            }
            else StartGitStatusRefresh(false);
            if (cancelled)
                ShowStatus($"操作已取消；已完成 {_operationSucceeded} 个操作。", MessageType.Warning);
            else if (_operationSucceeded + _operationFailed > 1)
                ShowStatus($"批量操作完成：成功 {_operationSucceeded}，失败 {_operationFailed}。"
                    + (_operationFailed > 0 ? "\n最近失败：" + _lastOperationError : ""),
                    _operationFailed > 0 ? MessageType.Error : MessageType.Info);
        }
        catch (Exception ex) { ShowStatus("刷新包状态失败：" + ex.Message, MessageType.Error); }
        finally
        {
            if (_reloadLocked)
            {
                EditorApplication.UnlockReloadAssemblies();
                _reloadLocked = false;
            }
            Repaint();
        }
    }
    private string GetPackageRelativePath(PackageInfo pkg)
    {
        return pkg.isAsset ? pkg.localFolder : Path.Combine("Packages", pkg.localFolder);
    }

    private string GetPackageDirectory(PackageInfo pkg)
    {
        return Path.Combine(_projectRoot, GetPackageRelativePath(pkg));
    }

    private string FindGitWorkingDirectory(string directory)
    {
        string projectRoot = Path.GetFullPath(_projectRoot).TrimEnd(Path.DirectorySeparatorChar);
        string current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        while (current.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(current, projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            string marker = Path.Combine(current, ".git");
            if (Directory.Exists(marker) || File.Exists(marker)) return current;
            if (string.Equals(current, projectRoot, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    private void OpenPackageDirectory(PackageInfo pkg)
    {
        string path = GetPackageDirectory(pkg);
        if (Directory.Exists(path))
            EditorUtility.RevealInFinder(path);
        else
            ShowStatus($"目录不存在: {path}", MessageType.Warning);
    }

    /// <summary>让 UPM 重新解析 Git 依赖；不直接修改只读的 PackageCache。</summary>
    private void UpdateGitPackage(PackageInfo pkg)
    {
        if (_isProcessing || pkg == null) return;
        if (string.IsNullOrEmpty(pkg.gitUrl))
        {
            ShowStatus($"{pkg?.packageName} 没有 Git URL，无法更新", MessageType.Warning);
            return;
        }

        if (!CanUseGitUrl(pkg.gitUrl)) return;

        _isProcessing = true;
        ShowStatus($"正在通过 UPM 更新 {pkg.packageName} ...", MessageType.Info);
        Repaint();

        var request = UnityEditor.PackageManager.Client.Add(pkg.gitUrl);
        EditorApplication.CallbackFunction onUpdate = null;
        onUpdate = () =>
        {
            if (!request.IsCompleted) return;
            EditorApplication.update -= onUpdate;
            _isProcessing = false;

            if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
            {
                RefreshPackageList(false);
                ShowStatus($"✅ {pkg.packageName} 已由 UPM 重新解析", MessageType.Info);
            }
            else
            {
                string error = request.Error != null ? request.Error.message : "未知错误";
                ShowStatus($"更新失败: {pkg.packageName} · {error}", MessageType.Error);
            }
            Repaint();
        };
        EditorApplication.update += onUpdate;
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
            Debug.LogError(PackageReferenceUtility.Redact(ex.ToString()));
        }
    }

    /// <summary>删除本地包目录（清除只读属性后删除）</summary>
    private void DeleteLocalPackage(PackageInfo pkg)
    {
        string localPath = Path.GetFullPath(GetPackageDirectory(pkg));
        if (!PackageReferenceUtility.IsWithin(_projectRoot, localPath)
            || PackageReferenceUtility.IsWithin(localPath, Path.Combine(_projectRoot, "Packages"))
            || PackageReferenceUtility.IsWithin(localPath, Path.Combine(_projectRoot, "LocalPackages"))
            || !File.Exists(Path.Combine(localPath, "package.json")))
        {
            ShowStatus("仅允许删除项目内的具体包目录，不能删除项目外路径或包容器目录。", MessageType.Error);
            return;
        }

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
            Debug.LogError(PackageReferenceUtility.Redact(ex.ToString()));
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
            if (pkg.isLocal && !pkg.isAsset)
                SwitchToGit(pkg);
        }
    }

    private void PullBatchFromGit(List<PackageInfo> packages)
    {
        foreach (var pkg in packages)
        {
            if (pkg.isLocal && !string.IsNullOrEmpty(pkg.repositoryPath))
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
            if (IsLocalRef(gitUrl))
                gitUrl = PackageReferenceUtility.PrepareLocalPackageReference(gitUrl, _projectRoot);
            else
                PackageReferenceUtility.ValidateGitUrl(gitUrl);
            string manifest = File.ReadAllText(_manifestPath);
            manifest = ReplaceManifestEntry(manifest, packageName, gitUrl);
            File.WriteAllText(_manifestPath, manifest);

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
            Debug.LogError(PackageReferenceUtility.Redact(ex.ToString()));
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
            info.branch = null;
        }

        // 提取 ?path=
        info.subPath = null;
        int pathIdx = working.IndexOf("?path=");
        if (pathIdx > 0)
        {
            info.subPath = Uri.UnescapeDataString(working.Substring(pathIdx + 6)).Replace('\\', '/').TrimStart('/');
            working = working.Substring(0, pathIdx);
        }

        // 清理 URL 用于显示
        info.gitUrl = url;
    }

    /// <summary>猜测本地文件夹名</summary>
    private string GuessLocalFolder(string packageName, PackageInfo info)
    {
        string local = Path.Combine(_projectRoot, "LocalPackages", packageName, info.subPath ?? "");
        // 兼容旧版创建的简称目录，以及切回 Git 时保留的嵌入工作副本。
        string localPackages = Path.Combine(_projectRoot, "LocalPackages");
        if (!Directory.Exists(local) && Directory.Exists(localPackages))
        {
            foreach (string directory in Directory.GetDirectories(localPackages))
            {
                if (Path.GetFileName(directory).Contains(".clone-")) continue;
                foreach (string candidate in new[] { directory, Path.Combine(directory, info.subPath ?? "") }.Distinct())
                {
                    string manifest = Path.Combine(candidate, "package.json");
                    if (!File.Exists(manifest)) continue;
                    try
                    {
                        if (JObject.Parse(File.ReadAllText(manifest))["name"]?.ToString() == packageName)
                            return PackageReferenceUtility.RelativePath(Path.Combine(_projectRoot, "Packages"), candidate);
                    }
                    catch { /* 忽略不完整的其他包，不覆盖已有目录。 */ }
                }
            }
        }
        return PackageReferenceUtility.RelativePath(Path.Combine(_projectRoot, "Packages"), local);
    }

    /// <summary>从本地引用解析文件夹名</summary>
    private static string ParseLocalFolder(string value)
    {
        // file:xxx 或 file:Packages/xxx
        string folder = value.Substring(5); // 去掉 "file:"
        // Preserve ../LocalPackages and nested paths; file: is relative to Packages.
        return folder.Replace('\\', '/');
    }

    /// <summary>从 .git/config 读取远程 URL</summary>
    private static string TryReadGitConfig(string localDir)
    {
        if (string.IsNullOrEmpty(localDir)) return null;
        try
        {
            string gitMarker = Path.Combine(localDir, ".git");
            string gitConfig = Path.Combine(gitMarker, "config");
            if (File.Exists(gitMarker))
            {
                var match = Regex.Match(File.ReadAllText(gitMarker), @"^gitdir:\s*(.+)$", RegexOptions.Multiline);
                if (match.Success)
                    gitConfig = Path.Combine(Path.GetFullPath(Path.Combine(localDir, match.Groups[1].Value.Trim())), "config");
            }
            if (!File.Exists(gitConfig)) return null;

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
            PackageReferenceUtility.ValidateGitUrl(gitUrl);
            string metaPath = Path.Combine(_projectRoot, "Packages", localFolder, ".gitswitcher.json");
            if (!PackageReferenceUtility.IsWithin(_projectRoot, metaPath))
                throw new ArgumentException("Git 元数据目录必须位于项目内。");
            string json = new JObject { ["gitUrl"] = gitUrl, ["branch"] = branch ?? "" }.ToString();
            File.WriteAllText(metaPath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(PackageReferenceUtility.Redact($"保存 Git 元数据失败: {ex.Message}"));
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
            var entry = JObject.Parse(File.ReadAllText(lockPath))["dependencies"]?[packageName];
            if (entry?["source"]?.ToString() != "git") return null;
            string value = (entry["version"] ?? entry["url"] ?? entry["repository"])?.ToString();
            if (!string.IsNullOrEmpty(value) && IsGitUrl(value)) return value;
        }
        catch (Exception ex)
        {
            Debug.LogWarning(PackageReferenceUtility.Redact($"读取 packages-lock.json 失败: {ex.Message}"));
        }

        return null;
    }

    /// <summary>精确替换 manifest 中某个包的引用值</summary>
    private static string ReplaceManifestEntry(string manifest, string packageName, string newValue)
    {
        var json = JObject.Parse(manifest);
        var dependencies = json["dependencies"] as JObject;
        if (dependencies == null) throw new InvalidDataException("manifest.json 缺少 dependencies 对象。");
        dependencies[packageName] = newValue;
        return json.ToString(Newtonsoft.Json.Formatting.Indented);
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

    private void ShowStatus(string message, MessageType type = MessageType.Info)
    {
        _statusMessage = PackageReferenceUtility.Redact(message);
        _statusType = type;
        _showStatusDetails = true;
        Repaint();
    }

    private bool CanUseGitUrl(string url)
    {
        try { PackageReferenceUtility.ValidateGitUrl(url); return true; }
        catch (Exception ex) { ShowStatus(ex.Message, MessageType.Error); return false; }
    }
}
