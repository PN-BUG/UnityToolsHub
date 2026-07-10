#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityToolsHub.Setup
{
    /// <summary>
    /// Nodin 自动配置 — 首次加载时确保 com.zko.nodin 已写入 manifest.json。
    /// 独立 asmdef，不引用 Nodin，确保即使 Nodin 未安装也能编译执行。
    /// </summary>
    [InitializeOnLoad]
    internal static class NodinSetup
    {
        private const string NodinPackageName = "com.zko.nodin";
        private const string NodinGitUrl = "https://github.com/PN-BUG/Nodin.git";

        static NodinSetup()
        {
            EnsureNodinInManifest();
        }

        /// <summary>
        /// 确保 manifest.json 中包含 com.zko.nodin 依赖。
        /// Nodin 是 UnityToolsHub 的核心依赖，嵌入式包的 package.json 依赖不会被 UPM 自动解析，
        /// 必须在 manifest.json 中声明。
        /// </summary>
        internal static bool EnsureNodinInManifest()
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                return false;

            string content = File.ReadAllText(manifestPath);

            // 已经包含 com.zko.nodin，无需修改
            if (content.Contains(NodinPackageName))
                return true;

            // 在 dependencies 块的第一个条目前插入 com.zko.nodin
            var match = Regex.Match(content, @"(""dependencies""\s*:\s*\{\s*\r?\n\s*)(""[^""]+"")");
            if (!match.Success)
                return false;

            string insert = $"{match.Groups[1].Value}\"{NodinPackageName}\": \"{NodinGitUrl}\",\n    {match.Groups[2].Value}";
            content = content.Substring(0, match.Index) + insert + content.Substring(match.Index + match.Length);

            File.WriteAllText(manifestPath, content);
            Debug.Log($"[UnityToolsHub] 已自动将 {NodinPackageName} 添加到 manifest.json，Unity 将在下次刷新时解析。");
            return true;
        }
    }
}
#endif