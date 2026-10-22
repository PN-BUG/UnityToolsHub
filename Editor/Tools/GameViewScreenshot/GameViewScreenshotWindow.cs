using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Captures only the rendered contents of the Unity Game view, excluding editor chrome.
/// </summary>
[ToolInfo("Game 视图截图", "媒体工具",
    Description = "将 Game 窗口当前显示的游戏画面保存为 PNG，不包含编辑器标签栏和工具栏。",
    Icon = "▣",
    Tags = new[] { "截图", "Game", "PNG", "画面" },
    Priority = 35)]
public sealed class GameViewScreenshotWindow : ToolEditorWindow
{
    private const string DefaultFolderName = "Screenshots";
    private const string DefaultFilePrefix = "GameView";

    private string _outputDirectory;
    private string _filePrefix;
    private string _lastCapturePath;
    private string _statusMessage = "就绪";
    private MessageType _statusType = MessageType.None;
    private Texture2D _previewTexture;
    private bool _revealAfterCapture;
    private bool _captureQueued;
    private IDisposable _hiddenUI;
    private bool _queuedShowUI;
    private bool _showUI;
    private bool _enableCaptureShortcut;
    private bool _recordingShortcut;
    private bool _shortcutHeld;
    private KeyCode _shortcutKey = KeyCode.F12;
    private EventModifiers _shortcutModifiers;
    private static readonly FieldInfo GlobalEventHandlerField = typeof(EditorApplication).GetField(
        "globalEventHandler", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    private const EventModifiers ShortcutModifierMask =
        EventModifiers.Control | EventModifiers.Shift | EventModifiers.Alt | EventModifiers.Command;

    private string PreferencePrefix => "UnityToolsHub.GameViewScreenshot." + Application.dataPath + ".";
    private static string DefaultOutputDirectory =>
        Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), DefaultFolderName);

    protected override string ToolTitle => "Game 视图截图";
    protected override string ToolIcon => "▣";
    protected override bool ShowStatusBar => true;

    [MenuItem("UnityToolsHub/Game 视图截图", priority = 135)]
    private static void Open()
    {
        var window = GetWindow<GameViewScreenshotWindow>("Game 视图截图");
        window.minSize = new Vector2(440f, 520f);
        window.Show();
    }

    protected override void OnToolEnable()
    {
        _outputDirectory = EditorPrefs.GetString(
            PreferencePrefix + "OutputDirectory",
            DefaultOutputDirectory);
        _filePrefix = EditorPrefs.GetString(PreferencePrefix + "FilePrefix", DefaultFilePrefix);
        _revealAfterCapture = EditorPrefs.GetBool(PreferencePrefix + "RevealAfterCapture", false);
        _showUI = EditorPrefs.GetBool(PreferencePrefix + "ShowUI", false);
        _enableCaptureShortcut = EditorPrefs.GetBool(PreferencePrefix + "EnableCaptureShortcut", false);
        _shortcutKey = (KeyCode)EditorPrefs.GetInt(PreferencePrefix + "ShortcutKey", (int)KeyCode.F12);
        _shortcutModifiers = (EventModifiers)EditorPrefs.GetInt(PreferencePrefix + "ShortcutModifiers", 0)
            & ShortcutModifierMask;
        AssemblyReloadEvents.beforeAssemblyReload += CancelQueuedCapture;
        EditorApplication.quitting += CancelQueuedCapture;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        UpdateShortcutRegistration();
    }

    protected override void OnToolDisable()
    {
        CancelQueuedCapture();
        AssemblyReloadEvents.beforeAssemblyReload -= CancelQueuedCapture;
        EditorApplication.quitting -= CancelQueuedCapture;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SetShortcutRegistration(false);
        _captureQueued = false;
        _recordingShortcut = false;
        DestroyPreview();
    }

    protected override void DrawToolContent()
    {
        DrawTitle("保存纯净的 Game 画面");
        RecordShortcut(Event.current);
        DrawBody("保存 Game 画面，不包含 Unity 编辑器界面。可选择是否显示游戏 UI，默认不显示。");
        DrawTags(new[] { "PNG", "原始分辨率", "编辑器工具" });

        DrawSection("当前画面");
        if (GameViewScreenshotCapture.TryGetSourceInfo(out var width, out var height, out var sourceError))
        {
            DrawToolHelpBox(
                $"Game 画面已就绪：{width} × {height}\n" +
                (EditorApplication.isPlaying ? "当前处于运行模式。" : "当前处于编辑模式，将保存 Game 窗口最后一次渲染的画面。"),
                MessageType.Info);
        }
        else
        {
            DrawToolHelpBox(sourceError, MessageType.Warning);
        }

        DrawSection("保存设置");
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        _outputDirectory = EditorGUILayout.TextField("保存目录", _outputDirectory);
        if (GUILayout.Button("选择…", GUILayout.Width(64f)))
        {
            ChooseOutputDirectory();
        }
        EditorGUILayout.EndHorizontal();

        _filePrefix = EditorGUILayout.TextField("文件名前缀", _filePrefix);
        _showUI = EditorGUILayout.ToggleLeft("显示 UI", _showUI);
        _revealAfterCapture = EditorGUILayout.ToggleLeft("截图后打开所在文件夹", _revealAfterCapture);
        if (EditorGUI.EndChangeCheck())
        {
            SavePreferences();
        }

        DrawSection("截图快捷键");
        EditorGUI.BeginChangeCheck();
        _enableCaptureShortcut = EditorGUILayout.ToggleLeft("窗口打开时启用截图快捷键", _enableCaptureShortcut);
        if (EditorGUI.EndChangeCheck())
        {
            _recordingShortcut = false;
            SavePreferences();
            UpdateShortcutRegistration();
        }

        using (new EditorGUI.DisabledScope(!_enableCaptureShortcut || GlobalEventHandlerField == null))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("当前快捷键", GetShortcutLabel());
            if (GUILayout.Button(_recordingShortcut ? "取消设置" : "设置快捷键…", GUILayout.Width(96f)))
            {
                _recordingShortcut = !_recordingShortcut;
                _shortcutHeld = false;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
        }
        if (_recordingShortcut)
            DrawToolHelpBox("在此窗口按下新的快捷键，可组合 Ctrl / Shift / Alt / Command；Esc 取消。", MessageType.Info);
        else
            DrawLabelDim("仅在此工具窗口打开且勾选启用时生效，关闭窗口后停用。");
        if (GlobalEventHandlerField == null)
            DrawToolHelpBox("当前 Unity 版本不支持编辑器全局快捷键监听。", MessageType.Warning);

        DrawLabelDim("文件名示例：GameView_20260920_183015_123.png");
        EditorGUILayout.Space(8f);

        using (new EditorGUI.DisabledScope(_captureQueued))
        {
            if (DrawSuccessButton(
                    _captureQueued ? "正在截取…" : "截取 Game 画面",
                    GUILayout.Height(38f),
                    GUILayout.ExpandWidth(true)))
            {
                QueueCapture();
            }
        }

        EditorGUILayout.BeginHorizontal();
        if (DrawFlatButton("打开保存目录", GUILayout.Height(28f)))
        {
            RevealOutputDirectory();
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastCapturePath)))
        {
            if (DrawFlatButton("复制截图路径", GUILayout.Height(28f)))
            {
                EditorGUIUtility.systemCopyBuffer = _lastCapturePath;
                SetStatus("已复制截图路径", MessageType.Info);
            }
        }
        EditorGUILayout.EndHorizontal();

        if (_previewTexture != null)
        {
            DrawSection("最近截图");
            var aspect = (float)_previewTexture.width / _previewTexture.height;
            var previewRect = GUILayoutUtility.GetAspectRect(
                aspect,
                GUILayout.MaxHeight(360f),
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(previewRect, _previewTexture, null, ScaleMode.ScaleToFit);
            EditorGUILayout.SelectableLabel(
                _lastCapturePath,
                EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }

    protected override void DrawStatusBarContent()
    {
        var color = _statusType == MessageType.Error
            ? ClrError
            : _statusType == MessageType.Warning
                ? ClrWarning
                : _statusType == MessageType.Info
                    ? ClrSuccess
                    : ClrTextDim;
        DrawStatusText(_statusMessage, color);
    }

    private void QueueCapture()
    {
        if (_captureQueued || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
        {
            return;
        }

        if (!GameViewScreenshotCapture.TryPrepareGameView(out var error))
        {
            SetStatus(error, MessageType.Error);
            return;
        }

        _captureQueued = true;
        _queuedShowUI = _showUI;
        SetStatus("正在刷新 Game 画面…", MessageType.Info);
        // Leave the keyboard/OnGUI callback before changing visibility or requesting rendering.
        EditorApplication.delayCall += PrepareQueuedFrame;
    }

    private void PrepareQueuedFrame()
    {
        EditorApplication.delayCall -= PrepareQueuedFrame;
        try
        {
            if (!_queuedShowUI)
                _hiddenUI = GameViewScreenshotCapture.HideUI();
            if (!GameViewScreenshotCapture.TryPrepareGameView(out var error))
                throw new InvalidOperationException(error);
            EditorApplication.QueuePlayerLoopUpdate();
            EditorApplication.delayCall += WaitForGameViewFrame;
        }
        catch (Exception exception)
        {
            CancelQueuedCapture();
            SetStatus("准备截图失败：" + exception.Message, MessageType.Error);
        }
    }

    private void WaitForGameViewFrame()
    {
        EditorApplication.delayCall -= WaitForGameViewFrame;
        // Allow the requested GameView repaint to finish before reading its render texture.
        EditorApplication.delayCall += CaptureQueuedFrame;
    }

    private void CaptureQueuedFrame()
    {
        EditorApplication.delayCall -= CaptureQueuedFrame;
        try
        {
            var outputPath = BuildOutputPath();
            if (!GameViewScreenshotCapture.TryCapture(outputPath, out var image, out var error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            DestroyPreview();
            _previewTexture = image;
            _lastCapturePath = outputPath;
            SetStatus($"截图成功：{image.width} × {image.height}", MessageType.Info);

            if (_revealAfterCapture)
                EditorUtility.RevealInFinder(outputPath);
        }
        catch (Exception exception)
        {
            SetStatus("保存路径无效：" + exception.Message, MessageType.Error);
        }
        finally
        {
            CancelQueuedCapture();
            Repaint();
        }
    }

    private void CancelQueuedCapture()
    {
        EditorApplication.delayCall -= PrepareQueuedFrame;
        EditorApplication.delayCall -= WaitForGameViewFrame;
        EditorApplication.delayCall -= CaptureQueuedFrame;
        _captureQueued = false;
        var hiddenUI = _hiddenUI;
        _hiddenUI = null;
        if (hiddenUI == null)
            return;
        hiddenUI.Dispose();
        GameViewScreenshotCapture.TryPrepareGameView(out _);
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        CancelQueuedCapture();
    }

    private string BuildOutputPath()
    {
        var directory = string.IsNullOrWhiteSpace(_outputDirectory)
            ? DefaultOutputDirectory
            : _outputDirectory.Trim();
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var prefix = SanitizeFileName(_filePrefix);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = DefaultFilePrefix;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var path = Path.Combine(directory, $"{prefix}_{timestamp}.png");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{prefix}_{timestamp}_{suffix++}.png");
        }

        _outputDirectory = directory;
        SavePreferences();
        return path;
    }

    private void ChooseOutputDirectory()
    {
        var currentDirectory = GetValidDirectoryOrProjectRoot();
        var selected = EditorUtility.OpenFolderPanel("选择 Game 截图保存目录", currentDirectory, string.Empty);
        if (string.IsNullOrEmpty(selected))
        {
            return;
        }

        _outputDirectory = selected;
        SavePreferences();
        Repaint();
    }

    private void RevealOutputDirectory()
    {
        try
        {
            var directory = GetValidDirectoryOrProjectRoot();
            Directory.CreateDirectory(directory);
            EditorUtility.RevealInFinder(directory);
        }
        catch (Exception exception)
        {
            SetStatus("无法打开保存目录：" + exception.Message, MessageType.Error);
        }
    }

    private string GetValidDirectoryOrProjectRoot()
    {
        if (!string.IsNullOrWhiteSpace(_outputDirectory))
        {
            try
            {
                return Path.GetFullPath(_outputDirectory.Trim());
            }
            catch (Exception)
            {
                // Fall through to a safe project-local directory.
            }
        }

        return DefaultOutputDirectory;
    }

    private void SavePreferences()
    {
        EditorPrefs.SetString(PreferencePrefix + "OutputDirectory", _outputDirectory ?? string.Empty);
        EditorPrefs.SetString(PreferencePrefix + "FilePrefix", _filePrefix ?? string.Empty);
        EditorPrefs.SetBool(PreferencePrefix + "RevealAfterCapture", _revealAfterCapture);
        EditorPrefs.SetBool(PreferencePrefix + "ShowUI", _showUI);
        EditorPrefs.SetBool(PreferencePrefix + "EnableCaptureShortcut", _enableCaptureShortcut);
        EditorPrefs.SetInt(PreferencePrefix + "ShortcutKey", (int)_shortcutKey);
        EditorPrefs.SetInt(PreferencePrefix + "ShortcutModifiers", (int)_shortcutModifiers);
    }

    private void UpdateShortcutRegistration()
    {
        SetShortcutRegistration(_enableCaptureShortcut);
    }

    private string GetShortcutLabel()
    {
        return ((_shortcutModifiers & EventModifiers.Control) != 0 ? "Ctrl + " : string.Empty) +
            ((_shortcutModifiers & EventModifiers.Shift) != 0 ? "Shift + " : string.Empty) +
            ((_shortcutModifiers & EventModifiers.Alt) != 0 ? "Alt + " : string.Empty) +
            ((_shortcutModifiers & EventModifiers.Command) != 0 ? "Command + " : string.Empty) + _shortcutKey;
    }

    private void SetShortcutRegistration(bool enabled)
    {
        if (GlobalEventHandlerField == null)
            return;

        var handler = GlobalEventHandlerField.GetValue(null) as EditorApplication.CallbackFunction;
        handler -= OnGlobalShortcut;
        if (enabled)
            handler += OnGlobalShortcut;
        GlobalEventHandlerField.SetValue(null, handler);
        _shortcutHeld = false;
    }

    private void OnGlobalShortcut()
    {
        var current = Event.current;
        if (current == null || !_enableCaptureShortcut || _recordingShortcut)
            return;
        if (current.type == EventType.KeyUp && current.keyCode == _shortcutKey)
            _shortcutHeld = false;
        if (current.type != EventType.KeyDown || current.keyCode != _shortcutKey ||
            (current.modifiers & ShortcutModifierMask) != _shortcutModifiers)
            return;

        current.Use();
        if (_shortcutHeld)
            return;
        _shortcutHeld = true;
        QueueCapture();
    }

    private void RecordShortcut(Event current)
    {
        if (!_recordingShortcut || current == null || current.type != EventType.KeyDown)
            return;
        if (current.keyCode == KeyCode.Escape)
        {
            _recordingShortcut = false;
            current.Use();
            return;
        }
        if (current.keyCode == KeyCode.None || current.keyCode >= KeyCode.Mouse0 ||
            current.keyCode == KeyCode.LeftControl || current.keyCode == KeyCode.RightControl ||
            current.keyCode == KeyCode.LeftShift || current.keyCode == KeyCode.RightShift ||
            current.keyCode == KeyCode.LeftAlt || current.keyCode == KeyCode.RightAlt ||
            current.keyCode == KeyCode.LeftCommand || current.keyCode == KeyCode.RightCommand ||
            current.keyCode == KeyCode.AltGr)
            return;

        _shortcutKey = current.keyCode;
        _shortcutModifiers = current.modifiers & ShortcutModifierMask;
        _recordingShortcut = false;
        SavePreferences();
        current.Use();
        Repaint();
    }

    private void SetStatus(string message, MessageType type)
    {
        _statusMessage = message;
        _statusType = type;
        Repaint();
    }

    private void DestroyPreview()
    {
        if (_previewTexture == null)
        {
            return;
        }

        DestroyImmediate(_previewTexture);
        _previewTexture = null;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        return new string(value.Trim().Where(character => !invalidCharacters.Contains(character)).ToArray());
    }
}

internal static class GameViewScreenshotCapture
{
    private const string GameViewTypeName = "UnityEditor.GameView";
    private const string RenderTextureFieldName = "m_RenderTexture";

    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type GameViewType = typeof(EditorWindow).Assembly.GetType(GameViewTypeName);
    private static readonly FieldInfo RenderTextureField =
        GameViewType?.GetField(RenderTextureFieldName, InstanceFlags);

    public static bool TryGetSourceInfo(out int width, out int height, out string error)
    {
        width = 0;
        height = 0;

        if (!TryGetGameViewAndTexture(out _, out var source, out error))
        {
            return false;
        }

        width = source.width;
        height = source.height;
        return true;
    }

    public static bool TryPrepareGameView(out string error)
    {
        if (!TryGetGameView(out var gameView, out error))
        {
            return false;
        }

        gameView.Repaint();
        error = null;
        return true;
    }

    public static bool TryCapture(string outputPath, out Texture2D image, out string error)
    {
        image = null;

        if (!TryGetGameViewAndTexture(out _, out var source, out error))
        {
            return false;
        }

        RenderTexture readableTarget = null;
        var previousActive = RenderTexture.active;
        try
        {
            readableTarget = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            // ReadPixels uses a bottom-left origin; Direct3D-like render textures use top-left.
            var scale = SystemInfo.graphicsUVStartsAtTop
                ? new Vector2(1f, -1f)
                : Vector2.one;
            var offset = SystemInfo.graphicsUVStartsAtTop
                ? new Vector2(0f, 1f)
                : Vector2.zero;
            Graphics.Blit(source, readableTarget, scale, offset);

            RenderTexture.active = readableTarget;
            image = new Texture2D(source.width, source.height, TextureFormat.RGB24, false)
            {
                name = "GameView Screenshot Preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            image.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            image.Apply(false, false);

            var pngBytes = image.EncodeToPNG();
            File.WriteAllBytes(outputPath, pngBytes);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            if (image != null)
            {
                UnityEngine.Object.DestroyImmediate(image);
                image = null;
            }

            error = "截图失败：" + exception.Message;
            return false;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (readableTarget != null)
            {
                RenderTexture.ReleaseTemporary(readableTarget);
            }
        }
    }

    public static IDisposable HideUI()
    {
        return new HiddenUIScope();
    }

    // The window restores visibility after the normal GameView repaint, or on cancellation/reload.
    private sealed class HiddenUIScope : IDisposable
    {
        private readonly Canvas[] _canvases;
        private readonly VisualElement[] _roots;
        private readonly StyleEnum<DisplayStyle>[] _display;

        public HiddenUIScope()
        {
            _canvases = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(canvas => canvas.gameObject.scene.IsValid() && canvas.isActiveAndEnabled).ToArray();
            _roots = Resources.FindObjectsOfTypeAll<UIDocument>()
                .Where(document => document.gameObject.scene.IsValid() && document.isActiveAndEnabled)
                .Select(document => document.rootVisualElement).Where(root => root != null).ToArray();
            _display = _roots.Select(root => root.style.display).ToArray();
            try
            {
                foreach (var canvas in _canvases)
                    canvas.enabled = false;
                foreach (var root in _roots)
                    root.style.display = DisplayStyle.None;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var canvas in _canvases)
                if (canvas != null)
                    canvas.enabled = true;
            for (var index = 0; index < _roots.Length; index++)
                _roots[index].style.display = _display[index];
        }
    }

    private static bool TryGetGameViewAndTexture(
        out EditorWindow gameView,
        out RenderTexture renderTexture,
        out string error)
    {
        renderTexture = null;
        if (!TryGetGameView(out gameView, out error))
        {
            return false;
        }

        if (RenderTextureField == null)
        {
            error = "当前 Unity 版本无法访问 Game 视图渲染纹理。";
            return false;
        }

        renderTexture = RenderTextureField.GetValue(gameView) as RenderTexture;
        if (renderTexture == null || !renderTexture.IsCreated() || renderTexture.width <= 0 || renderTexture.height <= 0)
        {
            error = "Game 视图尚未完成渲染。请切换到 Game 标签，等待画面出现后重试。";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetGameView(out EditorWindow gameView, out string error)
    {
        gameView = null;
        if (GameViewType == null)
        {
            error = "当前 Unity 版本中未找到 Game 视图类型。";
            return false;
        }

        if (EditorWindow.focusedWindow != null && GameViewType.IsInstanceOfType(EditorWindow.focusedWindow))
        {
            gameView = EditorWindow.focusedWindow;
        }
        else
        {
            gameView = Resources.FindObjectsOfTypeAll(GameViewType)
                .OfType<EditorWindow>()
                .FirstOrDefault();
        }

        if (gameView == null)
        {
            error = "没有打开的 Game 窗口。请先打开 Window > General > Game。";
            return false;
        }

        error = null;
        return true;
    }
}
