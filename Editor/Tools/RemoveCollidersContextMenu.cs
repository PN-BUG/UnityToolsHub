using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RemoveCollidersContextMenu
{
    private const string SelectedMenu = "GameObject/UnityToolsHub/去除碰撞器/仅选中物体";
    private const string RecursiveMenu = "GameObject/UnityToolsHub/去除碰撞器/递归子物体";

    [MenuItem(SelectedMenu, false, 2000)]
    private static void RemoveFromSelectedObjects()
    {
        RemoveColliders(false);
    }

    [MenuItem(RecursiveMenu, false, 2001)]
    private static void RemoveFromSelectedObjectsAndChildren()
    {
        RemoveColliders(true);
    }

    [MenuItem(SelectedMenu, true)]
    [MenuItem(RecursiveMenu, true)]
    private static bool ValidateSelection()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode && GetSelectedObjects().Length > 0;
    }

    private static GameObject[] GetSelectedObjects()
    {
        return Selection.GetFiltered<GameObject>(SelectionMode.Editable | SelectionMode.ExcludePrefab);
    }

    private static void RemoveColliders(bool includeChildren)
    {
        if (!ValidateSelection())
            return;

        // Collect first so overlapping parent/child selections remove each component only once.
        var colliders = new HashSet<Component>();
        foreach (GameObject gameObject in GetSelectedObjects())
        {
            foreach (Collider collider in includeChildren
                         ? gameObject.GetComponentsInChildren<Collider>(true)
                         : gameObject.GetComponents<Collider>())
                colliders.Add(collider);

            foreach (Collider2D collider in includeChildren
                         ? gameObject.GetComponentsInChildren<Collider2D>(true)
                         : gameObject.GetComponents<Collider2D>())
                colliders.Add(collider);
        }

        if (colliders.Count == 0)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(includeChildren ? "递归去除碰撞器" : "去除选中物体碰撞器");
        try
        {
            foreach (Component collider in colliders)
            {
                if (collider != null)
                    Undo.DestroyObjectImmediate(collider);
            }
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }
    }
}
