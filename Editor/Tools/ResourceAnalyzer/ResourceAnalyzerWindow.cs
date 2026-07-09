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
        private int _lastClickedFilteredIndex = -1;

        // ──────────── 分析器注册（预留扩展） ────────────
        private readonly Dictionary<string, IResourceAnalyzer> _analyzers = new();
        private string _activeAnalyzerKey = "Texture";

        // ──────────── UI 缓存 ────────────
        private GUIStyle _headerStyle;
        private GUIStyle _tagStyle;
        private GUIStyle _issueBoxStyle;
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
                        GUI.enabled = _cachedSelectedCount > 0;
                        GUI.backgroundColor = _cachedSelectedCount > 0 ? new Color(1f, 0.7f, 0.3f) : Color.white;
                        if (GUILayout.Button($"批量优化 ({_cachedSelectedCount})", GUILayout.Width(110)))
                        {
                            BatchOptimize();
                        }
                        GUI.backgroundColor = Color.white;
                        GUI.enabled = true;
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
                        DrawStatCard("总内存", FormatBytes(_totalMemory), new Color(0.6f, 0.8f, 0.6f));
                        DrawStatCard("总磁盘", FormatBytes(_totalDisk), new Color(0.7f, 0.7f, 0.7f));
                        DrawStatCard("可节省", FormatBytes(_totalSaving), new Color(0.3f, 0.9f, 0.5f));
                    }
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
                // 全选 Toggle
                bool allSelected = filteredCount > 0 && _cachedSelectedCount == filteredCount;
                bool newAll = GUILayout.Toggle(allSelected, "", GUILayout.Width(18));
                if (newAll != allSelected)
                {
                    foreach (var r in filtered) r.IsSelected = newAll;
                    _cachedSelectedCount = newAll ? filteredCount : 0;
                }

                DrawTableHeader("资源名称", 180);
                DrawTableHeader("尺寸", 70);
                DrawTableHeader("磁盘", 65);
                DrawTableHeader("内存", 65);
                DrawTableHeader("格式", 90);
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
            bool shiftHeld = Event.current.shift;

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (int i = 0; i < filteredCount; i++)
            {
                var result = filtered[i];
                int globalIndex = _indexMap[result];
                bool isFocused = globalIndex == _selectedResultIndex;

                // 行样式
                var rowStyle = isFocused ? GUI.skin.box : (i % 2 == 0 ? _evenRowStyle : _altRowStyle);

                using (new EditorGUILayout.HorizontalScope(rowStyle, GUILayout.Height(ROW_HEIGHT)))
                {
                    // 选中框 + Shift 多选
                    bool oldSel = result.IsSelected;
                    bool newSel = GUILayout.Toggle(oldSel, "", GUILayout.Width(18));
                    if (newSel != oldSel)
                    {
                        HandleSelectionToggle(filtered, i, newSel, shiftHeld);
                    }

                    // 名称（可点击选中）
                    if (GUILayout.Button(result.AssetName, EditorStyles.label, GUILayout.Width(180)))
                    {
                        _selectedResultIndex = globalIndex;
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                        if (obj != null) EditorGUIUtility.PingObject(obj);
                    }

                    GUILayout.Label($"{result.Width}x{result.Height}", GUILayout.Width(70));
                    GUILayout.Label(result.FormattedDisk, GUILayout.Width(65));
                    GUILayout.Label(result.FormattedMemory, GUILayout.Width(65));
                    GUILayout.Label(result.CurrentFormat, EditorStyles.miniLabel, GUILayout.Width(90));
                    GUILayout.Label(result.HasMipmap ? "✓" : "-", GUILayout.Width(50));
                    GUILayout.Label(result.IsReadWriteEnabled ? "✓" : "-", GUILayout.Width(32));

                    var sevColor = result.ErrorCount > 0 ? Color.red : result.WarningCount > 0 ? Color.yellow : Color.green;
                    DrawSeverityTag(result.SeverityLabel, sevColor);
                    GUILayout.Label(result.Suggestions.Count.ToString(), GUILayout.Width(35));
                }

                // 行点击 → 选中详情
                if (Event.current.type == UnityEngine.EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    _selectedResultIndex = globalIndex;
                    _lastClickedFilteredIndex = i;
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 处理 Toggle 点击，支持 Shift 范围选择
        /// </summary>
        private void HandleSelectionToggle(List<ResourceAnalysisResult> filtered, int filteredIndex, bool value, bool shiftHeld)
        {
            if (shiftHeld && _lastClickedFilteredIndex >= 0 && _lastClickedFilteredIndex != filteredIndex)
            {
                // Shift 范围选择：上次点击到本次点击之间的所有项
                int from = Mathf.Min(_lastClickedFilteredIndex, filteredIndex);
                int to = Mathf.Max(_lastClickedFilteredIndex, filteredIndex);
                for (int j = from; j <= to; j++)
                {
                    filtered[j].IsSelected = value;
                }
            }
            else
            {
                filtered[filteredIndex].IsSelected = value;
                _lastClickedFilteredIndex = filteredIndex;
            }

            // 刷新选中计数
            _cachedSelectedCount = 0;
            foreach (var r in _cachedFiltered)
                if (r.IsSelected) _cachedSelectedCount++;
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

            if (_selectedResultIndex < 0 || _selectedResultIndex >= _results.Count)
            {
                EditorGUILayout.HelpBox("在左侧列表中点击资源查看详情", MessageType.Info);
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            var result = _results[_selectedResultIndex];

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
                DrawDetailRow("尺寸", $"{result.Width} x {result.Height}");
                DrawDetailRow("MaxSize", $"{result.CurrentMaxSize} → {result.RecommendedMaxSize}");
                DrawDetailRow("格式", $"{result.CurrentFormat} → {result.RecommendedFormat}");
                DrawDetailRow("Mipmap", result.HasMipmap ? "开启" : "关闭");
                DrawDetailRow("Read/Write", result.IsReadWriteEnabled ? "开启（占用翻倍!）" : "关闭");
                DrawDetailRow("Streaming", result.IsStreamingMipmaps ? "开启" : "关闭");
                DrawDetailRow("Alpha", result.HasAlpha ? (result.HasTransparentAlpha ? "有透明" : "有通道无不透明") : "无");
                DrawDetailRow("类型", result.IsNormalMap ? "Normal Map" : result.IsUIAsset ? "UI 纹理" : "普通纹理");
                DrawDetailRow("Wrap", result.WrapMode);
                DrawDetailRow("Filter", result.FilterMode);
                DrawDetailRow("内存", result.FormattedMemory);
                DrawDetailRow("磁盘", result.FormattedDisk);
            }

            EditorGUILayout.Space(6);

            // 优化建议
            EditorGUILayout.LabelField($"优化建议 ({result.Suggestions.Count})", EditorStyles.boldLabel);

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

        private void DrawSuggestion(OptimizationSuggestion suggestion, ResourceAnalysisResult result)
        {
            MessageType msgType = suggestion.Severity switch
            {
                IssueSeverity.Error => MessageType.Error,
                IssueSeverity.Warning => MessageType.Warning,
                _ => MessageType.Info
            };

            string header = suggestion.Severity switch
            {
                IssueSeverity.Error => "❌ 严重",
                IssueSeverity.Warning => "⚠️ 警告",
                _ => "ℹ️ 提示"
            };

            string text = $"{suggestion.Description}\n建议: {suggestion.Recommendation}";
            if (suggestion.EstimatedSavingBytes > 0)
                text += $"\n预估节省: {suggestion.FormattedSaving}";

            EditorGUILayout.HelpBox($"{header}\n{text}", msgType);

            if (suggestion.CanAutoFix)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    GUI.backgroundColor = new Color(0.4f, 0.8f, 0.9f);
                    if (GUILayout.Button("应用此优化", GUILayout.Width(90)))
                    {
                        var analyzer = GetActiveAnalyzer();
                        if (analyzer != null && analyzer.ApplyOptimization(result, _selectedPlatform))
                        {
                            // 重新分析该资源
                            var newResults = analyzer.Analyze(new List<string> { result.AssetPath }, _selectedPlatform);
                            if (newResults.Count > 0)
                            {
                                int idx = _results.IndexOf(result);
                                _results[idx] = newResults[0];
                                _selectedResultIndex = idx;
                                RefreshStats();
                            }
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            EditorGUILayout.Space(2);
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

            // 执行分析
            _results = analyzer.Analyze(allPaths, _selectedPlatform);
            _hasAnalyzed = true;
            _selectedResultIndex = _results.Count > 0 ? 0 : -1;
            RefreshStats();

            Debug.Log($"[ResourceAnalyzer] 分析完成: {_results.Count} 个资源, 平台: {PlatformOptimizationProfile.GetPlatformName(_selectedPlatform)}");
        }

        /// <summary>批量优化选中的资源</summary>
        private void BatchOptimize()
        {
            var analyzer = GetActiveAnalyzer();
            if (analyzer == null) return;

            var toOptimize = _results.Where(r => r.IsSelected).ToList();
            if (toOptimize.Count == 0) return;

            if (!EditorUtility.DisplayDialog("批量优化",
                $"确认对 {toOptimize.Count} 个资源执行优化？\n平台: {PlatformOptimizationProfile.GetPlatformName(_selectedPlatform)}\n\n此操作会修改资源导入设置并重新导入。",
                "确认", "取消"))
                return;

            int successCount = 0;
            for (int i = 0; i < toOptimize.Count; i++)
            {
                var result = toOptimize[i];
                EditorUtility.DisplayProgressBar("批量优化", $"优化中: {result.AssetName} ({i + 1}/{toOptimize.Count})", (float)(i + 1) / toOptimize.Count);

                if (analyzer.ApplyOptimization(result, _selectedPlatform))
                    successCount++;
            }

            EditorUtility.ClearProgressBar();

            // 重新分析
            RunAnalysis();
            Debug.Log($"[ResourceAnalyzer] 批量优化完成: {successCount}/{toOptimize.Count} 个资源已优化");
        }

        /// <summary>导出分析报告</summary>
        private void ExportReport()
        {
            string path = EditorUtility.SaveFilePanel("导出分析报告", "", $"ResourceReport_{_selectedPlatform}_{DateTime.Now:yyyyMMdd_HHmm}", "csv");
            if (string.IsNullOrEmpty(path)) return;

            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("资源路径,资源名称,类型,宽度,高度,磁盘大小,内存估算,当前格式,推荐格式,MaxSize,推荐MaxSize,Mipmap,Read/Write,NPOT,Alpha,UI资源,严重问题数,警告数,可节省字节");
                foreach (var r in _results)
                {
                    writer.WriteLine($"\"{r.AssetPath}\",{r.AssetName},{r.ResourceType},{r.Width},{r.Height},{r.CurrentDiskBytes},{r.CurrentMemoryBytes},{r.CurrentFormat},{r.RecommendedFormat},{r.CurrentMaxSize},{r.RecommendedMaxSize},{r.HasMipmap},{r.IsReadWriteEnabled},{r.IsNPOT},{r.HasAlpha},{r.IsUIAsset},{r.ErrorCount},{r.WarningCount},{r.TotalEstimatedSaving}");
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
                    FilterMode.LargeSize => r.CurrentMaxSize > 1024,
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

            _issueBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4)
            };

            // 交替行背景
            _altRowStyle = new GUIStyle(GUI.skin.box);
            _altRowStyle.normal.background = HubPalette.MakeTex(1, 1, new Color(0, 0, 0, 0.04f));

            // 偶数行：与 box 相同 padding 但透明背景，保证列对齐
            _evenRowStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = null }
            };

            // 表头行样式：统一内边距与数据行一致
            _headerRowStyle = new GUIStyle(EditorStyles.toolbar)
            {
                padding = GUI.skin.box.padding
            };
        }
    }
}
#endif
