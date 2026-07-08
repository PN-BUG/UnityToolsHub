#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>
/// Addressable 分组下拉选择特性
/// 标记在 string 字段上，Inspector 中显示为 Addressable 分组下拉框。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class AddressableGroupDropdownAttribute : PropertyAttribute
{
    /// <summary>是否在下拉列表中包含 "（默认分组）" 选项</summary>
    public bool IncludeDefault { get; set; } = true;
}
#endif
