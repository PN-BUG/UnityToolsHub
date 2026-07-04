#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

[ToolInfo("视频首帧导出", "媒体工具",
    Description = "把文件夹下所有视频导出第一帧 PNG（文件名与视频一致）。\n\n支持自定义输出分辨率，适合生成视频缩略图。",
    Icon = "🎬", Tags = new[] { "视频", "PNG导出" })]
public class VideoFirstFrameExporter : EditorWindow
{
    private DefaultAsset inputFolder;
    private DefaultAsset outputFolder;

    private int targetWidth = 256;
    private int targetHeight = 256;

    [MenuItem("UnityToolsHub/Video/Export First Frame PNGs")]
    public static void Open()
    {
        var w = GetWindow<VideoFirstFrameExporter>("Video First Frame");
        w.minSize = new Vector2(420, 140);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("把文件夹下所有视频导出第一帧 PNG（文件名与视频一致）", EditorStyles.boldLabel);

        inputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Input Folder", inputFolder, typeof(DefaultAsset), false);
        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("输出尺寸 (0 = 使用视频原始尺寸)", EditorStyles.boldLabel);
        targetWidth = EditorGUILayout.IntField("Width", targetWidth);
        targetHeight = EditorGUILayout.IntField("Height", targetHeight);

        EditorGUILayout.Space(8);
      

        using (new EditorGUI.DisabledScope(inputFolder == null || outputFolder == null))
        {
            if (GUILayout.Button("Export"))
            {
                string inPath = AssetDatabase.GetAssetPath(inputFolder);
                string outPath = AssetDatabase.GetAssetPath(outputFolder);

                if (!AssetDatabase.IsValidFolder(inPath) || !AssetDatabase.IsValidFolder(outPath))
                {
                    Debug.LogError("Input/Output 必须是 Project 里的文件夹（Assets/...）。");
                    return;
                }

                ExportAll(inPath, outPath, targetWidth, targetHeight);
            }
        }

        EditorGUILayout.HelpBox(
            "提示：如果某些视频导不出来，检查视频导入设置/编码格式，或先确认该视频能被 VideoPlayer 播放。",
            MessageType.Info);
    }

    private static void ExportAll(string inputFolderPath, string outputFolderPath, int width, int height)
    {
        // 找到所有视频资源
        var guids = AssetDatabase.FindAssets("", new[] { inputFolderPath });
        var videoPaths = new List<string>();

        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (IsVideoAsset(p)) videoPaths.Add(p);
        }

        if (videoPaths.Count == 0)
        {
            Debug.LogWarning($"在 {inputFolderPath} 没找到视频文件。");
            return;
        }

        EditorCoroutineRunner.StartCoroutine(ExportCoroutine(videoPaths, outputFolderPath, width, height));
    }

    private static bool IsVideoAsset(string assetPath)
    {
        string ext = Path.GetExtension(assetPath).ToLowerInvariant();
        return ext == ".mp4" || ext == ".mov" || ext == ".webm" || ext == ".avi" || ext == ".m4v";
    }

    private static IEnumerator ExportCoroutine(List<string> videoAssetPaths, string outputFolderPath, int width, int height)
    {
        // 创建临时对象
        var go = new GameObject("[VideoFirstFrameExporter_TEMP]");
        go.hideFlags = HideFlags.HideAndDontSave;

        var vp = go.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.waitForFirstFrame = true;
        vp.skipOnDrop = false;

        int ok = 0, fail = 0;

        try
        {
            for (int i = 0; i < videoAssetPaths.Count; i++)
            {
                string assetPath = videoAssetPaths[i];
                string fullPath = Path.GetFullPath(assetPath); // VideoPlayer.url 需要系统路径
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string outAssetPath = $"{outputFolderPath}/{fileName}.png";

                EditorUtility.DisplayProgressBar(
                    "Export Video First Frames",
                    $"{i + 1}/{videoAssetPaths.Count}: {fileName}",
                    (float)(i + 1) / videoAssetPaths.Count
                );

                // 输出已存在就覆盖（你也可以改成跳过）
                string outFull = Path.GetFullPath(outAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outFull)!);

                // 准备视频
                vp.url = fullPath;
                vp.Prepare();

                // 等待 Prepare 完成（加超时避免卡死）
                float t = 0f;
                const float prepareTimeout = 10f;
                while (!vp.isPrepared && t < prepareTimeout)
                {
                    t += 0.1f;
                    yield return new EditorWaitForSeconds(0.1f);
                }

                if (!vp.isPrepared || vp.texture == null || vp.width <= 0 || vp.height <= 0)
                {
                    Debug.LogWarning($"[FAIL] Prepare失败或无法获取纹理：{assetPath}");
                    fail++;
                    continue;
                }

                int w = (width > 0) ? width : (int)vp.width;
                int h = (height > 0) ? height : (int)vp.height;

                // RenderTexture 接第一帧
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                rt.wrapMode = TextureWrapMode.Clamp;
                rt.filterMode = FilterMode.Bilinear;

                vp.targetTexture = rt;

                // 播放并立刻停在第一帧：
                // SetDirectAudioMute 已经不需要；我们禁用了音频输出
                vp.time = 0;
                vp.Play();

                // 等待真正拿到第一帧（某些视频需要一两帧）
                // 这里用帧计数 + 超时保护
                int frameWait = 0;
                const int maxFrameWait = 60;
                while (vp.frame < 0 && frameWait < maxFrameWait)
                {
                    frameWait++;
                    yield return null;
                }

                // 有些视频 vp.frame 不可靠，至少等一帧再抓
                yield return null;

                vp.Pause();

                // 从 RT 读回 Texture2D
                var prev = RenderTexture.active;
                RenderTexture.active = rt;

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false, false);

                RenderTexture.active = prev;

                // 写 PNG
                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(outFull, png);

                // 写完后强制导入，并修正导入设置，避免 960 -> 1024
                AssetDatabase.ImportAsset(outAssetPath, ImportAssetOptions.ForceUpdate);

                var importer = AssetImporter.GetAtPath(outAssetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite; // 或 Sprite 看你用途
                    importer.npotScale = TextureImporterNPOTScale.None; // 关键：不做 NPOT 缩放
                    importer.mipmapEnabled = false;

                    // 关键：确保 maxTextureSize >= 你要求的尺寸（这里保险给大点）
                    importer.maxTextureSize = 1024;

                    importer.textureCompression = TextureImporterCompression.Uncompressed; // 可选：避免压缩影响
                    importer.SaveAndReimport();
                }

                // 清理
                UnityEngine.Object.DestroyImmediate(tex);
                vp.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);

                ok++;
                // 让编辑器喘口气
                yield return null;

                Debug.Log($"[OK] {assetPath} -> {outAssetPath}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.Refresh();
            Debug.Log($"导出完成：成功 {ok}，失败 {fail}。输出目录：{outputFolderPath}");
        }
    }

    // ======= 简单 Editor Coroutine 支持（不依赖 Package） =======
    private class EditorCoroutineRunner
    {
        private static readonly List<IEnumerator> _routines = new();

        public static void StartCoroutine(IEnumerator routine)
        {
            _routines.Add(routine);
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            for (int i = _routines.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!_routines[i].MoveNext())
                        _routines.RemoveAt(i);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _routines.RemoveAt(i);
                }
            }

            if (_routines.Count == 0)
                EditorApplication.update -= Update;
        }
    }

    private class EditorWaitForSeconds
    {
        public float seconds;
        public EditorWaitForSeconds(float s) { seconds = s; }
    }
}
#endif