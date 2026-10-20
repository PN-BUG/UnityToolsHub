using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityPackageCreator.Editor
{
    /// <summary>Shared reference handling for the existing package importers; no automatic imports.</summary>
    public static class PackageReferenceUtility
    {
        private static readonly StringComparison PathComparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        [Serializable]
        private class PackageManifest { public string name; }

        public static bool IsWithin(string directory, string path)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.Equals(root, PathComparison) || full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
        }

        public static string RelativePath(string directory, string path)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Uri.UnescapeDataString(new Uri(root).MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString());
        }

        public static string ResolveLocalReference(string reference, string projectRoot)
        {
            if (!reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("不是本地包引用。");
            if (reference.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(new Uri(reference).LocalPath);
            string path = reference.Substring(5).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, "Packages", path));
        }

        /// <summary>Copy external/cache packages into LocalPackages and return a Packages-relative file: reference.</summary>
        public static string PrepareLocalPackageReference(string sourcePath, string projectRoot)
        {
            projectRoot = Path.GetFullPath(projectRoot);
            string source = sourcePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? ResolveLocalReference(sourcePath, projectRoot)
                : Path.GetFullPath(Path.IsPathRooted(sourcePath) ? sourcePath : Path.Combine(projectRoot, sourcePath));
            bool folder = Directory.Exists(source);
            RejectLinkedAncestors(source);
            string name;
            if (folder)
            {
                string manifest = Path.Combine(source, "package.json");
                if (!File.Exists(manifest)) throw new ArgumentException("所选目录缺少 package.json。");
                name = JsonUtility.FromJson<PackageManifest>(File.ReadAllText(manifest))?.name;
                if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^[a-z0-9][a-z0-9._-]*$"))
                    throw new ArgumentException("package.json 中的包名无效。");
            }
            else
            {
                if (!File.Exists(source) || !source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("请选择包含 package.json 的目录或 .tgz 文件。");
                name = Path.GetFileName(source);
            }

            bool transient = IsWithin(Path.Combine(projectRoot, "Library"), source)
                || IsWithin(Path.Combine(projectRoot, "Temp"), source)
                || IsWithin(Path.Combine(projectRoot, "obj"), source);
            if (!IsWithin(projectRoot, source) || transient)
            {
                string parent = folder ? Path.Combine(projectRoot, "LocalPackages")
                    : Path.Combine(projectRoot, "LocalPackages", "Archives");
                string destination = Path.Combine(parent, name);
                RejectLinkedAncestors(parent);
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("项目 LocalPackages 中已存在同名包，不会覆盖。请直接选择已有包，或先备份并调整目录。");
                Directory.CreateDirectory(parent);
                string staging = destination + ".import-" + Guid.NewGuid().ToString("N");
                try
                {
                    if (folder)
                    {
                        CopyPackageDirectory(source, staging);
                        Directory.Move(staging, destination);
                    }
                    else
                    {
                        RejectLink(source);
                        File.Copy(source, staging, false);
                        File.Move(staging, destination);
                    }
                }
                finally
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    if (File.Exists(staging)) File.Delete(staging);
                }
                source = destination;
            }
            RejectLink(source);
            return "file:" + RelativePath(Path.Combine(projectRoot, "Packages"), source).Replace('\\', '/');
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("本地包包含符号链接或目录联接，请先复制为普通文件，避免依赖项目外路径。");
        }

        private static void RejectLinkedAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current)) RejectLink(current);
                current = Path.GetDirectoryName(current);
            }
        }

        private static void CopyPackageDirectory(string source, string destination)
        {
            RejectLink(source);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                string name = Path.GetFileName(file);
                if (name == ".git" || name == ".gitswitcher.json" || IsPrivateFile(name)) continue;
                RejectLink(file);
                File.Copy(file, Path.Combine(destination, name), false);
            }
            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                if (name == ".git" || name == ".svn" || name == ".hg"
                    || name.Equals("keystore", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("LocalSecrets", StringComparison.OrdinalIgnoreCase)) continue;
                CopyPackageDirectory(directory, Path.Combine(destination, name));
            }
        }

        private static bool IsPrivateFile(string name)
        {
            string plain = name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 5) : name;
            return plain.Equals("XorKey.txt", StringComparison.OrdinalIgnoreCase)
                || plain.Equals("keystore", StringComparison.OrdinalIgnoreCase)
                || plain.Equals("LocalSecrets", StringComparison.OrdinalIgnoreCase)
                || plain.Equals(".env", StringComparison.OrdinalIgnoreCase)
                || plain.Equals(".env.local", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(plain, @"(?i)\.(keystore|jks|p12|pfx)$");
        }

        public static void ValidateGitUrl(string value)
        {
            const string message = "Git URL 不得包含密码、令牌或认证参数。请使用无凭据的仓库地址，并通过 Git 凭据管理器或 SSH 认证。";
            if (string.IsNullOrWhiteSpace(value) || Regex.IsMatch(value, "[\\s\"<>]"))
                throw new ArgumentException("Git URL 格式无效。");
            if (value.StartsWith("git@", StringComparison.Ordinal) && Regex.IsMatch(value, @"^git@[^/:]+:.+$"))
            {
                int queryStart = value.IndexOf('?');
                int fragmentStart = value.IndexOf('#');
                if (queryStart >= 0 && (fragmentStart < 0 || queryStart < fragmentStart))
                    ValidateGitQuery(value.Substring(queryStart + 1, (fragmentStart < 0 ? value.Length : fragmentStart) - queryStart - 1), message);
                return;
            }
            Uri uri;
            string address = value.StartsWith("git+", StringComparison.OrdinalIgnoreCase) ? value.Substring(4) : value;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri)
                || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ssh" && uri.Scheme != "git"))
                throw new ArgumentException("请选择 HTTPS 或 SSH Git 仓库地址。");
            if (!string.IsNullOrEmpty(uri.UserInfo)
                && (uri.Scheme != "ssh" || Uri.UnescapeDataString(uri.UserInfo).Contains(":")))
                throw new ArgumentException(message);
            ValidateGitQuery(uri.Query.TrimStart('?'), message);
        }

        private static void ValidateGitQuery(string query, string message)
        {
            foreach (string item in query.Split('&'))
            {
                if (item.Length == 0) continue;
                int equals = item.IndexOf('=');
                if (equals < 0 || item.Substring(0, equals) != "path") throw new ArgumentException(message);
                string path = Uri.UnescapeDataString(item.Substring(equals + 1)).Replace('\\', '/');
                if (Array.IndexOf(path.Split('/'), "..") >= 0)
                    throw new ArgumentException("Git 包子目录不能包含父目录跳转。");
            }
        }

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = Regex.Replace(text, @"(?i)((?:git\+)?(?:https?|ssh|git)://)[^/\s]+@", "$1[REDACTED]@");
            text = Regex.Replace(text, @"(?i)([?&](?!path=)[^=&#\s]+)=([^&#\s""']+)", "$1=[REDACTED]");
            text = Regex.Replace(text, @"(?i)(Authorization\s*:\s*)[^\r\n]+", "$1[REDACTED]");
            text = Regex.Replace(text, @"(?i)\b(access_token|token|password|passwd|api_key|client_secret)(\s*[:=]\s*)[^\s,;""']+", "$1$2[REDACTED]");
            return Regex.Replace(text, @"(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-(?:proj-)?[A-Za-z0-9_-]{20,})", "[REDACTED]");
        }
    }
}
