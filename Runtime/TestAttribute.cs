using System;

/// <summary>
/// Marks a method, field, or property for display in the Unity Tools Hub test window.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property,
    AllowMultiple = false)]
public class TestAttribute : Attribute
{
    /// <summary>Member name displayed in the test window.</summary>
    public string Name { get; }

    public TestAttribute(string name)
    {
        Name = name;
    }
}
