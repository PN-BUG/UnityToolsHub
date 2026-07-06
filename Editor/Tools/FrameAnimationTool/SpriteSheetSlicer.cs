using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 精灵图集切分工具
/// 支持将一张 Sprite Sheet 切分成单独的帧并自动生成动画
/// </summary>
[ToolInfo("精灵图集切分", "媒体工具",
    Description = "精灵图集切分工具：将 Sprite Sheet 按行列切分成单独帧。\n\n支持自定义行列数、自动命名，可直接生成动画剪辑。",
    Icon = "✂", Tags = new[] { "Sprite", "切分", "图集" })]
public class SpriteSheetSlicer : EditorWindow
{
    private Texture2D sourceTexture;
    private int columns = 4;
    private int rows = 4;
    private int totalFrames = 0;
    private int startFrame = 0;
    private float frameRate = 12f;
    private bool loopTime = true;
    private string animName = "SpriteSheetAnim";
    private string savePath = "Assets/Animations";

    private Vector2 scrollPos;
    private List<Sprite> slicedSprites = new List<Sprite>();

    [MenuItem("UnityToolsHub/精灵图集切分工具", false, 201)]
    public static void ShowWindow()
    {
        var window = GetWindow<SpriteSheetSlicer>("精灵图集切分工具");
        window.minSize = new Vector2(400, 500);
        window.Show();
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.Space(6);
        DrawTitle();
        EditorGUILayout.Space(4);
        DrawTextureInput();
        EditorGUILayout.Space(4);
        DrawGridSettings();
        EditorGUILayout.Space(4);
        DrawAnimSettings();
        EditorGUILayout.Space(8);
        DrawActionButtons();

        EditorGUILayout.EndScrollView();
    }

    private void DrawTitle()
    {
        EditorGUILayout.LabelField("🖼️ 精灵图集切分工具", new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        });
        DrawSeparator();
    }

    private void DrawTextureInput()
    {
        EditorGUILayout.LabelField("📁 源图片", EditorStyles.boldLabel);
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Sprite Sheet", sourceTexture, typeof(Texture2D), false);

        if (sourceTexture != null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"尺寸: {sourceTexture.width} x {sourceTexture.height}");

            // 检查 TextureImporter 设置
            string path = AssetDatabase.GetAssetPath(sourceTexture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.isReadable == false)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("图片需要开启 Read/Write 设置，点击下方按钮自动修复。", MessageType.Warning);
                if (GUILayout.Button("自动修复 Texture 设置"))
                {
                    importer.isReadable = true;
                    importer.textureType = TextureImporterType.Default;
                    importer.SaveAndReimport();
                }
            }
            else
            {
                EditorGUILayout.EndHorizontal();
            }

            // 预览缩略图
            GUILayout.Label(sourceTexture, GUILayout.Height(100), GUILayout.ExpandWidth(false));
        }
    }

    private void DrawGridSettings()
    {
        DrawSeparator();
        EditorGUILayout.LabelField("📐 网格切分设置", EditorStyles.boldLabel);

        columns = EditorGUILayout.IntField("列数", Mathf.Max(1, columns));
        rows = EditorGUILayout.IntField("行数", Mathf.Max(1, rows));
        totalFrames = columns * rows;

        EditorGUILayout.LabelField($"总帧数: {totalFrames}");

        startFrame = EditorGUILayout.IntSlider("起始帧 (跳过前N帧)", startFrame, 0, totalFrames - 1);

        if (sourceTexture != null)
        {
            int cellW = sourceTexture.width / columns;
            int cellH = sourceTexture.height / rows;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"每帧尺寸: {cellW} x {cellH} px");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawAnimSettings()
    {
        DrawSeparator();
        EditorGUILayout.LabelField("⚙️ 动画设置", EditorStyles.boldLabel);

        animName = EditorGUILayout.TextField("动画名称", animName);
        frameRate = EditorGUILayout.Slider("帧率 (FPS)", frameRate, 1f, 60f);
        loopTime = EditorGUILayout.Toggle("循环播放", loopTime);
        savePath = EditorGUILayout.TextField("保存路径", savePath);
    }

    private void DrawActionButtons()
    {
        DrawSeparator();

        GUI.enabled = sourceTexture != null;

        if (GUILayout.Button("🔪 1. 切分精灵图集", GUILayout.Height(32)))
        {
            SliceTexture();
        }

        GUI.enabled = slicedSprites.Count > 0;

        if (GUILayout.Button("🎞️ 2. 生成帧动画", GUILayout.Height(32)))
        {
            GenerateAnimation();
        }

        GUI.enabled = true;

        if (slicedSprites.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox($"已切分 {slicedSprites.Count} 帧，点击「生成帧动画」完成创建。", MessageType.Info);
        }
    }

    private void DrawSeparator()
    {
        EditorGUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        EditorGUILayout.Space(2);
    }

    /// <summary>
    /// 切分 Texture 为多个 Sprite
    /// </summary>
    private void SliceTexture()
    {
        slicedSprites.Clear();

        string texturePath = AssetDatabase.GetAssetPath(sourceTexture);
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("错误", "无法获取 TextureImporter！", "确定");
            return;
        }

        // 设置 Sprite 模式为 Multiple
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.isReadable = true;

        int cellW = sourceTexture.width / columns;
        int cellH = sourceTexture.height / rows;

        var spriteSheet = new List<SpriteMetaData>();

        int frameIndex = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                // Unity 纹理坐标从左下角开始
                var meta = new SpriteMetaData
                {
                    name = $"{sourceTexture.name}_{frameIndex:D4}",
                    rect = new Rect(col * cellW, sourceTexture.height - (row + 1) * cellH, cellW, cellH),
                    pivot = new Vector2(0.5f, 0.5f)
                };
                spriteSheet.Add(meta);
                frameIndex++;
            }
        }

        importer.spritesheet = spriteSheet.ToArray();
        importer.SaveAndReimport();

        // 重新加载切分后的 Sprite
        var assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
        foreach (var asset in assets)
        {
            var sprite = asset as Sprite;
            if (sprite != null)
            {
                slicedSprites.Add(sprite);
            }
        }

        // 按名称排序
        slicedSprites.Sort((a, b) =>
        {
            string na = a.name;
            string nb = b.name;
            return FrameAnimationMenuExtension_NaturalCompare(na, nb);
        });

        // 跳过起始帧
        if (startFrame > 0 && startFrame < slicedSprites.Count)
        {
            slicedSprites.RemoveRange(0, startFrame);
        }

        Debug.Log($"[精灵图集切分] ✅ 切分完成: {slicedSprites.Count} 帧");
        EditorUtility.DisplayDialog("完成", $"已切分 {slicedSprites.Count} 帧！", "好的");
    }

    /// <summary>
    /// 生成 AnimationClip
    /// </summary>
    private void GenerateAnimation()
    {
        if (slicedSprites.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先切分精灵图集！", "确定");
            return;
        }

        EnsureDirectory(savePath);
        string clipPath = $"{savePath}/{animName}.anim";

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
        {
            if (!EditorUtility.DisplayDialog("覆盖确认", $"文件已存在: {clipPath}\n是否覆盖？", "覆盖", "取消"))
                return;
        }

        var clip = new AnimationClip
        {
            name = animName,
            frameRate = frameRate
        };

        if (loopTime)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        float timePerFrame = 1f / frameRate;
        int count = loopTime ? slicedSprites.Count + 1 : slicedSprites.Count;
        var keyframes = new ObjectReferenceKeyframe[count];

        for (int i = 0; i < slicedSprites.Count; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i * timePerFrame,
                value = slicedSprites[i]
            };
        }

        if (loopTime)
        {
            keyframes[slicedSprites.Count] = new ObjectReferenceKeyframe
            {
                time = slicedSprites.Count * timePerFrame,
                value = slicedSprites[0]
            };
        }

        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        AssetDatabase.CreateAsset(clip, clipPath);
        AssetDatabase.SaveAssets();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = clip;
        EditorGUIUtility.PingObject(clip);

        Debug.Log($"[精灵图集切分] ✅ 动画已创建: {clipPath}");
        EditorUtility.DisplayDialog("完成",
            $"帧动画创建成功！\n路径: {clipPath}\n帧数: {slicedSprites.Count}\n帧率: {frameRate} FPS",
            "好的");
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

    /// <summary>
    /// 自然排序
    /// </summary>
    private static int FrameAnimationMenuExtension_NaturalCompare(string a, string b)
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
