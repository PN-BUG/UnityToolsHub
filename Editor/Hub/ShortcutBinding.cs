#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 快捷键绑定结构体
/// 支持解析/序列化快捷键字符串、MenuItem 格式、Event 输入
/// </summary>
public partial class UnityToolsHub
{
    /// <summary>快捷键绑定（可序列化存储到 EditorPrefs）</summary>
    [Serializable]
    private struct ShortcutBinding
    {
        public KeyCode key;
        public bool ctrl;
        public bool alt;
        public bool shift;

        public bool IsValid => key != KeyCode.None;

        /// <summary>转为可读字符串，如 "Ctrl+Shift+E"</summary>
        public override string ToString()
        {
            if (!IsValid) return "";
            var parts = new List<string>();
            if (ctrl)  parts.Add("Ctrl");
            if (alt)   parts.Add("Alt");
            if (shift) parts.Add("Shift");
            parts.Add(KeyToDisplay(key));
            return string.Join("+", parts);
        }

        /// <summary>转为 Unity MenuItem 格式，如 "%#e"</summary>
        public string ToMenuItemShortcut()
        {
            if (!IsValid) return "";
            var sb = new System.Text.StringBuilder();
            if (ctrl)  sb.Append('%');
            if (alt)   sb.Append('&');
            if (shift) sb.Append('#');
            sb.Append(char.ToLower(KeyToDisplay(key)[0]));
            return sb.ToString();
        }

        private static string KeyToDisplay(KeyCode k)
        {
            // 数字键
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9)
                return ((char)('0' + (k - KeyCode.Alpha0))).ToString();
            // 字母键
            if (k >= KeyCode.A && k <= KeyCode.Z)
                return ((char)('A' + (k - KeyCode.A))).ToString();
            // 功能键
            if (k >= KeyCode.F1 && k <= KeyCode.F12)
                return $"F{(int)(k - KeyCode.F1 + 1)}";
            // 常用特殊键
            return k switch
            {
                KeyCode.Space        => "Space",
                KeyCode.Return       => "Enter",
                KeyCode.Escape       => "Esc",
                KeyCode.Tab          => "Tab",
                KeyCode.Backspace    => "Bksp",
                KeyCode.Delete       => "Del",
                KeyCode.UpArrow      => "↑",
                KeyCode.DownArrow    => "↓",
                KeyCode.LeftArrow    => "←",
                KeyCode.RightArrow   => "→",
                KeyCode.KeypadEnter  => "NumEnter",
                KeyCode.KeypadPlus   => "Num+",
                KeyCode.KeypadMinus  => "Num-",
                KeyCode.KeypadMultiply => "Num*",
                KeyCode.KeypadDivide => "Num/",
                KeyCode.KeypadPeriod => "Num.",
                KeyCode.Comma        => ",",
                KeyCode.Period       => ".",
                KeyCode.Slash        => "/",
                KeyCode.Semicolon    => ";",
                KeyCode.Quote        => "'",
                KeyCode.LeftBracket  => "[",
                KeyCode.RightBracket => "]",
                KeyCode.BackQuote    => "`",
                KeyCode.Minus        => "-",
                KeyCode.Equals       => "=",
                KeyCode.Backslash    => "\\",
                _                    => k.ToString()
            };
        }

        /// <summary>从可读字符串解析，如 "Ctrl+Shift+E"</summary>
        public static ShortcutBinding Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return default;
            var result = new ShortcutBinding();
            var parts = s.Split('+');
            foreach (var raw in parts)
            {
                var trimmed = raw.Trim();
                if (trimmed.Equals("Ctrl",  StringComparison.OrdinalIgnoreCase)) { result.ctrl  = true; continue; }
                if (trimmed.Equals("Alt",   StringComparison.OrdinalIgnoreCase)) { result.alt   = true; continue; }
                if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase)) { result.shift = true; continue; }
                // 尝试匹配 KeyCode
                if (TryParseKey(trimmed, out var kc)) result.key = kc;
            }
            return result;
        }

        /// <summary>从 MenuItem 格式解析，如 "%#e"</summary>
        public static ShortcutBinding ParseMenuItem(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return default;
            var result = new ShortcutBinding();
            foreach (char c in shortcut)
            {
                switch (c)
                {
                    case '%': result.ctrl  = true; break;
                    case '&': result.alt   = true; break;
                    case '#': result.shift = true; break;
                    case '_': break; // MenuItem 分隔符
                    default:
                        if (char.IsLetter(c))
                        {
                            var upper = char.ToUpper(c);
                            result.key = (KeyCode)System.Enum.Parse(typeof(KeyCode), upper.ToString());
                        }
                        break;
                }
            }
            return result;
        }

        private static bool TryParseKey(string s, out KeyCode key)
        {
            // 先尝试直接解析
            if (System.Enum.TryParse(s, true, out key)) return true;

            // 反向映射显示名 → KeyCode
            key = s.ToLowerInvariant() switch
            {
                "↑" or "up"          => KeyCode.UpArrow,
                "↓" or "down"        => KeyCode.DownArrow,
                "←" or "left"        => KeyCode.LeftArrow,
                "→" or "right"       => KeyCode.RightArrow,
                "enter" or "return"  => KeyCode.Return,
                "esc"                => KeyCode.Escape,
                "bksp"               => KeyCode.Backspace,
                "del"                => KeyCode.Delete,
                "space"              => KeyCode.Space,
                "tab"                => KeyCode.Tab,
                "num+"               => KeyCode.KeypadPlus,
                "num-"               => KeyCode.KeypadMinus,
                "num*"               => KeyCode.KeypadMultiply,
                "num/"               => KeyCode.KeypadDivide,
                "num."               => KeyCode.KeypadPeriod,
                "numenter"           => KeyCode.KeypadEnter,
                _                    => KeyCode.None
            };
            return key != KeyCode.None;
        }

        /// <summary>从 Event 构建快捷键绑定</summary>
        public static ShortcutBinding FromEvent(Event e)
        {
            if (e == null || e.type != EventType.KeyDown) return default;
            if (e.keyCode == KeyCode.None) return default;

            // 排除纯修饰键
            if (e.keyCode == KeyCode.LeftControl  || e.keyCode == KeyCode.RightControl ||
                e.keyCode == KeyCode.LeftShift    || e.keyCode == KeyCode.RightShift   ||
                e.keyCode == KeyCode.LeftAlt      || e.keyCode == KeyCode.RightAlt     ||
                e.keyCode == KeyCode.LeftCommand  || e.keyCode == KeyCode.RightCommand ||
                e.keyCode == KeyCode.LeftWindows  || e.keyCode == KeyCode.RightWindows)
                return default;

            return new ShortcutBinding
            {
                key   = e.keyCode,
                ctrl  = e.control,
                alt   = e.alt,
                shift = e.shift
            };
        }

        public override bool Equals(object obj)
        {
            if (obj is ShortcutBinding other)
                return key == other.key && ctrl == other.ctrl && alt == other.alt && shift == other.shift;
            return false;
        }

        public override int GetHashCode() => HashCode.Combine(key, ctrl, alt, shift);

        /// <summary>获取单个键的显示名（公开静态版本）</summary>
        public static string KeyDisplay(KeyCode k) => KeyToDisplay(k);
    }
}
#endif
