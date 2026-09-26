using System;

/// <summary>
/// Declares metadata used by Unity Tools Hub to discover and present an editor tool.
/// The attribute intentionally has no Unity, Nodin, or UnityFramework dependency.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ToolInfoAttribute : Attribute
{
    /// <summary>Tool display name.</summary>
    public string Name { get; }

    /// <summary>Category shown in Unity Tools Hub.</summary>
    public string Category { get; }

    /// <summary>Description shown in the details panel.</summary>
    public string Description { get; set; }

    /// <summary>Emoji or other BMP-safe icon text.</summary>
    public string Icon { get; set; } = "⚙";

    /// <summary>Optional search tags.</summary>
    public string[] Tags { get; set; }

    /// <summary>Optional shortcut hint, for example Ctrl+Shift+E.</summary>
    public string Shortcut { get; set; }

    /// <summary>Lower values are displayed first.</summary>
    public int Priority { get; set; }

    /// <summary>Optional tool author.</summary>
    public string Author { get; set; } = "";

    /// <summary>Optional author or project URL.</summary>
    public string AuthorLink { get; set; } = "";

    /// <summary>
    /// Whether this is a third-party tool. Third-party tools are disabled by
    /// default until the user enables them in Unity Tools Hub.
    /// </summary>
    public bool IsThirdParty { get; set; }

    public ToolInfoAttribute(string name, string category)
    {
        Name = name;
        Category = category;
    }
}
