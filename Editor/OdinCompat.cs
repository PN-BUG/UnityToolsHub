// ═══════════════════════════════════════════════════════════════
//  Odin Inspector 兼容层（命名空间伪装）
//
//  无 Odin 时在 Sirenix.OdinInspector 命名空间中提供空桩，
//  使业务代码只需 using Sirenix.OdinInspector; 即可编译，
//  有 Odin 时由真实程序集提供，绘制器自动生效。
//
//  业务代码 —— 零 #if：
//
//    using Sirenix.OdinInspector;
//    using Sirenix.OdinInspector.Editor;
//
//    public class MyTool : OdinEditorWindow
//    {
//        [FoldoutGroup("xxx")]
//        [ShowIf("zzz")]
//        public float myField;
//    }
//
//  唯一需要 #if 的场景：
//    OnGUI 原生回退（#if !ODIN_INSPECTOR）
// ═══════════════════════════════════════════════════════════════

#if !ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sirenix.OdinInspector
{
    // 统一目标：Field | Property | Method
    // Odin 真实属性大多同时支持三者，桩也保持一致以避免 CS0592

    // ── 分组 ──────────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class FoldoutGroupAttribute : Attribute
    {
        public int Order { get; set; }
        public FoldoutGroupAttribute(string _) { }
        public FoldoutGroupAttribute(string _, bool expanded = false) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class BoxGroupAttribute : Attribute
    {
        public BoxGroupAttribute(string _) { }
    }

    // ── 标签与显示 ────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class LabelTextAttribute : Attribute
    {
        public LabelTextAttribute(string _) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class HideLabelAttribute : Attribute { }

    // ── 下拉 ──────────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ValueDropdownAttribute : Attribute
    {
        public ValueDropdownAttribute(string _) { }
    }

    // ── 按钮与颜色 ────────────────────────────────────────
    public enum ButtonSizes { Small, Medium, Large }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class ButtonAttribute : Attribute
    {
        public ButtonAttribute() { }
        public ButtonAttribute(string _) { }
        public ButtonAttribute(ButtonSizes _) { }
        public ButtonAttribute(string _, ButtonSizes __) { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class GUIColorAttribute : Attribute
    {
        public GUIColorAttribute(float r, float g, float b) { }
        public GUIColorAttribute(string _) { }
    }

    // ── 条件显示 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ShowIfAttribute : Attribute
    {
        public ShowIfAttribute(string _) { }
        public ShowIfAttribute(string _, object __) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class HideIfAttribute : Attribute
    {
        public HideIfAttribute(string _) { }
        public HideIfAttribute(string _, object __) { }
    }

    // ── 条件启用 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class EnableIfAttribute : Attribute
    {
        public EnableIfAttribute(string _) { }
        public EnableIfAttribute(string _, object __) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class DisableIfAttribute : Attribute
    {
        public DisableIfAttribute(string _) { }
        public DisableIfAttribute(string _, object __) { }
    }

    // ── Inspector GUI 回调 ─────────────────────────────────
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class OnInspectorGUIAttribute : Attribute
    {
        public OnInspectorGUIAttribute() { }
        public OnInspectorGUIAttribute(string _) { }
    }

    // ── 只读 / 显示 ──────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ShowInInspectorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class MultiLinePropertyAttribute : Attribute
    {
        public MultiLinePropertyAttribute(int _) { }
    }

    // ── 路径与资源 ────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class FolderPathAttribute : Attribute
    {
        public bool AbsolutePath { get; set; }
        public bool RequireExistingPath { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class AssetsOnlyAttribute : Attribute { }

    // ── 事件回调 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class OnValueChangedAttribute : Attribute
    {
        public OnValueChangedAttribute(string _) { }
    }

    // ── 列表绘制 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ListDrawerSettingsAttribute : Attribute
    {
        public bool ShowFoldout { get; set; } = true;
        public bool DraggableItems { get; set; } = true;
        public int NumberOfItemsPerPage { get; set; }
        public bool ShowIndexLabels { get; set; }
        public bool HideAddButton { get; set; }
    }

    // ── 信息提示 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = true)]
    public class InfoBoxAttribute : Attribute
    {
        public InfoBoxAttribute(string _) { }
        public InfoBoxAttribute(string _, object __) { }
        public InfoBoxAttribute(string _, object __, string ___) { }
    }

    /// <summary>信息提示类型（与 Odin InfoMessageType 对应）</summary>
    public enum InfoMessageType { None, Info, Warning, Error }
}

namespace Sirenix.OdinInspector.Editor
{
    /// <summary>
    /// OdinEditorWindow 桩 —— 无 Odin 时退化为普通 EditorWindow。
    /// 提供 virtual OnEnable/OnDisable 以匹配 Odin 真实 API，
    /// 使业务代码 protected override void OnEnable() 可编译。
    /// </summary>
    public class OdinEditorWindow : UnityEditor.EditorWindow
    {
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
    }

    /// <summary>ValueDropdownItem 桩</summary>
    public struct ValueDropdownItem<T>
    {
        public string Text { get; }
        public T Value { get; }
        public ValueDropdownItem(string text, T value) { Text = text; Value = value; }
    }

    /// <summary>ValueDropdownList 桩 —— 支持 { "name", value } 集合初始化器</summary>
    public class ValueDropdownList<T> : List<ValueDropdownItem<T>>
    {
        public void Add(string name, T value) => Add(new ValueDropdownItem<T>(name, value));
    }
}
#endif
