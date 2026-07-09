// ═══════════════════════════════════════════════════════════════
//  Nodin — Editor 桩类型
//  NodinEditorWindow / NodinEditor / ValueDropdown 辅助类型
// ═══════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Nodin;

namespace Nodin.Editor
{
    /// <summary>
    /// NodinEditorWindow 桩 —— 通过反射自动绘制 Inspector。
    /// 子类无需手写 OnGUI，OnEnable 中自动初始化绘制器。
    /// </summary>
    public class NodinEditorWindow : EditorWindow
    {
        private NodinDrawer _drawer;

        protected virtual void OnEnable()
        {
            _drawer = new NodinDrawer(this);
        }

        protected virtual void OnDisable() { }

        private void OnGUI()
        {
            _drawer?.Draw();
        }
    }

    /// <summary>ValueDropdownItem 桩</summary>
    public struct ValueDropdownItem<T>
    {
        public string Text { get; }
        public T Value { get; }
        public ValueDropdownItem(string text, T value) { Text = text; Value = value; }
    }

    /// <summary>ValueDropdownList 桩</summary>
    public class ValueDropdownList<T> : List<ValueDropdownItem<T>>
    {
        public void Add(string name, T value) => Add(new ValueDropdownItem<T>(name, value));
    }

    /// <summary>
    /// 通用 ScriptableObject 编辑器桩。
    /// 无 Odin 时通过 NodinDrawer 反射自动绘制 Inspector。
    /// </summary>
    [CustomEditor(typeof(ScriptableObject), true)]
    public class NodinEditor : UnityEditor.Editor
    {
        private NodinDrawer _drawer;

        private void OnEnable()
        {
            _drawer = new NodinDrawer(target);
        }

        public override void OnInspectorGUI()
        {
            _drawer?.Draw();
        }
    }
}
