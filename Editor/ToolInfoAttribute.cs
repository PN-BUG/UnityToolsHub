using System;

/// <summary>
/// 工具信息特性 —— 标记在 EditorWindow 类上，Hub 面板自动发现并展示。
/// 
/// 使用方式：在你的 EditorWindow 类上方添加 [ToolInfo] 特性即可。
/// Hub 会在启动时扫描所有程序集，发现带有此特性的类并自动归类显示。
/// 
/// 示例：
/// [ToolInfo("帧动画创建", "媒体工具", Description = "逐帧动画创建工具", Icon = "🎞", Tags = new[] { "动画" })]
/// public class FrameAnimationCreator : EditorWindow { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ToolInfoAttribute : Attribute
{
    /// <summary>工具显示名称（必填）</summary>
    public string Name { get; }

    /// <summary>所属分类名称（必填），相同名称的工具自动归入同一分类</summary>
    public string Category { get; }

    /// <summary>功能描述（必填），显示在右侧详情面板</summary>
    public string Description { get; set; }

    /// <summary>工具图标（Emoji 或 BMP 安全字符，默认 "⚙"）</summary>
    public string Icon { get; set; } = "⚙";

    /// <summary>搜索标签（可选），用于搜索过滤</summary>
    public string[] Tags { get; set; }

    /// <summary>快捷键提示（可选），如 "Ctrl+Shift+E"</summary>
    public string Shortcut { get; set; }

    /// <summary>排序优先级（可选），数字越小越靠前，默认 0</summary>
    public int Priority { get; set; }

    public ToolInfoAttribute(string name, string category)
    {
        Name = name;
        Category = category;
    }
}
