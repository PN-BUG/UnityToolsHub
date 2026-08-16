#if UNITY_EDITOR
using System;
namespace Zko.UnityToolsHub.Toolbar
{
    public enum ToolbarSide { Left, Right }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ToolbarButtonAttribute : Attribute
    {
        public string Text { get; }
        public string Tooltip { get; set; }
        public ToolbarSide Side { get; set; } = ToolbarSide.Left;
        public int Order { get; set; }
        /// <summary>按钮固定宽度；小于等于 0 时根据 Text 自动计算。</summary>
        public float Width { get; set; }
        public bool DefaultEnabled { get; set; } = true;
        public ToolbarButtonAttribute(string text) { Text = text; }
    }
}
#endif
