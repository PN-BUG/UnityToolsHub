using System.Collections.Generic;
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
            str = str.Replace(kv.Key, kv.Value);
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
            str = str.Replace(kv.Key, $"<color={color}>{kv.Value}</color>");
        }
        return str;
    }
}