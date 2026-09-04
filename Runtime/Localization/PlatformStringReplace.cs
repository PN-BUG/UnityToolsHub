using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class PlatformStringReplace
{
    public static Dictionary<string, string> GetReplaceDict(bool hasJoystick)
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        if (hasJoystick)
        {
            return new Dictionary<string, string>()
            {
                { "{确认}", "A" },
                { "{取消}", "B" },
                { "{合体键}", "A" },
                { "{换脚键}", "LB/RB" },
                { "{撤回键}", "B" }
            };
        }
        return new Dictionary<string, string>()
        {
            { "{合体键}", "F" },
            { "{换脚键}", "Q/E/鼠标滚轮" },
            { "{撤回键}", "Z" }
        };
#elif UNITY_ANDROID || UNITY_IOS
        return new Dictionary<string, string>()
        {
            { "{合体键}", "合体按钮" },
            { "{换脚键}", "换脚按钮" },
            { "{撤回键}", "撤回按钮" }
        };
#elif UNITY_PS4 || UNITY_PS5
        return new Dictionary<string, string>()
        {
            { "{确认}", "×" },
            { "{取消}", "○" },
            { "{合体键}", "×" },
            { "{换脚键}", "LB/RB" },
            { "{撤回键}", "○" }
        };
#elif UNITY_XBOXONE
        return new Dictionary<string, string>()
        {
            { "{确认}", "A" },
            { "{取消}", "B" },
            { "{合体键}", "A" },
            { "{换脚键}", "LB/RB" },
            { "{撤回键}", "B" }
        };
#else
        return new Dictionary<string, string>();
#endif
    }

    public static string Replace(string str, bool hasJoystick)
    {
        var dict = GetReplaceDict(hasJoystick);
        foreach (var kv in dict)
        {
            str = ReplaceTokenWithLatinSpacing(str, kv.Key, kv.Value);
        }
        return str;
    }
    public static string Replace(string str, bool hasJoystick, Color color)
    {
        return Replace(str, hasJoystick, ColorUtility.ToHtmlStringRGBA(color));
    }
    public static string Replace(string str, bool hasJoystick, string color)
    {
        var dict = GetReplaceDict(hasJoystick);
        foreach (var kv in dict)
        {
            str = ReplaceTokenWithLatinSpacing(str, kv.Key, $"<color={color}>{kv.Value}</color>");
        }
        return str;
    }

    /// <summary>
    /// Keeps platform placeholders readable inside Latin text. For example,
    /// "press{确认}to continue" becomes "press A to continue". CJK text is
    /// intentionally left compact: "按{确认}继续" remains "按A继续".
    /// </summary>
    private static string ReplaceTokenWithLatinSpacing(string source, string token, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token))
        {
            return source;
        }

        var firstIndex = source.IndexOf(token, System.StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            return source;
        }

        var result = new StringBuilder(source.Length + replacement.Length);
        var sourceIndex = 0;
        var matchIndex = firstIndex;
        while (matchIndex >= 0)
        {
            result.Append(source, sourceIndex, matchIndex - sourceIndex);

            var tokenEnd = matchIndex + token.Length;
            if (matchIndex > 0 && IsLatinWordCharacter(source[matchIndex - 1]))
            {
                result.Append(' ');
            }

            result.Append(replacement);

            if (tokenEnd < source.Length && IsLatinWordCharacter(source[tokenEnd]))
            {
                result.Append(' ');
            }

            sourceIndex = tokenEnd;
            matchIndex = source.IndexOf(token, sourceIndex, System.StringComparison.Ordinal);
        }

        result.Append(source, sourceIndex, source.Length - sourceIndex);
        return result.ToString();
    }

    private static bool IsLatinWordCharacter(char value)
    {
        return value <= 0x7F && (char.IsLetterOrDigit(value) || value == '_');
    }
}
