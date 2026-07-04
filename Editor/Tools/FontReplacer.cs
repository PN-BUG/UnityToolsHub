#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.Linq;
using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;


[ToolInfo("字体替换", "字体工具",
    Description = "批量替换 Prefab 和 Scene 中的 UGUI / TextMeshPro 字体。\n\n支持指定目标文件夹，自动检测 TMP 是否可用。",
    Icon = "🔤", Tags = new[] { "UGUI", "TMP", "批量替换" })]
public class FontReplacer : EditorWindow
{
    private Font newFont; // 普通UGUI字体
    private bool hasTMP;
#if TMP_PRESENT
    private TMPro.TMP_FontAsset newTMPFont;
#endif

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

    private void OnEnable()
    {
        // 自动检测 TMP
        hasTMP = System.AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetType("TMPro.TMP_FontAsset") != null);
    }

    private void OnGUI()
    {
        GUILayout.Label("Font Replacement Settings", EditorStyles.boldLabel);

        newFont = (Font)EditorGUILayout.ObjectField("New UGUI Font", newFont, typeof(Font), false);

        if (hasTMP)
        {
#if TMP_PRESENT
            newTMPFont = (TMPro.TMP_FontAsset)EditorGUILayout.ObjectField("New TextMeshPro Font", newTMPFont, typeof(TMPro.TMP_FontAsset), false);
#endif
        }
        else
        {
            EditorGUILayout.HelpBox("TextMeshPro not detected. TMP font replacement will be skipped.", MessageType.Info);
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Replace Fonts In Open Scene"))
        {
            if (EditorUtility.DisplayDialog("Confirm", "Replace fonts in current open scene?", "Yes", "No"))
                ReplaceFontsInOpenScene();
        }

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

        if (isProcessing)
        {
            EditorGUILayout.HelpBox("正在处理，请等待完成 或 点击 Cancel 停止", MessageType.Info);
            if (GUILayout.Button("Cancel", GUILayout.MaxWidth(120)))
            {
                StopProcessing();
            }
        }
    }

    #region Open Scene
    private void ReplaceFontsInOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        int count = ReplaceFontsInScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"当前打开场景字体替换完成，总共替换 {count} 个对象！");
    }
    #endregion

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

    // Helper: convert single GameObject's Text to TMP, return 1 if converted
    private int ConvertTextToTMP_Single(GameObject go)
    {
        var txt = go.GetComponent<Text>();
        if (txt == null) return 0;
        if (!hasTMP)
        {
            Debug.LogWarning("TextMeshPro not present. Skipping conversion.");
            return 0;
        }
        // prefer direct API when compiled with TMP, otherwise use reflection
#if TMP_PRESENT
        var text = txt.text;
        var color = txt.color;
        var size = txt.fontSize;
        DestroyImmediate(txt, true);
        var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        try { tmp.enableAutoSizing = false; } catch { }
        tmp.fontSize = size;
        try { tmp.ForceMeshUpdate(); } catch { }
        try { tmp.SetVerticesDirty(); tmp.SetLayoutDirty(); } catch { }
        if (newTMPFont != null) tmp.font = newTMPFont;
        return 1;
#else
        var text = txt.text;
        var color = txt.color;
        var size = txt.fontSize;
        DestroyImmediate(txt, true);

        var tmpType = FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI");
        if (tmpType == null)
        {
            Debug.LogWarning("Cannot find TextMeshProUGUI type. Skipping conversion.");
            return 0;
        }

        var tmpComp = go.AddComponent(tmpType);

        // set properties via reflection
        try
        {
            var textProp = tmpType.GetProperty("text");
            var colorProp = tmpType.GetProperty("color");
            var fontSizeProp = tmpType.GetProperty("fontSize");
            var autoProp = tmpType.GetProperty("enableAutoSizing");

            if (textProp != null) textProp.SetValue(tmpComp, text);
            if (colorProp != null) colorProp.SetValue(tmpComp, color);
            if (autoProp != null)
            {
                try { autoProp.SetValue(tmpComp, false); } catch { }
            }
            if (fontSizeProp != null)
            {
                try { fontSizeProp.SetValue(tmpComp, Convert.ChangeType(size, fontSizeProp.PropertyType)); } catch { }
            }

            // try to call ForceMeshUpdate, SetVerticesDirty, SetLayoutDirty
            var force = tmpType.GetMethod("ForceMeshUpdate");
            var setV = tmpType.GetMethod("SetVerticesDirty");
            var setL = tmpType.GetMethod("SetLayoutDirty");
            try { force?.Invoke(tmpComp, null); } catch { }
            try { setV?.Invoke(tmpComp, null); } catch { }
            try { setL?.Invoke(tmpComp, null); } catch { }
        }
        catch { }

        return 1;
#endif
    }

    // Helper: convert all TMP children under root GameObject to Text, return count
    private int ConvertTMPToText_Root(GameObject root)
    {
        int count = 0;
#if TMP_PRESENT
        var tmps = root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        foreach (var tmp in tmps)
        {
            count += ConvertTMPToText_Single(tmp.gameObject);
        }
#endif
        return count;
    }

    // Helper: convert single GameObject's TMP to Text, return 1 if converted
    private int ConvertTMPToText_Single(GameObject go)
    {
        if (!hasTMP) return 0;

#if TMP_PRESENT
        var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp == null) return 0;
        var text = tmp.text;
        var color = tmp.color;
        int size = 0;
        try { bool wasAuto = tmp.enableAutoSizing; if (wasAuto) tmp.enableAutoSizing = false; size = Mathf.RoundToInt(tmp.fontSize); if (wasAuto) tmp.enableAutoSizing = true; } catch { size = Mathf.RoundToInt(tmp.fontSize); }
        DestroyImmediate(tmp, true);
        var t = go.AddComponent<Text>();
        t.text = text;
        t.color = color;
        if (newFont != null) t.font = newFont;
        if (size > 0) t.fontSize = size;
        return 1;
#else
        var tmpType = FindTypeInLoadedAssemblies("TMPro.TextMeshProUGUI");
        if (tmpType == null) return 0;
        var tmpComp = go.GetComponent(tmpType);
        if (tmpComp == null) return 0;

        string text = string.Empty;
        Color color = Color.white;
        int size = 0;
        try
        {
            var textProp = tmpType.GetProperty("text");
            var colorProp = tmpType.GetProperty("color");
            var fontSizeProp = tmpType.GetProperty("fontSize");
            if (textProp != null) text = (string)textProp.GetValue(tmpComp);
            if (colorProp != null) color = (Color)colorProp.GetValue(tmpComp);
            if (fontSizeProp != null) size = Convert.ToInt32(fontSizeProp.GetValue(tmpComp));
        }
        catch { }

        DestroyImmediate((UnityEngine.Object)tmpComp, true);

        var t = go.AddComponent<Text>();
        t.text = text;
        t.color = color;
        if (newFont != null) t.font = newFont;
        if (size > 0) t.fontSize = size;
        return 1;
#endif
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
        EditorApplication.update -= ProcessScenes;
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

        foreach (var t in prefabRoot.GetComponentsInChildren<Text>(true))
        {
            if (newFont != null)
            {
                t.font = newFont;
                changed = true;
                totalObjects++;
            }
        }

#if TMP_PRESENT
        if (hasTMP && newTMPFont != null)
        {
            foreach (var tmp in prefabRoot.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                tmp.font = newTMPFont;
                changed = true;
                totalObjects++;
            }
        }
#endif

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
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (newFont != null)
                {
                    Undo.RecordObject(t, "Replace Font");
                    t.font = newFont;
                    count++;
                }
            }

#if TMP_PRESENT
            if (hasTMP && newTMPFont != null)
            {
                foreach (var tmp in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    Undo.RecordObject(tmp, "Replace TMP Font");
                    tmp.font = newTMPFont;
                    count++;
                }
            }
#endif
        }
        return count;
    }

    private bool CheckFontSelected()
    {
        if (newFont == null
#if TMP_PRESENT
            && (hasTMP && newTMPFont == null)
#endif
        )
        {
            Debug.LogWarning("请至少选择一个新字体！");
            return false;
        }
        return true;
    }
}
#endif