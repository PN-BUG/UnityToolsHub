#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public partial class FontReplacer
{
    private static readonly string[] ScopeLabels = { "当前场景", "全部已打开场景", "选中物体及子物体" };
    private static readonly string[] MatchLabels = { "全部字体", "仅指定原字体" };

    [SerializeField] private int sceneScope;
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool replaceText = true;
    [SerializeField] private bool replaceTMP = true;
    [SerializeField] private bool matchSourceFont;
    [SerializeField] private Font sourceFont;
    [SerializeField] private UnityEngine.Object sourceTMPFont;
    [SerializeField] private bool showAdvanced;
    private Vector2 scrollPosition;
    private Type tmpTextType;
    private Type tmpFontType;
    private PropertyInfo tmpFontProperty;
    private string sceneResult;

    private void DetectTMP()
    {
        tmpTextType = FindTypeInLoadedAssemblies("TMPro.TMP_Text");
        tmpFontType = FindTypeInLoadedAssemblies("TMPro.TMP_FontAsset");
        tmpFontProperty = tmpTextType?.GetProperty("font");
        hasTMP = tmpFontType != null && tmpTextType != null && tmpFontProperty != null;
    }

    private void DrawSceneFontControls()
    {
        EditorGUILayout.LabelField("场景字体替换", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        sceneScope = EditorGUILayout.Popup("作用范围", sceneScope, ScopeLabels);
        includeInactive = EditorGUILayout.Toggle("包含未激活物体", includeInactive);
        matchSourceFont = EditorGUILayout.Popup("替换方式", matchSourceFont ? 1 : 0, MatchLabels) == 1;

        replaceText = EditorGUILayout.ToggleLeft("替换 UGUI Text", replaceText);
        using (new EditorGUI.DisabledScope(!replaceText))
        {
            if (matchSourceFont)
                sourceFont = (Font)EditorGUILayout.ObjectField("Text 原字体", sourceFont, typeof(Font), false);
            newFont = (Font)EditorGUILayout.ObjectField("Text 目标字体", newFont, typeof(Font), false);
        }

        if (hasTMP)
        {
            replaceTMP = EditorGUILayout.ToggleLeft("替换 TMP（UI / 3D）", replaceTMP);
            using (new EditorGUI.DisabledScope(!replaceTMP))
            {
                if (matchSourceFont)
                    sourceTMPFont = EditorGUILayout.ObjectField("TMP 原字体", sourceTMPFont, tmpFontType, false);
                newTMPFont = EditorGUILayout.ObjectField("TMP 目标字体", newTMPFont, tmpFontType, false);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("未检测到 TextMeshPro，仍可正常替换 UGUI Text 字体。", MessageType.Info);
        }

        if (EditorGUI.EndChangeCheck()) sceneResult = null;
        if (matchSourceFont)
            EditorGUILayout.HelpBox("只替换引用指定原字体的组件；原字体留空表示仅匹配未设置字体的组件。", MessageType.Info);

        bool inPrefabStage = PrefabStageUtility.GetCurrentPrefabStage() != null;
        if (inPrefabStage)
            EditorGUILayout.HelpBox("请返回普通场景后执行场景替换。", MessageType.Info);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("请在编辑模式下使用字体替换。", MessageType.Info);
        if (!HasValidFontSettings())
            EditorGUILayout.HelpBox("请勾选要处理的组件，并为每种勾选的组件指定目标字体。", MessageType.Info);

        using (new EditorGUI.DisabledScope(!HasValidFontSettings() || inPrefabStage))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("统计待替换组件"))
            {
                var targets = FindSceneFontChanges();
                int textCount = targets.FindAll(component => component is Text).Count;
                sceneResult = $"待替换：Text {textCount} 个，TMP {targets.Count - textCount} 个（已是目标字体的组件不计入）。";
            }
            if (GUILayout.Button("替换字体（可撤销）")) ReplaceSceneFonts();
        }
        if (!string.IsNullOrEmpty(sceneResult)) EditorGUILayout.HelpBox(sceneResult, MessageType.Info);
        EditorGUILayout.HelpBox("修改后请保存场景；支持 Ctrl+Z 撤销。Text 使用 Font，TMP 使用 TMP Font Asset。", MessageType.None);
        EditorGUILayout.Space();
    }

    private bool HasValidFontSettings()
    {
        bool useTMP = hasTMP && replaceTMP;
        return (replaceText || useTMP) && (!replaceText || newFont != null) &&
            (!useTMP || (newTMPFont != null && tmpFontType.IsInstanceOfType(newTMPFont)));
    }

    private static bool IsSceneObject(GameObject go)
    {
        return go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid() && go.scene.isLoaded &&
            !EditorSceneManager.IsPreviewScene(go.scene) &&
            StageUtility.GetStageHandle(go).Equals(StageUtility.GetMainStageHandle());
    }

    private List<Component> FindSceneFontChanges()
    {
        var roots = new List<GameObject>();
        if (sceneScope == 2)
        {
            foreach (var go in Selection.gameObjects)
                if (IsSceneObject(go)) roots.Add(go);
        }
        else if (sceneScope == 1)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                    roots.AddRange(scene.GetRootGameObjects());
            }
        }
        else
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                roots.AddRange(scene.GetRootGameObjects());
        }

        var result = new List<Component>();
        var seen = new HashSet<int>();
        foreach (var root in roots)
        {
            if (!IsSceneObject(root)) continue;
            foreach (var component in FindFontChangesUnderRoot(root))
                if (seen.Add(component.GetInstanceID())) result.Add(component);
        }
        return result;
    }

    private List<Component> FindFontChangesUnderRoot(GameObject root)
    {
        var result = new List<Component>();
        if (replaceText && newFont != null)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(includeInactive))
            {
                if (!includeInactive && !text.gameObject.activeInHierarchy) continue;
                if (text.font != newFont && (!matchSourceFont || text.font == sourceFont)) result.Add(text);
            }
        }
        if (hasTMP && replaceTMP && newTMPFont != null && tmpFontType.IsInstanceOfType(newTMPFont))
        {
            foreach (var component in root.GetComponentsInChildren(tmpTextType, includeInactive))
            {
                if (!includeInactive && !component.gameObject.activeInHierarchy) continue;
                var font = (UnityEngine.Object)tmpFontProperty.GetValue(component);
                if (font != newTMPFont && (!matchSourceFont || font == sourceTMPFont)) result.Add(component);
            }
        }
        return result;
    }

    private void ApplyFontChange(Component component, bool recordUndo)
    {
        if (recordUndo) Undo.RecordObject(component, "替换场景字体");
        if (component is Text text) text.font = newFont;
        else tmpFontProperty.SetValue(component, newTMPFont);
        RecordComponentChange(component);
    }

    private static void RecordComponentChange(Component component)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(component))
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        EditorUtility.SetDirty(component);
    }

    private int ReplaceFontsUnderRoot(GameObject root, bool recordUndo)
    {
        var targets = FindFontChangesUnderRoot(root);
        foreach (var component in targets) ApplyFontChange(component, recordUndo);
        return targets.Count;
    }

    private void ReplaceSceneFonts()
    {
        if (!HasValidFontSettings() || EditorApplication.isPlayingOrWillChangePlaymode ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        var targets = FindSceneFontChanges();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("替换场景字体");
        int textCount = 0;
        int tmpCount = 0;
        try
        {
            foreach (var component in targets)
            {
                ApplyFontChange(component, true);
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                if (component is Text) textCount++;
                else tmpCount++;
            }
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
            sceneResult = $"已替换：Text {textCount} 个，TMP {tmpCount} 个。可使用 Ctrl+Z 撤销。";
        }
        Debug.Log("Font Replacer：" + sceneResult);
    }
}
#endif
