// ═══════════════════════════════════════════════════════════════
//  Nodin — No Odin Inspector
//  轻量级属性定义，提供与 Odin Inspector 兼容的特性集合
//
//  所有特性使用 Nodin 命名空间。
// ═══════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace Nodin
{
    // ══════════════════════════════════════════════════════════
    //  分组 & 布局
    // ══════════════════════════════════════════════════════════

    /// <summary>将字段/方法归入可折叠分组，支持 '/' 分隔的子分组</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class FoldoutGroupAttribute : Attribute
    {
        public string GroupName { get; }
        public bool Expanded { get; }
        public int Order { get; set; }

        public FoldoutGroupAttribute(string groupName)
        {
            GroupName = groupName;
        }

        public FoldoutGroupAttribute(string groupName, bool expanded = false)
        {
            GroupName = groupName;
            Expanded = expanded;
        }
    }

    /// <summary>将字段归入带标题的盒子分组</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class BoxGroupAttribute : Attribute
    {
        public string GroupName { get; }
        public BoxGroupAttribute(string groupName) { GroupName = groupName; }
    }

    // ══════════════════════════════════════════════════════════
    //  标签 & 显示
    // ══════════════════════════════════════════════════════════

    /// <summary>自定义 Inspector 中显示的标签文本</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class LabelTextAttribute : Attribute
    {
        public string Text { get; }
        public LabelTextAttribute(string text) { Text = text; }
    }

    /// <summary>隐藏字段标签，仅显示值控件</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class HideLabelAttribute : Attribute { }

    /// <summary>在字段上方显示信息提示框</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = true)]
    public class InfoBoxAttribute : Attribute
    {
        public string Message { get; }
        public InfoMessageType Type { get; }
        public string VisibleIfMemberName { get; }

        public InfoBoxAttribute(string message)
        {
            Message = message;
            Type = InfoMessageType.Info;
            VisibleIfMemberName = null;
        }

        public InfoBoxAttribute(string message, object type)
        {
            Message = message;
            Type = (InfoMessageType)type;
            VisibleIfMemberName = null;
        }

        public InfoBoxAttribute(string message, object type, string visibleIfMemberName)
        {
            Message = message;
            Type = (InfoMessageType)type;
            VisibleIfMemberName = visibleIfMemberName;
        }
    }

    /// <summary>信息提示类型</summary>
    public enum InfoMessageType { None, Info, Warning, Error }

    /// <summary>将字符串字段绘制为多行文本区域</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class MultiLinePropertyAttribute : Attribute
    {
        public int Lines { get; }
        public MultiLinePropertyAttribute(int lines) { Lines = lines; }
    }

    // ══════════════════════════════════════════════════════════
    //  条件显示 & 启用
    // ══════════════════════════════════════════════════════════

    /// <summary>当指定成员值等于目标值时显示字段（默认 true）</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ShowIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }

        public ShowIfAttribute(string memberName)
        {
            MemberName = memberName;
            Value = true;
        }

        public ShowIfAttribute(string memberName, object value)
        {
            MemberName = memberName;
            Value = value;
        }
    }

    /// <summary>当指定成员值等于目标值时隐藏字段（默认 true）</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class HideIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }

        public HideIfAttribute(string memberName)
        {
            MemberName = memberName;
            Value = true;
        }

        public HideIfAttribute(string memberName, object value)
        {
            MemberName = memberName;
            Value = value;
        }
    }

    /// <summary>当指定成员值等于目标值时启用字段（默认 true）</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class EnableIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }

        public EnableIfAttribute(string memberName)
        {
            MemberName = memberName;
            Value = true;
        }

        public EnableIfAttribute(string memberName, object value)
        {
            MemberName = memberName;
            Value = value;
        }
    }

    /// <summary>当指定成员值等于目标值时禁用字段（默认 true）</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class DisableIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }

        public DisableIfAttribute(string memberName)
        {
            MemberName = memberName;
            Value = true;
        }

        public DisableIfAttribute(string memberName, object value)
        {
            MemberName = memberName;
            Value = value;
        }
    }

    /// <summary>将字段标记为只读（不可编辑）</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ReadOnlyAttribute : Attribute { }

    // ══════════════════════════════════════════════════════════
    //  按钮 & 动作
    // ══════════════════════════════════════════════════════════

    /// <summary>按钮尺寸</summary>
    public enum ButtonSizes { Small, Medium, Large }

    /// <summary>将方法绘制为 Inspector 按钮</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class ButtonAttribute : Attribute
    {
        public string Name { get; }
        public ButtonSizes Size { get; }

        public ButtonAttribute() { Name = null; Size = ButtonSizes.Medium; }
        public ButtonAttribute(string name) { Name = name; Size = ButtonSizes.Medium; }
        public ButtonAttribute(ButtonSizes size) { Name = null; Size = size; }
        public ButtonAttribute(string name, ButtonSizes size) { Name = name; Size = size; }
    }

    /// <summary>设置按钮或字段的 GUI 颜色</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class GUIColorAttribute : Attribute
    {
        public Color Color { get; }

        public GUIColorAttribute(float r, float g, float b) { Color = new Color(r, g, b); }
        public GUIColorAttribute(string hex)
        {
            Color = ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }
    }

    /// <summary>在 Inspector 中插入自定义 GUI 绘制回调</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class OnInspectorGUIAttribute : Attribute
    {
        public string MethodName { get; }
        public OnInspectorGUIAttribute() { MethodName = null; }
        public OnInspectorGUIAttribute(string methodName) { MethodName = methodName; }
    }

    /// <summary>强制在 Inspector 中显示非 public 字段</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ShowInInspectorAttribute : Attribute { }

    // ══════════════════════════════════════════════════════════
    //  字段行为
    // ══════════════════════════════════════════════════════════

    /// <summary>下拉列表选项来源（方法名或字段名）</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ValueDropdownAttribute : Attribute
    {
        public string MemberName { get; }
        public ValueDropdownAttribute(string memberName) { MemberName = memberName; }
    }

    /// <summary>文件夹路径选择器</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class FolderPathAttribute : Attribute
    {
        public bool AbsolutePath { get; set; }
        public bool RequireExistingPath { get; set; }
    }

    /// <summary>限制 Object 引用仅允许 Asset（非场景对象）</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class AssetsOnlyAttribute : Attribute { }

    /// <summary>字段值改变后回调指定方法</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class OnValueChangedAttribute : Attribute
    {
        public string MethodName { get; }
        public OnValueChangedAttribute(string methodName) { MethodName = methodName; }
    }

    /// <summary>List 字段的绘制设置</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ListDrawerSettingsAttribute : Attribute
    {
        public bool ShowFoldout { get; set; } = true;
        public bool DraggableItems { get; set; } = true;
        public int NumberOfItemsPerPage { get; set; }
        public bool ShowIndexLabels { get; set; }
        public bool HideAddButton { get; set; }
    }
}
