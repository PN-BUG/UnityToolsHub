#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

internal static class ProjectTextLocalizationImporter
{
    // Import revision 6: escape brace placeholders for Unity Localization 1.4.5 SmartFormat parsing.
    private const string CollectionName = "ProjectText";
    private const string OutputDirectory = "Assets/Localization/ProjectText";
    private const string RelativeInputPath = ".codex_spreadsheet_work/localization-import.json";
    private const string RelativeRequestPath = ".codex_spreadsheet_work/localization-import.request";
    private const string RelativeReportPath = ".codex_spreadsheet_work/localization-import.report.txt";
    private static readonly Regex LiteralBraceTokenRegex = new Regex(
        "(?<!\\{)\\{([^{}]+)\\}(?!\\})",
        RegexOptions.Compiled);
    private static readonly Regex EscapedBraceTokenRegex = new Regex(
        "\\{\\{([^{}]+)\\}\\}",
        RegexOptions.Compiled);
    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    private static string InputPath => Path.Combine(ProjectRoot, RelativeInputPath);
    private static string RequestPath => Path.Combine(ProjectRoot, RelativeRequestPath);
    private static string ReportPath => Path.Combine(ProjectRoot, RelativeReportPath);

    [Serializable]
    private sealed class ImportData { public ImportRow[] rows; }

    [Serializable]
    private sealed class ImportRow
    {
        public string zhHans;
        public string zhTW;
        public string en;
    }

    [InitializeOnLoadMethod]
    private static void ImportWhenRequested()
    {
        if (!File.Exists(RequestPath)) return;
        EditorApplication.delayCall += () =>
        {
            try { ImportInternal(); }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath, "FAILED\n" + exception, System.Text.Encoding.UTF8);
                Debug.LogException(exception);
            }
            finally { if (File.Exists(RequestPath)) File.Delete(RequestPath); }
        };
    }

    [MenuItem("GameTools/Localization/导入 ProjectText 翻译", false, 920)]
    private static void ImportFromMenu()
    {
        ImportInternal();
        EditorUtility.DisplayDialog("Localization", "ProjectText 翻译已导入。", "确定");
    }

    private static void ImportInternal()
    {
        if (!File.Exists(InputPath)) throw new FileNotFoundException("找不到翻译数据。", InputPath);
        var data = JsonUtility.FromJson<ImportData>(File.ReadAllText(InputPath));
        if (data == null || data.rows == null || data.rows.Length == 0) throw new InvalidDataException("翻译数据为空。");
        if (data.rows.Any(row => row == null || row.zhHans == null || row.zhTW == null || row.en == null))
            throw new InvalidDataException("翻译数据包含空行或缺失语言。");

        var existing = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
        if (existing != null || AssetDatabase.IsValidFolder(OutputDirectory))
        {
            if (!AssetDatabase.DeleteAsset(OutputDirectory))
                throw new IOException("无法删除旧的 ProjectText Localization 资源目录。");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        Directory.CreateDirectory(OutputDirectory);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var collection = LocalizationEditorSettings.CreateStringTableCollection(CollectionName, OutputDirectory);

        var zhHans = GetOrCreateTable(collection, "zh-Hans");
        var zhTW = GetOrCreateTable(collection, "zh-TW");
        var en = GetOrCreateTable(collection, "en");

        foreach (var row in data.rows)
        {
            var shared = collection.SharedData.AddKey(row.zhHans);
            AddEntry(zhHans, shared.Id, row.zhHans);
            AddEntry(zhTW, shared.Id, row.zhTW);
            AddEntry(en, shared.Id, row.en);
        }

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(zhHans);
        EditorUtility.SetDirty(zhTW);
        EditorUtility.SetDirty(en);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var report = $"SUCCESS\nCollection={CollectionName}\nRows={data.rows.Length}\nLocales=zh-Hans,zh-TW,en\nDirectory={OutputDirectory}";
        File.WriteAllText(ReportPath, report, System.Text.Encoding.UTF8);
        Debug.Log($"ProjectText Localization 导入完成：{data.rows.Length} 条 × 3 种语言。");
    }

    private static StringTable GetOrCreateTable(StringTableCollection collection, string localeCode)
    {
        var identifier = new LocaleIdentifier(localeCode);
        return collection.GetTable(identifier) as StringTable ?? collection.AddNewTable(identifier) as StringTable;
    }

    private static void AddEntry(StringTable table, long id, string value)
    {
        var encodedValue = LiteralBraceTokenRegex.Replace(value ?? string.Empty, "{{$1}}");
        var entry = table.AddEntry(id, encodedValue);
        entry.IsSmart = EscapedBraceTokenRegex.IsMatch(encodedValue);
    }
}
#endif
