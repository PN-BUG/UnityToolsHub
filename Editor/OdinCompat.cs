// ═══════════════════════════════════════════════════════════════
//  Odin Inspector 兼容层
//  当 Odin Inspector 未安装时，提供空特性占位，保证代码编译通过。
//  实际渲染由各工具的 OnGUI() 回退逻辑负责。
// ═══════════════════════════════════════════════════════════════

#if !ODIN_INSPECTOR
using System;

namespace UnityToolsHubCompat
{
    // ── 分组 ──────────────────────────────────────────────
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

    // ── 标签与显示 ────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class LabelTextAttribute : Attribute
    {
        public LabelTextAttribute(string _) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ShowInInspectorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class HideLabelAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MultiLinePropertyAttribute : Attribute
    {
        public MultiLinePropertyAttribute(int _) { }
    }

    // ── 路径 ──────────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class FolderPathAttribute : Attribute
    {
        public bool AbsolutePath { get; set; }
    }

    // ── 下拉 ──────────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ValueDropdownAttribute : Attribute
    {
        public ValueDropdownAttribute(string _) { }
    }

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

    // ── 按钮与颜色 ────────────────────────────────────────
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

    // ── 事件回调 ──────────────────────────────────────────
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class OnValueChangedAttribute : Attribute
    {
        public OnValueChangedAttribute(string _) { }
    }
}
#endif
