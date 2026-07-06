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
///  JSON 查看与编辑工具
/// ═══════════════════════════════════════════════════════════════
///  通用工具，不依赖项目特定类型。用于打开、查看、编辑、格式化 JSON 文件。
///  • 打开 .json 文件，自动格式化显示
///  • 编辑 JSON 内容并保存
///  • 格式化 / 压缩 JSON
///  • 最近使用文件记录，快速切换
///  • 支持拖拽打开
/// ═══════════════════════════════════════════════════════════════
/// </summary>
[ToolInfo("JSON 查看与编辑", "数据处理",
    Description = "通用 JSON 文件查看与编辑器。\n\n• 打开 .json 文件，自动格式化显示\n• 编辑 JSON 并直接保存\n• 格式化 / 压缩 JSON\n• 最近文件记录，拖拽打开",
    Icon = "📝",
    Tags = new[] { "JSON", "编辑", "查看", "格式化", "通用" },
    Shortcut = "",
    Priority = 26)]
public class JsonViewer : EditorWindow
{
    #region 常量

    private const int MaxRecentCount = 15;
    private const string PrefKeyPrefix = "JsonViewer.";
    private const string PrefKeyFilePath = PrefKeyPrefix + "FilePath";
    private const string PrefKeyRecent = PrefKeyPrefix + "RecentFiles";

    #endregion

    #region 字段

    private string _filePath = "";
    private List<string> _recentFiles = new List<string>();

    private string _contentText = "";
    private string _originalContent = "";
    private bool _isDirty = false;
    private bool _editMode = true;

    private string _statusMessage = "";
    private MessageType _statusType = MessageType.Info;

    private Vector2 _contentScrollPos;
    private Vector2 _mainScrollPos;

    // 项目内文件浏览器
    private bool _showProjectBrowser = false;
    private string _browserFilter = "";
    private List<string> _browserFiles = new List<string>();
    private Vector2 _browserScroll;

    #endregion

    #region 窗口管理

    [MenuItem("Tools/数据处理/JSON 查看与编辑")]
    public static void ShowWindow()
    {
        var window = GetWindow<JsonViewer>("JSON 查看与编辑");
        window.minSize = new Vector2(640f, 450f);
        window.Show();
    }

    private void OnEnable()
    {
        _filePath = EditorPrefs.GetString(PrefKeyFilePath, "");
        _recentFiles = LoadRecentList();
    }

    private void OnDisable()
    {
        EditorPrefs.SetString(PrefKeyFilePath, _filePath);
        SaveRecentList();
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos);

        DrawHeader();
        DrawFilePathRow();
        DrawActionButtons();
        DrawStatusMessage();
        DrawContentSection();

        EditorGUILayout.EndScrollView();

        HandleDragAndDrop();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("JSON 查看与编辑", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("通用 JSON 文件查看与编辑器 —— 打开 .json，格式化显示，编辑后直接保存。", MessageType.Info);
        EditorGUILayout.Space(5);
    }

    private void DrawFilePathRow()
    {
        EditorGUILayout.LabelField("文件路径", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("JSON:", GUILayout.Width(44));
        EditorGUI.BeginChangeCheck();
        _filePath = EditorGUILayout.TextField(_filePath);
        if (EditorGUI.EndChangeCheck())
        {
            if (_isDirty)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "未保存更改",
                    "当前内容已修改，是否保存？",
                    "保存并切换",
                    "取消",
                    "放弃更改");
                if (choice == 0) SaveJson();
                else if (choice == 1) return;
            }
            Repaint();
        }

        if (GUILayout.Button("浏览", EditorStyles.miniButton, GUILayout.Width(44)))
        {
            _showProjectBrowser = !_showProjectBrowser;
            _browserFilter = "";
            if (_showProjectBrowser) RefreshProjectFiles();
        }

        DrawRecentDropdown();
        EditorGUILayout.EndHorizontal();

        DrawRecentInlineList();

        // ── 项目内文件浏览器 ──
        if (_showProjectBrowser)
            DrawProjectFileBrowser(p => { _filePath = p; _showProjectBrowser = false; LoadJsonFile(); });

        EditorGUILayout.Space(5);
    }

    private void DrawRecentDropdown()
    {
        if (!GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(22))) return;

        _recentFiles.RemoveAll(f => !File.Exists(f));
        var menu = new GenericMenu();

        if (_recentFiles.Count > 0)
        {
            for (int i = 0; i < _recentFiles.Count; i++)
            {
                string path = _recentFiles[i];
                string name = Path.GetFileName(path);
                string folder = ToAssetPath(Path.GetDirectoryName(path));
                menu.AddItem(new GUIContent($"{name}  [{folder}]"), false,
                    () => { _filePath = path; LoadJsonFile(); });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("清除最近记录"), false,
                () => { _recentFiles.Clear(); Repaint(); });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("(暂无最近使用)"));
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在项目中查找 .json 文件..."), false, () =>
        {
            _showProjectBrowser = true;
            _browserFilter = "";
            RefreshProjectFiles();
        });

        menu.ShowAsContext();
    }

    private void DrawRecentInlineList()
    {
        _recentFiles.RemoveAll(f => !File.Exists(f));
        if (_recentFiles.Count == 0) return;

        int showCount = Math.Min(_recentFiles.Count, 5);
        for (int i = 0; i < showCount; i++)
        {
            string path = _recentFiles[i];
            string name = Path.GetFileName(path);
            string folder = ToAssetPath(Path.GetDirectoryName(path));

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(48);
            if (GUILayout.Button($"  {name}", EditorStyles.linkLabel))
            { _filePath = path; LoadJsonFile(); }
            EditorGUILayout.LabelField(folder, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);
        if (GUILayout.Button("📂 打开文件", GUILayout.Height(28f)))
            LoadJsonFile();

        GUI.backgroundColor = new Color(0.45f, 0.78f, 0.45f);
        GUI.enabled = _isDirty;
        if (GUILayout.Button("💾 保存", GUILayout.Height(28f)))
            SaveJson();
        GUI.enabled = true;

        GUI.backgroundColor = Color.white;

        if (!string.IsNullOrEmpty(_contentText))
        {
            if (GUILayout.Button("格式化", GUILayout.Height(28f), GUILayout.Width(70)))
                FormatJson();
            if (GUILayout.Button("压缩", GUILayout.Height(28f), GUILayout.Width(55)))
                CompactJson();
        }

        if (GUILayout.Button("📋 复制", GUILayout.Height(28f), GUILayout.Width(55)))
        {
            if (!string.IsNullOrEmpty(_contentText))
            {
                GUIUtility.systemCopyBuffer = _contentText;
                SetStatus("已复制到剪贴板", MessageType.Info);
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
            if (_isDirty) EditorGUILayout.LabelField("状态: 已修改 ●", EditorStyles.miniLabel);
            EditorGUILayout.Space(3);
        }
    }

    private void DrawContentSection()
    {
        if (string.IsNullOrEmpty(_contentText) && string.IsNullOrEmpty(_filePath))
        {
            EditorGUILayout.HelpBox("请打开一个 JSON 文件，或直接拖拽 .json 文件到此窗口。", MessageType.None);
            return;
        }

        if (string.IsNullOrEmpty(_contentText) && !string.IsNullOrEmpty(_filePath))
        {
            if (GUILayout.Button("📂 点击加载文件", GUILayout.Height(40f)))
                LoadJsonFile();
            return;
        }

        EditorGUILayout.LabelField("JSON 内容", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _editMode = EditorGUILayout.ToggleLeft("启用编辑", _editMode, GUILayout.Width(75));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"字符数: {_contentText.Length:N0}", EditorStyles.miniLabel, GUILayout.Width(110));
        EditorGUILayout.EndHorizontal();

        _contentScrollPos = EditorGUILayout.BeginScrollView(_contentScrollPos, GUILayout.ExpandHeight(true));

        if (_editMode)
        {
            string newContent = EditorGUILayout.TextArea(_contentText, GUILayout.ExpandHeight(true), GUILayout.MinHeight(300));
            if (newContent != _contentText)
            {
                _contentText = newContent;
                _isDirty = newContent != _originalContent;
            }
        }
        else
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(_contentText, GUILayout.ExpandHeight(true), GUILayout.MinHeight(300));
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
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            _filePath = path;
            LoadJsonFile();
        }
        evt.Use();
    }

    #endregion

    #region 文件加载与保存

    private void LoadJsonFile()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            _showProjectBrowser = true;
            _browserFilter = "";
            RefreshProjectFiles();
            return;
        }

        if (!File.Exists(_filePath))
        {
            SetStatus($"文件不存在: {_filePath}", MessageType.Error);
            return;
        }

        try
        {
            string raw = File.ReadAllText(_filePath, Encoding.UTF8);

            // 尝试格式化
            try
            {
                JToken token = JToken.Parse(raw);
                _contentText = token.ToString(Formatting.Indented);
            }
            catch (JsonReaderException)
            {
                _contentText = raw;
                SetStatus("⚠ 文件内容不是合法 JSON，以原始文本显示", MessageType.Warning);
            }

            _originalContent = _contentText;
            _isDirty = false;
            _editMode = true;
            RecordRecent(_filePath);

            string fn = Path.GetFileName(_filePath);
            long fs = new FileInfo(_filePath).Length;
            SetStatus($"✅ 已加载: {fn} ({FormatFileSize(fs)})", MessageType.Info);
        }
        catch (Exception ex)
        {
            _contentText = "";
            SetStatus($"加载失败: {ex.Message}", MessageType.Error);
            Debug.LogError($"[JsonViewer] {ex}");
        }
        Repaint();
    }

    private void SaveJson()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            string s = EditorUtility.SaveFilePanel("保存 JSON", Application.dataPath, "data.json", "json");
            if (string.IsNullOrEmpty(s)) return;
            _filePath = s;
        }

        // 验证 JSON 格式
        if (_editMode)
        {
            try { JToken.Parse(_contentText); }
            catch (JsonReaderException jex)
            {
                bool force = EditorUtility.DisplayDialog(
                    "JSON 格式无效",
                    $"内容不是合法 JSON:\n{jex.Message}\n\n是否以原始文本强制保存？",
                    "强制保存", "取消");
                if (!force) return;
            }
        }

        try
        {
            File.WriteAllText(_filePath, _contentText, Encoding.UTF8);
            _originalContent = _contentText;
            _isDirty = false;
            RecordRecent(_filePath);

            string fn = Path.GetFileName(_filePath);
            SetStatus($"✅ 已保存: {fn}", MessageType.Info);
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败: {ex.Message}", MessageType.Error);
            Debug.LogError($"[JsonViewer] {ex}");
        }
        Repaint();
    }

    #endregion

    #region JSON 操作

    private void FormatJson()
    {
        try
        {
            JToken token = JToken.Parse(_contentText);
            _contentText = token.ToString(Formatting.Indented);
            _isDirty = _contentText != _originalContent;
            SetStatus("JSON 已格式化", MessageType.Info);
        }
        catch (JsonReaderException ex)
        { SetStatus($"格式无效: {ex.Message}", MessageType.Error); }
        Repaint();
    }

    private void CompactJson()
    {
        try
        {
            JToken token = JToken.Parse(_contentText);
            _contentText = token.ToString(Formatting.None);
            _isDirty = _contentText != _originalContent;
            SetStatus("JSON 已压缩", MessageType.Info);
        }
        catch (JsonReaderException ex)
        { SetStatus($"格式无效: {ex.Message}", MessageType.Error); }
        Repaint();
    }

    #endregion

    #region 最近使用文件

    private void RecordRecent(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        _recentFiles.Remove(path);
        _recentFiles.Insert(0, path);
        while (_recentFiles.Count > MaxRecentCount)
            _recentFiles.RemoveAt(_recentFiles.Count - 1);
        SaveRecentList();
    }

    private List<string> LoadRecentList()
    {
        string json = EditorPrefs.GetString(PrefKeyRecent, "[]");
        try { return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    private void SaveRecentList()
    {
        EditorPrefs.SetString(PrefKeyRecent, JsonConvert.SerializeObject(_recentFiles));
    }

    #endregion

    #region 工具方法

    private static string BrowseProjectFile(string currentPath)
    {
        string startDir = Application.dataPath;
        if (!string.IsNullOrEmpty(currentPath))
        {
            string dir = Path.GetDirectoryName(currentPath);
            if (Directory.Exists(dir)) startDir = dir;
        }
        return EditorUtility.OpenFilePanel("选择 .json 文件", startDir, "json");
    }

    /// <summary>
    /// 扫描项目 Assets 目录，找出所有 .json 文件
    /// </summary>
    private void RefreshProjectFiles()
    {
        _browserFiles.Clear();
        try
        {
            string[] files = Directory.GetFiles(Application.dataPath, "*.json", SearchOption.AllDirectories);
            _browserFiles.AddRange(files);
            _browserFiles.Sort();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[JsonViewer] 扫描项目文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 绘制项目内文件浏览器面板（搜索 + 列表）
    /// </summary>
    private void DrawProjectFileBrowser(Action<string> onSelect)
    {
        EditorGUI.indentLevel++;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 顶部：搜索 + 按钮
        EditorGUILayout.BeginHorizontal();
        _browserFilter = EditorGUILayout.TextField("搜索:", _browserFilter);
        if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(22)))
            RefreshProjectFiles();
        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
            _showProjectBrowser = false;
        if (GUILayout.Button("📁 系统对话框...", EditorStyles.miniButton, GUILayout.Width(100)))
        {
            string selected = BrowseProjectFile(null);
            if (!string.IsNullOrEmpty(selected))
            {
                _showProjectBrowser = false;
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

        EditorGUILayout.HelpBox($"项目内共 {_browserFiles.Count} 个 .json 文件，匹配 {filtered.Count} 个", MessageType.None);
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

    private void SetStatus(string message, MessageType type)
    {
        _statusMessage = message;
        _statusType = type;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
        return $"{bytes / (1024f * 1024f):F1} MB";
    }

    #endregion
}
#endif
