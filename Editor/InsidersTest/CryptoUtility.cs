#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
/// <summary>
/// ═══════════════════════════════════════════════════════════════
///  加密解密工具
/// ═══════════════════════════════════════════════════════════════
///  通用 XOR 加密解密工具，不依赖项目特定类型。
///  • 解密 .bytes 文件 → 自动识别 JSON / 原始文本
///  • 编辑内容后加密保存回 .bytes
///  • .bytes ↔ .json 互转
///  • Hex 原始字节预览
///  • 自定义 XOR 密钥
///  • 最近使用文件记录
///  • 支持拖拽打开
/// ═══════════════════════════════════════════════════════════════
/// </summary>
[ToolInfo("加密解密工具", "数据处理",
    Description = "通用 XOR 加密解密与二进制互转工具。\n\n• 解密 .bytes → 查看/编辑\n• 加密保存回 .bytes\n• .bytes ↔ .json 互转\n• 自定义密钥 · Hex 预览\n• 拖拽打开 · 最近文件",
    Icon = "🔐",
    Tags = new[] { "加密", "解密", "bytes", "二进制", "互转", "通用" },
    Shortcut = "",
    Priority = 25)]
public class CryptoUtility : EditorWindow
{
    #region 常量

    private const int MaxRecentCount = 15;
    private const string PrefKeyPrefix = "CryptoUtility.";
    private const string PrefKeyXor = PrefKeyPrefix + "XorKey";
    private const string PrefKeyBinaryPath = PrefKeyPrefix + "BinaryPath";
    private const string PrefKeyJsonPath = PrefKeyPrefix + "JsonPath";
    private const string PrefKeyRecentBinary = PrefKeyPrefix + "RecentBinary";
    private const string PrefKeyRecentJson = PrefKeyPrefix + "RecentJson";

    #endregion

    #region 字段

    private string _xorKey = "解密大傻子";
    private bool _showKey = false;

    private string _binaryFilePath = "";
    private string _jsonFilePath = "";

    private List<string> _recentBinaryFiles = new List<string>();
    private List<string> _recentJsonFiles = new List<string>();

    private string _contentText = "";
    private bool _isJsonMode = false;
    private bool _editMode = false;
    private string _rawHexPreview = "";

    private string _statusMessage = "";
    private MessageType _statusType = MessageType.Info;

    private Vector2 _contentScrollPos;
    private Vector2 _mainScrollPos;

    // 项目内文件浏览器
    private bool _showBinaryBrowser = false;
    private bool _showJsonBrowser = false;
    private string _browserFilter = "";
    private List<string> _browserFiles = new List<string>();
    private Vector2 _browserScroll;

    // 批量处理
    private bool _showBatchMode = false;
    private string _batchFolderPath = "";
    private string _batchExtension = "bytes";
    private bool _batchSubfolders = true;
    private string _batchOutputFolder = "";
    private bool _batchUseSameFolder = true;
    private string _batchLog = "";
    private Vector2 _batchLogScroll;
    private List<string> _recentBatchFolders = new List<string>();
    private const string PrefKeyRecentBatchFolders = PrefKeyPrefix + "RecentBatchFolders";

    #endregion

    #region 窗口管理

    [MenuItem("Tools/数据处理/加密解密工具")]
    public static void ShowWindow()
    {
        var window = GetWindow<CryptoUtility>("加密解密工具");
        window.minSize = new Vector2(640f, 500f);
        window.Show();
    }

    private void OnEnable()
    {
        _xorKey = EditorPrefs.GetString(PrefKeyXor, "解密大傻子");
        _binaryFilePath = EditorPrefs.GetString(PrefKeyBinaryPath, "");
        _jsonFilePath = EditorPrefs.GetString(PrefKeyJsonPath, "");
        _recentBinaryFiles = LoadRecentList(PrefKeyRecentBinary);
        _recentJsonFiles = LoadRecentList(PrefKeyRecentJson);
        _recentBatchFolders = LoadRecentList(PrefKeyRecentBatchFolders);
        // 清理不存在的文件夹
        _recentBatchFolders.RemoveAll(f => !Directory.Exists(f));
    }

    private void OnDisable()
    {
        EditorPrefs.SetString(PrefKeyXor, _xorKey);
        EditorPrefs.SetString(PrefKeyBinaryPath, _binaryFilePath);
        EditorPrefs.SetString(PrefKeyJsonPath, _jsonFilePath);
        SaveRecentList(PrefKeyRecentBinary, _recentBinaryFiles);
        SaveRecentList(PrefKeyRecentJson, _recentJsonFiles);
        SaveRecentList(PrefKeyRecentBatchFolders, _recentBatchFolders);
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos);

        DrawHeader();
        DrawKeySection();
        DrawFileSection();
        DrawBatchSection();
        DrawActionButtons();
        DrawStatusMessage();
        DrawContentSection();

        EditorGUILayout.EndScrollView();

        HandleDragAndDrop();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("加密解密工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "通用 XOR 加密解密工具 —— 解密 .bytes 查看内容，编辑后加密保存。支持 .bytes ↔ .json 互转。",
            MessageType.Info);
        EditorGUILayout.Space(5);
    }

    private void DrawKeySection()
    {
        EditorGUILayout.LabelField("XOR 密钥", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (_showKey)
            _xorKey = EditorGUILayout.TextField("密钥:", _xorKey);
        else
        {
            EditorGUILayout.LabelField("密钥:", GUILayout.Width(40));
            EditorGUILayout.PasswordField(_xorKey);
        }
        _showKey = EditorGUILayout.ToggleLeft("显示", _showKey, GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
    }

    private void DrawFileSection()
    {
        EditorGUILayout.LabelField("文件路径", EditorStyles.boldLabel);

        // ── Binary 行 ──
        _binaryFilePath = DrawFileRow("Binary:", _binaryFilePath, "bytes",
            _recentBinaryFiles,
            () => { _showBinaryBrowser = !_showBinaryBrowser; _showJsonBrowser = false; _browserFilter = ""; if (_showBinaryBrowser) RefreshProjectFiles("bytes"); });

        EditorGUILayout.Space(1);

        // ── JSON 行 ──
        _jsonFilePath = DrawFileRow("JSON:", _jsonFilePath, "json",
            _recentJsonFiles,
            () => { _showJsonBrowser = !_showJsonBrowser; _showBinaryBrowser = false; _browserFilter = ""; if (_showJsonBrowser) RefreshProjectFiles("json"); });

        if (string.IsNullOrEmpty(_binaryFilePath) && string.IsNullOrEmpty(_jsonFilePath)
            && _recentBinaryFiles.Count == 0 && _recentJsonFiles.Count == 0
            && !_showBinaryBrowser && !_showJsonBrowser)
        {
            EditorGUILayout.HelpBox("提示：可拖拽 .bytes 或 .json 文件到此窗口。点击「浏览」查找项目内文件。", MessageType.None);
        }

        // ── 项目内文件浏览器 ──
        if (_showBinaryBrowser)
            DrawProjectFileBrowser("bytes", p => { _binaryFilePath = p; _showBinaryBrowser = false; Repaint(); });
        if (_showJsonBrowser)
            DrawProjectFileBrowser("json", p => { _jsonFilePath = p; _showJsonBrowser = false; Repaint(); });

        EditorGUILayout.Space(5);
    }

    private string DrawFileRow(string label, string path, string extension,
        List<string> recentList, Action onBrowse)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(50));
        EditorGUI.BeginChangeCheck();
        path = EditorGUILayout.TextField(path);
        if (EditorGUI.EndChangeCheck()) Repaint();

        if (GUILayout.Button("浏览", EditorStyles.miniButton, GUILayout.Width(44)))
            onBrowse?.Invoke();

        DrawRecentDropdown(recentList, extension, newPath => { path = newPath; Repaint(); }, onBrowse);
        EditorGUILayout.EndHorizontal();

        DrawRecentInline(recentList, newPath => { path = newPath; Repaint(); });

        return path;
    }

    private void DrawBatchSection()
    {
        EditorGUILayout.Space(3);

        // 折叠头
        EditorGUILayout.BeginHorizontal();
        _showBatchMode = EditorGUILayout.Foldout(_showBatchMode, "📦 批量处理", true, EditorStyles.foldoutHeader);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (!_showBatchMode) return;

        EditorGUI.indentLevel++;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // ── 文件夹路径 ──
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("文件夹:", GUILayout.Width(50));
        _batchFolderPath = EditorGUILayout.TextField(_batchFolderPath);
        if (GUILayout.Button("浏览", EditorStyles.miniButton, GUILayout.Width(44)))
        {
            string s = EditorUtility.OpenFolderPanel("选择要处理的文件夹",
                string.IsNullOrEmpty(_batchFolderPath) ? Application.dataPath : _batchFolderPath, "");
            if (!string.IsNullOrEmpty(s)) { _batchFolderPath = s; Repaint(); }
        }
        // 最近使用文件夹下拉
        if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(22)))
        {
            _recentBatchFolders.RemoveAll(f => !Directory.Exists(f));
            var menu = new GenericMenu();
            if (_recentBatchFolders.Count > 0)
            {
                for (int i = 0; i < Math.Min(_recentBatchFolders.Count, 10); i++)
                {
                    string f = _recentBatchFolders[i];
                    menu.AddItem(new GUIContent(ToAssetPath(f)), false,
                        () => { _batchFolderPath = f; Repaint(); });
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("清除最近记录"), false,
                    () => { _recentBatchFolders.Clear(); Repaint(); });
            }
            else menu.AddDisabledItem(new GUIContent("(暂无)"));
            menu.ShowAsContext();
        }
        EditorGUILayout.EndHorizontal();

        // ── 选项行 ──
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("扩展名:", GUILayout.Width(50));
        _batchExtension = EditorGUILayout.TextField(_batchExtension, GUILayout.Width(60));
        GUILayout.Space(10);
        _batchSubfolders = EditorGUILayout.ToggleLeft("包含子文件夹", _batchSubfolders, GUILayout.Width(100));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // ── 输出文件夹 ──
        _batchUseSameFolder = EditorGUILayout.ToggleLeft("输出到源文件夹（覆盖）", _batchUseSameFolder);
        if (!_batchUseSameFolder)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("输出到:", GUILayout.Width(50));
            _batchOutputFolder = EditorGUILayout.TextField(_batchOutputFolder);
            if (GUILayout.Button("浏览", EditorStyles.miniButton, GUILayout.Width(44)))
            {
                string s = EditorUtility.OpenFolderPanel("选择输出文件夹",
                    string.IsNullOrEmpty(_batchOutputFolder) ? Application.dataPath : _batchOutputFolder, "");
                if (!string.IsNullOrEmpty(s)) { _batchOutputFolder = s; Repaint(); }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(3);

        // ── 操作按钮行 ──
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);
        if (GUILayout.Button("🔓 批量解密", GUILayout.Height(28f)))
            BatchDecrypt();

        GUI.backgroundColor = new Color(1f, 0.55f, 0.25f);
        if (GUILayout.Button("🔒 批量加密", GUILayout.Height(28f)))
            BatchEncrypt();

        GUI.backgroundColor = new Color(0.45f, 0.78f, 0.45f);
        if (GUILayout.Button("📤 批量导出 JSON", GUILayout.Height(28f)))
            BatchExportJson();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // ── 日志 ──
        if (!string.IsNullOrEmpty(_batchLog))
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("处理日志", EditorStyles.boldLabel);
            if (GUILayout.Button("清除日志", EditorStyles.miniButton, GUILayout.Width(70)))
                _batchLog = "";

            _batchLogScroll = EditorGUILayout.BeginScrollView(_batchLogScroll, GUILayout.MaxHeight(150));
            EditorGUILayout.TextArea(_batchLog, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(5);
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);
        if (GUILayout.Button("🔓 解密并查看", GUILayout.Height(30f)))
            DecryptAndView();

        GUI.backgroundColor = new Color(1f, 0.55f, 0.25f);
        if (GUILayout.Button("🔒 加密并保存", GUILayout.Height(30f)))
            EncryptAndSave();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.45f, 0.78f, 0.45f);
        if (GUILayout.Button("📤 Binary → JSON 导出", GUILayout.Height(26f)))
            ExportBinaryToJson();

        GUI.backgroundColor = new Color(0.78f, 0.65f, 0.35f);
        if (GUILayout.Button("📥 JSON → Binary 导入", GUILayout.Height(26f)))
            ImportJsonToBinary();

        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("📋 复制内容", GUILayout.Height(26f), GUILayout.Width(90)))
        {
            if (!string.IsNullOrEmpty(_contentText))
            {
                GUIUtility.systemCopyBuffer = _contentText;
                SetStatus("内容已复制到剪贴板", MessageType.Info);
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(5);
    }

    private void DrawStatusMessage()
    {
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.HelpBox(_statusMessage, _statusType);

            if (_isJsonMode)
                EditorGUILayout.LabelField("当前模式: JSON", EditorStyles.miniLabel);
            else if (!string.IsNullOrEmpty(_contentText))
                EditorGUILayout.LabelField("当前模式: 原始文本（非 JSON）", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
        }
    }

    private void DrawContentSection()
    {
        if (string.IsNullOrEmpty(_contentText))
            return;

        EditorGUILayout.LabelField("数据内容", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _editMode = EditorGUILayout.ToggleLeft("启用编辑", _editMode, GUILayout.Width(75));

        if (_isJsonMode)
        {
            if (GUILayout.Button("压缩 JSON", GUILayout.Width(80)))
                CompactJson();
            if (GUILayout.Button("格式化 JSON", GUILayout.Width(90)))
                FormatJson();
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"字符数: {_contentText.Length:N0}", EditorStyles.miniLabel, GUILayout.Width(110));
        EditorGUILayout.EndHorizontal();

        // Hex 预览（非 JSON 模式）
        if (!_isJsonMode && !string.IsNullOrEmpty(_rawHexPreview))
        {
            if (EditorGUILayout.Foldout(true, "Hex 预览 (前 512 字节)"))
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextArea(_rawHexPreview, GUILayout.Height(60));
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.Space(3);
        }

        _contentScrollPos = EditorGUILayout.BeginScrollView(_contentScrollPos, GUILayout.ExpandHeight(true));

        if (_editMode)
        {
            string newContent = EditorGUILayout.TextArea(_contentText, GUILayout.ExpandHeight(true), GUILayout.MinHeight(280));
            if (newContent != _contentText)
                _contentText = newContent;
        }
        else
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(_contentText, GUILayout.ExpandHeight(true), GUILayout.MinHeight(280));
            EditorGUI.EndDisabledGroup();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(5);
    }

    #endregion

    #region 拖拽

    private void HandleDragAndDrop()
    {
        Event evt = Event.current;
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            return;

        string[] paths = DragAndDrop.paths;
        if (paths == null || paths.Length == 0) return;

        string path = paths[0];
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".bytes" || ext == ".json")
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                if (ext == ".bytes")
                {
                    _binaryFilePath = path;
                    DecryptAndView();
                }
                else
                {
                    _jsonFilePath = path;
                }
            }

            evt.Use();
        }
    }

    #endregion

    #region XOR 加密/解密

    private static byte[] XorCrypt(byte[] data, string key)
    {
        if (data == null || data.Length == 0)
            return Array.Empty<byte>();

        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length == 0)
            return data;

        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
        return result;
    }

    #endregion

    #region 解密查看

    private void DecryptAndView()
    {
        if (!ValidateBinaryPath()) return;

        try
        {
            byte[] encrypted = File.ReadAllBytes(_binaryFilePath);
            byte[] decrypted = XorCrypt(encrypted, _xorKey);
            string text = Encoding.UTF8.GetString(decrypted);
            _rawHexPreview = GenerateHexPreview(decrypted, 512);

            if (TryParseAndFormatJson(text, out string formattedJson))
            {
                _contentText = formattedJson;
                _isJsonMode = true;
                _editMode = true;
                RecordRecent(_recentBinaryFiles, _binaryFilePath, PrefKeyRecentBinary);

                string fn = Path.GetFileName(_binaryFilePath);
                long fs = new FileInfo(_binaryFilePath).Length;
                SetStatus($"✅ 解密成功 → JSON 模式 | {fn} ({FormatFileSize(fs)})", MessageType.Info);
            }
            else
            {
                _contentText = text;
                _isJsonMode = false;
                _editMode = true;
                RecordRecent(_recentBinaryFiles, _binaryFilePath, PrefKeyRecentBinary);

                string fn = Path.GetFileName(_binaryFilePath);
                long fs = new FileInfo(_binaryFilePath).Length;
                SetStatus($"⚠ 解密成功 → 原始文本 | {fn} ({FormatFileSize(fs)})", MessageType.Warning);
            }
        }
        catch (Exception ex)
        {
            ClearContent();
            SetStatus($"解密失败: {ex.Message}", MessageType.Error);
            Debug.LogError($"[CryptoUtility] {ex}");
        }
        Repaint();
    }

    #endregion

    #region 加密保存

    private void EncryptAndSave()
    {
        if (string.IsNullOrEmpty(_contentText))
        { SetStatus("没有可保存的内容，请先解密一个文件", MessageType.Warning); return; }
        if (!ValidateBinaryPath()) return;

        try
        {
            if (_isJsonMode)
            {
                try { JToken.Parse(_contentText); }
                catch (JsonReaderException jex)
                { SetStatus($"JSON 格式无效: {jex.Message}", MessageType.Error); return; }
            }

            byte[] plainBytes = Encoding.UTF8.GetBytes(_contentText);
            byte[] encrypted = XorCrypt(plainBytes, _xorKey);
            File.WriteAllBytes(_binaryFilePath, encrypted);
            RecordRecent(_recentBinaryFiles, _binaryFilePath, PrefKeyRecentBinary);

            string fn = Path.GetFileName(_binaryFilePath);
            long fs = new FileInfo(_binaryFilePath).Length;
            SetStatus($"✅ 加密保存成功！{fn} ({FormatFileSize(fs)})", MessageType.Info);
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            SetStatus($"加密保存失败: {ex.Message}", MessageType.Error);
            Debug.LogError($"[CryptoUtility] {ex}");
        }
        Repaint();
    }

    #endregion

    #region Binary ↔ JSON 互转

    private void ExportBinaryToJson()
    {
        if (!ValidateBinaryPath()) return;
        if (!File.Exists(_binaryFilePath))
        { SetStatus($"文件不存在: {_binaryFilePath}", MessageType.Error); return; }

        try
        {
            byte[] encrypted = File.ReadAllBytes(_binaryFilePath);
            byte[] decrypted = XorCrypt(encrypted, _xorKey);
            string text = Encoding.UTF8.GetString(decrypted);

            if (TryParseAndFormatJson(text, out string formattedJson))
            { text = formattedJson; _contentText = formattedJson; _isJsonMode = true; _editMode = true; }
            else { _contentText = text; _isJsonMode = false; _editMode = true; }

            string outputPath = GetJsonOutputPath();
            File.WriteAllText(outputPath, text, Encoding.UTF8);
            _jsonFilePath = outputPath;
            RecordRecent(_recentJsonFiles, outputPath, PrefKeyRecentJson);

            SetStatus($"✅ JSON 导出成功: {Path.GetFileName(outputPath)}", MessageType.Info);
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            SetStatus($"导出失败: {ex.Message}", MessageType.Error);
            Debug.LogError($"[CryptoUtility] {ex}");
        }
        Repaint();
    }

    private void ImportJsonToBinary()
    {
        if (string.IsNullOrEmpty(_jsonFilePath))
        { SetStatus("请先选择 JSON 文件", MessageType.Warning); return; }
        if (!File.Exists(_jsonFilePath))
        { SetStatus($"JSON 文件不存在: {_jsonFilePath}", MessageType.Error); return; }
        if (!ValidateBinaryPath()) return;

        try
        {
            string jsonText = File.ReadAllText(_jsonFilePath, Encoding.UTF8);

            try
            {
                JToken parsed = JToken.Parse(jsonText);
                _contentText = parsed.ToString(Formatting.Indented);
                _isJsonMode = true;
                _editMode = true;
            }
            catch (JsonReaderException)
            {
                _contentText = jsonText;
                _isJsonMode = false;
                _editMode = true;
                SetStatus("⚠ 不是合法 JSON，将以原始文本加密保存", MessageType.Warning);
            }

            byte[] plainBytes = Encoding.UTF8.GetBytes(jsonText);
            byte[] encrypted = XorCrypt(plainBytes, _xorKey);
            File.WriteAllBytes(_binaryFilePath, encrypted);
            RecordRecent(_recentJsonFiles, _jsonFilePath, PrefKeyRecentJson);
            RecordRecent(_recentBinaryFiles, _binaryFilePath, PrefKeyRecentBinary);

            string fn = Path.GetFileName(_binaryFilePath);
            long fs = new FileInfo(_binaryFilePath).Length;
            SetStatus($"✅ JSON → Binary 转换成功！{fn} ({FormatFileSize(fs)})", MessageType.Info);
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            SetStatus($"导入失败: {ex.Message}", MessageType.Error);
            Debug.LogError($"[CryptoUtility] {ex}");
        }
        Repaint();
    }

    #endregion

    #region JSON 工具

    private void CompactJson()
    {
        try
        {
            JToken token = JToken.Parse(_contentText);
            _contentText = token.ToString(Formatting.None);
            SetStatus("JSON 已压缩", MessageType.Info);
        }
        catch (JsonReaderException ex)
        { SetStatus($"格式无效: {ex.Message}", MessageType.Error); }
        Repaint();
    }

    private void FormatJson()
    {
        try
        {
            JToken token = JToken.Parse(_contentText);
            _contentText = token.ToString(Formatting.Indented);
            SetStatus("JSON 已格式化", MessageType.Info);
        }
        catch (JsonReaderException ex)
        { SetStatus($"格式无效: {ex.Message}", MessageType.Error); }
        Repaint();
    }

    private static bool TryParseAndFormatJson(string text, out string formattedJson)
    {
        formattedJson = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            JToken token = JToken.Parse(text);
            formattedJson = token.ToString(Formatting.Indented);
            return true;
        }
        catch (JsonReaderException) { return false; }
    }

    #endregion

    #region 最近使用文件

    private void DrawRecentDropdown(List<string> recentList, string extension, Action<string> onSelect, Action onBrowse)
    {
        if (!GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(22))) return;

        recentList.RemoveAll(f => !File.Exists(f));
        var menu = new GenericMenu();

        if (recentList.Count > 0)
        {
            for (int i = 0; i < recentList.Count; i++)
            {
                string p = recentList[i];
                string name = Path.GetFileName(p);
                string folder = ToAssetPath(Path.GetDirectoryName(p));
                menu.AddItem(new GUIContent($"{name}  [{folder}]"), false,
                    () => onSelect?.Invoke(p));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("清除最近记录"), false, () =>
            { recentList.Clear(); Repaint(); });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("(暂无最近使用)"));
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent($"在项目中查找 .{extension} 文件..."), false,
            () => onBrowse?.Invoke());

        menu.ShowAsContext();
    }

    private void DrawRecentInline(List<string> recentList, Action<string> onSelect)
    {
        recentList.RemoveAll(f => !File.Exists(f));
        if (recentList.Count == 0) return;

        int showCount = Math.Min(recentList.Count, 5);
        for (int i = 0; i < showCount; i++)
        {
            string p = recentList[i];
            string name = Path.GetFileName(p);
            string folder = ToAssetPath(Path.GetDirectoryName(p));

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(54);
            if (GUILayout.Button($"  {name}", EditorStyles.linkLabel))
                onSelect?.Invoke(p);
            EditorGUILayout.LabelField(folder, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void RecordRecent(List<string> list, string path, string prefKey)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        list.Remove(path);
        list.Insert(0, path);
        while (list.Count > MaxRecentCount)
            list.RemoveAt(list.Count - 1);
        SaveRecentList(prefKey, list);
    }

    private static List<string> LoadRecentList(string key)
    {
        string json = EditorPrefs.GetString(key, "[]");
        try { return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    private static void SaveRecentList(string key, List<string> list)
    {
        EditorPrefs.SetString(key, JsonConvert.SerializeObject(list));
    }

    #endregion

    #region 工具方法

    private bool ValidateBinaryPath()
    {
        if (string.IsNullOrEmpty(_binaryFilePath))
        { SetStatus("请先输入二进制文件路径（.bytes）", MessageType.Warning); return false; }
        return true;
    }

    private string GetJsonOutputPath()
    {
        if (!string.IsNullOrEmpty(_jsonFilePath)) return _jsonFilePath;
        string dir = Path.GetDirectoryName(_binaryFilePath);
        string name = Path.GetFileNameWithoutExtension(_binaryFilePath);
        return Path.Combine(dir ?? Application.dataPath, $"{name}.json");
    }

    private void ClearContent()
    {
        _contentText = "";
        _rawHexPreview = "";
        _isJsonMode = false;
        _editMode = false;
    }

    private void SetStatus(string message, MessageType type)
    {
        _statusMessage = message;
        _statusType = type;
        if (type == MessageType.Error) Debug.LogError($"[CryptoUtility] {message}");
        else if (type == MessageType.Warning) Debug.LogWarning($"[CryptoUtility] {message}");
    }

    private static string BrowseProjectFile(string extension, string currentPath)
    {
        string startDir = Application.dataPath;
        if (!string.IsNullOrEmpty(currentPath))
        {
            string dir = Path.GetDirectoryName(currentPath);
            if (Directory.Exists(dir)) startDir = dir;
        }
        return EditorUtility.OpenFilePanel($"选择 .{extension} 文件", startDir, extension);
    }

    /// <summary>
    /// 扫描项目 Assets 目录，找出所有指定扩展名的文件
    /// </summary>
    private void RefreshProjectFiles(string extension)
    {
        _browserFiles.Clear();
        try
        {
            string[] files = Directory.GetFiles(Application.dataPath, $"*.{extension}", SearchOption.AllDirectories);
            _browserFiles.AddRange(files);
            _browserFiles.Sort();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CryptoUtility] 扫描项目文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 绘制项目内文件浏览器面板（搜索 + 列表）
    /// </summary>
    private void DrawProjectFileBrowser(string extension, Action<string> onSelect)
    {
        EditorGUI.indentLevel++;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 顶部：搜索 + 按钮
        EditorGUILayout.BeginHorizontal();
        _browserFilter = EditorGUILayout.TextField("搜索:", _browserFilter);
        if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(22)))
            RefreshProjectFiles(extension);
        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
        {
            _showBinaryBrowser = false;
            _showJsonBrowser = false;
        }
        if (GUILayout.Button("📁 系统对话框...", EditorStyles.miniButton, GUILayout.Width(100)))
        {
            string selected = BrowseProjectFile(extension, null);
            if (!string.IsNullOrEmpty(selected))
            {
                _showBinaryBrowser = false;
                _showJsonBrowser = false;
                onSelect?.Invoke(selected);
            }
        }
        EditorGUILayout.EndHorizontal();

        // 过滤文件列表
        var filtered = string.IsNullOrEmpty(_browserFilter)
            ? _browserFiles
            : _browserFiles.FindAll(f =>
                Path.GetFileName(f).IndexOf(_browserFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                ToAssetPath(f).IndexOf(_browserFilter, StringComparison.OrdinalIgnoreCase) >= 0);

        // 文件列表
        if (filtered.Count == 0)
        {
            EditorGUILayout.LabelField(_browserFiles.Count == 0 ? "正在扫描..." : "无匹配文件",
                EditorStyles.centeredGreyMiniLabel);
        }
        else
        {
            _browserScroll = EditorGUILayout.BeginScrollView(_browserScroll, GUILayout.MaxHeight(260));
            foreach (string filePath in filtered)
            {
                string fileName = Path.GetFileName(filePath);
                string folder = ToAssetPath(Path.GetDirectoryName(filePath));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button($"  {fileName}", EditorStyles.linkLabel))
                {
                    onSelect?.Invoke(filePath);
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(folder, EditorStyles.miniLabel, GUILayout.MaxWidth(300));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.HelpBox($"项目内共 {_browserFiles.Count} 个 .{extension} 文件，匹配 {filtered.Count} 个", MessageType.None);
        EditorGUILayout.EndVertical();
        EditorGUI.indentLevel--;
    }

    private static string ToAssetPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return "";
        if (fullPath.StartsWith(Application.dataPath))
            return "Assets" + fullPath.Substring(Application.dataPath.Length);
        return fullPath;
    }

    private static string GenerateHexPreview(byte[] data, int maxBytes)
    {
        if (data == null || data.Length == 0) return "(空数据)";
        int len = Math.Min(data.Length, maxBytes);
        var sb = new StringBuilder();
        for (int i = 0; i < len; i++)
        {
            sb.Append(data[i].ToString("X2"));
            sb.Append(' ');
            if ((i + 1) % 16 == 0) sb.AppendLine();
        }
        if (data.Length > maxBytes)
            sb.AppendLine($"... (共 {data.Length} 字节)");
        return sb.ToString();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
        return $"{bytes / (1024f * 1024f * 1024f):F1} GB";
    }

    #endregion

    #region 批量处理

    private void BatchEncrypt()
    {
        if (!ValidateBatchFolder()) return;
        RecordRecentBatchFolder(_batchFolderPath);

        string[] files = GetBatchFiles();
        if (files.Length == 0)
        {
            AppendLog($"[⚠] 未找到 .{_batchExtension} 文件");
            SetStatus($"未找到 .{_batchExtension} 文件", MessageType.Warning);
            return;
        }

        string outputDir = GetBatchOutputDir();
        int success = 0, fail = 0;
        AppendLog($"[▶] 批量加密开始，共 {files.Length} 个文件");

        foreach (string filePath in files)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                byte[] encrypted = XorCrypt(data, _xorKey);
                string dest = GetBatchDestPath(filePath, outputDir);
                File.WriteAllBytes(dest, encrypted);
                AppendLog($"  ✅ {Path.GetFileName(filePath)} → {FormatFileSize(new FileInfo(dest).Length)}");
                success++;
            }
            catch (Exception ex)
            {
                AppendLog($"  ❌ {Path.GetFileName(filePath)}: {ex.Message}");
                fail++;
            }
        }

        AppendLog($"[✓] 完成：成功 {success}，失败 {fail}");
        SetStatus($"批量加密完成：成功 {success}，失败 {fail}", fail > 0 ? MessageType.Warning : MessageType.Info);
        AssetDatabase.Refresh();
        Repaint();
    }

    private void BatchDecrypt()
    {
        if (!ValidateBatchFolder()) return;
        RecordRecentBatchFolder(_batchFolderPath);

        string[] files = GetBatchFiles();
        if (files.Length == 0)
        {
            AppendLog($"[⚠] 未找到 .{_batchExtension} 文件");
            SetStatus($"未找到 .{_batchExtension} 文件", MessageType.Warning);
            return;
        }

        string outputDir = GetBatchOutputDir();
        int success = 0, fail = 0;
        AppendLog($"[▶] 批量解密开始，共 {files.Length} 个文件");

        foreach (string filePath in files)
        {
            try
            {
                byte[] encrypted = File.ReadAllBytes(filePath);
                byte[] decrypted = XorCrypt(encrypted, _xorKey);
                string dest = GetBatchDestPath(filePath, outputDir);
                File.WriteAllBytes(dest, decrypted);
                AppendLog($"  ✅ {Path.GetFileName(filePath)} → {FormatFileSize(new FileInfo(dest).Length)}");
                success++;
            }
            catch (Exception ex)
            {
                AppendLog($"  ❌ {Path.GetFileName(filePath)}: {ex.Message}");
                fail++;
            }
        }

        AppendLog($"[✓] 完成：成功 {success}，失败 {fail}");
        SetStatus($"批量解密完成：成功 {success}，失败 {fail}", fail > 0 ? MessageType.Warning : MessageType.Info);
        AssetDatabase.Refresh();
        Repaint();
    }

    private void BatchExportJson()
    {
        if (!ValidateBatchFolder()) return;
        RecordRecentBatchFolder(_batchFolderPath);

        string[] files = GetBatchFiles();
        if (files.Length == 0)
        {
            AppendLog($"[⚠] 未找到 .{_batchExtension} 文件");
            SetStatus($"未找到 .{_batchExtension} 文件", MessageType.Warning);
            return;
        }

        string outputDir = GetBatchOutputDir();
        int success = 0, fail = 0;
        AppendLog($"[▶] 批量导出 JSON 开始，共 {files.Length} 个文件");

        foreach (string filePath in files)
        {
            try
            {
                byte[] encrypted = File.ReadAllBytes(filePath);
                byte[] decrypted = XorCrypt(encrypted, _xorKey);
                string text = Encoding.UTF8.GetString(decrypted);

                // 尝试格式化 JSON 使其更可读
                if (TryParseAndFormatJson(text, out string formatted))
                    text = formatted;

                string dest = Path.Combine(outputDir,
                    Path.GetFileNameWithoutExtension(filePath) + ".json");
                File.WriteAllText(dest, text, Encoding.UTF8);
                AppendLog($"  ✅ {Path.GetFileName(filePath)} → {Path.GetFileName(dest)}");
                success++;
            }
            catch (Exception ex)
            {
                AppendLog($"  ❌ {Path.GetFileName(filePath)}: {ex.Message}");
                fail++;
            }
        }

        AppendLog($"[✓] 完成：成功 {success}，失败 {fail}");
        SetStatus($"批量导出 JSON 完成：成功 {success}，失败 {fail}", fail > 0 ? MessageType.Warning : MessageType.Info);
        AssetDatabase.Refresh();
        Repaint();
    }

    private bool ValidateBatchFolder()
    {
        if (string.IsNullOrEmpty(_batchFolderPath))
        { SetStatus("请先选择要处理的文件夹", MessageType.Warning); return false; }
        if (!Directory.Exists(_batchFolderPath))
        { SetStatus($"文件夹不存在: {_batchFolderPath}", MessageType.Error); return false; }
        if (string.IsNullOrWhiteSpace(_batchExtension))
        { SetStatus("请输入文件扩展名", MessageType.Warning); return false; }
        return true;
    }

    private string[] GetBatchFiles()
    {
        try
        {
            string ext = _batchExtension.TrimStart('.');
            SearchOption option = _batchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.GetFiles(_batchFolderPath, $"*.{ext}", option);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CryptoUtility] 扫描文件失败: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private string GetBatchOutputDir()
    {
        if (_batchUseSameFolder || string.IsNullOrEmpty(_batchOutputFolder))
            return _batchFolderPath;

        if (!Directory.Exists(_batchOutputFolder))
            Directory.CreateDirectory(_batchOutputFolder);
        return _batchOutputFolder;
    }

    private string GetBatchDestPath(string sourceFile, string outputDir)
    {
        if (_batchUseSameFolder)
            return sourceFile;
        return Path.Combine(outputDir, Path.GetFileName(sourceFile));
    }

    private void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _batchLog += $"[{timestamp}] {message}\n";
    }

    private void RecordRecentBatchFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        _recentBatchFolders.Remove(path);
        _recentBatchFolders.Insert(0, path);
        while (_recentBatchFolders.Count > MaxRecentCount)
            _recentBatchFolders.RemoveAt(_recentBatchFolders.Count - 1);
        SaveRecentList(PrefKeyRecentBatchFolders, _recentBatchFolders);
    }

    #endregion
}
#endif
