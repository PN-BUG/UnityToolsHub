#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Nodin;
using Nodin.Editor;

// ═══════════════════════════════════════════════════════════════
// 编码转换工具 — 将项目文件批量转换为指定编码
// ═══════════════════════════════════════════════════════════════

[ToolInfo("编码转换", "文件工具",
    Description = "批量转换文件编码格式。\n\n"
        + "• 自动检测原始编码（支持 UTF-8/16/32、GBK/GB18030、Shift-JIS、EUC-KR、Latin1 等）\n"
        + "• 可指定目标编码（UTF-8 无/有 BOM、UTF-16 LE/BE 等）\n"
        + "• 支持预览模式，转换前先查看影响范围\n"
        + "• 支持按文件夹和文件类型过滤",
    Icon = "🔤", Tags = new[] { "编码", "UTF-8", "转换" })]
public class EncodingConverterSafe : NodinEditorWindow
{
    #region ── 字段 ────────────────────────────────────────

    [FoldoutGroup("文件筛选")]
    [LabelText("目标文件夹"), FolderPath(AbsolutePath = true)]
    [Tooltip("留空则转换整个 Assets 目录")]
    public string targetFolder = "";

    [FoldoutGroup("文件筛选")]
    [LabelText("文件匹配模式")]
    [Tooltip("支持通配符，用分号分隔多个模式，如 *.cs;*.json;*.txt")]
    public string filePattern = "*.cs";

    [FoldoutGroup("文件筛选")]
    [LabelText("递归子目录")]
    public bool recursive = true;

    [FoldoutGroup("编码设置")]
    [LabelText("目标编码")]
    [ValueDropdown("GetTargetEncodings")]
    public string targetEncodingName = "UTF-8 (无 BOM)";

    [FoldoutGroup("编码设置")]
    [LabelText("自动检测源编码")]
    [Tooltip("关闭后可手动指定源编码（用于已知编码的批量文件）")]
    public bool autoDetect = true;

    [FoldoutGroup("编码设置")]
    [LabelText("手动指定源编码")]
    [ValueDropdown("GetSourceEncodings")]
    [ShowIf("autoDetect", false)]
    public string manualSourceEncoding = "GB18030 (简体中文)";

    [FoldoutGroup("编码设置")]
    [LabelText("跳过已经是目标编码的文件")]
    public bool skipAlreadyTarget = true;

    [FoldoutGroup("编码设置")]
    [LabelText("排除 .meta 文件")]
    public bool excludeMetaFiles = true;

    [FoldoutGroup("执行")]
    [LabelText("预览文件数上限")]
    [Tooltip("预览模式下最多显示的文件数量")]
    [Range(10, 500)]
    public int previewLimit = 100;

    [FoldoutGroup("状态")]
    [LabelText("扫描结果"), ReadOnly, MultiLineProperty(5)]
    [ShowInInspector]
    private string scanResult = "";

    [FoldoutGroup("状态")]
    [LabelText("转换报告"), ReadOnly, MultiLineProperty(8)]
    [ShowInInspector]
    private string convertReport = "";

    #endregion

    #region ── 菜单项 ──────────────────────────────────────

    [MenuItem("UnityToolsHub/Convert Encoding/编码转换工具窗口")]
    public static void ShowWindow()
    {
        var window = GetWindow<EncodingConverterSafe>();
        window.titleContent = new GUIContent("🔤 编码转换");
        window.minSize = new Vector2(480, 600);
        window.Show();
    }

    /// <summary>
    /// 一键转换：所有 .cs 文件 → UTF-8 无 BOM（保留原快速入口）
    /// </summary>
    [MenuItem("UnityToolsHub/Convert Encoding/一键转换 C# → UTF-8 无 BOM")]
    public static void QuickConvertAllCsToUtf8NoBom()
    {
        ConvertFiles(
            Application.dataPath,
            "*.cs",
            SearchOption.AllDirectories,
            new UTF8Encoding(false),
            autoDetect: true,
            skipAlreadyTarget: true,
            excludeMeta: false,
            out int success, out int skipped, out int failed);

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("转换完成",
            $"✅ 成功: {success}  |  ⏭ 跳过: {skipped}  |  ❌ 失败: {failed}",
            "确定");
    }

    #endregion

    #region ── 按钮（Odin） / OnGUI（原生） ─────────────────────

    [FoldoutGroup("执行")]
    [Button("🔍 扫描文件", ButtonSizes.Large), GUIColor(0.5f, 0.7f, 1f)]
    public void ScanFiles()
    {
        string root = GetTargetFolder();
        string[] patterns = ParsePatterns(filePattern);
        SearchOption searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var allFiles = new List<string>();
        foreach (string pattern in patterns)
        {
            allFiles.AddRange(Directory.GetFiles(root, pattern.Trim(), searchOpt));
        }

        // 去重 & 排除 .meta
        var files = allFiles
            .Distinct()
            .Where(f => !excludeMetaFiles || !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        // 分析编码分布
        var encodingStats = new Dictionary<string, int>();
        int needConvert = 0;
        int alreadyTarget = 0;

        foreach (string file in files)
        {
            Encoding detected = DetectEncoding(file);
            string encName = GetEncodingDisplayName(detected);
            encodingStats.TryGetValue(encName, out int cnt);
            encodingStats[encName] = cnt + 1;

            if (skipAlreadyTarget && IsSameEncoding(detected, GetTargetEncoding()))
                alreadyTarget++;
            else
                needConvert++;
        }

        // 构建扫描报告
        var sb = new StringBuilder();
        sb.AppendLine($"📂 文件夹: {root}");
        sb.AppendLine($"📄 匹配文件: {files.Count} 个");
        sb.AppendLine($"📊 需转换: {needConvert}  |  已是目标编码: {alreadyTarget}");
        sb.AppendLine();
        sb.AppendLine("── 编码分布 ──");
        foreach (var kv in encodingStats.OrderByDescending(kv => kv.Value))
            sb.AppendLine($"  {kv.Key}: {kv.Value} 个");

        scanResult = sb.ToString();
    }

    [FoldoutGroup("执行")]
    [Button("👁 预览转换", ButtonSizes.Large), GUIColor(0.9f, 0.85f, 0.4f)]
    public void PreviewConversion()
    {
        string root = GetTargetFolder();
        string[] patterns = ParsePatterns(filePattern);
        SearchOption searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        Encoding targetEnc = GetTargetEncoding();

        var allFiles = new List<string>();
        foreach (string pattern in patterns)
            allFiles.AddRange(Directory.GetFiles(root, pattern.Trim(), searchOpt));

        var files = allFiles
            .Distinct()
            .Where(f => !excludeMetaFiles || !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"👁 预览 — 目标编码: {targetEncodingName}");
        sb.AppendLine();

        int shown = 0;
        foreach (string file in files)
        {
            if (shown >= previewLimit)
            {
                sb.AppendLine($"... 还有 {files.Count - previewLimit} 个文件未显示（已达预览上限）");
                break;
            }

            Encoding srcEnc = autoDetect ? DetectEncoding(file) : GetManualSourceEncoding();

            if (skipAlreadyTarget && IsSameEncoding(srcEnc, targetEnc))
                continue;

            string relPath = GetRelativePath(file);
            sb.AppendLine($"  {GetEncodingDisplayName(srcEnc)} → {targetEncodingName}");
            sb.AppendLine($"    {relPath}");
            shown++;
        }

        if (shown == 0)
            sb.AppendLine("✅ 没有需要转换的文件（所有文件已是目标编码）");

        convertReport = sb.ToString();
    }

    [FoldoutGroup("执行")]
    [Button("🚀 执行转换", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.4f)]
    public void ExecuteConversion()
    {
        string root = GetTargetFolder();
        string[] patterns = ParsePatterns(filePattern);
        SearchOption searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        Encoding targetEnc = GetTargetEncoding();

        ConvertFiles(
            root, patterns, searchOpt, targetEnc,
            autoDetect, skipAlreadyTarget, excludeMetaFiles,
            out int success, out int skipped, out int failed);

        AssetDatabase.Refresh();

        var sb = new StringBuilder();
        sb.AppendLine($"🚀 转换完成");
        sb.AppendLine($"✅ 成功: {success}");
        sb.AppendLine($"⏭ 跳过: {skipped}");
        if (failed > 0) sb.AppendLine($"❌ 失败: {failed}");

        convertReport = sb.ToString();

        EditorUtility.DisplayDialog("转换完成", convertReport, "确定");
    }

    #endregion

    #region ── 核心转换逻辑 ────────────────────────────────

    /// <summary>
    /// 批量转换文件编码（静态方法，供菜单项和按钮共用）
    /// </summary>
    private static void ConvertFiles(
        string root,
        string[] patterns,
        SearchOption searchOption,
        Encoding targetEncoding,
        bool autoDetect,
        bool skipAlreadyTarget,
        bool excludeMeta,
        out int success,
        out int skipped,
        out int failed)
    {
        success = 0;
        skipped = 0;
        failed = 0;

        // 收集所有匹配文件
        var allFiles = new List<string>();
        foreach (string pattern in patterns)
            allFiles.AddRange(Directory.GetFiles(root, pattern.Trim(), searchOption));

        var files = allFiles
            .Distinct()
            .Where(f => !excludeMeta || !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int total = files.Count;
        if (total == 0)
        {
            Debug.LogWarning("未找到匹配的文件。");
            return;
        }

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];

                // 显示进度条 & 支持取消
                if (EditorUtility.DisplayCancelableProgressBar(
                    "编码转换中...",
                    $"({i + 1}/{total}) {Path.GetFileName(file)}",
                    (float)(i + 1) / total))
                {
                    Debug.LogWarning($"转换已取消。已完成 {success} 个，跳过 {skipped} 个，失败 {failed} 个。");
                    break;
                }

                try
                {
                    Encoding sourceEnc = autoDetect ? DetectEncoding(file) : Encoding.UTF8;

                    // 跳过已是目标编码的文件
                    if (skipAlreadyTarget && IsSameEncoding(sourceEnc, targetEncoding))
                    {
                        skipped++;
                        continue;
                    }

                    // 读取 → 写入
                    string content = File.ReadAllText(file, sourceEnc);
                    File.WriteAllText(file, content, targetEncoding);
                    success++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Debug.LogError($"转换失败: {GetRelativePath(file)}\n{ex.Message}");
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"编码转换完成 — ✅ {success}  |  ⏭ {skipped}  |  ❌ {failed}  |  📂 {root}");
    }

    /// <summary>
    /// 一键转换重载（接受 string 模式）
    /// </summary>
    private static void ConvertFiles(
        string root,
        string singlePattern,
        SearchOption searchOption,
        Encoding targetEncoding,
        bool autoDetect,
        bool skipAlreadyTarget,
        bool excludeMeta,
        out int success,
        out int skipped,
        out int failed)
    {
        ConvertFiles(root, new[] { singlePattern }, searchOption, targetEncoding,
            autoDetect, skipAlreadyTarget, excludeMeta, out success, out skipped, out failed);
    }

    #endregion

    #region ── 编码检测 ────────────────────────────────────

    /// <summary>
    /// 自动检测文件编码
    /// 检测顺序: BOM 标记 → UTF-8 字节特征 → 多字节编码试探 → GB18030 兜底
    /// </summary>
    private static Encoding DetectEncoding(string file)
    {
        byte[] buffer = File.ReadAllBytes(file);

        if (buffer.Length == 0)
            return new UTF8Encoding(false);

        // ── BOM 检测 ──
        // UTF-8 BOM: EF BB BF
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            return new UTF8Encoding(true);

        // UTF-32 LE BOM: FF FE 00 00
        if (buffer.Length >= 4 && buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
            return Encoding.UTF32;

        // UTF-32 BE BOM: 00 00 FE FF
        if (buffer.Length >= 4 && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
        {
            return new UTF32Encoding(true, true);
        }

        // UTF-16 LE BOM: FF FE
        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            return Encoding.Unicode;

        // UTF-16 BE BOM: FE FF
        if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // ── 无 BOM 编码检测 ──

        // UTF-8（无 BOM）：验证字节序列合法性
        if (IsValidUtf8(buffer))
            return new UTF8Encoding(false);

        // 尝试 GB18030（兼容 GBK/GB2312，覆盖绝大多数中文文件）
        try
        {
            Encoding gb18030 = Encoding.GetEncoding("GB18030");
            string test = gb18030.GetString(buffer);
            // 回编码验证（排除误判）
            byte[] reEncoded = gb18030.GetBytes(test);
            if (ByteArrayStartsWith(buffer, reEncoded, Math.Min(buffer.Length, reEncoded.Length)))
                return gb18030;
        }
        catch { }

        // 尝试 Shift-JIS（日文）
        if (ContainsSjisPattern(buffer))
        {
            try
            {
                Encoding sjis = Encoding.GetEncoding("shift_jis");
                return sjis;
            }
            catch { }
        }

        // 兜底：系统默认 ANSI 编码
        return Encoding.Default;
    }

    /// <summary>
    /// 验证字节序列是否为合法 UTF-8
    /// </summary>
    private static bool IsValidUtf8(byte[] data)
    {
        int i = 0;
        while (i < data.Length)
        {
            byte c = data[i];

            int byteCount;
            if ((c & 0x80) == 0) { byteCount = 1; }           // 0xxxxxxx — ASCII
            else if ((c & 0xE0) == 0xC0) { byteCount = 2; }    // 110xxxxx — 2字节序列
            else if ((c & 0xF0) == 0xE0) { byteCount = 3; }    // 1110xxxx — 3字节序列（含中文）
            else if ((c & 0xF8) == 0xF0) { byteCount = 4; }    // 11110xxx — 4字节序列
            else return false;                                   // 非法起始字节

            // 检查后续字节是否为 10xxxxxx
            for (int j = 1; j < byteCount; j++)
            {
                if (i + j >= data.Length) return false;
                if ((data[i + j] & 0xC0) != 0x80) return false;
            }

            // 拒绝过长编码（overlong encoding）
            if (byteCount == 2 && ((c & 0xFE) == 0xC0)) return false; // C0/C1 是非法起始
            if (byteCount > 4) return false;

            i += byteCount;
        }
        return true;
    }

    /// <summary>
    /// 检测是否包含日文 Shift-JIS 特征字节
    /// </summary>
    private static bool ContainsSjisPattern(byte[] data)
    {
        int sjisHints = 0;
        for (int i = 0; i < data.Length - 1; i++)
        {
            // Shift-JIS 双字节范围: 0x81-0x9F 或 0xE0-0xEF 开头
            if ((data[i] >= 0x81 && data[i] <= 0x9F) || (data[i] >= 0xE0 && data[i] <= 0xEF))
            {
                byte second = data[i + 1];
                if ((second >= 0x40 && second <= 0x7E) || (second >= 0x80 && second <= 0xFC))
                {
                    sjisHints++;
                    i++; // 跳过第二个字节
                }
            }
        }
        // 需要有足够多的双字节特征才判定为 SJIS
        return sjisHints > data.Length * 0.05;
    }

    /// <summary>
    /// 判断两个 Encoding 是否为"相同编码"（用于跳过判断）
    /// </summary>
    private static bool IsSameEncoding(Encoding a, Encoding b)
    {
        if (a == null || b == null) return false;

        // 同是 UTF-8（不论 BOM）
        if (a is UTF8Encoding && b is UTF8Encoding)
            return true;

        // 同是 Unicode (UTF-16 LE)
        if (a is UnicodeEncoding && b is UnicodeEncoding)
            return true;

        return a.CodePage == b.CodePage && a.EncodingName == b.EncodingName;
    }

    /// <summary>
    /// 比较两个字节数组的前 minLen 个字节是否相同
    /// </summary>
    private static bool ByteArrayStartsWith(byte[] a, byte[] b, int minLen)
    {
        if (minLen == 0) return false;
        for (int i = 0; i < minLen; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    #endregion

    #region ── 辅助方法 ────────────────────────────────────

    /// <summary>获取目标文件夹绝对路径</summary>
    private string GetTargetFolder()
    {
        return string.IsNullOrEmpty(targetFolder) ? Application.dataPath : targetFolder;
    }

    /// <summary>解析分号分隔的文件匹配模式</summary>
    private static string[] ParsePatterns(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return new[] { "*.*" };

        return pattern.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
    }

    /// <summary>获取文件相对于 Assets 的路径</summary>
    private static string GetRelativePath(string fullPath)
    {
        string assetsPath = Application.dataPath.Replace("/", "\\");
        if (fullPath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            return "Assets" + fullPath.Substring(assetsPath.Length);
        return fullPath;
    }

    /// <summary>获取目标编码对象</summary>
    private Encoding GetTargetEncoding()
    {
        switch (targetEncodingName)
        {
            case "UTF-8 (无 BOM)": return new UTF8Encoding(false);
            case "UTF-8 (有 BOM)": return new UTF8Encoding(true);
            case "UTF-16 LE": return Encoding.Unicode;
            case "UTF-16 BE": return Encoding.BigEndianUnicode;
            case "UTF-32 LE": return Encoding.UTF32;
            case "ASCII": return Encoding.ASCII;
            case "GB18030 (简体中文)": return Encoding.GetEncoding("GB18030");
            case "Shift-JIS (日文)": return Encoding.GetEncoding("shift_jis");
            case "EUC-KR (韩文)": return Encoding.GetEncoding("euc-kr");
            case "Latin1 (ISO-8859-1)": return Encoding.GetEncoding("iso-8859-1");
            default: return new UTF8Encoding(false);
        }
    }

    /// <summary>获取手动指定的源编码对象</summary>
    private Encoding GetManualSourceEncoding()
    {
        switch (manualSourceEncoding)
        {
            case "UTF-8 (无 BOM)": return new UTF8Encoding(false);
            case "UTF-8 (有 BOM)": return new UTF8Encoding(true);
            case "UTF-16 LE": return Encoding.Unicode;
            case "UTF-16 BE": return Encoding.BigEndianUnicode;
            case "GB18030 (简体中文)": return Encoding.GetEncoding("GB18030");
            case "Shift-JIS (日文)": return Encoding.GetEncoding("shift_jis");
            case "EUC-KR (韩文)": return Encoding.GetEncoding("euc-kr");
            default: return Encoding.GetEncoding("GB18030");
        }
    }

    /// <summary>获取编码的显示名称</summary>
    private static string GetEncodingDisplayName(Encoding enc)
    {
        if (enc is UTF8Encoding utf8)
        {
            // 通过尝试获取 Preamble 判断是否有 BOM
            byte[] preamble = utf8.GetPreamble();
            return preamble.Length > 0 ? "UTF-8 (有 BOM)" : "UTF-8 (无 BOM)";
        }
        if (enc is UnicodeEncoding) return "UTF-16 LE";
        if (enc.CodePage == 1201) return "UTF-16 BE";
        if (enc.CodePage == 12000) return "UTF-32 LE";
        if (enc.CodePage == 12001) return "UTF-32 BE";
        if (enc.CodePage == 54936) return "GB18030";
        if (enc.CodePage == 932) return "Shift-JIS";
        if (enc.CodePage == 51949) return "EUC-KR";
        if (enc.CodePage == 20127) return "ASCII";
        if (enc.CodePage == 28591) return "Latin1";
        return enc.EncodingName;
    }

    /// <summary>供 Odin ValueDropdown 使用的目标编码列表</summary>
    private static ValueDropdownList<string> GetTargetEncodings()
    {
        return new ValueDropdownList<string>
        {
            { "UTF-8 (无 BOM) — 推荐", "UTF-8 (无 BOM)" },
            { "UTF-8 (有 BOM)", "UTF-8 (有 BOM)" },
            { "UTF-16 LE", "UTF-16 LE" },
            { "UTF-16 BE", "UTF-16 BE" },
            { "UTF-32 LE", "UTF-32 LE" },
            { "ASCII", "ASCII" },
            { "GB18030 (简体中文)", "GB18030 (简体中文)" },
            { "Shift-JIS (日文)", "Shift-JIS (日文)" },
            { "EUC-KR (韩文)", "EUC-KR (韩文)" },
            { "Latin1 (ISO-8859-1)", "Latin1 (ISO-8859-1)" },
        };
    }

    /// <summary>供 Odin ValueDropdown 使用的源编码列表</summary>
    private static ValueDropdownList<string> GetSourceEncodings()
    {
        return new ValueDropdownList<string>
        {
            { "GB18030 (简体中文) — 默认", "GB18030 (简体中文)" },
            { "UTF-8 (无 BOM)", "UTF-8 (无 BOM)" },
            { "UTF-8 (有 BOM)", "UTF-8 (有 BOM)" },
            { "UTF-16 LE", "UTF-16 LE" },
            { "UTF-16 BE", "UTF-16 BE" },
            { "Shift-JIS (日文)", "Shift-JIS (日文)" },
            { "EUC-KR (韩文)", "EUC-KR (韩文)" },
        };
    }

    #endregion
}
#endif
