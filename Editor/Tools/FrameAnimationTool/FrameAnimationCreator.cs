using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 一键创建帧动画编辑器工具
/// 功能：
/// 1. 从选中的图片序列自动创建 Sprite 动画
/// 2. 支持按文件名数字排序
/// 3. 可配置帧率、循环、动画名称
/// 4. 自动挂载到选中的 GameObject 上
/// </summary>
public class FrameAnimationCreator : EditorWindow
{
    // ========== 配置参数 ==========
    private string animationName = "NewFrameAnimation";
    private float frameRate = 12f;
    private bool loopTime = true;
    private string savePath = "Assets/Animations";
    private bool autoAttachToSelected = true;
    private bool createAnimatorController = false;

    // ========== 自动命名 ==========
    private bool autoNameFromFolder = true;

    // ========== 共享 Controller ==========
    private bool useSharedController = false;
    private string sharedControllerName = "CharacterAnimator";

    // ========== 多角色自动分类 ==========
    private bool autoGroupByCharacter = false;
    private Vector2 groupScrollPos;

    // ========== 预制体生成 ==========
    private bool createPrefab = false;
    private bool attachPreviewScript = true;
    private string prefabSavePath = "Assets/Prefabs/FrameAnimations";

    // ========== 数据源 ==========
    private List<Sprite> spriteFrames = new List<Sprite>();
    private Object folderAsset;
    private Vector2 scrollPos;
    private bool showPreview;
    private int previewIndex;
    private double lastPreviewTime;

    // ========== 批量子文件夹 ==========
    private bool scanSubFolders = false;
    private string rootFolderPath = "";
    private Vector2 subFolderScrollPos;
    private List<SubFolderInfo> subFolders = new List<SubFolderInfo>();

    private class SubFolderInfo
    {
        public string name;
        public string fullPath;
        public int spriteCount;
        public bool selected = true;
        public List<Sprite> sprites;
    }

    /// <summary>
    /// 角色分组信息：一个角色包含多个动画子文件夹
    /// </summary>
    private class CharacterGroup
    {
        public string characterName;              // 角色名（如 Knight）
        public string characterPath;              // 角色文件夹路径
        public bool selected = true;
        public bool useSharedController = true;   // 该角色是否合并到一个 Controller
        public List<SubFolderInfo> animations;    // 该角色下的所有动画子文件夹
    }
    private List<CharacterGroup> characterGroups = new List<CharacterGroup>();

    // ========== 样式 ==========
    private GUIStyle headerStyle;
    private GUIStyle dropAreaStyle;
    private GUIStyle previewBoxStyle;

    [MenuItem("UnityToolsHub/帧动画创建工具", false, 200)]
    public static void ShowWindow()
    {
        var window = GetWindow<FrameAnimationCreator>("帧动画创建工具");
        window.minSize = new Vector2(420, 560);
        window.Show();
    }

    private void OnEnable()
    {
        // 自动收集已选中的 Sprite
        CollectSelectedSprites();
    }

    private void InitStyles()
    {
        if (headerStyle != null) return;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };

        dropAreaStyle = new GUIStyle("box")
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Italic,
            normal = { textColor = Color.gray }
        };

        previewBoxStyle = new GUIStyle("box")
        {
            alignment = TextAnchor.MiddleCenter,
            stretchWidth = true
        };
    }

    private void OnGUI()
    {
        InitStyles();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawTitle();
        EditorGUILayout.Space(4);
        DrawFolderInput();
        EditorGUILayout.Space(4);

        if (scanSubFolders && characterGroups.Count > 0 && autoGroupByCharacter)
        {
            DrawCharacterGroupList();
            EditorGUILayout.Space(4);
        }
        else if (scanSubFolders && subFolders.Count > 0)
        {
            DrawSubFolderList();
            EditorGUILayout.Space(4);
        }
        else
        {
            DrawDropArea();
            EditorGUILayout.Space(4);
            DrawSpriteList();
            EditorGUILayout.Space(4);
        }

        DrawSettings();
        EditorGUILayout.Space(4);

        if (scanSubFolders && characterGroups.Count > 0 && autoGroupByCharacter)
        {
            EditorGUILayout.Space(8);
            DrawBatchCreateButton();
        }
        else if (!scanSubFolders || subFolders.Count == 0)
        {
            DrawPreview();
            EditorGUILayout.Space(8);
            DrawCreateButton();
        }
        else
        {
            EditorGUILayout.Space(8);
            DrawBatchCreateButton();
        }

        EditorGUILayout.EndScrollView();
    }

    #region ========== 绘制 UI ==========

    private void DrawTitle()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("🎞️ 帧动画创建工具", headerStyle);
        EditorGUILayout.Space(2);
        DrawSeparator();
    }

    private void DrawFolderInput()
    {
        EditorGUILayout.LabelField("📁 图片来源", EditorStyles.boldLabel);

        // 扫描子文件夹开关
        EditorGUI.BeginChangeCheck();
        scanSubFolders = EditorGUILayout.Toggle("扫描子文件夹（每个文件夹生成一个动画）", scanSubFolders);
        if (EditorGUI.EndChangeCheck())
        {
            if (!scanSubFolders) subFolders.Clear();
            if (folderAsset != null)
            {
                string path = AssetDatabase.GetAssetPath(folderAsset);
                if (scanSubFolders)
                    ScanSubFolders(path);
                else
                    LoadSpritesFromFolder(path);
            }
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("图片文件夹");

        var newFolder = EditorGUILayout.ObjectField(folderAsset, typeof(DefaultAsset), false);
        if (newFolder != folderAsset)
        {
            folderAsset = newFolder;
            if (folderAsset != null)
            {
                string path = AssetDatabase.GetAssetPath(folderAsset);
                if (scanSubFolders)
                    ScanSubFolders(path);
                else
                    LoadSpritesFromFolder(path);

                // 自动根据文件夹名设置动画名称
                if (autoNameFromFolder)
                    animationName = Path.GetFileName(path);
            }
        }

        if (GUILayout.Button("浏览", GUILayout.Width(50)))
        {
            string path = EditorUtility.OpenFolderPanel("选择帧动画图片文件夹", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                if (path.StartsWith(Application.dataPath))
                    path = "Assets" + path.Substring(Application.dataPath.Length);

                if (scanSubFolders)
                    ScanSubFolders(path);
                else
                    LoadSpritesFromFolder(path);
                folderAsset = AssetDatabase.LoadAssetAtPath<Object>(path);

                // 自动根据文件夹名设置动画名称
                if (autoNameFromFolder)
                    animationName = Path.GetFileName(path);
            }
        }
        EditorGUILayout.EndHorizontal();

        // 显示信息
        if (scanSubFolders && subFolders.Count > 0)
        {
            int totalAnims = subFolders.Count(s => s.selected);
            int totalFrames = subFolders.Where(s => s.selected).Sum(s => s.spriteCount);
            EditorGUILayout.HelpBox($"已扫描到 {subFolders.Count} 个子文件夹，选中 {totalAnims} 个，共 {totalFrames} 帧", MessageType.Info);
        }
        else if (spriteFrames.Count > 0)
        {
            EditorGUILayout.HelpBox($"已加载 {spriteFrames.Count} 帧  |  预计时长: {spriteFrames.Count / frameRate:F2}s", MessageType.Info);
        }
    }

    private void DrawDropArea()
    {
        EditorGUILayout.LabelField("🖱️ 拖入 Sprite 图片", EditorStyles.boldLabel);

        var dropRect = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "将 Sprite 图片或包含图片的文件夹拖放到这里", dropAreaStyle);

        // 拖拽处理
        var evt = Event.current;
        if (evt.type == EventType.DragUpdated && dropRect.Contains(evt.mousePosition))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform && dropRect.Contains(evt.mousePosition))
        {
            DragAndDrop.AcceptDrag();
            ProcessDraggedObjects(DragAndDrop.objectReferences);
            evt.Use();
        }
    }

    private void DrawSpriteList()
    {
        if (spriteFrames.Count == 0) return;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"📋 帧列表 ({spriteFrames.Count} 帧)", EditorStyles.boldLabel);

        if (GUILayout.Button("清空", GUILayout.Width(50)))
        {
            spriteFrames.Clear();
            folderAsset = null;
        }
        EditorGUILayout.EndHorizontal();

        // 显示前几帧的缩略图预览
        int displayCount = Mathf.Min(spriteFrames.Count, 10);
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < displayCount; i++)
        {
            if (spriteFrames[i] == null) continue;
            var tex = AssetPreview.GetAssetPreview(spriteFrames[i]);
            if (tex != null)
            {
                GUILayout.Label(tex, GUILayout.Width(48), GUILayout.Height(48));
            }
        }
        if (spriteFrames.Count > displayCount)
        {
            EditorGUILayout.LabelField($"...+{spriteFrames.Count - displayCount}", GUILayout.Width(50));
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSettings()
    {
        DrawSeparator();
        EditorGUILayout.LabelField("⚙️ 动画设置", EditorStyles.boldLabel);

        // 自动命名
        autoNameFromFolder = EditorGUILayout.Toggle("根据文件夹自动命名", autoNameFromFolder);
        using (new EditorGUI.DisabledScope(autoNameFromFolder))
        {
            animationName = EditorGUILayout.TextField("动画名称", animationName);
        }

        frameRate = EditorGUILayout.Slider("帧率 (FPS)", frameRate, 1f, 60f);
        loopTime = EditorGUILayout.Toggle("循环播放", loopTime);
        savePath = EditorGUILayout.TextField("保存路径", savePath);
        autoAttachToSelected = EditorGUILayout.Toggle("自动挂载到选中物体", autoAttachToSelected);

        DrawSeparator();
        EditorGUILayout.LabelField("🎮 Animator 设置", EditorStyles.boldLabel);

        if (scanSubFolders && subFolders.Count > 0)
        {
            // 多角色自动分类
            autoGroupByCharacter = EditorGUILayout.Toggle(new GUIContent("🔄 多角色自动分类",
                "自动识别角色文件夹结构（如 Knight/idle, Knight/walk），为每个角色生成独立的共享 Controller"),
                autoGroupByCharacter);

            if (autoGroupByCharacter)
            {
                // 角色分组模式下，不需要手动设置共享 Controller
                useSharedController = false;
                createAnimatorController = true;
            }
            else
            {
                // 批量模式：提供共享 Controller 选项
                useSharedController = EditorGUILayout.Toggle(new GUIContent("合并到同一个 Controller",
                    "所有子文件夹的动画将添加到同一个 AnimatorController 中（适合同一角色的多个动画）"),
                    useSharedController);

                if (useSharedController)
                {
                    EditorGUI.indentLevel++;
                    sharedControllerName = EditorGUILayout.TextField("Controller 名称", sharedControllerName);
                    createAnimatorController = true;
                    EditorGUI.indentLevel--;
                }
                else
                {
                    createAnimatorController = EditorGUILayout.Toggle("每个动画创建独立 Controller", createAnimatorController);
                }
            }
        }
        else
        {
            useSharedController = false;
            createAnimatorController = EditorGUILayout.Toggle("同时创建 AnimatorController", createAnimatorController);
        }

        DrawSeparator();
        EditorGUILayout.LabelField("📦 预制体生成", EditorStyles.boldLabel);
        createPrefab = EditorGUILayout.Toggle("自动生成预制体", createPrefab);
        if (createPrefab)
        {
            EditorGUI.indentLevel++;
            attachPreviewScript = EditorGUILayout.Toggle("挂载预览工具脚本", attachPreviewScript);
            prefabSavePath = EditorGUILayout.TextField("预制体保存路径", prefabSavePath);
            EditorGUI.indentLevel--;
        }

        // 实时显示时长
        if (spriteFrames.Count > 0)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"总时长: {spriteFrames.Count / frameRate:F2} 秒");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawPreview()
    {
        if (spriteFrames.Count == 0) return;

        DrawSeparator();
        EditorGUILayout.LabelField("👁️ 预览", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        showPreview = EditorGUILayout.Toggle("启用预览", showPreview);

        if (GUILayout.Button("⏮", GUILayout.Width(30))) { previewIndex = 0; }
        if (GUILayout.Button("◀", GUILayout.Width(30))) { previewIndex = Mathf.Max(0, previewIndex - 1); }
        EditorGUILayout.LabelField($"{previewIndex + 1}/{spriteFrames.Count}", GUILayout.Width(60));
        if (GUILayout.Button("▶", GUILayout.Width(30))) { previewIndex = Mathf.Min(spriteFrames.Count - 1, previewIndex + 1); }
        if (GUILayout.Button("⏭", GUILayout.Width(30))) { previewIndex = spriteFrames.Count - 1; }
        EditorGUILayout.EndHorizontal();

        if (showPreview && spriteFrames[previewIndex] != null)
        {
            // 自动播放
            if (EditorApplication.timeSinceStartup - lastPreviewTime > 1f / frameRate)
            {
                previewIndex = (previewIndex + 1) % spriteFrames.Count;
                lastPreviewTime = EditorApplication.timeSinceStartup;
                Repaint();
            }

            var tex = AssetPreview.GetAssetPreview(spriteFrames[previewIndex]);
            if (tex != null)
            {
                var rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
                rect.x = (position.width - 128) * 0.5f;
                GUI.DrawTexture(new Rect(rect.x, rect.y, 128, 128), tex, ScaleMode.ScaleToFit);
            }
        }
    }

    private void DrawCreateButton()
    {
        DrawSeparator();
        GUI.enabled = spriteFrames.Count > 0 && !string.IsNullOrEmpty(animationName);

        var buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fixedHeight = 42,
            fontStyle = FontStyle.Bold
        };

        if (GUILayout.Button("✨ 一键创建帧动画", buttonStyle))
        {
            CreateFrameAnimation();
        }
        GUI.enabled = true;
    }

    /// <summary>
    /// 绘制子文件夹列表
    /// </summary>
    private void DrawSubFolderList()
    {
        DrawSeparator();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"📂 子文件夹列表 ({subFolders.Count} 个)", EditorStyles.boldLabel);

        if (GUILayout.Button("全选", GUILayout.Width(50)))
            subFolders.ForEach(s => s.selected = true);
        if (GUILayout.Button("全不选", GUILayout.Width(55)))
            subFolders.ForEach(s => s.selected = false);
        if (GUILayout.Button("刷新", GUILayout.Width(50)) && folderAsset != null)
            ScanSubFolders(AssetDatabase.GetAssetPath(folderAsset));
        EditorGUILayout.EndHorizontal();

        subFolderScrollPos = EditorGUILayout.BeginScrollView(subFolderScrollPos, GUILayout.MaxHeight(200));
        for (int i = 0; i < subFolders.Count; i++)
        {
            var sf = subFolders[i];
            EditorGUILayout.BeginHorizontal();

            sf.selected = EditorGUILayout.Toggle(sf.selected, GUILayout.Width(20));

            // 文件夹图标 + 名称
            EditorGUILayout.LabelField($"📁 {sf.name}", GUILayout.MinWidth(120));

            // 帧数
            if (sf.spriteCount > 0)
            {
                var prevColor = GUI.color;
                GUI.color = Color.green;
                EditorGUILayout.LabelField($"{sf.spriteCount} 帧", GUILayout.Width(60));
                GUI.color = prevColor;
            }
            else
            {
                var prevColor = GUI.color;
                GUI.color = Color.red;
                EditorGUILayout.LabelField("无图片", GUILayout.Width(60));
                GUI.color = prevColor;
            }

            // 预计时长
            if (sf.spriteCount > 0)
                EditorGUILayout.LabelField($"{sf.spriteCount / frameRate:F2}s", GUILayout.Width(55));

            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        // 批量设置覆盖选项
        EditorGUILayout.Space(2);
        overwriteExisting = EditorGUILayout.Toggle("覆盖已存在的动画文件", overwriteExisting);
    }

    private bool overwriteExisting = false;

    /// <summary>
    /// 绘制批量创建按钮
    /// </summary>
    private void DrawBatchCreateButton()
    {
        DrawSeparator();

        int selectedCount;
        if (autoGroupByCharacter && characterGroups.Count > 0)
        {
            selectedCount = characterGroups.Count(g => g.selected && g.animations.Any(a => a.spriteCount > 0));
        }
        else
        {
            selectedCount = subFolders.Count(s => s.selected && s.spriteCount > 0);
        }

        GUI.enabled = selectedCount > 0;

        var buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fixedHeight = 42,
            fontStyle = FontStyle.Bold
        };

        string label = autoGroupByCharacter && characterGroups.Count > 0
            ? $"🚀 批量创建 ({selectedCount} 个角色)"
            : $"🚀 批量创建帧动画 ({selectedCount} 个)";

        if (GUILayout.Button(label, buttonStyle))
        {
            BatchCreateAnimations();
        }
        GUI.enabled = true;
    }

    /// <summary>
    /// 绘制角色分组列表（多角色自动分类模式）
    /// </summary>
    private void DrawCharacterGroupList()
    {
        DrawSeparator();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"🎭 角色分组 ({characterGroups.Count} 个角色)", EditorStyles.boldLabel);

        if (GUILayout.Button("全选", GUILayout.Width(50)))
            characterGroups.ForEach(g => g.selected = true);
        if (GUILayout.Button("全不选", GUILayout.Width(55)))
            characterGroups.ForEach(g => g.selected = false);
        if (GUILayout.Button("刷新", GUILayout.Width(50)) && folderAsset != null)
            ScanSubFolders(AssetDatabase.GetAssetPath(folderAsset));
        EditorGUILayout.EndHorizontal();

        int totalAnims = characterGroups.Where(g => g.selected).Sum(g => g.animations.Count(a => a.spriteCount > 0));
        int totalFrames = characterGroups.Where(g => g.selected).SelectMany(g => g.animations).Sum(a => a.spriteCount);
        EditorGUILayout.HelpBox($"选中 {totalAnims} 个动画，共 {totalFrames} 帧  |  每个角色将生成独立的 Controller", MessageType.Info);

        groupScrollPos = EditorGUILayout.BeginScrollView(groupScrollPos, GUILayout.MaxHeight(300));

        for (int i = 0; i < characterGroups.Count; i++)
        {
            var group = characterGroups[i];
            int animCount = group.animations.Count(a => a.spriteCount > 0);

            // 角色头部
            EditorGUILayout.BeginHorizontal();

            group.selected = EditorGUILayout.Toggle(group.selected, GUILayout.Width(20));

            // 折叠按钮 + 角色名
            EditorGUILayout.LabelField($"🎭 {group.characterName}", EditorStyles.boldLabel, GUILayout.MinWidth(100));

            // 统计
            var prevColor = GUI.color;
            GUI.color = animCount > 0 ? Color.green : Color.red;
            EditorGUILayout.LabelField($"{animCount} 动画", GUILayout.Width(65));
            GUI.color = prevColor;

            EditorGUILayout.EndHorizontal();

            // 动画子文件夹列表（缩进显示）
            if (group.selected)
            {
                EditorGUI.indentLevel++;
                for (int j = 0; j < group.animations.Count; j++)
                {
                    var anim = group.animations[j];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(30); // 缩进

                    if (anim.spriteCount > 0)
                    {
                        GUI.color = Color.green;
                        EditorGUILayout.LabelField($"📁 {anim.name}", GUILayout.MinWidth(100));
                        EditorGUILayout.LabelField($"{anim.spriteCount} 帧", GUILayout.Width(50));
                        EditorGUILayout.LabelField($"{anim.spriteCount / frameRate:F2}s", GUILayout.Width(45));
                        GUI.color = prevColor;
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        EditorGUILayout.LabelField($"📁 {anim.name} (无图片)", GUILayout.MinWidth(100));
                        GUI.color = prevColor;
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            // 分隔线
            if (i < characterGroups.Count - 1)
            {
                EditorGUILayout.Space(2);
                var rect = EditorGUILayout.GetControlRect(false, 1);
                EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.15f));
                EditorGUILayout.Space(2);
            }
        }

        EditorGUILayout.EndScrollView();

        // 覆盖选项
        EditorGUILayout.Space(2);
        overwriteExisting = EditorGUILayout.Toggle("覆盖已存在的动画文件", overwriteExisting);
    }

    private void DrawSeparator()
    {
        EditorGUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        EditorGUILayout.Space(2);
    }

    #endregion

    #region ========== 核心逻辑 ==========

    /// <summary>
    /// 从文件夹加载所有 Sprite 并按名称排序
    /// </summary>
    private void LoadSpritesFromFolder(string folderPath)
    {
        spriteFrames.Clear();

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            Debug.LogWarning($"[帧动画工具] 文件夹不存在: {folderPath}");
            return;
        }

        // 获取文件夹下所有图片文件
        string[] extensions = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.psd", "*.bmp" };
        var imageFiles = new List<string>();
        foreach (var ext in extensions)
        {
            imageFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly));
        }

        // 按文件名中的数字排序（自然排序）
        imageFiles.Sort(NaturalCompare);

        foreach (var file in imageFiles)
        {
            string assetPath = file.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/"))
            {
                // 如果是绝对路径，尝试转换
                if (assetPath.StartsWith(Application.dataPath))
                {
                    assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                }
                else continue;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
            {
                spriteFrames.Add(sprite);
            }
            else
            {
                // 如果整张图是一个 Sprite（没有切割），尝试直接加载 Texture2D 再获取
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex != null)
                {
                    // 确保 Sprite 模式已设置
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer != null && importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.SaveAndReimport();
                        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    }
                    if (sprite != null)
                        spriteFrames.Add(sprite);
                }
            }
        }

        Debug.Log($"[帧动画工具] 从 {folderPath} 加载了 {spriteFrames.Count} 帧");
    }

    /// <summary>
    /// 处理拖拽进来的对象
    /// </summary>
    private void ProcessDraggedObjects(Object[] objects)
    {
        foreach (var obj in objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);

            if (AssetDatabase.IsValidFolder(path))
            {
                LoadSpritesFromFolder(path);
                return;
            }

            // 单个 Sprite
            var sprite = obj as Sprite;
            if (sprite != null)
            {
                spriteFrames.Add(sprite);
                continue;
            }

            // Texture2D 尝试转 Sprite
            var tex = obj as Texture2D;
            if (tex != null)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s != null) spriteFrames.Add(s);
            }
        }
    }

    /// <summary>
    /// 收集当前选中的 Sprite
    /// </summary>
    private void CollectSelectedSprites()
    {
        foreach (var obj in Selection.objects)
        {
            var sprite = obj as Sprite;
            if (sprite != null)
            {
                spriteFrames.Add(sprite);
                continue;
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.IsValidFolder(path))
            {
                folderAsset = obj;
                rootFolderPath = path;
                if (scanSubFolders)
                    ScanSubFolders(path);
                else
                    LoadSpritesFromFolder(path);
                return;
            }
        }
    }

    /// <summary>
    /// 扫描子文件夹
    /// </summary>
    private void ScanSubFolders(string folderPath)
    {
        subFolders.Clear();
        rootFolderPath = folderPath;

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            Debug.LogWarning($"[帧动画工具] 文件夹不存在: {folderPath}");
            return;
        }

        // 先检查根文件夹自身是否包含图片
        var rootSprites = GetSpritesInFolder(folderPath);
        if (rootSprites.Count > 0)
        {
            subFolders.Add(new SubFolderInfo
            {
                name = Path.GetFileName(folderPath) + " (根目录)",
                fullPath = folderPath,
                spriteCount = rootSprites.Count,
                sprites = rootSprites,
                selected = true
            });
        }

        // 获取所有子文件夹（递归获取但只取直接子目录中的图片）
        var subDirs = Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories);
        foreach (var dir in subDirs)
        {
            string assetPath = dir.Replace('\\', '/');
            if (assetPath.StartsWith(Application.dataPath))
                assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

            var sprites = GetSpritesInFolder(assetPath);
            string dirName = assetPath.Substring(folderPath.Length).TrimStart('/');

            subFolders.Add(new SubFolderInfo
            {
                name = dirName,
                fullPath = assetPath,
                spriteCount = sprites.Count,
                sprites = sprites,
                selected = sprites.Count > 0 // 默认只选中有图片的
            });
        }

        // 按名称排序
        subFolders.Sort((a, b) => NaturalCompare(a.name, b.name));

        Debug.Log($"[帧动画工具] 扫描完成: {subFolders.Count} 个子文件夹，{subFolders.Count(s => s.spriteCount > 0)} 个含图片");

        // ===== 自动构建角色分组 =====
        BuildCharacterGroups(folderPath);
    }

    /// <summary>
    /// 根据文件夹层级自动识别角色分组
    /// 识别逻辑：
    ///   根目录/角色A/动画1/ → 角色A 包含 动画1
    ///   根目录/角色A/动画2/ → 角色A 包含 动画2
    ///   根目录/角色B/动画1/ → 角色B 包含 动画1
    /// 如果根目录自身有图片 → 归入「_Root」组
    /// 如果只有一层子文件夹 → 不分组（退化为普通模式）
    /// </summary>
    private void BuildCharacterGroups(string rootPath)
    {
        characterGroups.Clear();

        // 只保留有图片的子文件夹
        var validFolders = subFolders.Where(s => s.spriteCount > 0).ToList();
        if (validFolders.Count == 0) return;

        // 检查文件夹深度：找到最浅的含有多个子文件夹的层级
        // 如果子文件夹名包含 '/'，说明有更深层级结构
        var depthMap = new Dictionary<int, List<SubFolderInfo>>(); // depth -> folders
        foreach (var sf in validFolders)
        {
            // 计算相对路径的深度（相对于 rootPath）
            string relativePath = sf.fullPath.Length > rootPath.Length
                ? sf.fullPath.Substring(rootPath.Length).TrimStart('/')
                : "";
            int depth = relativePath.Count(c => c == '/');
            if (!depthMap.ContainsKey(depth))
                depthMap[depth] = new List<SubFolderInfo>();
            depthMap[depth].Add(sf);
        }

        // 判断是否有多层结构
        // 如果所有含图片的文件夹都在同一深度且深度>=1，检查它们是否有共同的父级
        var allAnimFolders = validFolders.Where(s => !s.name.Contains("(根目录)")).ToList();

        // 按父文件夹分组
        var parentGroups = new Dictionary<string, List<SubFolderInfo>>();
        foreach (var sf in allAnimFolders)
        {
            string parentDir = Path.GetDirectoryName(sf.fullPath)?.Replace('\\', '/') ?? "";
            if (parentDir.StartsWith(Application.dataPath))
                parentDir = "Assets" + parentDir.Substring(Application.dataPath.Length);

            if (!parentGroups.ContainsKey(parentDir))
                parentGroups[parentDir] = new List<SubFolderInfo>();
            parentGroups[parentDir].Add(sf);
        }

        // 检查是否有多个不同的父文件夹（说明有多角色结构）
        var distinctParents = parentGroups.Keys
            .Where(p => p != rootPath && p.StartsWith(rootPath))
            .ToList();

        if (distinctParents.Count <= 1)
        {
            // 没有多角色结构，不分组
            Debug.Log($"[帧动画工具] 未检测到多角色结构，使用普通模式");
            return;
        }

        // 构建角色分组
        foreach (var kvp in parentGroups)
        {
            string parentPath = kvp.Key;
            var anims = kvp.Value;

            // 跳过根目录自身的图片（如果有）
            if (parentPath == rootPath) continue;

            // 角色名 = 父文件夹相对于 rootPath 的名称
            string charName = parentPath.Length > rootPath.Length
                ? parentPath.Substring(rootPath.Length).TrimStart('/')
                : Path.GetFileName(parentPath);

            characterGroups.Add(new CharacterGroup
            {
                characterName = charName,
                characterPath = parentPath,
                selected = true,
                useSharedController = true,
                animations = anims
            });
        }

        // 处理根目录自身的图片（如果有）
        var rootAnims = validFolders.Where(s => s.name.Contains("(根目录)")).ToList();
        if (rootAnims.Count > 0)
        {
            characterGroups.Insert(0, new CharacterGroup
            {
                characterName = "_Root",
                characterPath = rootPath,
                selected = true,
                useSharedController = true,
                animations = rootAnims
            });
        }

        // 按角色名排序
        characterGroups.Sort((a, b) => NaturalCompare(a.characterName, b.characterName));

        Debug.Log($"[帧动画工具] 🎭 检测到 {characterGroups.Count} 个角色: {string.Join(", ", characterGroups.Select(g => g.characterName))}");
    }

    /// <summary>
    /// 获取指定文件夹中的所有 Sprite（不递归子目录）
    /// </summary>
    private List<Sprite> GetSpritesInFolder(string folderPath)
    {
        var sprites = new List<Sprite>();
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return sprites;

        string[] extensions = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.psd", "*.bmp" };
        var imageFiles = new List<string>();
        foreach (var ext in extensions)
        {
            imageFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly));
        }
        imageFiles.Sort(NaturalCompare);

        foreach (var file in imageFiles)
        {
            string assetPath = file.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/"))
            {
                if (assetPath.StartsWith(Application.dataPath))
                    assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                else continue;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
            {
                sprites.Add(sprite);
            }
            else
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex != null)
                {
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer != null && importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.SaveAndReimport();
                        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    }
                    if (sprite != null)
                        sprites.Add(sprite);
                }
            }
        }
        return sprites;
    }

    /// <summary>
    /// 批量创建帧动画
    /// </summary>
    private void BatchCreateAnimations()
    {
        // 根据模式选择数据源
        if (autoGroupByCharacter && characterGroups.Count > 0)
        {
            BatchCreateCharacterGrouped();
            return;
        }

        var selectedFolders = subFolders.Where(s => s.selected && s.spriteCount > 0).ToList();
        if (selectedFolders.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请至少选中一个含有图片的子文件夹！", "确定");
            return;
        }

        // 确保保存路径存在
        EnsureDirectory(savePath);

        int successCount = 0;
        int skipCount = 0;
        int failCount = 0;
        var createdClips = new List<AnimationClip>();
        AnimatorController sharedCtrl = null;
        string sharedCtrlPath = null;

        try
        {
            // ===== 阶段一：创建所有 AnimationClip =====
            for (int i = 0; i < selectedFolders.Count; i++)
            {
                var sf = selectedFolders[i];

                EditorUtility.DisplayProgressBar("批量创建帧动画",
                    $"正在创建动画: {sf.name} ({i + 1}/{selectedFolders.Count})",
                    (float)i / selectedFolders.Count * 0.7f);

                // 用文件夹名作为动画名（自动识别）
                string animName = SanitizeFileName(sf.name);
                string clipPath = $"{savePath}/{animName}.anim";

                // 检查是否已存在
                if (!overwriteExisting && AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
                {
                    Debug.Log($"[帧动画工具] ⏭️ 跳过（已存在）: {clipPath}");
                    skipCount++;
                    continue;
                }

                // 创建 AnimationClip
                var clip = new AnimationClip
                {
                    name = animName,
                    frameRate = frameRate
                };

                if (loopTime)
                {
                    var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
                    clipSettings.loopTime = true;
                    AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
                }

                float timePerFrame = 1f / frameRate;
                int frameCount = loopTime ? sf.sprites.Count + 1 : sf.sprites.Count;
                var keyframes = new ObjectReferenceKeyframe[frameCount];

                for (int j = 0; j < sf.sprites.Count; j++)
                {
                    keyframes[j] = new ObjectReferenceKeyframe
                    {
                        time = j * timePerFrame,
                        value = sf.sprites[j]
                    };
                }

                if (loopTime)
                {
                    keyframes[sf.sprites.Count] = new ObjectReferenceKeyframe
                    {
                        time = sf.sprites.Count * timePerFrame,
                        value = sf.sprites[0]
                    };
                }

                var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

                AssetDatabase.CreateAsset(clip, clipPath);
                createdClips.Add(clip);

                Debug.Log($"[帧动画工具] ✅ 创建动画: {clipPath} ({sf.sprites.Count}帧)");
                successCount++;
            }

            // ===== 阶段二：创建 AnimatorController =====
            sharedCtrlPath = $"{savePath}/{SanitizeFileName(sharedControllerName)}.controller";

            if (useSharedController && createdClips.Count > 0)
            {
                EditorUtility.DisplayProgressBar("批量创建帧动画", "正在创建共享 AnimatorController...", 0.75f);

                // 创建或覆盖共享 Controller
                if (!overwriteExisting && AssetDatabase.LoadAssetAtPath<AnimatorController>(sharedCtrlPath) != null)
                {
                    // 如果已存在，加载并追加
                    sharedCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(sharedCtrlPath);
                    Debug.Log($"[帧动画工具] 📎 追加到已有 Controller: {sharedCtrlPath}");
                }
                else
                {
                    sharedCtrl = AnimatorController.CreateAnimatorControllerAtPath(sharedCtrlPath);
                    Debug.Log($"[帧动画工具] 🎮 创建共享 Controller: {sharedCtrlPath}");
                }

                if (sharedCtrl != null)
                {
                    // 为每个 Clip 添加状态
                    var layer = sharedCtrl.layers[0];
                    var stateMachine = layer.stateMachine;

                    foreach (var clip in createdClips)
                    {
                        // 检查是否已存在同名状态
                        bool exists = false;
                        foreach (var existingState in stateMachine.states)
                        {
                            if (existingState.state.name == clip.name)
                            {
                                exists = true;
                                // 更新 Motion
                                existingState.state.motion = clip;
                                break;
                            }
                        }

                        if (!exists)
                        {
                            var state = stateMachine.AddState(clip.name);
                            state.motion = clip;
                            state.speed = 1f;

                            // 第一个动画设为默认状态
                            if (stateMachine.defaultState == null)
                                stateMachine.defaultState = state;
                        }
                    }

                    // 如果只有一个动画且需要循环，设为默认
                    if (createdClips.Count == 1)
                    {
                        var defaultState = stateMachine.defaultState;
                        if (defaultState != null)
                            defaultState.motion = createdClips[0];
                    }
                }
            }
            else if (createAnimatorController)
            {
                // 独立 Controller 模式：每个动画单独一个 Controller
                EditorUtility.DisplayProgressBar("批量创建帧动画", "正在创建独立 AnimatorController...", 0.75f);

                foreach (var clip in createdClips)
                {
                    string ctrlPath = $"{savePath}/{clip.name}Controller.controller";
                    UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
                }
            }

            // ===== 阶段三：生成预制体 =====
            if (createPrefab)
            {
                EditorUtility.DisplayProgressBar("批量创建帧动画", "正在生成预制体...", 0.9f);

                RuntimeAnimatorController prefabCtrl = sharedCtrl;
                foreach (var clip in createdClips)
                {
                    string animName = clip.name;
                    if (prefabCtrl == null && createAnimatorController)
                    {
                        string ctrlPath = $"{savePath}/{animName}Controller.controller";
                        prefabCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
                    }
                    CreatePrefabWithAnimation(animName, clip, prefabCtrl,
                        selectedFolders.First(sf => SanitizeFileName(sf.name) == animName).sprites);
                }
            }

            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // ===== 汇总结果 =====
        string sharedInfo = useSharedController && sharedCtrl != null
            ? $"\n🎮 共享 Controller: {sharedCtrlPath}\n   包含 {createdClips.Count} 个动画状态"
            : "";

        string resultMsg = $"批量创建完成！\n\n" +
                           $"✅ 成功: {successCount} 个\n" +
                           (skipCount > 0 ? $"⏭️ 跳过: {skipCount} 个（已存在）\n" : "") +
                           (failCount > 0 ? $"❌ 失败: {failCount} 个\n" : "") +
                           $"📁 保存路径: {savePath}" +
                           sharedInfo;

        Debug.Log($"[帧动画工具] 批量创建完成: 成功{successCount}, 跳过{skipCount}, 失败{failCount}");
        EditorUtility.DisplayDialog("批量创建完成", resultMsg, "好的");

        // 高亮创建的资源
        if (useSharedController && sharedCtrl != null)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = sharedCtrl;
            EditorGUIUtility.PingObject(sharedCtrl);
        }
        else if (createdClips.Count > 0)
        {
            EditorUtility.FocusProjectWindow();
            Selection.objects = createdClips.ToArray();
        }
    }

    /// <summary>
    /// 多角色分组模式：为每个角色创建独立的 Controller
    /// 文件夹结构示例:
    ///   Characters/Knight/idle/ → KnightController.controller + idle.anim
    ///   Characters/Knight/walk/ → KnightController.controller + walk.anim
    ///   Characters/Mage/cast/   → MageController.controller   + cast.anim
    /// </summary>
    private void BatchCreateCharacterGrouped()
    {
        var selectedGroups = characterGroups.Where(g => g.selected && g.animations.Any(a => a.spriteCount > 0)).ToList();
        if (selectedGroups.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请至少选中一个含有动画的角色！", "确定");
            return;
        }

        int totalAnimCount = selectedGroups.Sum(g => g.animations.Count(a => a.spriteCount > 0));
        int processedCount = 0;
        int successCount = 0;
        int skipCount = 0;

        var allCreatedClips = new List<AnimationClip>();
        var allCreatedControllers = new List<AnimatorController>();
        var summaryLines = new List<string>();

        try
        {
            foreach (var group in selectedGroups)
            {
                var animFolders = group.animations.Where(a => a.spriteCount > 0).ToList();
                if (animFolders.Count == 0) continue;

                // 为每个角色创建子目录
                string charSavePath = $"{savePath}/{SanitizeFileName(group.characterName)}";
                EnsureDirectory(charSavePath);

                var createdClips = new List<AnimationClip>();

                // ===== 阶段一：为该角色创建所有 AnimationClip =====
                foreach (var anim in animFolders)
                {
                    processedCount++;
                    EditorUtility.DisplayProgressBar("多角色批量创建",
                        $"[{group.characterName}] {anim.name} ({processedCount}/{totalAnimCount})",
                        (float)processedCount / totalAnimCount * 0.7f);

                    string animName = SanitizeFileName(anim.name);
                    string clipPath = $"{charSavePath}/{animName}.anim";

                    if (!overwriteExisting && AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
                    {
                        Debug.Log($"[帧动画工具] ⏭️ 跳过（已存在）: {clipPath}");
                        skipCount++;
                        continue;
                    }

                    var clip = new AnimationClip
                    {
                        name = animName,
                        frameRate = frameRate
                    };

                    if (loopTime)
                    {
                        var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
                        clipSettings.loopTime = true;
                        AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
                    }

                    float timePerFrame = 1f / frameRate;
                    int frameCount = loopTime ? anim.sprites.Count + 1 : anim.sprites.Count;
                    var keyframes = new ObjectReferenceKeyframe[frameCount];

                    for (int j = 0; j < anim.sprites.Count; j++)
                    {
                        keyframes[j] = new ObjectReferenceKeyframe
                        {
                            time = j * timePerFrame,
                            value = anim.sprites[j]
                        };
                    }

                    if (loopTime)
                    {
                        keyframes[anim.sprites.Count] = new ObjectReferenceKeyframe
                        {
                            time = anim.sprites.Count * timePerFrame,
                            value = anim.sprites[0]
                        };
                    }

                    var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

                    AssetDatabase.CreateAsset(clip, clipPath);
                    createdClips.Add(clip);
                    allCreatedClips.Add(clip);
                    successCount++;

                    Debug.Log($"[帧动画工具] ✅ [{group.characterName}] {clipPath} ({anim.sprites.Count}帧)");
                }

                // ===== 阶段二：为该角色创建共享 AnimatorController =====
                if (createdClips.Count > 0)
                {
                    EditorUtility.DisplayProgressBar("多角色批量创建",
                        $"正在创建 {group.characterName} 的 Controller...", 0.85f);

                    string ctrlPath = $"{charSavePath}/{SanitizeFileName(group.characterName)}.controller";
                    AnimatorController ctrl = null;

                    if (!overwriteExisting && AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null)
                    {
                        ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
                        Debug.Log($"[帧动画工具] 📎 追加到已有 Controller: {ctrlPath}");
                    }
                    else
                    {
                        ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
                        Debug.Log($"[帧动画工具] 🎮 创建 Controller: {ctrlPath}");
                    }

                    if (ctrl != null)
                    {
                        var layer = ctrl.layers[0];
                        var stateMachine = layer.stateMachine;

                        foreach (var clip in createdClips)
                        {
                            bool exists = stateMachine.states.Any(s => s.state.name == clip.name);
                            if (!exists)
                            {
                                var state = stateMachine.AddState(clip.name);
                                state.motion = clip;
                                state.speed = 1f;

                                if (stateMachine.defaultState == null || stateMachine.states.Length == 1)
                                    stateMachine.defaultState = state;
                            }
                        }

                        allCreatedControllers.Add(ctrl);
                    }

                    summaryLines.Add($"🎭 {group.characterName}: {createdClips.Count} 个动画 → {ctrlPath}");

                    // ===== 阶段三：为该角色生成一个完整预制体 =====
                    if (createPrefab)
                    {
                        CreateCharacterPrefab(group.characterName, ctrl, animFolders, charSavePath);
                    }
                }
            }

            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // ===== 汇总结果 =====
        string summary = string.Join("\n", summaryLines);
        string resultMsg = $"多角色批量创建完成！\n\n" +
                           $"✅ 成功: {successCount} 个动画\n" +
                           (skipCount > 0 ? $"⏭️ 跳过: {skipCount} 个（已存在）\n" : "") +
                           $"📁 保存路径: {savePath}\n\n" +
                           $"━━━ 角色详情 ━━━\n{summary}";

        Debug.Log($"[帧动画工具] 多角色批量创建完成: 成功{successCount}, 跳过{skipCount}");
        EditorUtility.DisplayDialog("多角色创建完成", resultMsg, "好的");

        // 高亮创建的 Controller
        if (allCreatedControllers.Count > 0)
        {
            EditorUtility.FocusProjectWindow();
            Selection.objects = allCreatedControllers.ToArray();
        }
    }

    /// <summary>
    /// 清理文件名中的非法字符
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            name = name.Replace(c, '_');
        }
        // 替换常见分隔符
        name = name.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
        return name;
    }

    /// <summary>
    /// 一键创建帧动画
    /// </summary>
    private void CreateFrameAnimation()
    {
        if (spriteFrames.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先添加 Sprite 帧！", "确定");
            return;
        }

        // 确保保存路径存在
        EnsureDirectory(savePath);

        string clipPath = $"{savePath}/{animationName}.anim";

        // 检查是否已存在
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
        {
            if (!EditorUtility.DisplayDialog("覆盖确认", $"动画文件已存在:\n{clipPath}\n\n是否覆盖？", "覆盖", "取消"))
            {
                return;
            }
        }

        // 创建 AnimationClip
        AnimationClip clip = CreateSpriteAnimationClip();

        if (clip == null)
        {
            EditorUtility.DisplayDialog("错误", "创建动画失败，请查看控制台日志。", "确定");
            return;
        }

        // 保存
        AssetDatabase.CreateAsset(clip, clipPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[帧动画工具] ✅ 动画已创建: {clipPath}");

        // 可选：创建 AnimatorController
        RuntimeAnimatorController controller = null;
        if (createAnimatorController)
        {
            string controllerPath = $"{savePath}/{animationName}Controller.controller";
            controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
            Debug.Log($"[帧动画工具] ✅ AnimatorController 已创建: {controllerPath}");
        }

        // 自动挂载到选中物体
        if (autoAttachToSelected && Selection.activeGameObject != null)
        {
            AttachAnimationToGameObject(Selection.activeGameObject, clip);
        }

        // 可选：生成预制体
        GameObject createdPrefab = null;
        if (createPrefab)
        {
            createdPrefab = CreatePrefabWithAnimation(animationName, clip, controller, spriteFrames);
        }

        // 高亮选中创建的资源
        EditorUtility.FocusProjectWindow();
        if (createdPrefab != null)
        {
            Selection.activeObject = createdPrefab;
            EditorGUIUtility.PingObject(createdPrefab);
        }
        else
        {
            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
        }

        string extra = createPrefab ? $"\n预制体: {prefabSavePath}/{animationName}.prefab" : "";
        EditorUtility.DisplayDialog("完成", $"帧动画创建成功！\n\n路径: {clipPath}\n帧数: {spriteFrames.Count}\n帧率: {frameRate} FPS\n时长: {spriteFrames.Count / frameRate:F2}s{extra}", "好的");
    }

    /// <summary>
    /// 创建 Sprite 帧动画的 AnimationClip
    /// </summary>
    private AnimationClip CreateSpriteAnimationClip()
    {
        var clip = new AnimationClip
        {
            name = animationName,
            frameRate = frameRate
        };

        // 设置循环
        if (loopTime)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        // 创建 SpriteRenderer 的 ObjectReferenceKeyframes
        var keyframes = new ObjectReferenceKeyframe[spriteFrames.Count];
        float timePerFrame = 1f / frameRate;

        for (int i = 0; i < spriteFrames.Count; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i * timePerFrame,
                value = spriteFrames[i]
            };
        }

        // 绑定到 SpriteRenderer.m_Sprite 属性
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        // 如果需要循环，在最后一帧后回到第一帧
        if (loopTime)
        {
            var loopKeyframes = new ObjectReferenceKeyframe[keyframes.Length + 1];
            System.Array.Copy(keyframes, loopKeyframes, keyframes.Length);
            loopKeyframes[keyframes.Length] = new ObjectReferenceKeyframe
            {
                time = spriteFrames.Count * timePerFrame,
                value = spriteFrames[0]
            };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, loopKeyframes);
        }

        return clip;
    }

    /// <summary>
    /// 将动画挂载到 GameObject
    /// </summary>
    private void AttachAnimationToGameObject(GameObject go, AnimationClip clip)
    {
        // 尝试获取 Animator
        var animator = go.GetComponent<Animator>();
        if (animator == null)
        {
            animator = go.AddComponent<Animator>();
        }

        // 确保有 SpriteRenderer
        if (go.GetComponent<SpriteRenderer>() == null)
        {
            go.AddComponent<SpriteRenderer>();
        }

        // 如果创建了 Controller，绑定 Controller；否则直接播放 Clip
        if (createAnimatorController)
        {
            string controllerPath = $"{savePath}/{animationName}Controller.controller";
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }
        }

        Debug.Log($"[帧动画工具] ✅ 动画已挂载到 {go.name}");
    }

    /// <summary>
    /// 为角色创建完整预制体（一个角色一个预制体，包含所有动画）
    /// 预制体结构：SpriteRenderer + Animator(共享Controller) + FrameAnimPreview(多动画)
    /// </summary>
    private void CreateCharacterPrefab(string characterName, AnimatorController controller,
        List<SubFolderInfo> animFolders, string charAnimPath)
    {
        string targetDir = $"{prefabSavePath}/{SanitizeFileName(characterName)}";
        EnsureDirectory(targetDir);

        var go = new GameObject(SanitizeFileName(characterName));

        // SpriteRenderer — 默认显示 idle 的第一帧，没有 idle 则显示第一个动画
        var sr = go.AddComponent<SpriteRenderer>();
        var idleFolder = animFolders.FirstOrDefault(a => a.name.ToLower().Contains("idle"))
                      ?? animFolders.FirstOrDefault();
        if (idleFolder != null && idleFolder.sprites.Count > 0)
            sr.sprite = idleFolder.sprites[0];

        // Animator — 绑定角色的共享 Controller
        var animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        // FrameAnimPreview — 包含所有动画
        if (attachPreviewScript)
        {
            var previewType = System.Type.GetType("FrameAnimPreview");
            if (previewType != null)
            {
                var preview = go.AddComponent(previewType);

                // 构建 AnimClip 列表
                var animClipType = previewType.GetNestedType("AnimClip");
                if (animClipType != null)
                {
                    // 通过反射创建 List<AnimClip> 并赋值
                    var listType = typeof(List<>).MakeGenericType(animClipType);
                    var animList = System.Activator.CreateInstance(listType);

                    var addMethod = listType.GetMethod("Add");
                    var nameField = animClipType.GetField("name");
                    var framesField = animClipType.GetField("frames");
                    var loopField = animClipType.GetField("loop");

                    foreach (var animFolder in animFolders)
                    {
                        if (animFolder.sprites.Count == 0) continue;

                        var animClip = System.Activator.CreateInstance(animClipType);
                        nameField?.SetValue(animClip, SanitizeFileName(animFolder.name));
                        framesField?.SetValue(animClip, new List<Sprite>(animFolder.sprites));
                        loopField?.SetValue(animClip, loopTime);
                        addMethod?.Invoke(animList, new[] { animClip });
                    }

                    previewType.GetField("animations")?.SetValue(preview, animList);
                }

                // 设置其他字段
                previewType.GetField("frameRate")?.SetValue(preview, frameRate);
                previewType.GetField("playInEditMode")?.SetValue(preview, true);
                previewType.GetField("playOnAwake")?.SetValue(preview, false);

                // 设置默认动画为 idle（如果有）
                string defaultName = idleFolder != null ? SanitizeFileName(idleFolder.name) : "";
                previewType.GetField("defaultAnimName")?.SetValue(preview, defaultName);

                Debug.Log($"[帧动画工具] 🎬 角色预览脚本已挂载: {characterName} ({animFolders.Count} 个动画)");
            }
            else
            {
                Debug.LogWarning("[帧动画工具] 未找到 FrameAnimPreview 脚本，跳过挂载。");
            }
        }

        // 保存预制体
        string prefabPath = $"{targetDir}/{SanitizeFileName(characterName)}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Debug.Log($"[帧动画工具] 📦 角色预制体已创建: {prefabPath}");

        Object.DestroyImmediate(go);
    }

    /// <summary>
    /// 创建带有帧动画的预制体（单动画模式）
    /// </summary>
    /// <param name="name">预制体名称</param>
    /// <param name="clip">动画剪辑</param>
    /// <param name="controller">AnimatorController（可为null）</param>
    /// <param name="sprites">Sprite 列表</param>
    /// <param name="customSavePath">自定义保存路径（为null时使用全局 prefabSavePath）</param>
    private GameObject CreatePrefabWithAnimation(string name, AnimationClip clip,
        RuntimeAnimatorController controller, List<Sprite> sprites, string customSavePath = null)
    {
        string targetDir = customSavePath ?? prefabSavePath;
        EnsureDirectory(targetDir);

        // 创建临时 GameObject
        var go = new GameObject(name);

        // 添加 SpriteRenderer，设置第一帧
        var sr = go.AddComponent<SpriteRenderer>();
        if (sprites.Count > 0)
            sr.sprite = sprites[0];

        // 添加 Animator
        var animator = go.AddComponent<Animator>();
        if (controller != null)
        {
            animator.runtimeAnimatorController = controller;
        }
        else
        {
            // 没有 Controller 时，直接用 AnimationClip 创建一个
            string ctrlPath = $"{targetDir}/{name}Controller.controller";
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
            animator.runtimeAnimatorController = ctrl;
        }

        // 可选：挂载预览脚本（通过反射，避免 Editor 程序集直接引用运行时类型）
        if (attachPreviewScript)
        {
            var previewType = System.Type.GetType("FrameAnimPreview");
            if (previewType != null)
            {
                var preview = go.AddComponent(previewType);

                // 构建 AnimClip 列表（单动画模式）
                var animClipType = previewType.GetNestedType("AnimClip");
                if (animClipType != null)
                {
                    var listType = typeof(List<>).MakeGenericType(animClipType);
                    var animList = System.Activator.CreateInstance(listType);
                    var addMethod = listType.GetMethod("Add");

                    var animClip = System.Activator.CreateInstance(animClipType);
                    animClipType.GetField("name")?.SetValue(animClip, name);
                    animClipType.GetField("frames")?.SetValue(animClip, new List<Sprite>(sprites));
                    animClipType.GetField("loop")?.SetValue(animClip, loopTime);
                    addMethod?.Invoke(animList, new[] { animClip });

                    previewType.GetField("animations")?.SetValue(preview, animList);
                }

                previewType.GetField("frameRate")?.SetValue(preview, frameRate);
                previewType.GetField("playInEditMode")?.SetValue(preview, true);
                previewType.GetField("playOnAwake")?.SetValue(preview, false);
                previewType.GetField("defaultAnimName")?.SetValue(preview, name);

                Debug.Log($"[帧动画工具] 🎬 预览脚本已挂载: {name}");
            }
            else
            {
                Debug.LogWarning("[帧动画工具] 未找到 FrameAnimPreview 脚本，跳过挂载。请确认 Assets/Scripts/FrameAnimPreview.cs 存在。");
            }
        }

        // 保存为预制体
        string prefabPath = $"{targetDir}/{name}.prefab";

        // 检查是否已存在
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            // 覆盖已有预制体
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Debug.Log($"[帧动画工具] 📦 预制体已覆盖: {prefabPath}");
        }
        else
        {
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Debug.Log($"[帧动画工具] 📦 预制体已创建: {prefabPath}");
        }

        // 销毁临时对象
        Object.DestroyImmediate(go);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    #endregion

    #region ========== 工具方法 ==========

    /// <summary>
    /// 确保目录存在
    /// </summary>
    private static void EnsureDirectory(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts = path.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    /// <summary>
    /// 自然排序比较器（将文件名中的数字作为数值比较）
    /// 例如: frame_1, frame_2, frame_10 而不是 frame_1, frame_10, frame_2
    /// </summary>
    private static int NaturalCompare(string a, string b)
    {
        string nameA = Path.GetFileNameWithoutExtension(a);
        string nameB = Path.GetFileNameWithoutExtension(b);

        int ia = 0, ib = 0;
        while (ia < nameA.Length && ib < nameB.Length)
        {
            char ca = nameA[ia];
            char cb = nameB[ib];

            if (char.IsDigit(ca) && char.IsDigit(cb))
            {
                // 提取数字部分进行比较
                int numA = 0, numB = 0;
                while (ia < nameA.Length && char.IsDigit(nameA[ia]))
                {
                    numA = numA * 10 + (nameA[ia] - '0');
                    ia++;
                }
                while (ib < nameB.Length && char.IsDigit(nameB[ib]))
                {
                    numB = numB * 10 + (nameB[ib] - '0');
                    ib++;
                }
                if (numA != numB) return numA.CompareTo(numB);
            }
            else
            {
                int cmp = char.ToLower(ca).CompareTo(char.ToLower(cb));
                if (cmp != 0) return cmp;
                ia++;
                ib++;
            }
        }
        return nameA.Length.CompareTo(nameB.Length);
    }

    #endregion
}
