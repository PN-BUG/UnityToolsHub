using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.UI;

public static class CreateLegacyUIMenu
{
    // ========= 一级菜单：Legacy Text =========
    [MenuItem("GameObject/UI/Text (Legacy)", false, 2031)]
    private static void CreateLegacyText(MenuCommand menuCommand)
    {
        GameObject go = DefaultControls.CreateText(GetStandardResources());
        go.name = "Text";
        PlaceUIElementRoot(go, menuCommand);
    }

    // ========= 一级菜单：Legacy Button =========
    [MenuItem("GameObject/UI/Button (Legacy)", false, 2032)]
    private static void CreateLegacyButton(MenuCommand menuCommand)
    {
        GameObject go = DefaultControls.CreateButton(GetStandardResources());
        go.name = "Button";
        PlaceUIElementRoot(go, menuCommand);
    }

    // ========= 一级菜单：Legacy Dropdown =========
    [MenuItem("GameObject/UI/Dropdown (Legacy)", false, 2033)]
    private static void CreateLegacyDropdown(MenuCommand menuCommand)
    {
        GameObject go = DefaultControls.CreateDropdown(GetStandardResources());
        go.name = "Dropdown";
        PlaceUIElementRoot(go, menuCommand);
    }

    // ========= 一级菜单：Legacy InputField =========
    [MenuItem("GameObject/UI/Input Field (Legacy)", false, 2034)]
    private static void CreateLegacyInputField(MenuCommand menuCommand)
    {
        GameObject go = DefaultControls.CreateInputField(GetStandardResources());
        go.name = "Input Field";
        PlaceUIElementRoot(go, menuCommand);
    }

    // ========= 公共方法 =========
    private static DefaultControls.Resources GetStandardResources()
    {
        return new DefaultControls.Resources();
    }

    private static void PlaceUIElementRoot(GameObject element, MenuCommand menuCommand)
    {
        GameObject parent = menuCommand.context as GameObject;

        if (parent == null || parent.GetComponentInParent<Canvas>() == null)
        {
            parent = GetOrCreateCanvasGameObject();
        }

        Undo.RegisterCreatedObjectUndo(element, "Create " + element.name);
        GameObjectUtility.SetParentAndAlign(element, parent);

        // 确保 RectTransform 显示正常
        RectTransform rectTransform = element.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.localPosition = Vector3.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }

        Selection.activeGameObject = element;
    }

    private static GameObject GetOrCreateCanvasGameObject()
    {
        Canvas canvas = FindHelper.FindAnyObject<Canvas>();
        if (canvas != null)
            return canvas.gameObject;

        GameObject canvasGO = new GameObject("Canvas");
        canvasGO.layer = LayerMask.NameToLayer("UI");

        Canvas c = canvasGO.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");

        if (FindHelper.FindAnyObject<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject(
                "EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule)
            );
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        return canvasGO;
    }
}