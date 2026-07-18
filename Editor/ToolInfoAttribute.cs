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
///
/// ── 第三方工具规范 ──────────────────────────────────────────
///
/// 标记方式：
/// [ToolInfo("工具名", "分类",
///     IsThirdParty = true,
///     Author = "作者名",
///     AuthorLink = "https://github.com/yourname")]
/// public class MyTool : EditorWindow { ... }
///
/// 安全模型：
/// • 第三方工具（IsThirdParty = true）默认在 Hub 中禁用
/// • 禁用状态下工具不出现在分类列表中，无法通过 Hub 打开
/// • 需在 Hub → 第三方工具管理面板中手动启用
/// • 启用后工具正常显示并可使用
/// • 已启用的工具可随时禁用
///
/// 推荐做法：
/// • 第三方工具应明确标注 Author 和 AuthorLink
/// • AuthorLink 指向工具的主页/仓库/联系方式
/// • 第三方工具的代码应允许公开审查
///
/// ── 第三方工具包目录结构（Git / 本地 UPM 包）────────────────
///
/// MyToolPackage/
/// ├── package.json          # UPM 包描述文件
/// ├── Editor/
/// │   └── MyTool.cs          # 含 [ToolInfo(IsThirdParty=true)] 的工具脚本
/// └── Runtime/               # 可选运行时代码
///
/// 导入方式：
/// 1. Git URL: 管理面板 → 从 Git 导入 → 输入 URL
/// 2. 本地路径: 管理面板 → 从本地导入 → 选择含 package.json 的文件夹
/// 3. 手动添加: 在 .cs 文件上添加 [ToolInfo(IsThirdParty=true)] 特性
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

    /// <summary>工具作者（可选），显示在工具详情页和第三方工具管理面板</summary>
    public string Author { get; set; } = "";

    /// <summary>作者链接/主页 URL（可选），点击可跳转</summary>
    public string AuthorLink { get; set; } = "";

    /// <summary>
    /// 是否为第三方工具（非官方），默认 false。
    /// 第三方工具在 Hub 中默认禁用（不显示在分类列表、无法打开），
    /// 需在「第三方工具管理」面板中手动启用。
    /// </summary>
    public bool IsThirdParty { get; set; } = false;

    public ToolInfoAttribute(string name, string category)
    {
        Name = name;
        Category = category;
    }
}
