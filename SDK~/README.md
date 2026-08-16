# Unity Tools Hub SDK

This package only declares metadata. It has no dependency on Unity Tools Hub and does not open or own your window. Keep the tool's existing `MenuItem` so it remains standalone when the Hub is absent.

```csharp
using UnityEditor;
using UnityToolsHub.SDK;

[UnityTool("Sprite Tool", "Art Tools", Description = "Processes sprites in batches")]
public sealed class SpriteToolWindow : EditorWindow
{
    [MenuItem("Tools/Example/Sprite Tool")]
    public static void OpenStandalone() => GetWindow<SpriteToolWindow>();
}
```

For a menu-only tool, set `EntryKind = "menu"` and `MenuItem`. For a static launcher, set `EntryKind = "static"` and `StaticMethod = "Namespace.Type.Method"`.
