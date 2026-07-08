// ═══════════════════════════════════════════════════════════════
//  Odin Inspector 兼容层
//  ┌─────────────────────────────────────────────────────────────┐
//  │ 设计目的：最小化业务代码中的 #if ODIN_INSPECTOR              │
//  │                                                             │
//  │ 【非密封类】（FoldoutGroup、LabelText、ValueDropdown 等）    │
//  │   有 Odin → 继承 Odin 原生属性，绘制器自动生效               │
//  │   无 Odin → System.Attribute 占位                           │
//  │   业务代码：using UnityToolsHubCompat; 即可                  │
//  │                                                             │
//  │ 【密封类】（ShowIf、InfoBox、FolderPath、ReadOnly 等）       │
//  │   因 sealed 无法继承，using 别名不导出                       │
//  │   业务代码需要：                                             │
//  │     using UnityToolsHubCompat;                              │
//  │     #if ODIN_INSPECTOR                                      │
//  │     using Sirenix.OdinInspector;  ← 密封类走这里            │
//  │     #endif                                                  │
//  └─────────────────────────────────────────────────────────────┘
// ═══════════════════════════════════════════════════════════════

using System;

namespace UnityToolsHubCompat
{
    // ════════════════════════════════════════════════════════
    //  非密封类 → 有 Odin 时继承，无 Odin 时占位
    //  业务代码只需 using UnityToolsHubCompat; 即可
    // ════════════════════════════════════════════════════════

    // ── 分组 ──────────────────────────────────────────────
#if ODIN_INSPECTOR
    public class FoldoutGroupAttribute : Sirenix.OdinInspector.FoldoutGroupAttribute
    {
        public FoldoutGroupAttribute(string groupName) : base(groupName) { }
    }

    public class BoxGroupAttribute : Sirenix.OdinInspector.BoxGroupAttribute
    {
        public BoxGroupAttribute(string groupName) : base(groupName) { }
    }
#else
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class FoldoutGroupAttribute : Attribute
    {
        public FoldoutGroupAttribute(string _) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class BoxGroupAttribute : Attribute
    {
        public BoxGroupAttribute(string _) { }
    }
#endif

    // ── 标签与显示 ────────────────────────────────────────
#if ODIN_INSPECTOR
    public class LabelTextAttribute : Sirenix.OdinInspector.LabelTextAttribute
    {
        public LabelTextAttribute(string text) : base(text) { }
    }

    public class HideLabelAttribute : Sirenix.OdinInspector.HideLabelAttribute { }
#else
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class LabelTextAttribute : Attribute
    {
        public LabelTextAttribute(string _) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class HideLabelAttribute : Attribute { }
#endif

    // ── 下拉 ──────────────────────────────────────────────
#if ODIN_INSPECTOR
    public class ValueDropdownAttribute : Sirenix.OdinInspector.ValueDropdownAttribute
    {
        public ValueDropdownAttribute(string valuesGetter) : base(valuesGetter) { }
    }
#else
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ValueDropdownAttribute : Attribute
    {
        public ValueDropdownAttribute(string _) { }
    }
#endif

    // ── 按钮与颜色 ────────────────────────────────────────
#if ODIN_INSPECTOR
    public enum ButtonSizes { Small, Medium, Large }

    [AttributeUsage(AttributeTargets.Method)]
    public class ButtonAttribute : Attribute
    {
        public ButtonAttribute() { }
        public ButtonAttribute(string _) { }
        public ButtonAttribute(string _, ButtonSizes __) { }
    }

    public class GUIColorAttribute : Sirenix.OdinInspector.GUIColorAttribute
    {
        public GUIColorAttribute(float r, float g, float b) : base(r, g, b) { }
        public GUIColorAttribute(string color) : base(color) { }
    }
#else
    public enum ButtonSizes { Small, Medium, Large }

    [AttributeUsage(AttributeTargets.Method)]
    public class ButtonAttribute : Attribute
    {
        public ButtonAttribute() { }
        public ButtonAttribute(string _) { }
        public ButtonAttribute(string _, ButtonSizes __) { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class GUIColorAttribute : Attribute
    {
        public GUIColorAttribute(float r, float g, float b) { }
        public GUIColorAttribute(string _) { }
    }
#endif

    // ════════════════════════════════════════════════════════════════
    //  密封类 → sealed 无法继承
    //  有 Odin 时由 Sirenix.OdinInspector 提供（绘制器生效）
    //  无 Odin 时提供空属性桩（编译通过）
    // ════════════════════════════════════════════════════════════════

#if !ODIN_INSPECTOR

    // ── 条件显示 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ShowIfAttribute : Attribute
    {
        public ShowIfAttribute(string _, object __) { }
        public ShowIfAttribute(string _) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class HideIfAttribute : Attribute
    {
        public HideIfAttribute(string _, object __) { }
        public HideIfAttribute(string _) { }
    }

    // ── 只读 / 显示 ──────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ShowInInspectorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MultiLinePropertyAttribute : Attribute
    {
        public MultiLinePropertyAttribute(int _) { }
    }

    // ── 路径与资源 ────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class FolderPathAttribute : Attribute
    {
        public bool AbsolutePath { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class AssetsOnlyAttribute : Attribute { }

    // ── 事件回调 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class OnValueChangedAttribute : Attribute
    {
        public OnValueChangedAttribute(string _) { }
    }

    // ── 列表绘制 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ListDrawerSettingsAttribute : Attribute
    {
        public bool ShowFoldout { get; set; } = true;
        public bool DraggableItems { get; set; } = true;
        public int NumberOfItemsPerPage { get; set; }
    }

    // ── 信息提示 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class InfoBoxAttribute : Attribute
    {
        public InfoBoxAttribute(string _) { }
        public InfoBoxAttribute(string _, object __) { }
        public InfoBoxAttribute(string _, object __, string ___) { }
    }

    /// <summary>信息提示类型（与 Odin InfoMessageType 对应）</summary>
    public enum InfoMessageType { None, Info, Warning, Error }
#endif

    // ════════════════════════════════════════════════════════════════
    //  使用说明
    // ════════════════════════════════════════════════════════════════
    //
    //  业务代码模板（属性行零 #if）：
    //
    //    #if ODIN_INSPECTOR
    //    using Sirenix.OdinInspector;
    //    #else
    //    using UnityToolsHubCompat;
    //    #endif
    //
    //    [FoldoutGroup("xxx")]
    //    [LabelText("yyy")]
    //    [ShowIf("zzz")]
    //    public float myField;
    //
    //  唯一需要 #if 的场景：Odin 回调方法（如 ValueDropdown 的值获取函数）
}
