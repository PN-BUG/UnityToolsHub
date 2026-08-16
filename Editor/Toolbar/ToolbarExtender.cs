#if UNITY_EDITOR
using System;
using System.Collections.Generic;
namespace Zko.UnityToolsHub.Toolbar
{
    public static class ToolbarExtender
    {
        public static readonly List<Action> LeftToolbarGUI = new List<Action>();
        public static readonly List<Action> RightToolbarGUI = new List<Action>();
    }
}
#endif
