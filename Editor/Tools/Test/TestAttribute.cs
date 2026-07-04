using System;

/// <summary>
/// 测试标记特性 —— 标记在方法或字段上，在测试窗口中统一展示。
/// 
/// 使用方式：
///   [Test("创建存档")] public void CreateSaveData() { ... }   // 方法 → 面板显示按钮
///   [Test("玩家速度")] public float speed = 5f;                 // 字段 → 面板显示可编辑值
/// 
/// 所有标记了 [Test] 的成员会聚合到测试窗口中（快捷键 Ctrl+T 打开/关闭）。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false)]
public class TestAttribute : Attribute
{
    /// <summary>显示名称</summary>
    public string Name { get; }

    public TestAttribute(string name)
    {
        Name = name;
    }
}
