#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>文档文件操作。仅显式保存/新建时写入；磁盘正文不匹配时拒绝覆盖。</summary>
public static class FrameworkDocumentation
{
    public sealed class Page
    {
        public string RelativePath;
        public string Title;
        public string Content;
    }

    public static string GetDocumentationRoot()
    {
        // 支持作为 Assets 文件夹、嵌入包或本地 Git 包安装，且不引用框架运行时程序集。
        var package = PackageInfo.GetAllRegisteredPackages()
            .FirstOrDefault(p => p.name == "com.unityframework.core");
        if (package != null)
            return Path.Combine(package.resolvedPath, "Documentation");
        foreach (string guid in AssetDatabase.FindAssets("FrameworkDocumentationWindow t:MonoScript"))
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(AssetDatabase.GUIDToAssetPath(guid))));
            while (directory != null)
            {
                string manifest = Path.Combine(directory.FullName, "package.json");
                if (File.Exists(manifest) && System.Text.RegularExpressions.Regex.IsMatch(
                    File.ReadAllText(manifest), "\"name\"\\s*:\\s*\"com\\.unityframework\\.core\""))
                    return Path.Combine(directory.FullName, "Documentation");
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("找不到 UnityFramework 包，请确认框架及 Documentation 目录已完整安装。");
    }

    /// <summary>解析根目录内的路径，拒绝绝对路径、越界、链接及重解析点。</summary>
    public static string ResolvePath(string root, string relativePath, bool markdownOnly = true)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath) || relativePath.Contains(":"))
            throw new ArgumentException("必须使用文档目录内的相对路径。");
        string directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(directory, relativePath));
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(directory + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("路径超出了文档目录。");
        if (markdownOnly && !string.Equals(Path.GetExtension(full), ".md", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("仅允许编辑 Markdown 文件（.md）。");
        for (string current = full; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("文档操作不支持符号链接或重解析点：" + current);
            if (string.Equals(current, directory, comparison))
                break;
        }
        return full;
    }

    public static List<Page> ReadPages(string root)
    {
        var pages = new List<Page>();
        if (!Directory.Exists(root))
            return pages;
        ReadDirectory(root, "", pages);
        return pages.OrderBy(p => p.RelativePath == "index.md" ? 0 : p.RelativePath.StartsWith("Guides/") ? 1 : 2)
            .ThenBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReadDirectory(string root, string relative, List<Page> pages)
    {
        string directory = relative.Length == 0 ? root : ResolvePath(root, relative, false);
        foreach (string file in Directory.GetFiles(directory, "*.md"))
        {
            string path = (relative.Length == 0 ? "" : relative + "/") + Path.GetFileName(file);
            string text = LoadDocument(root, path);
            string heading = text.Split('\n').FirstOrDefault(l => l.StartsWith("# "));
            pages.Add(new Page { RelativePath = path, Title = heading == null ? Path.GetFileNameWithoutExtension(file) : heading.Substring(2).Trim(), Content = text });
        }
        foreach (string child in Directory.GetDirectories(directory))
        {
            string name = Path.GetFileName(child);
            if (name.StartsWith(".") || name.EndsWith("~")
                || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                continue;
            ReadDirectory(root, (relative.Length == 0 ? "" : relative + "/") + name, pages);
        }
    }

    public static string LoadDocument(string root, string relativePath)
        => File.ReadAllText(ResolvePath(root, relativePath), Encoding.UTF8);

    /// <param name="expectedContent">打开时的磁盘正文；为 null 表示新建且文件必须不存在。</param>
    public static void SaveDocument(string root, string relativePath, string content, string expectedContent)
    {
        string path = ResolvePath(root, relativePath);
        if (expectedContent == null && File.Exists(path))
            throw new IOException("该文档已经存在，请使用其他文件名。");
        if (expectedContent != null && (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != expectedContent))
            throw new IOException("磁盘文档已被其他编辑器修改或删除。当前草稿已保留；请先复制草稿，再重新载入并合并，避免覆盖他人的更改。");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
            throw new IOException("文档为只读。请使用可写的嵌入包或本地包维护，再提交到框架仓库。");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content ?? "", new UTF8Encoding(false));
            if (expectedContent == null)
                File.Move(temporary, path); // 不覆盖已有文档。
            else
                File.Replace(temporary, path, null);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
#endif
