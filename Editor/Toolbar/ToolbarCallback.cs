#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace Zko.UnityToolsHub.Toolbar
{
    [InitializeOnLoad]
    internal static class ToolbarCallback
    {
        private static readonly Type ToolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        private static ScriptableObject _currentToolbar;
        static ToolbarCallback() { EditorApplication.update -= OnUpdate; EditorApplication.update += OnUpdate; }
        private static void OnUpdate()
        {
            if (_currentToolbar != null || ToolbarType == null) return;
            UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(ToolbarType);
            _currentToolbar = toolbars.Length > 0 ? toolbars[0] as ScriptableObject : null;
            if (_currentToolbar == null) return;
            FieldInfo field = ToolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
            var root = field?.GetValue(_currentToolbar) as VisualElement;
            if (root == null) return;
            Register(root, "ToolbarZoneLeftAlign", ToolbarExtender.LeftToolbarGUI);
            Register(root, "ToolbarZoneRightAlign", ToolbarExtender.RightToolbarGUI);
        }
        private static void Register(VisualElement root, string zoneName, List<Action> callbacks)
        {
            VisualElement zone = root.Q(zoneName);
            if (zone == null) return;
            var container = new IMGUIContainer(() =>
            {
                GUILayout.BeginHorizontal();
                foreach (Action callback in callbacks.ToArray()) callback?.Invoke();
                GUILayout.EndHorizontal();
            });
            container.style.flexGrow = 1;
            zone.Add(container);
        }
    }
}
#endif
