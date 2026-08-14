using System;

namespace UnityToolsHub.SDK
{
    /// <summary>Optional metadata. The SDK never opens or owns the annotated tool.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class UnityToolAttribute : Attribute
    {
        public string Name { get; }
        public string Category { get; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public string[] Tags { get; set; }
        public int Priority { get; set; }
        public string Author { get; set; }
        public string AuthorLink { get; set; }
        public string EntryKind { get; set; } = "window";
        public string MenuItem { get; set; }
        public string StaticMethod { get; set; }

        public UnityToolAttribute(string name, string category)
        {
            Name = name;
            Category = category;
        }
    }
}
