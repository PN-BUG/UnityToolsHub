#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityToolsHub.Setup
{
    /// <summary>Ensures UnityToolsHub can resolve Nodin without external assembly dependencies.</summary>
    [InitializeOnLoad]
    internal static class NodinSetup
    {
        private const string PackageName = "com.zko.nodin";
        private const string EmbeddedReference = "file:nodin";
        private const string GitReference = "https://github.com/PN-BUG/Nodin.git";
        private const string SessionKey = "UnityToolsHub.Nodin.ManifestStamp";

        static NodinSetup() => EnsureNodinInManifest();

        internal static bool EnsureNodinInManifest()
        {
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));
            if (!File.Exists(manifestPath)) return false;

            string packageDirectory = Path.Combine(Path.GetDirectoryName(manifestPath), "nodin");
            string reference = Directory.Exists(packageDirectory) ? EmbeddedReference : GitReference;
            string stamp = $"{File.GetLastWriteTimeUtc(manifestPath).Ticks}:{reference}";
            if (SessionState.GetString(SessionKey, string.Empty) == stamp) return true;

            try
            {
                string content = File.ReadAllText(manifestPath);
                if (!TryUpsertDependency(content, PackageName, reference, out string updated, out bool changed)) return false;
                if (changed)
                {
                    File.WriteAllText(manifestPath, updated);
                    stamp = $"{File.GetLastWriteTimeUtc(manifestPath).Ticks}:{reference}";
                    Debug.Log($"[UnityToolsHub] Nodin package source set to {reference}.");
                }
                SessionState.SetString(SessionKey, stamp);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[UnityToolsHub] Failed to update Nodin dependency: {exception.Message}");
                return false;
            }
        }

        private static bool TryUpsertDependency(string json, string key, string value, out string result, out bool changed)
        {
            result = json;
            changed = false;
            int dependencies = json.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (dependencies < 0) return false;
            int openBrace = json.IndexOf('{', dependencies);
            if (openBrace < 0 || !TryFindClosingBrace(json, openBrace, out int closeBrace)) return false;

            string quotedKey = "\"" + key + "\"";
            int keyIndex = json.IndexOf(quotedKey, openBrace + 1, closeBrace - openBrace - 1, StringComparison.Ordinal);
            if (keyIndex >= 0)
            {
                int valueStart = json.IndexOf('"', json.IndexOf(':', keyIndex) + 1);
                int valueEnd = valueStart >= 0 ? json.IndexOf('"', valueStart + 1) : -1;
                if (valueStart < 0 || valueEnd < 0) return false;
                if (json.Substring(valueStart + 1, valueEnd - valueStart - 1) == value) return true;
                result = json.Substring(0, valueStart + 1) + value + json.Substring(valueEnd);
                changed = true;
                return true;
            }

            string newline = json.Contains("\r\n") ? "\r\n" : "\n";
            string parentIndent = GetIndent(json, openBrace);
            int firstProperty = openBrace + 1;
            while (firstProperty < closeBrace && char.IsWhiteSpace(json[firstProperty])) firstProperty++;
            bool hasEntries = firstProperty < closeBrace;
            string propertyIndent = hasEntries ? GetIndent(json, firstProperty) : parentIndent + "  ";
            string insertion = hasEntries
                ? propertyIndent + quotedKey + ": \"" + value + "\"," + newline
                : newline + propertyIndent + quotedKey + ": \"" + value + "\"" + newline + parentIndent;
            result = json.Insert(hasEntries ? firstProperty : closeBrace, insertion);
            changed = true;
            return true;
        }

        private static bool TryFindClosingBrace(string text, int openBrace, out int closeBrace)
        {
            int depth = 0;
            for (int i = openBrace; i < text.Length; i++)
            {
                if (text[i] == '"')
                {
                    i++;
                    while (i < text.Length && text[i] != '"') i += text[i] == '\\' ? 2 : 1;
                    continue;
                }
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) { closeBrace = i; return true; }
            }
            closeBrace = -1;
            return false;
        }

        private static string GetIndent(string text, int index)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            int cursor = lineStart;
            while (cursor < text.Length && (text[cursor] == ' ' || text[cursor] == '\t')) cursor++;
            return text.Substring(lineStart, cursor - lineStart);
        }
    }
}
#endif
