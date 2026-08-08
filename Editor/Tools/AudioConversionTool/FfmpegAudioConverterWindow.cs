using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

[ToolInfo("音频转换器", "媒体工具",
    Description = "批量识别并转换异常音频文件。支持拖入多个文件，完成后可试听并安全替换源文件。",
    Icon = "🎵", Tags = new[] { "Audio", "FFmpeg", "WAV", "MP3", "M4A", "批量转换" }, Priority = 30)]
public sealed class FfmpegAudioConverterWindow : EditorWindow
{
    private const string FfmpegPathPreference = "MermaidsFallAVG.AudioConverter.FfmpegPath";
    private const string BundledFfmpegMarker = "__UNITY_TOOLS_HUB_BUNDLED_FFMPEG__";
    private const string DefaultOutputFolder = "Assets/Audio/Converted";
    private static readonly string[] AudioExtensions = { ".mp3", ".m4a", ".aac", ".mp4", ".wav", ".ogg", ".flac" };

    private enum OutputFormat { Wav, Mp3 }
    private enum ItemStatus { Queued, Converting, Failed }

    private sealed class SourceItem
    {
        public string absolutePath;
        public string assetPath;
        public string detectedFormat;
        public string warning;
        public ItemStatus status;
        public bool selected = true;
    }

    private sealed class CompletedItem
    {
        public string sourceAbsolutePath;
        public string sourceAssetPath;
        public string outputAbsolutePath;
        public string outputAssetPath;
        public bool replaced;
    }

    private readonly List<SourceItem> sources = new List<SourceItem>();
    private readonly List<CompletedItem> completed = new List<CompletedItem>();
    private readonly StringBuilder processLog = new StringBuilder();
    private readonly object processLogLock = new object();

    private string ffmpegPath;
    private string outputFolder = DefaultOutputFolder;
    private OutputFormat outputFormat = OutputFormat.Wav;
    private Process conversionProcess;
    private SourceItem convertingItem;
    private string convertingOutputAbsolutePath;
    private Vector2 sourceScroll;
    private Vector2 completedScroll;
    private Vector2 logScroll;
    private string status;

    private static MethodInfo playPreviewClip;
    private static MethodInfo stopAllPreviewClips;

    [MenuItem("UnityToolsHub/音频转换器", priority = 135)]
    [MenuItem("Tools/Audio/FFmpeg Audio Converter", priority = 135)]
    private static void Open()
    {
        GetWindow<FfmpegAudioConverterWindow>("Audio Converter");
    }

    private void OnEnable()
    {
        // The marker is resolved from this script's directory every time, so moving
        // UnityToolsHub does not leave a stale absolute path in EditorPrefs.
        ffmpegPath = EditorPrefs.GetString(FfmpegPathPreference, BundledFfmpegMarker);
        if (ffmpegPath == "ffmpeg") ffmpegPath = BundledFfmpegMarker; // Migrate the old default.
    }

    private void OnDisable()
    {
        EditorApplication.update -= PollConversion;
        if (conversionProcess != null && !conversionProcess.HasExited) conversionProcess.Kill();
        conversionProcess?.Dispose();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("拖入多个音频文件即可识别真实格式。转换默认保留源文件；请在完成列表确认试听后再执行替换。", MessageType.Info);
        DrawSettings();
        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
                DrawSourceQueue();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
                DrawCompletedList();
        }
        DrawLog();
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("转换设置", EditorStyles.boldLabel);
        outputFormat = (OutputFormat)EditorGUILayout.EnumPopup("输出格式", outputFormat);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.SelectableLabel(outputFolder, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("输出目录", GUILayout.Width(80)))
            {
                var folder = EditorUtility.OpenFolderPanel("选择 Assets 内的输出目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(folder))
                {
                    var assetPath = ToAssetPath(folder);
                    if (assetPath == null) EditorUtility.DisplayDialog("无效目录", "输出目录必须位于本项目的 Assets 文件夹中。", "确定");
                    else outputFolder = assetPath;
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (ffmpegPath == BundledFfmpegMarker)
            {
                EditorGUILayout.SelectableLabel("随 UnityToolsHub 附带的 ffmpeg（自动定位）", EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
            else
            {
                var newPath = EditorGUILayout.TextField("ffmpeg", ffmpegPath);
                if (newPath != ffmpegPath)
                {
                    ffmpegPath = newPath;
                    EditorPrefs.SetString(FfmpegPathPreference, ffmpegPath);
                }
            }
            if (GUILayout.Button("选择 ffmpeg.exe", GUILayout.Width(120)))
            {
                var path = EditorUtility.OpenFilePanel("选择 ffmpeg.exe", "", "exe");
                if (!string.IsNullOrEmpty(path))
                {
                    ffmpegPath = path;
                    EditorPrefs.SetString(FfmpegPathPreference, ffmpegPath);
                }
            }
            if (ffmpegPath != BundledFfmpegMarker && GUILayout.Button("使用附带版本", GUILayout.Width(100)))
            {
                ffmpegPath = BundledFfmpegMarker;
                EditorPrefs.SetString(FfmpegPathPreference, ffmpegPath);
            }
        }
    }

    private void DrawSourceQueue()
    {
        EditorGUILayout.LabelField($"待转换列表 ({sources.Count})", EditorStyles.boldLabel);
        var dropRect = GUILayoutUtility.GetRect(0, 46, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "将一个或多个音频文件 / Project 资源拖到此处，或使用下方“添加当前选择”", EditorStyles.helpBox);
        HandleDragAndDrop(dropRect);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("添加当前选择")) AddSelectedAssets();
            if (GUILayout.Button("全选")) SetAllSelections(true);
            if (GUILayout.Button("取消全选")) SetAllSelections(false);
            if (GUILayout.Button("移除未选")) sources.RemoveAll(item => !item.selected && item.status != ItemStatus.Converting);
            if (GUILayout.Button("清空")) sources.RemoveAll(item => item.status != ItemStatus.Converting);
        }

        sourceScroll = EditorGUILayout.BeginScrollView(sourceScroll, GUILayout.MinHeight(170), GUILayout.MaxHeight(280));
        for (var index = 0; index < sources.Count; index++) DrawSourceItem(sources[index], index);
        EditorGUILayout.EndScrollView();

        var selectedCount = sources.FindAll(item => item.selected && item.status == ItemStatus.Queued).Count;
        using (new EditorGUI.DisabledScope(conversionProcess != null || selectedCount == 0))
        {
            if (GUILayout.Button($"转换所选 ({selectedCount})", GUILayout.Height(28))) StartNextConversion();
        }
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, conversionProcess == null ? MessageType.None : MessageType.Info);
    }

    private void DrawSourceItem(SourceItem item, int index)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            using (new EditorGUI.DisabledScope(item.status == ItemStatus.Converting))
                item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(18));
            EditorGUILayout.LabelField($"{index + 1}. {Path.GetFileName(item.absolutePath)}", GUILayout.MinWidth(70), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(item.detectedFormat, GUILayout.Width(90));
            var state = item.status == ItemStatus.Queued ? "待转换" : item.status == ItemStatus.Converting ? "转换中" : "失败";
            EditorGUILayout.LabelField(state, GUILayout.Width(55));
            if (GUILayout.Button("移除", GUILayout.Width(48)) && item.status != ItemStatus.Converting) sources.Remove(item);
        }
        if (!string.IsNullOrEmpty(item.warning)) EditorGUILayout.HelpBox(item.warning, MessageType.Warning);
    }

    private void DrawCompletedList()
    {
        EditorGUILayout.LabelField($"转换完成 ({completed.Count})", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(completed.Count == 0 || conversionProcess != null))
            {
                if (GUILayout.Button("一键替换全部")) ReplaceAll();
                if (GUILayout.Button("清空完成列表")) completed.Clear();
            }
        }

        completedScroll = EditorGUILayout.BeginScrollView(completedScroll, GUILayout.MinHeight(236), GUILayout.MaxHeight(350));
        foreach (var item in completed) DrawCompletedItem(item);
        EditorGUILayout.EndScrollView();
    }

    private void DrawCompletedItem(CompletedItem item)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(Path.GetFileName(item.outputAbsolutePath), GUILayout.MinWidth(110), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(item.replaced ? "已替换" : "待确认", GUILayout.Width(55));
            using (new EditorGUI.DisabledScope(!File.Exists(item.outputAbsolutePath)))
            {
                if (GUILayout.Button("播放", GUILayout.Width(48))) PlayPreview(item.outputAssetPath);
            }
            using (new EditorGUI.DisabledScope(item.replaced || conversionProcess != null))
            {
                if (GUILayout.Button("替换", GUILayout.Width(48))) ReplaceOne(item, true);
            }
        }
    }

    private void DrawLog()
    {
        string logText;
        lock (processLogLock) logText = processLog.ToString();
        if (logText.Length == 0) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("ffmpeg 输出", EditorStyles.boldLabel);
        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(70), GUILayout.MaxHeight(140));
        EditorGUILayout.TextArea(logText, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void HandleDragAndDrop(Rect dropRect)
    {
        var currentEvent = Event.current;
        if (!dropRect.Contains(currentEvent.mousePosition)) return;
        if (currentEvent.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var path in DragAndDrop.paths) AddPath(path);
            foreach (var asset in DragAndDrop.objectReferences) AddAsset(asset);
            currentEvent.Use();
        }
    }

    private void AddSelectedAssets()
    {
        foreach (var asset in Selection.objects) AddAsset(asset);
    }

    private void AddAsset(UnityEngine.Object asset)
    {
        if (asset == null) return;
        var assetPath = AssetDatabase.GetAssetPath(asset);
        if (!string.IsNullOrEmpty(assetPath)) AddPath(assetPath);
    }

    private void AddPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var absolutePath = Path.IsPathRooted(path) ? path : Path.Combine(GetProjectRoot(), path);
        absolutePath = Path.GetFullPath(absolutePath);
        if (Directory.Exists(absolutePath))
        {
            foreach (var file in Directory.GetFiles(absolutePath, "*.*", SearchOption.AllDirectories)) AddPath(file);
            return;
        }
        if (!File.Exists(absolutePath) || !IsAudioFile(absolutePath) || sources.Exists(item => PathsEqual(item.absolutePath, absolutePath))) return;

        DetectFormat(absolutePath, out var detectedFormat, out var warning);
        sources.Add(new SourceItem
        {
            absolutePath = absolutePath,
            assetPath = ToAssetPath(absolutePath),
            detectedFormat = detectedFormat,
            warning = warning,
            status = ItemStatus.Queued
        });
        status = null;
    }

    private void StartNextConversion()
    {
        convertingItem = sources.Find(item => item.selected && item.status == ItemStatus.Queued);
        if (convertingItem == null)
        {
            status = "所选文件已全部转换。";
            return;
        }

        var executablePath = ResolveFfmpegExecutable();
        if (string.IsNullOrEmpty(executablePath))
        {
            status = "未找到随工具附带的 ffmpeg.exe。请确认它位于 AudioConversionTool 文件夹内，或手动选择 ffmpeg.exe。";
            return;
        }

        convertingItem.status = ItemStatus.Converting;
        convertingOutputAbsolutePath = GetAvailableOutputPath(convertingItem.absolutePath);
        Directory.CreateDirectory(Path.GetDirectoryName(convertingOutputAbsolutePath));
        lock (processLogLock) processLog.Clear();

        var audioArguments = outputFormat == OutputFormat.Wav
            ? "-vn -c:a pcm_s16le -ar 44100 -ac 2"
            : "-vn -c:a libmp3lame -b:a 192k -ar 44100 -ac 2";
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"-hide_banner -loglevel error -y -i {Quote(convertingItem.absolutePath)} {audioArguments} {Quote(convertingOutputAbsolutePath)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            conversionProcess = new Process { StartInfo = startInfo };
            conversionProcess.OutputDataReceived += (_, e) => AppendProcessLog(e.Data);
            conversionProcess.ErrorDataReceived += (_, e) => AppendProcessLog(e.Data);
            conversionProcess.Start();
            conversionProcess.BeginOutputReadLine();
            conversionProcess.BeginErrorReadLine();
            EditorApplication.update += PollConversion;
            status = $"正在转换：{Path.GetFileName(convertingItem.absolutePath)}";
        }
        catch (Exception exception)
        {
            convertingItem.status = ItemStatus.Failed;
            conversionProcess?.Dispose();
            conversionProcess = null;
            status = $"无法启动 ffmpeg：{exception.Message}";
        }
    }

    private void PollConversion()
    {
        if (conversionProcess == null || !conversionProcess.HasExited) return;
        EditorApplication.update -= PollConversion;
        var exitCode = conversionProcess.ExitCode;
        conversionProcess.Dispose();
        conversionProcess = null;

        if (exitCode == 0 && File.Exists(convertingOutputAbsolutePath))
        {
            var outputAssetPath = ToAssetPath(convertingOutputAbsolutePath);
            AssetDatabase.Refresh();
            if (!string.IsNullOrEmpty(outputAssetPath)) AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);
            completed.Add(new CompletedItem
            {
                sourceAbsolutePath = convertingItem.absolutePath,
                sourceAssetPath = convertingItem.assetPath,
                outputAbsolutePath = convertingOutputAbsolutePath,
                outputAssetPath = outputAssetPath
            });
            sources.Remove(convertingItem);
        }
        else
        {
            convertingItem.status = ItemStatus.Failed;
            convertingItem.selected = false;
            status = $"转换失败：{Path.GetFileName(convertingItem.absolutePath)}。请查看 ffmpeg 输出。";
        }

        convertingItem = null;
        convertingOutputAbsolutePath = null;
        Repaint();
        StartNextConversion();
    }

    private void ReplaceAll()
    {
        var count = completed.FindAll(item => !item.replaced).Count;
        if (count == 0) return;
        if (!EditorUtility.DisplayDialog("确认一键替换", $"将永久删除 {count} 个源文件，并将转换后的文件改为源文件名（扩展名使用新格式）。\n\n此操作不可撤销。", "替换", "取消")) return;
        foreach (var item in completed) ReplaceOne(item, false);
    }

    private void ReplaceOne(CompletedItem item, bool confirm)
    {
        if (item.replaced) return;
        if (string.IsNullOrEmpty(item.sourceAssetPath) || string.IsNullOrEmpty(item.outputAssetPath))
        {
            EditorUtility.DisplayDialog("无法替换", "只有 Assets 目录内的源文件和输出文件才能由 Unity 安全替换。", "确定");
            return;
        }
        if (confirm && !EditorUtility.DisplayDialog("确认替换", $"将删除源文件：\n{item.sourceAssetPath}\n\n并用转换后的文件替换它。此操作不可撤销。", "替换", "取消")) return;

        var targetAssetPath = Path.ChangeExtension(item.sourceAssetPath, Path.GetExtension(item.outputAssetPath)).Replace('\\', '/');
        var sourceMetaPath = item.sourceAbsolutePath + ".meta";
        var outputMetaPath = item.outputAbsolutePath + ".meta";
        var targetAbsolutePath = Path.Combine(GetProjectRoot(), targetAssetPath);
        var targetMetaPath = targetAbsolutePath + ".meta";

        try
        {
            AssetDatabase.DisallowAutoRefresh();
            if (File.Exists(targetAbsolutePath) && !PathsEqual(targetAbsolutePath, item.outputAbsolutePath)) throw new IOException($"目标文件已存在：{targetAssetPath}");
            File.Delete(item.sourceAbsolutePath);
            if (File.Exists(outputMetaPath)) File.Delete(outputMetaPath);
            File.Move(item.outputAbsolutePath, targetAbsolutePath);
            if (File.Exists(sourceMetaPath)) File.Move(sourceMetaPath, targetMetaPath);
            item.outputAbsolutePath = targetAbsolutePath;
            item.outputAssetPath = targetAssetPath;
            item.replaced = true;
            status = $"已替换：{targetAssetPath}";
        }
        catch (Exception exception)
        {
            status = $"替换失败：{exception.Message}";
            UnityEngine.Debug.LogError(status);
        }
        finally
        {
            AssetDatabase.AllowAutoRefresh();
            AssetDatabase.Refresh();
        }
    }

    private void PlayPreview(string assetPath)
    {
        var clip = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        if (clip == null)
        {
            status = "音频尚未被 Unity 成功导入，无法播放预览。";
            return;
        }
        var audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        playPreviewClip = playPreviewClip ?? audioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        stopAllPreviewClips = stopAllPreviewClips ?? audioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public);
        if (playPreviewClip == null)
        {
            status = "当前 Unity 版本不支持通过编辑器工具播放预览。";
            return;
        }
        stopAllPreviewClips?.Invoke(null, null);
        playPreviewClip.Invoke(null, new object[] { clip, 0, false });
    }

    private string GetAvailableOutputPath(string sourcePath)
    {
        var extension = outputFormat == OutputFormat.Wav ? ".wav" : ".mp3";
        var folder = Path.Combine(GetProjectRoot(), outputFolder);
        var baseName = Path.Combine(folder, Path.GetFileNameWithoutExtension(sourcePath) + "_converted");
        var candidate = baseName + extension;
        var index = 1;
        while (File.Exists(candidate)) candidate = $"{baseName}_{index++}{extension}";
        return candidate;
    }

    private static void DetectFormat(string path, out string format, out string warning)
    {
        format = "Unknown";
        warning = null;
        try
        {
            using (var stream = File.OpenRead(path))
            {
                var header = new byte[12];
                var count = stream.Read(header, 0, header.Length);
                var extension = Path.GetExtension(path).ToUpperInvariant();
                if (count >= 8 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p') format = "M4A / MP4 container";
                else if (count >= 4 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F') format = "WAV / RIFF";
                else if (count >= 4 && header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S') format = "Ogg";
                else if (count >= 3 && header[0] == 'I' && header[1] == 'D' && header[2] == '3' || count >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0) format = "MP3";
                else format = extension.TrimStart('.') + " (unrecognized header)";
                if (extension == ".MP3" && format == "M4A / MP4 container") warning = "扩展名为 .mp3，但真实容器是 M4A/MP4；这会导致 Unity FMOD 导入失败。";
            }
        }
        catch (Exception exception)
        {
            format = "Read error";
            warning = exception.Message;
        }
    }

    private static bool IsAudioFile(string path)
    {
        var extension = Path.GetExtension(path);
        foreach (var audioExtension in AudioExtensions)
            if (string.Equals(extension, audioExtension, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private string ResolveFfmpegExecutable()
    {
        if (ffmpegPath != BundledFfmpegMarker) return string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath;

        var scriptGuids = AssetDatabase.FindAssets("FfmpegAudioConverterWindow t:MonoScript");
        foreach (var guid in scriptGuids)
        {
            var scriptAssetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!scriptAssetPath.EndsWith("FfmpegAudioConverterWindow.cs", StringComparison.Ordinal)) continue;
            var scriptDirectory = Path.Combine(GetProjectRoot(), Path.GetDirectoryName(scriptAssetPath) ?? string.Empty);
            var executables = Directory.GetFiles(scriptDirectory, "ffmpeg.exe", SearchOption.AllDirectories);
            if (executables.Length > 0) return executables[0];
        }
        return null;
    }

    private void SetAllSelections(bool selected)
    {
        foreach (var item in sources) if (item.status != ItemStatus.Converting) item.selected = selected;
    }

    private static string ToAssetPath(string absolutePath)
    {
        var assetsPath = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!path.StartsWith(assetsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !PathsEqual(path, assetsPath)) return null;
        return "Assets" + path.Substring(assetsPath.Length).Replace('\\', '/');
    }

    private static string GetProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static bool PathsEqual(string first, string second) => string.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private void AppendProcessLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (processLogLock) processLog.AppendLine(line);
    }
}
