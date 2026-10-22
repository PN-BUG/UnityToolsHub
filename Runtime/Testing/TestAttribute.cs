using System;

/// <summary>
/// 将方法、字段或属性加入测试窗口；可选布局参数只控制工具展示，不参与业务执行。
/// 未填写分组时沿用组件内的字段、属性、方法展示方式。
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property,
    AllowMultiple = false)]
public class TestAttribute : Attribute
{
    #region 展示配置

    /// <summary>测试窗口显示的成员名称。</summary>
    public string Name { get; }

    /// <summary>组件内部的业务分组名称；空字符串表示不建立业务折叠。</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>业务分组的显示顺序，较小的值优先；同一分组的全部成员必须填写一致。</summary>
    public int GroupOrder { get; set; }

    /// <summary>成员在业务分组中的显示顺序，可用于安排输入、按钮和只读结果。</summary>
    public int Order { get; set; }

    /// <summary>业务分组是否允许折叠；共用输入可设为 false，使其在组件展开时持续显示。</summary>
    public bool IsFoldout { get; set; } = true;

    /// <summary>业务折叠首次出现时是否展开；用户操作后及窗口刷新时使用已记录的状态。</summary>
    public bool ExpandedByDefault { get; set; }

    #endregion

    #region 初始化

    /// <summary>声明一个测试入口或输入；保持原有单参数调用方式。</summary>
    /// <param name="name">窗口显示名称，分组及排序通过可选命名参数提供。</param>
    public TestAttribute(string name)
    {
        // 1. 这里只记录展示元数据，测试窗口负责布局并调用原成员。
        Name = name;
    }

    #endregion
}
