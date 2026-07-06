using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 帧动画右键菜单扩展
/// 支持在 Project 窗口中直接右键选中的图片/文件夹一键创建帧动画
/// </summary>
public static class FrameAnimationMenuExtension
{
    // ========== 右键菜单：从选中图片创建帧动画 ==========

    /// <summary>
    /// 右键菜单：从选中的多张 Sprite 图片创建帧动画
    /// </summary>
    [MenuItem("Assets/帧动画/从选中图片创建动画", true)]
    private static bool ValidateCreateFromSelectedSprites()
    {
        // 至少选中 1 张图片
        return Selection.objects.Any(o => o is Sprite || o is Texture2D);
    }

    [MenuItem("Assets/帧动画/从选中图片创建动画", false, 3000)]
    private static void CreateFromSelectedSprites()
    {
        var sprites = CollectSpritesFromSelection();
        if (sprites.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请选中至少 1 张 Sprite 图片！", "确定");
            return;
        }

        // 弹出对话框获取动画名称
        string defaultName = GetDefaultAnimationName(sprites);
        string animName = EditorUtility.SaveFilePanelInProject(
            "保存帧动画", defaultName, "anim", "选择保存位置");

        if (string.IsNullOrEmpty(animName)) return;

        // 弹出设置对话框
        var settings = FrameAnimSettingsDialog.Show();
        if (settings == null) return;

        CreateAnimationClip(sprites, animName, settings);
    }

    /// <summary>
    /// 右键菜单：从文件夹创建帧动画
    /// </summary>
    [MenuItem("Assets/帧动画/从文件夹创建动画", true)]
    private static bool ValidateCreateFromFolder()
    {
        return Selection.objects.Length == 1 &&
               Selection.objects[0] is DefaultAsset &&
               AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.objects[0]));
    }

    [MenuItem("Assets/帧动画/从文件夹创建动画", false, 3001)]
    private static void CreateFromFolder()
    {
        string folderPath = AssetDatabase.GetAssetPath(Selection.objects[0]);
        var sprites = LoadSpritesFromFolder(folderPath);

        if (sprites.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "文件夹中没有找到 Sprite 图片！", "确定");
            return;
        }

        string folderName = Path.GetFileName(folderPath);
        string animName = EditorUtility.SaveFilePanelInProject(
            "保存帧动画", folderName, "anim", "选择保存位置");

        if (string.IsNullOrEmpty(animName)) return;

        var settings = FrameAnimSettingsDialog.Show();
        if (settings == null) return;

        CreateAnimationClip(sprites, animName, settings);
    }

    // ========== Hierarchy 右键菜单 ==========

    /// <summary>
    /// 在 Hierarchy 中右键：为选中物体快速创建帧动画
    /// </summary>
    [MenuItem("GameObject/帧动画/快速创建帧动画", false, 20)]
    private static void QuickCreateForGameObject()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("提示", "请先选中一个 GameObject！", "确定");
            return;
        }

        // 弹出文件夹选择
        string folder = EditorUtility.OpenFolderPanel("选择帧动画图片文件夹", "Assets", "");
        if (string.IsNullOrEmpty(folder)) return;

        if (folder.StartsWith(Application.dataPath))
            folder = "Assets" + folder.Substring(Application.dataPath.Length);

        var sprites = LoadSpritesFromFolder(folder);
        if (sprites.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "文件夹中没有找到图片！", "确定");
            return;
        }

        var settings = FrameAnimSettingsDialog.Show();
        if (settings == null) return;

        string savePath = $"Assets/Animations/{go.name}Anim.anim";
        EnsureDirectory("Assets/Animations");

        CreateAnimationClip(sprites, savePath, settings, go);
    }

    // ========== 核心创建逻辑 ==========

    private static void CreateAnimationClip(List<Sprite> sprites, string savePath,
        FrameAnimSettings settings, GameObject targetGo = null)
    {
        var clip = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(savePath),
            frameRate = settings.frameRate
        };

        // 设置循环
        if (settings.loopTime)
        {
            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
        }

        // 创建关键帧
        float timePerFrame = 1f / settings.frameRate;
        int frameCount = settings.loopTime ? sprites.Count + 1 : sprites.Count;
        var keyframes = new ObjectReferenceKeyframe[frameCount];

        for (int i = 0; i < sprites.Count; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i * timePerFrame,
                value = sprites[i]
            };
        }

        // 循环时多一帧回到起点
        if (settings.loopTime)
        {
            keyframes[sprites.Count] = new ObjectReferenceKeyframe
            {
                time = sprites.Count * timePerFrame,
                value = sprites[0]
            };
        }

        // 绑定 SpriteRenderer
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        // 保存
        AssetDatabase.CreateAsset(clip, savePath);
        AssetDatabase.SaveAssets();

        // 挂载到目标物体
        if (targetGo != null)
        {
            var sr = targetGo.GetComponent<SpriteRenderer>();
            if (sr == null) sr = targetGo.AddComponent<SpriteRenderer>();

            var animator = targetGo.GetComponent<Animator>();
            if (animator == null) animator = targetGo.AddComponent<Animator>();

            if (settings.createController)
            {
                string ctrlPath = savePath.Replace(".anim", "Controller.controller");
                var ctrl = UnityEditor.Animations.AnimatorController
                    .CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
                animator.runtimeAnimatorController = ctrl;
            }
        }

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<AnimationClip>(savePath);
        EditorGUIUtility.PingObject(Selection.activeObject);

        Debug.Log($"[帧动画] ✅ 创建成功: {savePath} ({sprites.Count}帧, {settings.frameRate}FPS)");
        EditorUtility.DisplayDialog("完成",
            $"帧动画创建成功！\n帧数: {sprites.Count}\n帧率: {settings.frameRate}FPS\n时长: {sprites.Count / settings.frameRate:F2}s",
            "好的");
    }

    // ========== 辅助方法 ==========

    private static List<Sprite> CollectSpritesFromSelection()
    {
        var sprites = new List<Sprite>();
        foreach (var obj in Selection.objects)
        {
            var s = obj as Sprite;
            if (s != null) { sprites.Add(s); continue; }

            var tex = obj as Texture2D;
            if (tex != null)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                var loaded = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (loaded != null) sprites.Add(loaded);
            }
        }
        return sprites;
    }

    private static List<Sprite> LoadSpritesFromFolder(string folderPath)
    {
        var sprites = new List<Sprite>();
        if (!Directory.Exists(folderPath)) return sprites;

        string[] exts = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.psd" };
        var files = new List<string>();
        foreach (var ext in exts)
            files.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly));

        // 自然排序
        files.Sort((a, b) =>
        {
            string na = Path.GetFileNameWithoutExtension(a);
            string nb = Path.GetFileNameWithoutExtension(b);
            return NaturalCompare(na, nb);
        });

        foreach (var file in files)
        {
            string assetPath = file.Replace('\\', '/');
            if (assetPath.StartsWith(Application.dataPath))
                assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                // 尝试设置为 Sprite 模式
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                }
            }
            if (sprite != null) sprites.Add(sprite);
        }
        return sprites;
    }

    private static string GetDefaultAnimationName(List<Sprite> sprites)
    {
        if (sprites.Count == 0) return "NewFrameAnim";
        string path = AssetDatabase.GetAssetPath(sprites[0]);
        return Path.GetFileName(Path.GetDirectoryName(path)) ?? "NewFrameAnim";
    }

    private static void EnsureDirectory(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static int NaturalCompare(string a, string b)
    {
        int ia = 0, ib = 0;
        while (ia < a.Length && ib < b.Length)
        {
            if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
            {
                int numA = 0, numB = 0;
                while (ia < a.Length && char.IsDigit(a[ia])) { numA = numA * 10 + (a[ia] - '0'); ia++; }
                while (ib < b.Length && char.IsDigit(b[ib])) { numB = numB * 10 + (b[ib] - '0'); ib++; }
                if (numA != numB) return numA.CompareTo(numB);
            }
            else
            {
                int cmp = char.ToLower(a[ia]).CompareTo(char.ToLower(b[ib]));
                if (cmp != 0) return cmp;
                ia++; ib++;
            }
        }
        return a.Length.CompareTo(b.Length);
    }
}

/// <summary>
/// 帧动画设置
/// </summary>
public class FrameAnimSettings
{
    public float frameRate = 12f;
    public bool loopTime = true;
    public bool createController = false;
}

/// <summary>
/// 帧动画设置弹窗
/// </summary>
public class FrameAnimSettingsDialog : EditorWindow
{
    public float frameRate = 12f;
    public bool loopTime = true;
    public bool createController = false;

    private static FrameAnimSettingsDialog window;
    private static FrameAnimSettings result;

    public static FrameAnimSettings Show()
    {
        result = null;
        window = CreateInstance<FrameAnimSettingsDialog>();
        window.titleContent = new GUIContent("帧动画设置");
        window.minSize = new Vector2(300, 180);
        window.maxSize = new Vector2(300, 180);
        window.ShowModal();
        return result;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("帧动画设置", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        frameRate = EditorGUILayout.Slider("帧率 (FPS)", frameRate, 1f, 60f);
        loopTime = EditorGUILayout.Toggle("循环播放", loopTime);
        createController = EditorGUILayout.Toggle("创建 AnimatorController", createController);

        EditorGUILayout.Space(15);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("创建", GUILayout.Height(30)))
        {
            result = new FrameAnimSettings
            {
                frameRate = frameRate,
                loopTime = loopTime,
                createController = createController
            };
            Close();
        }

        if (GUILayout.Button("取消", GUILayout.Height(30)))
        {
            result = null;
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }
}
