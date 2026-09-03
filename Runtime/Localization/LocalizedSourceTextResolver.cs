using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// Resolves business-code source text through a String Table without requiring callers to know a key.
/// The Simplified Chinese table is treated as the source-text index.
/// </summary>
public static class LocalizedSourceTextResolver
{
    public const string DefaultTable = "ProjectText";

    private const string SourceLocaleCode = "zh-Hans";
    private static readonly Regex TemplateTokenRegex =
        new Regex(@"\{(?!(?:确认|取消|合体键|换脚键|撤回键)\})[^{}]+\}", RegexOptions.Compiled);
    private static readonly Regex HanSpacingRegex =
        new Regex(@"(?<=[\u3400-\u9fff])[\p{Zs}\t]+(?=[\u3400-\u9fff])", RegexOptions.Compiled);

    private sealed class SourceEntry
    {
        public long Id;
        public string Source;
        public int LiteralLength;
    }

    private sealed class SourceIndex
    {
        public readonly Dictionary<string, SourceEntry> Exact =
            new Dictionary<string, SourceEntry>(StringComparer.Ordinal);
        public readonly List<SourceEntry> Templates = new List<SourceEntry>();
    }

    private static readonly Dictionary<string, Task<SourceIndex>> IndexTasks =
        new Dictionary<string, Task<SourceIndex>>(StringComparer.Ordinal);
    private static readonly object SyncRoot = new object();

    /// <summary>
    /// Converts source text to the selected locale. Missing tables, keys or translations safely return sourceText.
    /// </summary>
    public static async Task<string> ResolveAsync(string sourceText, string tableName = DefaultTable)
    {
        if (string.IsNullOrEmpty(sourceText) || string.IsNullOrEmpty(tableName))
            return sourceText;

        try
        {
            await LocalizationSettings.InitializationOperation.Task;
            var index = await GetIndexAsync(tableName);
            if (!TryResolveEntry(index, sourceText, out var sourceEntry, out var templateValues))
                return sourceText;

            TableReference tableReference = tableName;
            TableEntryReference entryReference = sourceEntry.Id;
            var operation = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(
                tableReference, entryReference);
            var localized = await operation.Task;
            if (string.IsNullOrEmpty(localized) || localized.StartsWith("No translation found", StringComparison.Ordinal))
                return sourceText;

            return ApplyTemplateValues(localized, templateValues);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"本地化原文转换失败，已回退原文：{sourceText}\n{exception.Message}");
            return sourceText;
        }
    }

    /// <summary>Clears cached source indexes after String Tables are regenerated at runtime or in play mode.</summary>
    public static void ClearCache()
    {
        lock (SyncRoot)
            IndexTasks.Clear();
    }

    private static Task<SourceIndex> GetIndexAsync(string tableName)
    {
        lock (SyncRoot)
        {
            if (!IndexTasks.TryGetValue(tableName, out var task))
            {
                task = BuildIndexAsync(tableName);
                IndexTasks.Add(tableName, task);
            }
            return task;
        }
    }

    private static async Task<SourceIndex> BuildIndexAsync(string tableName)
    {
        var sourceLocale = LocalizationSettings.AvailableLocales.GetLocale(SourceLocaleCode);
        if (sourceLocale == null)
            throw new InvalidOperationException($"找不到源语言：{SourceLocaleCode}");

        TableReference tableReference = tableName;
        var operation = LocalizationSettings.StringDatabase.GetTableAsync(tableReference, sourceLocale);
        var table = await operation.Task;
        if (table == null)
            throw new InvalidOperationException($"找不到 String Table：{tableName}_{SourceLocaleCode}");

        var index = new SourceIndex();
        foreach (var tableEntry in table.Values)
        {
            if (tableEntry == null || string.IsNullOrEmpty(tableEntry.Value))
                continue;

            var source = DecodeEscapedBraces(tableEntry.Value);
            var entry = new SourceEntry
            {
                Id = tableEntry.KeyId,
                Source = source,
                LiteralLength = TemplateTokenRegex.Replace(source, string.Empty).Length
            };

            AddExact(index, source, entry);
            var normalized = NormalizeHanSpacing(source);
            if (!string.Equals(source, normalized, StringComparison.Ordinal))
                AddExact(index, normalized, entry);

            if (TemplateTokenRegex.IsMatch(source))
                index.Templates.Add(entry);
        }

        // More literal context wins, so a broad template cannot steal a more specific match.
        index.Templates.Sort((left, right) => right.LiteralLength.CompareTo(left.LiteralLength));
        return index;
    }

    private static void AddExact(SourceIndex index, string source, SourceEntry entry)
    {
        if (!index.Exact.ContainsKey(source))
            index.Exact.Add(source, entry);
    }

    private static bool TryResolveEntry(SourceIndex index, string sourceText,
        out SourceEntry entry, out Dictionary<string, string> templateValues)
    {
        templateValues = null;
        if (index.Exact.TryGetValue(sourceText, out entry))
            return true;

        var normalized = NormalizeHanSpacing(sourceText);
        if (index.Exact.TryGetValue(normalized, out entry))
            return true;

        foreach (var candidate in index.Templates)
        {
            if (TryMatchTemplate(candidate.Source, sourceText, out templateValues) ||
                (!string.Equals(sourceText, normalized, StringComparison.Ordinal) &&
                 TryMatchTemplate(NormalizeHanSpacing(candidate.Source), normalized, out templateValues)))
            {
                entry = candidate;
                return true;
            }
        }

        entry = null;
        return false;
    }

    private static bool TryMatchTemplate(string template, string displayedValue,
        out Dictionary<string, string> values)
    {
        values = null;
        var tokens = TemplateTokenRegex.Matches(template).Cast<Match>().ToArray();
        if (tokens.Length == 0)
            return false;

        var pattern = new StringBuilder("^");
        var position = 0;
        foreach (var token in tokens)
        {
            pattern.Append(Regex.Escape(template.Substring(position, token.Index - position)));
            pattern.Append("(.+?)");
            position = token.Index + token.Length;
        }
        pattern.Append(Regex.Escape(template.Substring(position))).Append('$');

        var match = Regex.Match(displayedValue, pattern.ToString(), RegexOptions.Singleline);
        if (!match.Success)
            return false;

        values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < tokens.Length; index++)
            values[tokens[index].Value] = match.Groups[index + 1].Value;
        return true;
    }

    private static string ApplyTemplateValues(string localized, Dictionary<string, string> values)
    {
        if (values == null)
            return localized;
        foreach (var pair in values)
            localized = localized.Replace(pair.Key, pair.Value);
        return localized;
    }

    private static string DecodeEscapedBraces(string value)
    {
        return value.Replace("{{", "{").Replace("}}", "}");
    }

    private static string NormalizeHanSpacing(string value)
    {
        return string.IsNullOrEmpty(value) ? value : HanSpacingRegex.Replace(value, string.Empty);
    }
}
