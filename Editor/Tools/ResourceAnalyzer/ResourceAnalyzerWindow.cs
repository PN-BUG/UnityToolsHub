#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ResourceAnalyzer
{
    /// <summary>
    /// 资源分析与优化工具 —— 主窗口
    /// 菜单: Tools/资源分析与优化
    /// </summary>
    public class ResourceAnalyzerWindow : EditorWindow
    {
        // ──────────── 窗口状态 ────────────
        private TargetPlatform _selectedPlatform = TargetPlatform.Android;
        private string _scanPath = "Assets";
        private bool _onlyBuildAssets = true;
        private bool _lastAnalysisFilteredBuildAssets;
        private int _excludedAssetCount;
        private Vector2 _scrollPos;
        private Vector2 _detailScrollPos;
        private int _selectedResultIndex = -1;

        // ──────────── 分析结果 ────────────
        private List<ResourceAnalysisResult> _results = new();
        private bool _hasAnalyzed;

        // ──────────── 过滤与排序 ────────────
        private enum FilterMode { All, HasIssue, UI, NonUI, NPOT, LargeSize, ReadWrite }
        private FilterMode _filterMode = FilterMode.All;
        private enum SortColumn { Name, Size, Disk, Width, Format, Severity }
        private SortColumn _sortColumn = SortColumn.Severity;
        private bool _sortAscending;
        private string _searchKeyword = "";

        // ──────────── 性能缓存 ────────────
        private List<ResourceAnalysisResult> _cachedFiltered = new();
        private Dictionary<ResourceAnalysisResult, int> _indexMap = new();
        private bool _filterDirty = true;
        private int _cachedSelectedCount;
        private string _lastSearchKeyword = "";
        private FilterMode _lastFilterMode;
        private SortColumn _lastSortColumn;
        private bool _lastSortAscending;
        private const float ROW_HEIGHT = 20f;

        // ──────────── Shift 多选 ────────────
        private string _selectionAnchorPath;
        private string _hoveredAssetPath;
        private readonly Dictionary<string, bool> _combinedSuggestionSelection = new();
        private readonly Dictionary<string, bool> _expandedCombinedGroups = new();

        private sealed class SuggestionOccurrence
        {
            public ResourceAnalysisResult Resource;
            public OptimizationSuggestion Suggestion;
        }

        private sealed class CombinedSuggestion
        {
            public string Key;
            public OptimizationSuggestion Example;
            public readonly List<SuggestionOccurrence> Occurrences = new();
        }

        // ──────────── 分析器注册（预留扩展） ────────────
        private readonly Dictionary<string, IResourceAnalyzer> _analyzers = new();
        private string _activeAnalyzerKey = "Texture";

        // ──────────── UI 缓存 ────────────
        private GUIStyle _headerStyle;
        private GUIStyle _tagStyle;
        private GUIStyle _suggestionCardStyle;
        private GUIStyle _selectedSuggestionCardStyle;
        private GUIStyle _suggestionToggleStyle;
        private GUIStyle _suggestionTitleStyle;
        private GUIStyle _suggestionBodyStyle;
        private GUIStyle _suggestionDetailStyle;
        private bool _stylesInitialized;

        // ──────────── 折叠状态 ────────────
        private bool _showSummary = true;
        private bool _showDetail = true;
        private bool _showFilters = true;

        // ──────────── 统计缓存 ────────────
        private long _totalMemory;
        private long _totalDisk;
        private int _errorCount;
        private int _warningCount;
        private long _totalSaving;

        [MenuItem("UnityToolsHub/资源分析与优化", false, 200)]
        public static void ShowWindow()
        {
            var window = GetWindow<ResourceAnalyzerWindow>("资源分析与优化");
            window.minSize = new Vector2(900, 600);
        }

        private void OnEnable()
        {
            RegisterAnalyzers();
            wantsMouseMove = true;
            // 域重载后结果列表可能保留，但运行时索引字典会重新初始化。
            _filterDirty = true;
        }

        /// <summary>
        /// 注册所有分析器 —— 在此添加新的资源类型分析器
        /// </summary>
        private void RegisterAnalyzers()
        {
            _analyzers.Clear();
            _analyzers["Texture"] = new TextureAnalyzer();
            // TODO: 预留其它分析器
            // _analyzers["Mesh"]     = new MeshAnalyzer();
            // _analyzers["Audio"]    = new AudioClipAnalyzer();
            // _analyzers["AnimClip"] = new AnimationClipAnalyzer();
        }

        // ══════════════════════════════════════════════════════
        //  GUI
        // ══════════════════════════════════════════════════════

        private void OnGUI()
        {
            InitStyles();

            // ── 顶部工具栏 ──
            DrawToolbar();

            // ── 过滤栏 ──
            DrawFilterBar();

            EditorGUILayout.Space(2);

            // ── 主体区域（左右分栏） ──
            using (new EditorGUILayout.HorizontalScope())
            {
                // 左侧：结果列表
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    // 汇总面板
                    DrawSummaryPanel();
                    EditorGUILayout.Space(2);
                    // 结果表格
                    DrawResultsTable();
                }

                // 右侧：详情面板
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(320), GUILayout.ExpandHeight(true)))
                {
                    DrawDetailPanel();
                }
            }
        }

        // ──────────────── 工具栏 ────────────────
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // 平台选择
                GUILayout.Label("目标平台:", GUILayout.Width(60));
                _selectedPlatform = (TargetPlatform)EditorGUILayout.EnumPopup(_selectedPlatform, EditorStyles.toolbarPopup, GUILayout.Width(90));

                GUILayout.Space(10);

                // 扫描路径
                GUILayout.Label("扫描路径:", GUILayout.Width(60));
                _scanPath = EditorGUILayout.TextField(_scanPath, EditorStyles.toolbarSearchField, GUILayout.Width(200));

                if (GUILayout.Button("浏览...", EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    string selected = EditorUtility.OpenFolderPanel("选择扫描目录", _scanPath, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        // 转换为相对路径
                        string dataPath = Application.dataPath;
                        if (selected.StartsWith(dataPath))
                            _scanPath = "Assets" + selected.Substring(dataPath.Length);
                        else
                            _scanPath = selected;
                    }
                }

                GUILayout.Space(10);

                // 分析器选择（预留多分析器切换）
                if (_analyzers.Count > 1)
                {
                    GUILayout.Label("分析类型:", GUILayout.Width(60));
                    _activeAnalyzerKey = EditorGUILayout.Popup(
                        Array.IndexOf(_analyzers.Keys.ToArray(), _activeAnalyzerKey),
                        _analyzers.Keys.ToArray(),
                        EditorStyles.toolbarPopup,
                        GUILayout.Width(100)
                    ).ToString();
                }

                GUILayout.FlexibleSpace();

                // 扫描按钮
                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
                if (GUILayout.Button("开始分析", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    RunAnalysis();
                }
                GUI.backgroundColor = Color.white;

                // 导出报告
                if (_hasAnalyzed && GUILayout.Button("导出报告", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    ExportReport();
                }
            }

            _onlyBuildAssets = EditorGUILayout.ToggleLeft(
                "仅分析当前构建相关资源（启用场景、Resources、Addressables 及 AssetBundle 的依赖）",
                _onlyBuildAssets);
        }

        // ──────────────── 过滤栏 ────────────────
        private void DrawFilterBar()
        {
            _showFilters = EditorGUILayout.BeginFoldoutHeaderGroup(_showFilters, "过滤与搜索");
            if (_showFilters)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("筛选:", GUILayout.Width(35));
                    var newFilter = (FilterMode)EditorGUILayout.EnumPopup(_filterMode, GUILayout.Width(100));
                    if (newFilter != _filterMode) { _filterMode = newFilter; _filterDirty = true; }

                    GUILayout.Space(10);

                    GUILayout.Label("搜索:", GUILayout.Width(35));
                    _searchKeyword = EditorGUILayout.TextField(_searchKeyword, EditorStyles.toolbarSearchField, GUILayout.Width(180));

                    GUILayout.Space(10);

                    GUILayout.Label("排序:", GUILayout.Width(35));
                    var newSort = (SortColumn)EditorGUILayout.EnumPopup(_sortColumn, GUILayout.Width(100));
                    if (newSort != _sortColumn) { _sortColumn = newSort; _filterDirty = true; }

                    if (GUILayout.Button(_sortAscending ? "↑" : "↓", GUILayout.Width(22)))
                    {
                        _sortAscending = !_sortAscending;
                        _filterDirty = true;
                    }

                    GUILayout.FlexibleSpace();

                    // 批量操作
                    if (_hasAnalyzed)
                    {
                        RefreshFilteredCache();
                        int actionableCount = _cachedFiltered.Count(r => r.IsSelected && r.Suggestions.Any(s => s.CanAutoFix));
                        GUILayout.Label($"当前已选 {_cachedSelectedCount}", EditorStyles.miniLabel);
                        if (GUILayout.Button("清空选择", GUILayout.Width(70)))
                            ClearAssetSelection();

                        using (new EditorGUI.DisabledScope(actionableCount == 0))
                        {
                            GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
                            if (GUILayout.Button($"批量优化 ({actionableCount})", GUILayout.Width(110)))
                                BatchOptimize();
                            GUI.backgroundColor = Color.white;
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ──────────────── 汇总面板 ────────────────
        private void DrawSummaryPanel()
        {
            _showSummary = EditorGUILayout.BeginFoldoutHeaderGroup(_showSummary, "分析汇总");
            if (_showSummary && _hasAnalyzed)
            {
                RefreshFilteredCache();

                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawStatCard("资源总数", $"{_cachedFiltered.Count}", new Color(0.5f, 0.7f, 1f));
                        DrawStatCard("严重问题", $"{_errorCount}", new Color(1f, 0.4f, 0.4f));
                        DrawStatCard("警告", $"{_warningCount}", new Color(1f, 0.8f, 0.3f));
                        DrawStatCard("内存粗估", FormatBytes(_totalMemory), new Color(0.6f, 0.8f, 0.6f));
                        DrawStatCard("源文件", FormatBytes(_totalDisk), new Color(0.7f, 0.7f, 0.7f));
                        DrawStatCard("粗估可省", FormatBytes(_totalSaving), new Color(0.3f, 0.9f, 0.5f));
                    }
                    if (_lastAnalysisFilteredBuildAssets)
                        GUILayout.Label($"已排除 {_excludedAssetCount} 个未被当前构建入口引用的纹理（按当前配置估算）", EditorStyles.miniLabel);

                    int uncompressed = _cachedFiltered.Count(r => r.IsUncompressed);
                    int automatic = _cachedFiltered.Count(r => r.UsesAutomaticFormat && !r.IsUncompressed);
                    int explicitFormat = _cachedFiltered.Count - automatic - uncompressed;
                    int formatAdvice = _cachedFiltered.Count(r => r.Suggestions.Any(s => s.Action == OptimizationAction.Format));
                    GUILayout.Label($"格式状态（当前筛选）：自动压缩 {automatic} · 指定格式 {explicitFormat} · 未压缩 {uncompressed} · 格式建议 {formatAdvice}",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawStatCard(string label, string value, Color color)
        {
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            using (new EditorGUILayout.VerticalScope(GUI.skin.box, GUILayout.ExpandWidth(true)))
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                GUILayout.Label(value, EditorStyles.boldLabel);
            }
            GUI.backgroundColor = oldBg;
        }

        // ──────────────── 结果表格 ────────────────
        private void DrawResultsTable()
        {
            if (!_hasAnalyzed)
            {
                EditorGUILayout.HelpBox("点击「开始分析」扫描资源", MessageType.Info);
                return;
            }

            RefreshFilteredCache();
            var filtered = _cachedFiltered;
            int filteredCount = filtered.Count;

            // ── 表头 ──
            using (new EditorGUILayout.HorizontalScope(_headerRowStyle))
            {
                // 仅影响当前筛选结果，不会选中或取消隐藏资源。
                bool allSelected = filteredCount > 0 && _cachedSelectedCount == filteredCount;
                bool newAll = GUILayout.Toggle(allSelected,
                    new GUIContent("", "选择或取消当前筛选结果"), GUILayout.Width(18));
                if (newAll != allSelected)
                {
                    foreach (var r in filtered) r.IsSelected = newAll;
                    _cachedSelectedCount = newAll ? filteredCount : 0;
                    _selectionAnchorPath = null;
                    if (!newAll)
                        _selectedResultIndex = -1;
                    Repaint();
                }

                DrawTableHeader("资源名称", 180);
                DrawTableHeader("源图尺寸", 70);
                DrawTableHeader("源文件", 65);
                DrawTableHeader("内存粗估", 65);
                DrawTableHeader("格式状态", 90);
                DrawTableHeader("Mipmap", 50);
                DrawTableHeader("R/W", 32);
                DrawTableHeader("状态", 50);
                DrawTableHeader("问题", 35);
            }

            if (filteredCount == 0)
            {
                EditorGUILayout.HelpBox("没有匹配的资源", MessageType.Info);
                return;
            }

            // ── 表格内容 ──
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            bool trackHover = Event.current.type == EventType.MouseMove;
            string hoveredPath = null;

            for (int i = 0; i < filteredCount; i++)
            {
                var result = filtered[i];
                // 选中状态决定整行高亮，详情焦点不再冒充选中状态。
                var rowStyle = result.IsSelected ? _selectedRowStyle
                    : result.AssetPath == _hoveredAssetPath ? _hoverRowStyle
                    : i % 2 == 0 ? _evenRowStyle : _altRowStyle;
                Rect rowRect = EditorGUILayout.BeginHorizontal(rowStyle, GUILayout.Height(ROW_HEIGHT), GUILayout.ExpandWidth(true));
                {
                    if (trackHover && rowRect.Contains(Event.current.mousePosition))
                        hoveredPath = result.AssetPath;
                    // 复选框只增减当前项；Shift 点击可选择连续范围。
                    bool oldSel = result.IsSelected;
                    bool newSel = GUILayout.Toggle(oldSel, "", GUILayout.Width(18));
                    Rect checkboxRect = GUILayoutUtility.GetLastRect();
                    if (newSel != oldSel)
                        HandleCheckboxClick(filtered, i, newSel, Event.current.shift);

                    GUILayout.Label(result.AssetName, EditorStyles.label, GUILayout.Width(180));
                    GUILayout.Label($"{result.SourceWidth}x{result.SourceHeight}", GUILayout.Width(70));
                    GUILayout.Label(result.FormattedDisk, GUILayout.Width(65));
                    GUILayout.Label(result.FormattedMemory, GUILayout.Width(65));
                    GUILayout.Label(result.CurrentFormat, EditorStyles.miniLabel, GUILayout.Width(90));
                    GUILayout.Label(result.HasMipmap ? "✓" : "-", GUILayout.Width(50));
                    GUILayout.Label(result.IsReadWriteEnabled ? "✓" : "-", GUILayout.Width(32));

                    var sevColor = result.ErrorCount > 0 ? Color.red : result.WarningCount > 0 ? Color.yellow : Color.green;
                    DrawSeverityTag(result.SeverityLabel, sevColor);
                    GUILayout.Label(result.Suggestions.Count.ToString(), GUILayout.Width(35));
                    EditorGUILayout.EndHorizontal();

                    // 文件管理器式行选择：普通点击单选，Ctrl 增减，Shift 范围，Ctrl+Shift 追加范围。
                    var currentEvent = Event.current;
                    if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0
                        && rowRect.Contains(currentEvent.mousePosition)
                        && !checkboxRect.Contains(currentEvent.mousePosition))
                    {
                        HandleRowClick(filtered, i, currentEvent.shift,
                            currentEvent.control || currentEvent.command);
                        GUIUtility.keyboardControl = 0;
                        if (currentEvent.clickCount == 2)
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                            if (asset != null) EditorGUIUtility.PingObject(asset);
                        }
                        currentEvent.Use();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            if (trackHover && _hoveredAssetPath != hoveredPath)
            {
                _hoveredAssetPath = hoveredPath;
                Repaint();
            }

            // 列表焦点下支持资源管理器常用的全选与取消选择。
            var keyboardEvent = Event.current;
            if (keyboardEvent.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
            {
                if ((keyboardEvent.control || keyboardEvent.command) && keyboardEvent.keyCode == KeyCode.A)
                {
                    foreach (var result in filtered) result.IsSelected = true;
                    RefreshSelectedCount();
                    Repaint();
                    keyboardEvent.Use();
                }
                else if (keyboardEvent.keyCode == KeyCode.Escape)
                {
                    ClearAssetSelection();
                    keyboardEvent.Use();
                }
            }
        }

        /// <summary>
        /// 点击整行时使用文件管理器的选择规则。
        /// </summary>
        private void HandleRowClick(List<ResourceAnalysisResult> filtered, int filteredIndex, bool shiftHeld, bool additive)
        {
            int anchorIndex = string.IsNullOrEmpty(_selectionAnchorPath)
                ? -1
                : filtered.FindIndex(r => r.AssetPath == _selectionAnchorPath);
            if (shiftHeld && anchorIndex >= 0)
            {
                if (!additive) ClearAllAssetSelections();
                int from = Mathf.Min(anchorIndex, filteredIndex);
                int to = Mathf.Max(anchorIndex, filteredIndex);
                for (int j = from; j <= to; j++)
                    filtered[j].IsSelected = true;
            }
            else
            {
                if (additive)
                    filtered[filteredIndex].IsSelected = !filtered[filteredIndex].IsSelected;
                else
                {
                    ClearAllAssetSelections();
                    filtered[filteredIndex].IsSelected = true;
                }
                _selectionAnchorPath = filtered[filteredIndex].AssetPath;
            }

            _selectedResultIndex = _indexMap[filtered[filteredIndex]];
            RefreshSelectedCount();
            if (_cachedSelectedCount == 0)
                _selectedResultIndex = -1;
            Repaint();
        }

        private void HandleCheckboxClick(List<ResourceAnalysisResult> filtered, int filteredIndex, bool value, bool shiftHeld)
        {
            int anchorIndex = string.IsNullOrEmpty(_selectionAnchorPath)
                ? -1
                : filtered.FindIndex(r => r.AssetPath == _selectionAnchorPath);
            if (shiftHeld && anchorIndex >= 0)
            {
                int from = Mathf.Min(anchorIndex, filteredIndex);
                int to = Mathf.Max(anchorIndex, filteredIndex);
                for (int j = from; j <= to; j++)
                    filtered[j].IsSelected = value;
            }
            else
            {
                filtered[filteredIndex].IsSelected = value;
                _selectionAnchorPath = filtered[filteredIndex].AssetPath;
            }

            _selectedResultIndex = _indexMap[filtered[filteredIndex]];
            RefreshSelectedCount();
            if (_cachedSelectedCount == 0)
                _selectedResultIndex = -1;
            Repaint();
        }

        private void ClearAllAssetSelections()
        {
            foreach (var result in _results)
                result.IsSelected = false;
        }

        private void ClearAssetSelection()
        {
            ClearAllAssetSelections();
            _selectionAnchorPath = null;
            _selectedResultIndex = -1;
            RefreshSelectedCount();
            Repaint();
        }

        private void RefreshSelectedCount()
        {
            _cachedSelectedCount = _cachedFiltered.Count(r => r.IsSelected);
        }

        private void DrawTableHeader(string label, float width)
        {
            GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(width));
        }

        private void DrawSeverityTag(string label, Color color)
        {
            var oldColor = GUI.color;
            GUI.color = color;
            GUILayout.Label($"[{label}]", EditorStyles.miniLabel, GUILayout.Width(50));
            GUI.color = oldColor;
        }

        // ──────────────── 详情面板 ────────────────
        private void DrawDetailPanel()
        {
            _showDetail = EditorGUILayout.BeginFoldoutHeaderGroup(_showDetail, "资源详情");
            if (!_showDetail)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            var selectedResources = _cachedFiltered.Where(r => r.IsSelected).ToList();
            if (selectedResources.Count > 1)
            {
                _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos);
                DrawCombinedSuggestions(selectedResources);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            if (selectedResources.Count == 0 &&
                (_selectedResultIndex < 0 || _selectedResultIndex >= _results.Count))
            {
                EditorGUILayout.HelpBox("在左侧列表中点击资源查看详情", MessageType.Info);
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            var result = selectedResources.Count == 1
                ? selectedResources[0]
                : _results[_selectedResultIndex];

            _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos);

            // 预览
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(result.AssetPath);
            if (tex != null)
            {
                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                {
                    float previewSize = Mathf.Min(280, position.width - 340);
                    var rect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(rect, tex);
                }
            }

            EditorGUILayout.Space(4);

            // 基本信息
            EditorGUILayout.LabelField("基本信息", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                DrawDetailRow("路径", result.AssetPath);
                DrawDetailRow("源图尺寸", $"{result.SourceWidth} x {result.SourceHeight}");
                DrawDetailRow("导入尺寸", $"{result.Width} x {result.Height}");
                DrawDetailRow("MaxSize", $"{result.CurrentMaxSize} → {result.RecommendedMaxSize}");
                string recommendedFormat = result.RecommendedFormat == "AutomaticCompressed"
                    ? "自动压缩"
                    : result.RecommendedFormat;
                DrawDetailRow("格式", result.CurrentFormat == recommendedFormat
                    ? result.CurrentFormat
                    : $"{result.CurrentFormat} → {recommendedFormat}");
                DrawDetailRow("平台设置", result.HasPlatformOverride ? "独立覆盖" : "继承默认");
                DrawDetailRow("Mipmap", result.HasMipmap ? "开启" : "关闭");
                DrawDetailRow("Read/Write", result.IsReadWriteEnabled ? "开启（占用翻倍!）" : "关闭");
                DrawDetailRow("Streaming", result.IsStreamingMipmaps ? "开启" : "关闭");
                DrawDetailRow("Alpha", result.HasAlpha ? (result.HasTransparentAlpha ? "有透明" : "有通道无不透明") : "无");
                DrawDetailRow("类型", result.IsNormalMap ? "Normal Map" : result.IsUIAsset ? "UI 纹理" : "普通纹理");
                DrawDetailRow("Wrap", result.WrapMode);
                DrawDetailRow("Filter", result.FilterMode);
                DrawDetailRow("内存粗估", result.FormattedMemory);
                DrawDetailRow("源文件", result.FormattedDisk);
            }

            if (result.UsesAutomaticFormat && !result.IsUncompressed)
                EditorGUILayout.HelpBox("当前使用自动压缩。Unity 会按目标平台选择具体格式，无需仅为固定格式而创建覆盖设置。", MessageType.Info);
            else if (result.IsUncompressed && !result.Suggestions.Any(s => s.Action == OptimizationAction.Format))
                EditorGUILayout.HelpBox("当前未压缩，但这张纹理较小；工具不会为小图强制建议有损压缩。", MessageType.Info);

            EditorGUILayout.Space(6);

            // 优化建议
            EditorGUILayout.LabelField($"优化建议 ({result.Suggestions.Count})", EditorStyles.boldLabel);

            var autoFixSuggestions = result.Suggestions.Where(s => s.CanAutoFix).ToList();
            if (autoFixSuggestions.Count > 0)
            {
                int selectedCount = autoFixSuggestions.Count(s => s.IsSelected);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(selectedCount == 0))
                    {
                        if (GUILayout.Button($"应用所选 ({selectedCount})"))
                            ApplySuggestions(result, autoFixSuggestions.Where(s => s.IsSelected).ToList());
                    }

                    if (GUILayout.Button($"应用全部建议 ({autoFixSuggestions.Count})"))
                        ApplySuggestions(result, autoFixSuggestions);
                }
            }

            if (result.Suggestions.Count == 0)
            {
                EditorGUILayout.HelpBox("该资源设置合理，无需优化 ✓", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < result.Suggestions.Count; i++)
                {
                    DrawSuggestion(result.Suggestions[i], result);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDetailRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(72));
                EditorGUILayout.SelectableLabel(value, GUILayout.Height(16));
            }
        }

        private static List<CombinedSuggestion> CollectCombinedSuggestions(List<ResourceAnalysisResult> resources)
        {
            var byKey = new Dictionary<string, CombinedSuggestion>();
            foreach (var resource in resources)
            {
                foreach (var suggestion in resource.Suggestions)
                {
                    // 同类建议只展示一次；推荐参数不同（如 1024/2048）仍分别展示。
                    string key = $"{(int)suggestion.Action}|{(int)suggestion.Severity}|{suggestion.Recommendation}";
                    if (!byKey.TryGetValue(key, out var group))
                    {
                        group = new CombinedSuggestion { Key = key, Example = suggestion };
                        byKey.Add(key, group);
                    }
                    group.Occurrences.Add(new SuggestionOccurrence { Resource = resource, Suggestion = suggestion });
                }
            }

            return byKey.Values
                .OrderByDescending(g => g.Example.Severity)
                .ThenByDescending(g => g.Occurrences.Count)
                .ThenBy(g => g.Example.Recommendation, StringComparer.Ordinal)
                .ToList();
        }

        private void DrawCombinedSuggestions(List<ResourceAnalysisResult> resources)
        {
            var groups = CollectCombinedSuggestions(resources);
            var actionableGroups = groups.Where(g => g.Example.CanAutoFix).ToList();
            int selectedCount = actionableGroups.Count(g => _combinedSuggestionSelection.TryGetValue(g.Key, out bool selected) && selected);

            EditorGUILayout.LabelField($"当前筛选已选 {resources.Count} 个资源 · {groups.Count} 类建议", EditorStyles.boldLabel);
            if (groups.Count == 0)
            {
                EditorGUILayout.HelpBox("所选资源没有优化建议", MessageType.Info);
                return;
            }

            if (actionableGroups.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全选可应用"))
                        foreach (var group in actionableGroups) _combinedSuggestionSelection[group.Key] = true;
                    if (GUILayout.Button("清空建议选择"))
                        _combinedSuggestionSelection.Clear();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(selectedCount == 0))
                    {
                        if (GUILayout.Button($"应用所选 ({selectedCount})"))
                            ApplyCombinedSuggestions(actionableGroups.Where(g =>
                                _combinedSuggestionSelection.TryGetValue(g.Key, out bool selected) && selected).ToList());
                    }
                    if (GUILayout.Button($"应用全部建议 ({actionableGroups.Count})"))
                        ApplyCombinedSuggestions(actionableGroups);
                }
            }

            foreach (var group in groups)
            {
                var suggestion = group.Example;
                int resourceCount = group.Occurrences.Select(o => o.Resource).Distinct().Count();
                bool isSelected = suggestion.CanAutoFix
                    && _combinedSuggestionSelection.TryGetValue(group.Key, out bool selected) && selected;
                using (new EditorGUILayout.VerticalScope(
                           isSelected ? _selectedSuggestionCardStyle : _suggestionCardStyle))
                {
                    string title = $"{GetSeverityLabel(suggestion.Severity)} · 涉及 {resourceCount} 个资源";
                    if (suggestion.CanAutoFix)
                        _combinedSuggestionSelection[group.Key] = GUILayout.Toggle(isSelected, title, _suggestionToggleStyle);
                    else
                        GUILayout.Label($"{title} · 需手动处理", _suggestionTitleStyle);

                    GUILayout.Label(suggestion.Recommendation, _suggestionBodyStyle);

                    bool expanded = _expandedCombinedGroups.TryGetValue(group.Key, out bool isExpanded) && isExpanded;
                    expanded = EditorGUILayout.Foldout(expanded, $"查看涉及的资源 ({resourceCount})", true);
                    _expandedCombinedGroups[group.Key] = expanded;
                    if (expanded)
                    {
                        foreach (var occurrence in group.Occurrences
                                     .GroupBy(o => o.Resource.AssetPath)
                                     .Select(g => g.First()))
                        {
                            GUILayout.Label($"• {occurrence.Resource.AssetName}", EditorStyles.miniLabel);
                            GUILayout.Label($"  {occurrence.Suggestion.Description}", _suggestionDetailStyle);
                        }
                    }
                }
                EditorGUILayout.Space(3);
            }
        }

        private void ApplyCombinedSuggestions(List<CombinedSuggestion> groups)
        {
            var byResource = new Dictionary<ResourceAnalysisResult, List<OptimizationSuggestion>>();
            foreach (var group in groups)
            {
                if (!group.Example.CanAutoFix) continue;
                foreach (var occurrence in group.Occurrences)
                {
                    if (!byResource.TryGetValue(occurrence.Resource, out var suggestions))
                    {
                        suggestions = new List<OptimizationSuggestion>();
                        byResource.Add(occurrence.Resource, suggestions);
                    }
                    suggestions.Add(occurrence.Suggestion);
                }
            }
            if (byResource.Count == 0) return;

            string formatNote = groups.Any(g => g.Example.Action == OptimizationAction.Format)
                ? "\n压缩格式更改可能影响纹理画质，应用后请检查 UI 和模型显示。"
                : string.Empty;
            if (!EditorUtility.DisplayDialog("批量应用建议",
                    $"对当前筛选的 {byResource.Count} 个资源应用 {groups.Count} 类建议？\n平台: {PlatformOptimizationProfile.GetPlatformName(_selectedPlatform)}\n\n此操作会修改导入设置并重新导入资源。{formatNote}",
                    "确认", "取消"))
                return;

            var analyzer = GetActiveAnalyzer();
            if (analyzer == null) return;
            var selectedPaths = new HashSet<string>(_results.Where(r => r.IsSelected).Select(r => r.AssetPath));
            int successCount = 0;
            try
            {
                int index = 0;
                foreach (var pair in byResource)
                {
                    EditorUtility.DisplayProgressBar("批量应用建议", pair.Key.AssetName,
                        (float)++index / byResource.Count);
                    if (analyzer.ApplyOptimization(pair.Key, _selectedPlatform, pair.Value))
                        successCount++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            RunAnalysis();
            foreach (var result in _results)
                result.IsSelected = selectedPaths.Contains(result.AssetPath);
            _filterDirty = true;
            RefreshFilteredCache();
            _combinedSuggestionSelection.Clear();
            Debug.Log($"[ResourceAnalyzer] 批量应用建议完成: {successCount}/{byResource.Count} 个资源已优化");
        }

        private void DrawSuggestion(OptimizationSuggestion suggestion, ResourceAnalysisResult result)
        {
            using (new EditorGUILayout.VerticalScope(
                       suggestion.IsSelected && suggestion.CanAutoFix
                           ? _selectedSuggestionCardStyle : _suggestionCardStyle))
            {
                string title = $"{GetSeverityLabel(suggestion.Severity)} · {suggestion.Description}";
                if (suggestion.CanAutoFix)
                    suggestion.IsSelected = GUILayout.Toggle(suggestion.IsSelected, title, _suggestionToggleStyle);
                else
                    GUILayout.Label($"{title} · 需手动处理", _suggestionTitleStyle);

                GUILayout.Label($"建议：{suggestion.Recommendation}", _suggestionBodyStyle);
                if (suggestion.EstimatedSavingBytes > 0)
                    GUILayout.Label($"未压缩内存粗估节省：{suggestion.FormattedSaving}", _suggestionDetailStyle);

                if (suggestion.CanAutoFix)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        string buttonText = suggestion.Action == OptimizationAction.Format && result.IsUncompressed
                            ? "试用自动压缩"
                            : "应用此优化";
                        if (GUILayout.Button(buttonText, GUILayout.Width(100)))
                            ApplySuggestions(result, new[] { suggestion });
                    }
                }
            }
            EditorGUILayout.Space(3);
        }

        private static string GetSeverityLabel(IssueSeverity severity)
        {
            return severity switch
            {
                IssueSeverity.Error => "❌ 严重",
                IssueSeverity.Warning => "⚠️ 警告",
                _ => "ℹ️ 提示"
            };
        }

        private void ApplySuggestions(ResourceAnalysisResult result, IReadOnlyCollection<OptimizationSuggestion> suggestions)
        {
            var analyzer = GetActiveAnalyzer();
            if (analyzer == null || !analyzer.ApplyOptimization(result, _selectedPlatform, suggestions)) return;

            var newResults = analyzer.Analyze(new List<string> { result.AssetPath }, _selectedPlatform);
            if (newResults.Count == 0) return;

            int index = _results.IndexOf(result);
            if (index < 0) return;
            newResults[0].IsSelected = result.IsSelected;
            var selectedActions = new HashSet<OptimizationAction>(result.Suggestions
                .Where(s => s.IsSelected).Select(s => s.Action));
            foreach (var suggestion in newResults[0].Suggestions)
                suggestion.IsSelected = selectedActions.Contains(suggestion.Action);
            _results[index] = newResults[0];
            _selectedResultIndex = index;
            RefreshStats();
        }

        // ══════════════════════════════════════════════════════
        //  分析逻辑
        // ══════════════════════════════════════════════════════

        private void RunAnalysis()
        {
            var analyzer = GetActiveAnalyzer();
            if (analyzer == null)
            {
                EditorUtility.DisplayDialog("错误", "没有可用的分析器", "确定");
                return;
            }

            // 搜索资源
            var searchFilters = analyzer.AssetSearchFilters;
            var allPaths = new List<string>();

            foreach (string filter in searchFilters)
            {
                string[] guids = AssetDatabase.FindAssets(filter, new[] { _scanPath });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!allPaths.Contains(path))
                        allPaths.Add(path);
                }
            }

            if (allPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", $"在 {_scanPath} 下未找到匹配的资源", "确定");
                return;
            }

            _excludedAssetCount = 0;
            _lastAnalysisFilteredBuildAssets = _onlyBuildAssets;
            if (_onlyBuildAssets)
            {
                if (!BuildAssetInclusion.TryCollect(out var includedPaths, out var error))
                {
                    EditorUtility.DisplayDialog("构建资源筛选失败", error, "确定");
                    return;
                }

                _excludedAssetCount = allPaths.RemoveAll(path => !includedPaths.Contains(path));
            }

            // 执行分析
            _results = analyzer.Analyze(allPaths, _selectedPlatform);
            _hasAnalyzed = true;
            _selectedResultIndex = -1;
            _selectionAnchorPath = null;
            _combinedSuggestionSelection.Clear();
            _expandedCombinedGroups.Clear();
            RefreshStats();

            Debug.Log($"[ResourceAnalyzer] 分析完成: {_results.Count} 个资源, 排除未命中构建入口: {_excludedAssetCount}, 平台: {PlatformOptimizationProfile.GetPlatformName(_selectedPlatform)}");
        }

        /// <summary>批量优化选中的资源</summary>
        private void BatchOptimize()
        {
            var selectedResources = _cachedFiltered.Where(r => r.IsSelected).ToList();
            var groups = CollectCombinedSuggestions(selectedResources)
                .Where(g => g.Example.CanAutoFix).ToList();
            ApplyCombinedSuggestions(groups);
        }

        /// <summary>导出分析报告</summary>
        private void ExportReport()
        {
            string path = EditorUtility.SaveFilePanel("导出分析报告", "", $"ResourceReport_{_selectedPlatform}_{DateTime.Now:yyyyMMdd_HHmm}", "csv");
            if (string.IsNullOrEmpty(path)) return;

            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("资源路径,资源名称,类型,导入宽度,导入高度,源图宽度,源图高度,源文件字节,未压缩内存粗估,格式状态,推荐格式,MaxSize,推荐MaxSize,Mipmap,Read/Write,NPOT,Alpha,UI资源,严重问题数,警告数,粗估可节省未压缩内存字节");
                foreach (var r in _results)
                {
                    writer.WriteLine($"\"{r.AssetPath}\",{r.AssetName},{r.ResourceType},{r.Width},{r.Height},{r.SourceWidth},{r.SourceHeight},{r.CurrentDiskBytes},{r.CurrentMemoryBytes},{r.CurrentFormat},{r.RecommendedFormat},{r.CurrentMaxSize},{r.RecommendedMaxSize},{r.HasMipmap},{r.IsReadWriteEnabled},{r.IsNPOT},{r.HasAlpha},{r.IsUIAsset},{r.ErrorCount},{r.WarningCount},{r.TotalEstimatedSaving}");
                }
            }

            Debug.Log($"[ResourceAnalyzer] 报告已导出: {path}");
            EditorUtility.DisplayDialog("导出成功", $"报告已保存到:\n{path}", "确定");
        }

        // ══════════════════════════════════════════════════════
        //  辅助
        // ══════════════════════════════════════════════════════

        private IResourceAnalyzer GetActiveAnalyzer()
        {
            _analyzers.TryGetValue(_activeAnalyzerKey, out var analyzer);
            return analyzer;
        }

        /// <summary>
        /// 按需刷新过滤缓存 —— 只在筛选/排序/搜索条件变化或数据变化时重算
        /// </summary>
        private void RefreshFilteredCache()
        {
            // 检测条件是否变化
            if (!_filterDirty
                && _lastSearchKeyword == _searchKeyword
                && _lastFilterMode == _filterMode
                && _lastSortColumn == _sortColumn
                && _lastSortAscending == _sortAscending)
            {
                return;
            }

            _lastSearchKeyword = _searchKeyword;
            _lastFilterMode = _filterMode;
            _lastSortColumn = _sortColumn;
            _lastSortAscending = _sortAscending;
            _filterDirty = false;

            // 过滤
            _cachedFiltered.Clear();
            bool hasSearch = !string.IsNullOrEmpty(_searchKeyword);

            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];

                if (hasSearch)
                {
                    if (!r.AssetName.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase)
                        && !r.AssetPath.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                bool pass = _filterMode switch
                {
                    FilterMode.HasIssue => r.Suggestions.Count > 0,
                    FilterMode.UI => r.IsUIAsset,
                    FilterMode.NonUI => !r.IsUIAsset,
                    FilterMode.NPOT => r.IsNPOT,
                    FilterMode.LargeSize => Math.Min(Math.Max(r.SourceWidth, r.SourceHeight), r.CurrentMaxSize) > 1024,
                    FilterMode.ReadWrite => r.IsReadWriteEnabled,
                    _ => true
                };

                if (pass) _cachedFiltered.Add(r);
            }

            // 排序
            _cachedFiltered.Sort((a, b) =>
            {
                int cmp = _sortColumn switch
                {
                    SortColumn.Name => string.Compare(a.AssetName, b.AssetName, StringComparison.OrdinalIgnoreCase),
                    SortColumn.Size => a.CurrentMemoryBytes.CompareTo(b.CurrentMemoryBytes),
                    SortColumn.Disk => a.CurrentDiskBytes.CompareTo(b.CurrentDiskBytes),
                    SortColumn.Width => (a.Width * a.Height).CompareTo(b.Width * b.Height),
                    SortColumn.Format => string.Compare(a.CurrentFormat, b.CurrentFormat, StringComparison.OrdinalIgnoreCase),
                    SortColumn.Severity => GetSeverityWeight(a).CompareTo(GetSeverityWeight(b)),
                    _ => 0
                };
                return _sortAscending ? cmp : -cmp;
            });

            // 重建索引映射
            _indexMap.Clear();
            for (int i = 0; i < _results.Count; i++)
                _indexMap[_results[i]] = i;

            // 重算选中计数
            _cachedSelectedCount = 0;
            foreach (var r in _cachedFiltered)
                if (r.IsSelected) _cachedSelectedCount++;
        }

        private int GetSeverityWeight(ResourceAnalysisResult r)
        {
            return r.ErrorCount * 100 + r.WarningCount * 10 + r.Suggestions.Count;
        }

        /// <summary>刷新统计数据</summary>
        private void RefreshStats()
        {
            _totalMemory = 0;
            _totalDisk = 0;
            _errorCount = 0;
            _warningCount = 0;
            _totalSaving = 0;

            foreach (var r in _results)
            {
                _totalMemory += r.CurrentMemoryBytes;
                _totalDisk += r.CurrentDiskBytes;
                _errorCount += r.ErrorCount;
                _warningCount += r.WarningCount;
                _totalSaving += r.TotalEstimatedSaving;
            }

            // 强制刷新过滤缓存
            _filterDirty = true;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024f * 1024f):F2} MB";
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        private GUIStyle _altRowStyle;
        private GUIStyle _evenRowStyle;
        private GUIStyle _hoverRowStyle;
        private GUIStyle _selectedRowStyle;
        private GUIStyle _headerRowStyle;

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter
            };

            _tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                richText = true,
                alignment = TextAnchor.MiddleCenter
            };

            _suggestionCardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(2, 2, 3, 3)
            };
            _selectedSuggestionCardStyle = new GUIStyle(_suggestionCardStyle);
            var selectedSuggestionBackground = Palette.MakeTex(1, 1,
                EditorGUIUtility.isProSkin
                    ? new Color(0.18f, 0.33f, 0.50f, 0.92f)
                    : new Color(0.78f, 0.88f, 0.98f, 0.95f));
            _selectedSuggestionCardStyle.normal.background = selectedSuggestionBackground;
            _selectedSuggestionCardStyle.hover.background = selectedSuggestionBackground;
            _suggestionToggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _suggestionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
            _suggestionBodyStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            _suggestionDetailStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

            // 交替行背景
            _altRowStyle = new GUIStyle(GUI.skin.box);
            _altRowStyle.normal.background = Palette.MakeTex(1, 1, new Color(0, 0, 0, 0.04f));

            // 偶数行：与 box 相同 padding 但透明背景，保证列对齐
            _evenRowStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = null }
            };

            _hoverRowStyle = new GUIStyle(GUI.skin.box);
            var hoverBackground = Palette.MakeTex(1, 1,
                EditorGUIUtility.isProSkin
                    ? new Color(0.30f, 0.48f, 0.68f, 0.38f)
                    : new Color(0.32f, 0.56f, 0.83f, 0.20f));
            _hoverRowStyle.normal.background = hoverBackground;
            _hoverRowStyle.hover.background = hoverBackground;
            _hoverRowStyle.active.background = hoverBackground;

            // 与资源复选框共用 IsSelected 状态，整行持续高亮（失焦后也能辨认）。
            _selectedRowStyle = new GUIStyle(GUI.skin.box);
            var selectionColor = EditorGUIUtility.isProSkin
                ? new Color(0.23f, 0.45f, 0.73f, 0.72f)
                : new Color(0.27f, 0.55f, 0.88f, 0.42f);
            var selectionBackground = Palette.MakeTex(1, 1, selectionColor);
            _selectedRowStyle.normal.background = selectionBackground;
            _selectedRowStyle.hover.background = selectionBackground;
            _selectedRowStyle.active.background = selectionBackground;
            _selectedRowStyle.focused.background = selectionBackground;

            // 表头行样式：统一内边距与数据行一致
            _headerRowStyle = new GUIStyle(EditorStyles.toolbar)
            {
                padding = GUI.skin.box.padding
            };
        }
    }
}
#endif
