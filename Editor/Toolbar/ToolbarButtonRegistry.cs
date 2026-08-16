#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace Zko.UnityToolsHub.Toolbar
{
    internal sealed class ToolbarButtonDescriptor
    {
        public MethodInfo Method;
        public ToolbarButtonAttribute Attribute;
        public string Id;
        public bool IsValid;
        public string ValidationMessage;
    }

    [InitializeOnLoad]
    internal static class ToolbarButtonRegistry
    {
        private const string ConfigKey = "UnityToolsHub.ToolbarButtons.Config";
        private static List<ToolbarButtonDescriptor> _buttons;
        private static ToolbarButtonConfig _config;
        public static event Action Changed;
        public static IReadOnlyList<ToolbarButtonDescriptor> Buttons => _buttons;

        static ToolbarButtonRegistry()
        {
            Refresh();
            ToolbarExtender.LeftToolbarGUI.Remove(DrawLeft);
            ToolbarExtender.RightToolbarGUI.Remove(DrawRight);
            ToolbarExtender.LeftToolbarGUI.Add(DrawLeft);
            ToolbarExtender.RightToolbarGUI.Add(DrawRight);
        }

        public static void Refresh()
        {
            LoadConfig();
            _buttons = TypeCache.GetMethodsWithAttribute<ToolbarButtonAttribute>().Select(CreateDescriptor)
                .OrderBy(GetSide).ThenBy(GetOrder).ThenBy(b => b.Attribute.Text, StringComparer.OrdinalIgnoreCase).ToList();
            Changed?.Invoke();
        }

        public static bool IsEnabled(ToolbarButtonDescriptor b) { var s = _config.Find(b.Id); return s != null ? s.Enabled : b.Attribute.DefaultEnabled; }
        public static ToolbarSide GetSide(ToolbarButtonDescriptor b) { var s = _config.Find(b.Id); return s != null ? s.Side : b.Attribute.Side; }
        public static int GetOrder(ToolbarButtonDescriptor b) { var s = _config.Find(b.Id); return s != null ? s.Order : b.Attribute.Order; }
        public static void SetEnabled(ToolbarButtonDescriptor b, bool value) { GetState(b).Enabled = value; Save(); }
        public static void SetSide(ToolbarButtonDescriptor b, ToolbarSide value) { GetState(b).Side = value; Save(); }

        public static void Move(ToolbarButtonDescriptor b, int delta)
        {
            var list = _buttons.Where(x => GetSide(x) == GetSide(b)).OrderBy(GetOrder).ToList();
            int index = list.IndexOf(b), target = Mathf.Clamp(index + delta, 0, list.Count - 1);
            if (index == target) return;
            for (int i = 0; i < list.Count; i++) GetState(list[i]).Order = i;
            var a = GetState(b); var other = GetState(list[target]); int order = a.Order; a.Order = other.Order; other.Order = order;
            Save();
        }

        public static void Reset() { _config = new ToolbarButtonConfig(); EditorPrefs.DeleteKey(ConfigKey); Refresh(); RepaintToolbar(); }

        private static ToolbarButtonDescriptor CreateDescriptor(MethodInfo method)
        {
            bool valid = method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
            return new ToolbarButtonDescriptor {
                Method = method, Attribute = method.GetCustomAttribute<ToolbarButtonAttribute>(),
                Id = method.DeclaringType.AssemblyQualifiedName + "::" + method.Name, IsValid = valid,
                ValidationMessage = valid ? null : "方法必须是无参数、返回 void 的静态方法"
            };
        }

        private static void DrawLeft() { Draw(ToolbarSide.Left); }
        private static void DrawRight() { Draw(ToolbarSide.Right); }
        private static void Draw(ToolbarSide side)
        {
            foreach (var b in _buttons)
            {
                if (!b.IsValid || !IsEnabled(b) || GetSide(b) != side) continue;
                var content = new GUIContent(b.Attribute.Text, b.Attribute.Tooltip);
                GUIStyle style = EditorStyles.toolbarButton;
                float width = b.Attribute.Width > 0f
                    ? b.Attribute.Width
                    : Mathf.Max(24f, Mathf.Ceil(style.CalcSize(content).x + 6f));
                bool clicked = GUILayout.Button(content, style, GUILayout.Width(width));
                if (!clicked) continue;
                try { b.Method.Invoke(null, null); }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static ToolbarButtonState GetState(ToolbarButtonDescriptor b)
        {
            var state = _config.Find(b.Id);
            if (state != null) return state;
            state = new ToolbarButtonState { Id = b.Id, Enabled = b.Attribute.DefaultEnabled, Side = b.Attribute.Side, Order = b.Attribute.Order };
            _config.Items.Add(state); return state;
        }
        private static void LoadConfig()
        {
            try { string json = EditorPrefs.GetString(ConfigKey, ""); _config = string.IsNullOrEmpty(json) ? new ToolbarButtonConfig() : JsonUtility.FromJson<ToolbarButtonConfig>(json) ?? new ToolbarButtonConfig(); }
            catch { _config = new ToolbarButtonConfig(); }
        }
        private static void Save() { EditorPrefs.SetString(ConfigKey, JsonUtility.ToJson(_config)); Refresh(); RepaintToolbar(); }
        private static void RepaintToolbar() { UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }

        [Serializable] private sealed class ToolbarButtonConfig
        {
            public List<ToolbarButtonState> Items = new List<ToolbarButtonState>();
            public ToolbarButtonState Find(string id) => Items.Find(x => x.Id == id);
        }
        [Serializable] private sealed class ToolbarButtonState { public string Id; public bool Enabled; public ToolbarSide Side; public int Order; }
    }
}
#endif
