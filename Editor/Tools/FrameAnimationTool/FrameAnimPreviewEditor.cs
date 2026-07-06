using UnityEngine;
using UnityEditor;

/// <summary>
/// FrameAnimPreview 自定义 Inspector
/// 支持多动画切换、播放控制、帧预览
/// </summary>
[CustomEditor(typeof(FrameAnimPreview))]
public class FrameAnimPreviewEditor : Editor
{
    private SerializedProperty animations;
    private SerializedProperty frameRate;
    private SerializedProperty defaultAnimName;
    private SerializedProperty playInEditMode;
    private SerializedProperty playOnAwake;

    private int selectedAnimIndex;
    private string[] animNames = new string[0];

    private void OnEnable()
    {
        animations = serializedObject.FindProperty("animations");
        frameRate = serializedObject.FindProperty("frameRate");
        defaultAnimName = serializedObject.FindProperty("defaultAnimName");
        playInEditMode = serializedObject.FindProperty("playInEditMode");
        playOnAwake = serializedObject.FindProperty("playOnAwake");
        RefreshAnimNames();
    }

    private void RefreshAnimNames()
    {
        var preview = (FrameAnimPreview)target;
        animNames = preview.GetAnimationNames().ToArray();
        if (animNames.Length == 0)
            animNames = new string[] { "(无动画)" };

        // 尝试匹配当前播放的动画
        selectedAnimIndex = 0;
        for (int i = 0; i < animNames.Length; i++)
        {
            if (animNames[i] == preview.CurrentAnimName)
            {
                selectedAnimIndex = i;
                break;
            }
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var preview = (FrameAnimPreview)target;

        // ===== 标题 =====
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("🎞️ 帧动画预览工具", new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        });
        DrawSeparator();

        // ===== 动画列表 =====
        EditorGUILayout.PropertyField(animations, new GUIContent("动画列表"), true);

        int totalFrames = 0;
        foreach (var anim in preview.animations)
            totalFrames += anim.frames.Count;

        if (preview.animations.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"共 {preview.animations.Count} 个动画，{totalFrames} 帧",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("请添加动画剪辑并拖入 Sprite 帧", MessageType.Warning);
        }

        DrawSeparator();

        // ===== 播放设置 =====
        EditorGUILayout.LabelField("⚙️ 播放设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(frameRate);
        EditorGUILayout.PropertyField(defaultAnimName);
        EditorGUILayout.PropertyField(playInEditMode);
        EditorGUILayout.PropertyField(playOnAwake);

        DrawSeparator();

        // ===== 动画切换 =====
        if (preview.animations.Count > 0)
        {
            EditorGUILayout.LabelField("🔀 动画切换", EditorStyles.boldLabel);

            // 动画选择下拉
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("选择动画");
            int newIndex = EditorGUILayout.Popup(selectedAnimIndex, animNames);
            if (newIndex != selectedAnimIndex)
            {
                selectedAnimIndex = newIndex;
                preview.Play(animNames[selectedAnimIndex]);
            }
            EditorGUILayout.EndHorizontal();

            // 当前动画信息
            if (!string.IsNullOrEmpty(preview.CurrentAnimName))
            {
                EditorGUILayout.LabelField($"当前动画: {preview.CurrentAnimName}  |  帧 {preview.CurrentFrame + 1}/{preview.TotalFrames}");
            }

            DrawSeparator();

            // ===== 播放控制 =====
            EditorGUILayout.LabelField("🎮 播放控制", EditorStyles.boldLabel);

            // 帧滑块
            if (preview.TotalFrames > 0)
            {
                EditorGUI.BeginChangeCheck();
                int newFrame = EditorGUILayout.IntSlider("当前帧", preview.CurrentFrame, 0, preview.TotalFrames - 1);
                if (EditorGUI.EndChangeCheck())
                {
                    preview.GoToFrame(newFrame);
                }

                float progress = (float)preview.CurrentFrame / Mathf.Max(1, preview.TotalFrames - 1);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18), progress,
                    $"帧 {preview.CurrentFrame + 1} / {preview.TotalFrames}");
            }

            EditorGUILayout.Space(4);

            // 控制按钮
            EditorGUILayout.BeginHorizontal();
            var btnStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 30 };

            if (preview.IsPlaying)
            {
                if (GUILayout.Button("⏸ 暂停", btnStyle))
                    preview.Pause();
            }
            else
            {
                if (GUILayout.Button("▶ 播放", btnStyle))
                    preview.Play(preview.CurrentAnimName ?? (animNames.Length > 0 ? animNames[0] : ""));
            }

            if (GUILayout.Button("⏹ 停止", btnStyle))
                preview.Stop();
            EditorGUILayout.EndHorizontal();

            // 动画快速切换按钮（多于1个动画时显示）
            if (preview.animations.Count > 1)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("快速切换:", EditorStyles.miniLabel);
                int cols = Mathf.Min(preview.animations.Count, 4);
                int rows = Mathf.CeilToInt((float)preview.animations.Count / cols);

                for (int r = 0; r < rows; r++)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;
                        if (idx >= preview.animations.Count) break;

                        var anim = preview.animations[idx];
                        bool isCurrent = anim.name == preview.CurrentAnimName;
                        var style = isCurrent
                            ? new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 26 }
                            : new GUIStyle(GUI.skin.button) { fixedHeight = 26 };

                        if (GUILayout.Button(anim.name, style))
                        {
                            preview.Play(anim.name);
                            selectedAnimIndex = idx;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        // ===== 当前帧预览 =====
        if (preview.TotalFrames > 0 && preview.CurrentFrame >= 0 && preview.CurrentFrame < preview.TotalFrames)
        {
            DrawSeparator();
            EditorGUILayout.LabelField("👁️ 当前帧预览", EditorStyles.boldLabel);

            var currentClip = preview.animations.Find(a => a.name == preview.CurrentAnimName);
            if (currentClip != null && preview.CurrentFrame < currentClip.frames.Count)
            {
                var currentSprite = currentClip.frames[preview.CurrentFrame];
                if (currentSprite != null)
                {
                    var tex = AssetPreview.GetAssetPreview(currentSprite);
                    if (tex != null)
                    {
                        var rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
                        rect.x = (EditorGUIUtility.currentViewWidth - 128) * 0.5f;
                        GUI.DrawTexture(new Rect(rect.x, rect.y, 128, 128), tex, ScaleMode.ScaleToFit);
                    }
                    EditorGUILayout.LabelField(currentSprite.name, EditorStyles.centeredGreyMiniLabel);
                }
            }
        }

        serializedObject.ApplyModifiedProperties();

        if (preview.IsPlaying)
            Repaint();
    }

    private void DrawSeparator()
    {
        EditorGUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        EditorGUILayout.Space(2);
    }
}
