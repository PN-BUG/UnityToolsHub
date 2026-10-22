using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityPackageCreator.Editor;

public partial class GitPackageSwitcher
{
    public sealed class GitRepositoryStatus
    {
        public string repositoryPath, branch, upstream, error, remoteError;
        public int ahead, behind, changed, untracked, conflicts;
        public bool detached, remoteChecked, gitPackage, updateAvailable, pinned;
        public string installedRevision, remoteRevision;
        public DateTime remoteCheckedAt;
    }

    private sealed class GitStatusJob
    {
        public PackageInfo[] packages;
        public Task<GitRepositoryStatus> task;
    }

    private readonly List<GitStatusJob> _gitStatusJobs = new List<GitStatusJob>();
    private CancellationTokenSource _gitStatusCancellation;
    private static readonly SemaphoreSlim GitStatusSlots = new SemaphoreSlim(3);
    private bool _checkRemoteAfterLocal, _showGitClientSettings;
    private const string GitClientPathKey = "UnityToolsHub.GitClient.Path";
    private const string GitClientArgumentsKey = "UnityToolsHub.GitClient.Arguments";
    private string _gitClientPath;
    private string _gitClientArguments;

    private void StartGitStatusRefresh(bool checkRemote, bool checkRemoteAfterLocal = false)
    {
        _gitStatusCancellation?.Cancel();
        _gitStatusCancellation = new CancellationTokenSource();
        var token = _gitStatusCancellation.Token;
        _gitStatusJobs.Clear();
        _checkRemoteAfterLocal = checkRemoteAfterLocal;
        JObject locked = null;
        try { locked = JObject.Parse(File.ReadAllText(Path.Combine(_projectRoot, "Packages/packages-lock.json"))); }
        catch { /* A package may not have been resolved by UPM yet. */ }

        foreach (var group in _packages.GroupBy(p => p.isGit ? "url:" + p.gitUrl : "repo:" + p.repositoryPath))
        {
            var packages = group.ToArray();
            var first = packages[0];
            if (!first.isGit && string.IsNullOrEmpty(first.repositoryPath))
            {
                foreach (var pkg in packages) { pkg.gitStatus = new GitRepositoryStatus(); pkg.checkingStatus = false; }
                continue;
            }
            string repository = first.repositoryPath, url = first.gitUrl;
            string installed = locked?["dependencies"]?[first.packageName]?["hash"]?.ToString();
            bool gitPackage = first.isGit;
            foreach (var pkg in packages) pkg.checkingStatus = true;
            _gitStatusJobs.Add(new GitStatusJob
            {
                packages = packages,
                task = Task.Run(async () =>
                {
                    await GitStatusSlots.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        return gitPackage ? ReadGitPackageStatus(url, installed, checkRemote, token)
                            : ReadRepositoryStatus(repository, checkRemote, token);
                    }
                    finally { GitStatusSlots.Release(); }
                }, token)
            });
        }
        Repaint();
    }

    private void PollGitStatus()
    {
        for (int i = _gitStatusJobs.Count - 1; i >= 0; i--)
        {
            var job = _gitStatusJobs[i];
            if (!job.task.IsCompleted) continue;
            var result = job.task.Status == TaskStatus.RanToCompletion ? job.task.Result
                : new GitRepositoryStatus { error = PackageReferenceUtility.Redact(job.task.Exception?.GetBaseException().Message ?? "检查已取消") };
            foreach (var pkg in job.packages)
            {
                // A local refresh preserves the timestamp of the last successful remote check.
                if (!result.remoteChecked && string.IsNullOrEmpty(result.remoteError) && pkg.gitStatus != null)
                {
                    result.remoteChecked = pkg.gitStatus.remoteChecked;
                    result.remoteCheckedAt = pkg.gitStatus.remoteCheckedAt;
                    result.remoteError = pkg.gitStatus.remoteError;
                    if (result.gitPackage)
                    {
                        result.remoteRevision = pkg.gitStatus.remoteRevision;
                        result.updateAvailable = pkg.gitStatus.updateAvailable;
                    }
                }
                pkg.gitStatus = result;
                pkg.checkingStatus = false;
            }
            _gitStatusJobs.RemoveAt(i);
            Repaint();
        }
        if (_gitStatusJobs.Count == 0 && _checkRemoteAfterLocal)
        {
            _checkRemoteAfterLocal = false;
            StartGitStatusRefresh(true);
        }
    }

    /// <summary>Reads the whole repository. Optional fetch updates remote refs only, never merges or pushes.</summary>
    public static GitRepositoryStatus ReadRepositoryStatus(string directory, bool checkRemote = false,
        CancellationToken cancellationToken = default)
    {
        var result = new GitRepositoryStatus { repositoryPath = directory };
        try
        {
            result.repositoryPath = QueryGit(directory, cancellationToken, "rev-parse", "--show-toplevel").Trim();
            ReadWorkingTree(result, cancellationToken);
            if (checkRemote && !result.detached && !string.IsNullOrEmpty(result.upstream))
            {
                try
                {
                    string remote = QueryGit(result.repositoryPath, cancellationToken, "for-each-ref",
                        "--format=%(upstream:remotename)", "refs/heads/" + result.branch).Trim();
                    if (!string.IsNullOrEmpty(remote) && remote != ".")
                        QueryGit(result.repositoryPath, cancellationToken, "fetch", "--quiet", "--no-tags", "--", remote);
                    ReadWorkingTree(result, cancellationToken);
                    result.remoteChecked = true;
                    result.remoteCheckedAt = DateTime.UtcNow;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result.remoteError = PackageReferenceUtility.Redact(ex.Message); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { result.error = PackageReferenceUtility.Redact(ex.Message); }
        return result;
    }

    private static void ReadWorkingTree(GitRepositoryStatus result, CancellationToken token)
    {
        string output = QueryGit(result.repositoryPath, token, "status", "--porcelain=v2", "--branch", "--untracked-files=normal");
        result.ahead = result.behind = result.changed = result.untracked = result.conflicts = 0;
        result.upstream = null;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("# branch.head ")) result.branch = line.Substring(14).Trim();
            else if (line.StartsWith("# branch.upstream ")) result.upstream = line.Substring(18).Trim();
            else if (line.StartsWith("# branch.ab "))
            {
                var parts = line.Substring(12).Trim().Split(' ');
                if (parts.Length == 2) { int.TryParse(parts[0].TrimStart('+'), out result.ahead); int.TryParse(parts[1].TrimStart('-'), out result.behind); }
            }
            else if (line.StartsWith("? ")) result.untracked++;
            else if (line.StartsWith("u ")) { result.changed++; result.conflicts++; }
            else if (line.StartsWith("1 ") || line.StartsWith("2 ")) result.changed++;
        }
        result.detached = result.branch == "(detached)";
    }

    /// <summary>Compares a UPM lock-file commit with its configured remote ref without modifying the cache.</summary>
    public static GitRepositoryStatus ReadGitPackageStatus(string url, string installedRevision, bool checkRemote,
        CancellationToken cancellationToken = default)
    {
        var result = new GitRepositoryStatus { gitPackage = true, installedRevision = installedRevision };
        try
        {
            PackageReferenceUtility.ValidateGitUrl(url);
            int fragment = url.IndexOf('#');
            string revision = fragment < 0 ? "HEAD" : url.Substring(fragment + 1);
            string remote = fragment < 0 ? url : url.Substring(0, fragment);
            int query = remote.IndexOf("?path=", StringComparison.Ordinal);
            if (query >= 0) remote = remote.Substring(0, query);
            result.branch = revision;
            result.pinned = Regex.IsMatch(revision, "^[0-9a-fA-F]{7,40}$");
            if (!checkRemote || result.pinned) return result;
            var refs = revision == "HEAD" || revision.StartsWith("refs/", StringComparison.Ordinal)
                ? new[] { revision, revision + "^{}" }
                : new[] { "refs/heads/" + revision, "refs/tags/" + revision, "refs/tags/" + revision + "^{}" };
            string output = QueryGit(Environment.CurrentDirectory, cancellationToken,
                new[] { "ls-remote", "--exit-code", "--", remote }.Concat(refs).ToArray());
            var matches = output.Split('\n').Select(line => line.Trim().Split('\t')).Where(parts => parts.Length == 2).ToArray();
            // Prefer a branch over a same-named tag; dereference annotated tags to their commit.
            var match = matches.FirstOrDefault(parts => parts[1] == "refs/heads/" + revision)
                ?? matches.FirstOrDefault(parts => parts[1].EndsWith("^{}", StringComparison.Ordinal)) ?? matches.FirstOrDefault();
            if (match == null) throw new InvalidOperationException("远端未找到指定分支或标签");
            result.remoteRevision = match[0];
            result.updateAvailable = !string.IsNullOrEmpty(installedRevision) && !string.Equals(installedRevision, match[0], StringComparison.OrdinalIgnoreCase);
            result.remoteChecked = true;
            result.remoteCheckedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { result.remoteError = PackageReferenceUtility.Redact(ex.Message); }
        return result;
    }

    private static string QueryGit(string directory, CancellationToken token, params string[] arguments)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("git", string.Join(" ", arguments.Select(QuoteProcessArgument)))
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
        start.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
        using (var process = Process.Start(start))
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var timer = Stopwatch.StartNew();
            while (!process.WaitForExit(100))
            {
                if (!token.IsCancellationRequested && timer.Elapsed.TotalSeconds < 30) continue;
                try { process.Kill(); } catch { }
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Git 检查超时（30 秒），可稍后重试");
            }
            string output = stdout.GetAwaiter().GetResult(), error = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Git 检查失败（退出码 {process.ExitCode}）" : PackageReferenceUtility.Redact(error.Trim()));
            return output;
        }
    }

    // ProcessStartInfo.Arguments quoting; no shell is involved.
    private static string QuoteProcessArgument(string value)
    {
        var text = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            text.Append(c);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    /// <summary>Finds a standard Windows SourceTree installation without launching it.</summary>
    public static string FindSourceTreeExecutable()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return new[] { Path.Combine(local, "SourceTree/SourceTree.exe"), Path.Combine(local, "Atlassian/SourceTree/SourceTree.exe"),
            Path.Combine(programs, "Atlassian/SourceTree/SourceTree.exe") }.FirstOrDefault(File.Exists) ?? "";
    }

    /// <summary>Opens an existing repository in a local GUI client. Does not clone, commit, pull, or push.</summary>
    public static void OpenInGitClient(string repository, string executable, string argumentTemplate = "-f {path}")
    {
        if (!Directory.Exists(Path.Combine(repository, ".git")) && !File.Exists(Path.Combine(repository, ".git")))
            throw new DirectoryNotFoundException("目录不是 Git 仓库：" + repository);
        if (!File.Exists(executable)) throw new FileNotFoundException("请先在「Git 工具设置」中选择 Git 客户端", executable);
        // Support both {path} and the commonly entered quoted form "{path}".
        string arguments = argumentTemplate.Replace("\"{path}\"", "{path}").Replace("{path}", QuoteProcessArgument(repository));
        if (!argumentTemplate.Contains("{path}")) throw new ArgumentException("启动参数必须包含 {path}");
        using (Process.Start(new ProcessStartInfo(executable, arguments) { WorkingDirectory = repository, UseShellExecute = false })) { }
    }

    private void OpenPackageInGitClient(PackageInfo pkg)
    {
        EnsureGitClientSettings();
        if (!File.Exists(_gitClientPath)) { _showGitClientSettings = true; ShowStatus("请选择本机 Git 客户端可执行文件", MessageType.Warning); return; }
        try { OpenInGitClient(pkg.repositoryPath, _gitClientPath, _gitClientArguments); ShowStatus("已打开仓库：" + pkg.repositoryPath); }
        catch (Exception ex) { ShowStatus(ex.Message, MessageType.Warning); }
    }

    private void EnsureGitClientSettings()
    {
        if (_gitClientPath != null) return;
        _gitClientPath = EditorPrefs.GetString(GitClientPathKey, FindSourceTreeExecutable());
        _gitClientArguments = EditorPrefs.GetString(GitClientArgumentsKey, "-f {path}");
    }

    private void DrawGitClientSettings()
    {
        if (!_showGitClientSettings) return;
        EnsureGitClientSettings();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("本地 Git 工具", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _gitClientPath = EditorGUILayout.TextField("可执行文件", _gitClientPath);
        _gitClientArguments = EditorGUILayout.TextField("启动参数", _gitClientArguments);
        if (EditorGUI.EndChangeCheck()) { EditorPrefs.SetString(GitClientPathKey, _gitClientPath); EditorPrefs.SetString(GitClientArgumentsKey, _gitClientArguments); }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("选择程序"))
        {
            string path = EditorUtility.OpenFilePanel("选择 Git 客户端", Path.GetDirectoryName(_gitClientPath) ?? "", "exe");
            if (!string.IsNullOrEmpty(path)) { _gitClientPath = path; EditorPrefs.SetString(GitClientPathKey, path); }
        }
        if (GUILayout.Button("自动查找 SourceTree"))
        {
            _gitClientPath = FindSourceTreeExecutable(); _gitClientArguments = "-f {path}";
            EditorPrefs.SetString(GitClientPathKey, _gitClientPath); EditorPrefs.SetString(GitClientArgumentsKey, _gitClientArguments);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("SourceTree 参数：-f {path}。其他客户端可自定义参数，{path} 会替换为实际仓库根目录并自动加引号。远端检查只 fetch，不合并或上传。", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private sealed class GitStatusBadge
    {
        public string text;
        public Color color;
    }

    private List<GitStatusBadge> GetGitStatusBadges(PackageInfo pkg)
    {
        var badges = new List<GitStatusBadge>();
        Action<string, Color> add = (text, color) => badges.Add(new GitStatusBadge { text = text, color = color });
        var s = pkg.gitStatus;
        if (s == null) add("检查中…", ClrInfo);
        else if (!string.IsNullOrEmpty(s.error)) add(s.error.Contains("dubious ownership") ? "Git 信任未配置" : "状态读取失败", ClrError);
        else if (s.gitPackage)
        {
            if (s.pinned) add("固定提交", ClrInfo);
            else if (string.IsNullOrEmpty(s.installedRevision)) add("版本未解析", ClrWarning);
            else if (s.remoteChecked) add(s.updateAvailable ? "有更新" : "已是最新", s.updateAvailable ? ClrWarning : ClrSuccess);
        }
        else if (string.IsNullOrEmpty(s.repositoryPath)) add("无 Git 仓库", ClrTextDim);
        else
        {
            if (s.conflicts > 0) add($"冲突 {s.conflicts}", ClrError);
            if (s.behind > 0) add($"待更新 {s.behind}", ClrWarning);
            if (s.ahead > 0) add($"待上传 {s.ahead}", ClrInfo);
            if (s.changed + s.untracked > 0) add($"未提交 {s.changed + s.untracked}", ClrWarning);
            if (s.detached) add("游离 HEAD", ClrWarning);
            else if (string.IsNullOrEmpty(s.upstream)) add("未设置上游", ClrTextDim);
            if (badges.Count == 0) add(s.remoteChecked && string.IsNullOrEmpty(s.remoteError) ? "已同步" : "本地干净", ClrSuccess);
        }
        if (s != null && !string.IsNullOrEmpty(s.remoteError)) add("远端检查失败", ClrError);
        else if (s != null && !s.remoteChecked && !s.pinned && (s.gitPackage || !string.IsNullOrEmpty(s.upstream))) add("远端未检查", ClrTextDim);
        if (pkg.checkingStatus && s != null) add("检查中…", ClrInfo);
        return badges;
    }

    private float GetGitStatusHeight(PackageInfo pkg, float width)
    {
        float x = 0; int rows = 1;
        foreach (var badge in GetGitStatusBadges(pkg))
        {
            float w = Mathf.Min(width, _tagStyle.CalcSize(new GUIContent(badge.text)).x + 6);
            if (x > 0 && x + w > width) { rows++; x = 0; }
            x += w + 5;
        }
        return rows * 22;
    }

    private void DrawGitStatus(PackageInfo pkg, float width, float height)
    {
        var rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false), GUILayout.Height(height));
        float x = 0, y = 0;
        var s = pkg.gitStatus;
        string tooltip = "仓库整体状态：" + (pkg.repositoryPath ?? "UPM Git 包") + "\n未提交包括暂存、未暂存及未跟踪项。待上传仅统计已提交的提交数。";
        if (s != null)
        {
            tooltip += "\n上游：" + (s.upstream ?? "无");
            if (s.remoteChecked) tooltip += "\n远端检查：" + s.remoteCheckedAt.ToLocalTime().ToString("HH:mm:ss");
            else tooltip += "\n提交数量来自本地远程记录，点击「检查远端」获取最新状态。";
            tooltip += "\n" + s.error + "\n" + s.remoteError;
        }
        foreach (var badge in GetGitStatusBadges(pkg))
        {
            float w = Mathf.Min(width, _tagStyle.CalcSize(new GUIContent(badge.text)).x + 6);
            if (x > 0 && x + w > width) { x = 0; y += 22; }
            var tag = new Rect(rect.x + x, rect.y + y + 2, w, 18);
            Drawing.DrawRoundedRect(tag, new Color(badge.color.r, badge.color.g, badge.color.b, 0.18f), 4);
            _tagStyle.normal.textColor = badge.color;
            GUI.Label(tag, new GUIContent(badge.text, tooltip), _tagStyle);
            x += w + 5;
        }
    }
}
