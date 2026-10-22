#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.Linq;
using System;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;


[ToolInfo("字体替换", "字体工具",
    Description = "替换场景中 Text / TMP 的全部字体或指定原字体。支持当前场景、已打开场景和选中物体（含子物体），支持撤销。\n\nTMP 为可选依赖；另保留 Prefab 批处理与组件转换。",
    Icon = "🔤", Tags = new[] { "UGUI", "TMP", "批量替换" })]
public partial class FontReplacer : EditorWindow
{
    private const string ConvertContextMenu = "GameObject/Font Replacer/Text 与 TMP 相互转换";

    [SerializeField] private Font newFont; // 普通UGUI字体
    private bool hasTMP;
    [SerializeField] private UnityEngine.Object newTMPFont;

    private bool isProcessing = false;
    private int currentIndex = 0;
    private string[] prefabPaths;
    private string[] scenePaths;
    private int totalObjects = 0;

    private string prefabFolder = "Assets"; // 指定文件夹

    [MenuItem("UnityToolsHub/Font Replacer")]
    public static void ShowWindow()
    {
        GetWindow<FontReplacer>("Font Replacer");
    }

    [MenuItem(ConvertContextMenu, false, 20)]
    private static void ConvertSelectedTextComponents()
    {
        if (!ValidateConvertSelectedTextComponents()) return;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Convert Text and TMP");
        int converted = 0;
        foreach (GameObject gameObject in Selection.gameObjects)
        {
            Text legacyText = gameObject.GetComponent<Text>();
            if (legacyText != null)
            {
                if (ConvertLegacyTextToTMP(legacyText) != null) converted++;
                continue;
            }
            var tmpType = FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI");
            var tmpText = tmpType == null ? null : gameObject.GetComponent(tmpType);
            if (tmpText != null && ConvertTMPToLegacyText(tmpText) != null) converted++;
        }
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"Font Replacer：已转换 {converted} 个 Text/TMP 组件。");
    }

    [MenuItem(ConvertContextMenu, true)]
    private static bool ValidateConvertSelectedTextComponents()
    {
        var tmpType = FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI");
        return !EditorApplication.isPlayingOrWillChangePlaymode && tmpType != null &&
            Selection.gameObjects.Any(go => go.GetComponent<Text>() != null || go.GetComponent(tmpType) != null);
    }

    [MenuItem("CONTEXT/Text/转换为 TextMeshProUGUI")]
    private static void ConvertTextComponent(MenuCommand command)
    {
        ConvertLegacyTextToTMP((Text)command.context);
    }

    [MenuItem("CONTEXT/Text/转换为 TextMeshProUGUI", true)]
    private static bool ValidateConvertTextComponent(MenuCommand command)
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode &&
            FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI") != null;
    }

    [MenuItem("CONTEXT/TextMeshProUGUI/转换为 UGUI Text")]
    private static void ConvertTMPComponent(MenuCommand command)
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            ConvertTMPToLegacyText((Component)command.context);
    }

    // Type names and reflection keep TextMeshPro optional, including the conversion menus.
    private static Component ConvertLegacyTextToTMP(Text source)
    {
        Type tmpType = FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI");
        if (source == null || tmpType == null) return null;
        GameObject go = source.gameObject;
        string text = source.text;
        Color color = source.color;
        int size = source.fontSize;
        FontStyle style = source.fontStyle;
        TextAnchor alignment = source.alignment;
        bool raycast = source.raycastTarget;
        bool maskable = source.maskable;
        bool richText = source.supportRichText;
        Undo.DestroyObjectImmediate(source);
        Component target = Undo.AddComponent(go, tmpType);
        Undo.RecordObject(target, "Convert Text to TMP");
        SetTMPProperty(target, "text", text);
        SetTMPProperty(target, "color", color);
        SetTMPProperty(target, "fontSize", (float)size);
        string styleName = style == FontStyle.BoldAndItalic ? "Bold, Italic" : style.ToString();
        SetTMPEnum(target, "fontStyle", styleName);
        string[] alignments = { "TopLeft", "Top", "TopRight", "Left", "Center", "Right", "BottomLeft", "Bottom", "BottomRight" };
        SetTMPEnum(target, "alignment", alignments[(int)alignment]);
        SetTMPProperty(target, "raycastTarget", raycast);
        SetTMPProperty(target, "maskable", maskable);
        SetTMPProperty(target, "richText", richText);
        SetTMPProperty(target, "enableAutoSizing", false);
        RecordComponentChange(target);
        return target;
    }

    private static Text ConvertTMPToLegacyText(Component source)
    {
        if (source == null) return null;
        GameObject go = source.gameObject;
        string text = (string)GetTMPProperty(source, "text");
        Color color = (Color)GetTMPProperty(source, "color");
        int size = Mathf.Max(1, Mathf.RoundToInt((float)GetTMPProperty(source, "fontSize")));
        string style = GetTMPProperty(source, "fontStyle").ToString();
        string alignment = GetTMPProperty(source, "alignment").ToString();
        bool raycast = (bool)GetTMPProperty(source, "raycastTarget");
        bool maskable = (bool)GetTMPProperty(source, "maskable");
        bool richText = (bool)GetTMPProperty(source, "richText");
        Undo.DestroyObjectImmediate(source);
        Text target = Undo.AddComponent<Text>(go);
        Undo.RecordObject(target, "Convert TMP to Text");
        target.text = text;
        target.color = color;
        target.fontSize = size;
        bool bold = style.Contains("Bold");
        bool italic = style.Contains("Italic");
        target.fontStyle = bold ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold) :
            (italic ? FontStyle.Italic : FontStyle.Normal);
        int row = alignment.Contains("Top") ? 0 : alignment.Contains("Bottom") ? 2 : 1;
        int col = alignment.Contains("Left") ? 0 : alignment.Contains("Right") ? 2 : 1;
        target.alignment = (TextAnchor)(row * 3 + col);
        target.raycastTarget = raycast;
        target.maskable = maskable;
        target.supportRichText = richText;
        RecordComponentChange(target);
        return target;
    }

    private static object GetTMPProperty(Component component, string name)
    {
        return component.GetType().GetProperty(name).GetValue(component);
    }

    private static void SetTMPProperty(Component component, string name, object value)
    {
        component.GetType().GetProperty(name).SetValue(component, value);
    }

    private static void SetTMPEnum(Component component, string name, string value)
    {
        var property = component.GetType().GetProperty(name);
        property.SetValue(component, Enum.Parse(property.PropertyType, value));
    }

    private void OnEnable()
    {
        DetectTMP();
        minSize = new Vector2(440, 420);
    }

    private void OnDisable()
    {
        if (isProcessing) StopProcessing();
    }

    private void OnSelectionChange()
    {
        sceneResult = null;
        Repaint();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        using (new EditorGUI.DisabledScope(isProcessing || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            DrawSceneFontControls();
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "预制体批处理与组件转换", true);
            if (showAdvanced)
            {
                DrawAdvancedControls();
            }
        }
        if (isProcessing)
        {
            EditorGUILayout.HelpBox("正在处理，可点击取消停止后续处理。", MessageType.Info);
            if (GUILayout.Button("取消")) StopProcessing();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawAdvancedControls()
    {
        EditorGUILayout.HelpBox("字体替换共用上方字体与筛选条件。组件转换会更换组件类型。", MessageType.Info);
        GUILayout.Space(6);
        EditorGUILayout.LabelField("Convert Components", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Text -> TMP (Open Scene)"))
            {
                ConvertTextToTMPInOpenScene();
            }
            if (GUILayout.Button("TMP -> Text (Open Scene)"))
            {
                ConvertTMPToTextInOpenScene();
            }
        }

        if (GUILayout.Button("Replace Fonts In All Scenes"))
        {
            if (EditorUtility.DisplayDialog("Confirm", "Replace fonts in ALL scenes (this may modify many files). Continue?", "Yes", "No"))
                StartReplaceAllScenes();
        }

        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        prefabFolder = EditorGUILayout.TextField("Prefab Folder", prefabFolder);
        if (GUILayout.Button("Select Folder", GUILayout.MaxWidth(100)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Prefab Folder", "Assets", "");
            if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
            {
                prefabFolder = "Assets" + path.Substring(Application.dataPath.Length);
            }
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Replace Fonts In Prefabs In Folder"))
        {
            if (EditorUtility.DisplayDialog("Confirm", $"Replace fonts in prefabs under '{prefabFolder}'?", "Yes", "No"))
                StartReplacePrefabsInFolder(prefabFolder);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Text -> TMP (Prefabs in Folder)"))
            {
                StartConvertPrefabsInFolder(prefabFolder, true);
            }
            if (GUILayout.Button("TMP -> Text (Prefabs in Folder)"))
            {
                StartConvertPrefabsInFolder(prefabFolder, false);
            }
        }

        if (GUILayout.Button("Replace Fonts In All Prefabs"))
        {
            if (EditorUtility.DisplayDialog("Confirm", "Replace fonts in ALL prefabs?", "Yes", "No"))
                StartReplaceAllPrefabs();
        }
    }

    // Helper: convert all Text children under root GameObject to TMP, return count
    private int ConvertTextToTMP_Root(GameObject root)
    {
        int count = 0;
        var texts = root.GetComponentsInChildren<Text>(true);
        foreach (var t in texts)
        {
            count += ConvertTextToTMP_Single(t.gameObject);
        }
        return count;
    }

    private int ConvertTextToTMP_Single(GameObject go)
    {
        var target = ConvertLegacyTextToTMP(go.GetComponent<Text>());
        if (target == null) return 0;
        if (newTMPFont != null) SetTMPProperty(target, "font", newTMPFont);
        return 1;
    }

    private int ConvertTMPToText_Root(GameObject root)
    {
        var tmpType = FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI");
        if (tmpType == null) return 0;
        int count = 0;
        foreach (var component in root.GetComponentsInChildren(tmpType, true))
            count += ConvertTMPToText_Single(component.gameObject);
        return count;
    }

    private int ConvertTMPToText_Single(GameObject go)
    {
        var tmpType = FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI");
        if (tmpType == null) return 0;
        var target = ConvertTMPToLegacyText(go.GetComponent(tmpType));
        if (target == null) return 0;
        if (newFont != null) target.font = newFont;
        return 1;
    }

    #region All Scenes
    private void StartReplaceAllScenes()
    {
        if (!CheckFontSelected()) return;

        scenePaths = AssetDatabase.FindAssets("t:Scene")
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .ToArray();
        currentIndex = 0;
        totalObjects = 0;
        isProcessing = true;

        EditorApplication.update += ProcessScenes;
    }

    private void StopProcessing()
    {
        isProcessing = false;
        EditorApplication.update -= ProcessScenes;
        EditorApplication.update -= ProcessPrefabs;
        EditorApplication.update -= ProcessPrefabs_Convert;
        EditorUtility.ClearProgressBar();
        Debug.Log("Font replacement/process cancelled by user.");
    }

    private void ProcessScenes()
    {
        if (currentIndex >= scenePaths.Length)
        {
            EditorUtility.ClearProgressBar();
            isProcessing = false;
            EditorApplication.update -= ProcessScenes;
            AssetDatabase.SaveAssets();
            Debug.Log($"所有场景字体替换完成，总共替换 {totalObjects} 个对象！");
            return;
        }

        string path = scenePaths[currentIndex];
        EditorUtility.DisplayProgressBar("替换场景字体", path, (float)currentIndex / scenePaths.Length);

        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        int count = ReplaceFontsInScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);

        totalObjects += count;
        currentIndex++;
    }

    private static Type FindTypeInLoadedAssemblies(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }
    #endregion

    #region Prefabs
    private void StartReplaceAllPrefabs()
    {
        if (!CheckFontSelected()) return;

        prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        StartPrefabProcessing(prefabPaths);
    }

    private void StartReplacePrefabsInFolder(string folder)
    {
        if (!CheckFontSelected()) return;

        if (string.IsNullOrEmpty(folder)) folder = "Assets";
        prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        StartPrefabProcessing(prefabPaths);
    }

    private void StartPrefabProcessing(string[] paths)
    {
        currentIndex = 0;
        totalObjects = 0;
        isProcessing = true;
        EditorApplication.update += ProcessPrefabs;
    }

    private void ProcessPrefabs()
    {
        if (currentIndex >= prefabPaths.Length)
        {
            EditorUtility.ClearProgressBar();
            isProcessing = false;
            EditorApplication.update -= ProcessPrefabs;
            AssetDatabase.SaveAssets();
            Debug.Log($"所有预制体字体替换完成，总共替换 {totalObjects} 个对象！");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(prefabPaths[currentIndex]);
        EditorUtility.DisplayProgressBar("替换预制体字体", path, (float)currentIndex / prefabPaths.Length);

        // 使用 LoadPrefabContents 保证修改生效
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
        bool changed = false;

        int count = ReplaceFontsUnderRoot(prefabRoot, false);
        changed = count > 0;
        totalObjects += count;

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
        }
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        currentIndex++;
    }
    #endregion

    #region Convert Components
    private void ConvertTextToTMPInOpenScene()
    {
        var scene = SceneManager.GetActiveScene();
        int total = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            total += ConvertTextToTMP_Root(root);
        }
        if (total > 0)
            EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Converted {total} Text to TMP in open scene.");
    }

    private void ConvertTMPToTextInOpenScene()
    {
        var scene = SceneManager.GetActiveScene();
        int total = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            total += ConvertTMPToText_Root(root);
        }
        if (total > 0)
            EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Converted {total} TMP to Text in open scene.");
    }

    private void StartConvertPrefabsInFolder(string folder, bool toTMP)
    {
        if (string.IsNullOrEmpty(folder)) folder = "Assets";
        prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        convertToTMP = toTMP;

        currentIndex = 0;
        totalObjects = 0;
        isProcessing = true;

        EditorApplication.update += ProcessPrefabs_Convert;
    }

    private bool convertToTMP = true;

    private void ProcessPrefabs_Convert()
    {
        if (currentIndex >= prefabPaths.Length)
        {
            EditorUtility.ClearProgressBar();
            isProcessing = false;
            EditorApplication.update -= ProcessPrefabs_Convert;
            AssetDatabase.SaveAssets();
            Debug.Log($"所有预制体转换完成，总共处理 {totalObjects} 个对象！");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(prefabPaths[currentIndex]);
        EditorUtility.DisplayProgressBar("转换预制体", path, (float)currentIndex / prefabPaths.Length);

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
        bool changed = false;

        if (convertToTMP)
        {
            if (!hasTMP)
            {
                // nothing to do
            }
            else
            {
                int converted = 0;
                foreach (var t in prefabRoot.GetComponentsInChildren<Text>(true))
                {
                    converted += ConvertTextToTMP_Single(t.gameObject);
                }
                changed = converted > 0;
                totalObjects += converted;
            }
        }
        else
        {
            if (!hasTMP)
            {
                // nothing to do
            }
            else
            {
                int converted = 0;
                foreach (var go in prefabRoot.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject))
                {
                    converted += ConvertTMPToText_Single(go);
                }
                changed = converted > 0;
                totalObjects += converted;
            }
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
        }
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        currentIndex++;
    }
    #endregion
   
    private int ReplaceFontsInScene(Scene scene)
    {
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
            count += ReplaceFontsUnderRoot(root, true);
        return count;
    }

    private bool CheckFontSelected()
    {
        if (HasValidFontSettings()) return true;
        Debug.LogWarning("请为每种勾选的组件选择目标字体；至少勾选一种组件。");
        return false;
    }
}
#endif
