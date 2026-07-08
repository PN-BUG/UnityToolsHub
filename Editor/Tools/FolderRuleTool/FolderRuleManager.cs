#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 文件夹规则管理器 —— 统一管理所有 FolderRuleConfig 配置
///
/// 功能：
///   1. 自动发现项目中所有 FolderRuleConfig SO
///   2. 显示每个配置的规则概览
///   3. 扫描违规资源并列出
///   4. 一键修复违规项
///   5. 应用配置（刷新，新文件导入立即生效）
/// </summary>
[ToolInfo("文件夹规则管理", "资产工具",
    Description = "统一管理文件夹规则配置（SO）。\n" +
                  "支持文件命名规范检查、Addressable 自动添加、贴图导入规则。\n" +
                  "用户手动创建 SO 配置，面板自动发现并批量管理。",
    Icon = "📂", Tags = new[] { "文件夹", "规则", "命名", "Addressable", "贴图" })]
public class FolderRuleManager : EditorWindow
{
    // ── 数据结构 ──────────────────────────────────────────────
    [Serializable]
    private class ViolationEntry
    {
        public string assetPath;
        public string configName;
        public string ruleType;   // "命名", "Addressable", "贴图"
        public string message;
        public FolderRuleConfig config;
    }

    // ── 状态 ──────────────────────────────────────────────────
    private List<FolderRuleConfig> _configs = new List<FolderRuleConfig>();
    private List<ViolationEntry> _violations = new List<ViolationEntry>();
    private Vector2 _configScroll;
    private Vector2 _violationScroll;
    private int _selectedConfigIndex = -1;
    private bool _showViolations = true;
    private Vector2 _rightScroll;
    private bool _showConfigDetail = true;
    private bool _scanned;
    private string _statusMessage = "";
    private double _statusTime;

    // ── 过滤状态 ──────────────────────────────────────────────
    private string _filterRuleType = "全部";
    private static readonly string[] RuleTypeFilters = { "全部", "命名", "Addressable", "贴图" };

    // ── 菜单入口 ──────────────────────────────────────────────
    [MenuItem("UnityToolsHub/文件夹规则管理")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<FolderRuleManager>("文件夹规则管理");
        wnd.minSize = new Vector2(680, 420);
    }

    private void OnEnable()
    {
        RefreshConfigs();
    }

    private void OnProjectChange()
    {
        RefreshConfigs();
    }

    // ── 刷新配置列表 ──────────────────────────────────────────
    private void RefreshConfigs()
    {
        _configs.Clear();
        string[] guids = AssetDatabase.FindAssets("t:FolderRuleConfig");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<FolderRuleConfig>(path);
            if (config != null) _configs.Add(config);
        }
        // 按文件夹路径排序
        _configs.Sort((a, b) => string.Compare(a.NormalizedFolderPath, b.NormalizedFolderPath, StringComparison.Ordinal));
    }

    // ══════════════════════════════════════════════════════════
    //  GUI
    // ══════════════════════════════════════════════════════════
    private void OnGUI()
    {
        DrawToolbar();
        DrawStatusBar();

        EditorGUILayout.BeginHorizontal();
        {
            // 左侧：配置列表
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.4f));
            DrawConfigList();
            EditorGUILayout.EndVertical();

            // 右侧：详情 + 违规列表（整体可滚动）
            EditorGUILayout.BeginVertical();
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawConfigDetail();
            DrawViolationList();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();
    }

    // ── 工具栏 ────────────────────────────────────────────────
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            if (GUILayout.Button("刷新配置", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                RefreshConfigs();
                SetStatus("配置列表已刷新");
            }

            if (GUILayout.Button("扫描违规", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                ScanViolations();
            }

            if (GUILayout.Button("应用规则", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                ApplyAllRules();
            }

            if (GUILayout.Button("全部修复", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                FixAllViolations();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("创建新配置", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                CreateNewConfig();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    // ── 状态栏 ────────────────────────────────────────────────
    private void DrawStatusBar()
    {
        if (string.IsNullOrEmpty(_statusMessage)) return;
        if (EditorApplication.timeSinceStartup - _statusTime > 5) return;

        MessageType msgType = _statusMessage.Contains("错误") || _statusMessage.Contains("失败")
            ? MessageType.Warning : MessageType.Info;
        EditorGUILayout.HelpBox(_statusMessage, msgType);
    }

    // ── 左侧：配置列表 ───────────────────────────────────────
    private void DrawConfigList()
    {
        EditorGUILayout.Space(4);
        GUILayout.Label($"规则配置（{_configs.Count} 个）", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        _configScroll = EditorGUILayout.BeginScrollView(_configScroll);
        {
            if (_configs.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到 FolderRuleConfig 配置。\n点击「创建新配置」或右键 Create → UnityToolsHub → 文件夹规则配置", MessageType.Info);
            }

            for (int i = 0; i < _configs.Count; i++)
            {
                var config = _configs[i];
                if (config == null) continue;

                bool selected = i == _selectedConfigIndex;
                var bgColor = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 1f);

                EditorGUILayout.BeginVertical("box");
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        // 启用开关
                        bool newEnabled = EditorGUILayout.Toggle(config.enabled, GUILayout.Width(20));
                        if (newEnabled != config.enabled)
                        {
                            config.enabled = newEnabled;
                            EditorUtility.SetDirty(config);
                        }

                        // 名称 + 路径
                        if (GUILayout.Button($"{config.name}\n<size=10>{config.NormalizedFolderPath}</size>",
                            new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleLeft }))
                        {
                            _selectedConfigIndex = i;
                            _showConfigDetail = true;
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // 规则标签
                    EditorGUILayout.BeginHorizontal();
                    {
                        var smallStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
                        if (config.enableNamingRule)
                            GUILayout.Label("📝命名", smallStyle);
                        if (config.enableAddressable)
                            GUILayout.Label("📦Addressable", smallStyle);
                        if (config.enableTextureRule)
                            GUILayout.Label("🖼贴图", smallStyle);
                        if (!config.enableNamingRule && !config.enableAddressable && !config.enableTextureRule)
                            GUILayout.Label("（未启用任何规则）", smallStyle);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();

                GUI.backgroundColor = bgColor;
            }
        }
        EditorGUILayout.EndScrollView();
    }

    // ── 右侧上：配置详情 ─────────────────────────────────────
    private void DrawConfigDetail()
    {
        if (_selectedConfigIndex < 0 || _selectedConfigIndex >= _configs.Count)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("← 选择左侧配置查看详情", MessageType.Info);
            return;
        }

        var config = _configs[_selectedConfigIndex];
        if (config == null) return;

        _showConfigDetail = EditorGUILayout.Foldout(_showConfigDetail, $"配置详情：{config.name}", true);
        if (!_showConfigDetail) return;

        EditorGUI.indentLevel++;
        {
            EditorGUILayout.LabelField("文件夹", config.NormalizedFolderPath);
            EditorGUILayout.LabelField("递归", config.recursive ? "是" : "否");
            if (config.ignoredAssets != null && config.ignoredAssets.Count > 0)
            {
                EditorGUILayout.LabelField("忽略资源（" + config.ignoredAssets.Count + "）", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                for (int i = 0; i < config.ignoredAssets.Count; i++)
                {
                    var obj = config.ignoredAssets[i];
                    EditorGUILayout.BeginHorizontal();
                    {
                        // 只读 ObjectField，显示图标 + 名称，点击可选中
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField(obj, typeof(UnityEngine.Object), false, GUILayout.Height(18));
                        EditorGUI.EndDisabledGroup();
                        // 小标签：文件夹 or 资源
                        bool isFolder = obj != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj));
                        GUILayout.Label(isFolder ? "📁" : "📄", GUILayout.Width(18));
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            if (config.enableNamingRule)
            {
                EditorGUILayout.LabelField("命名规则", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("  正则", config.fileNamePattern);
                EditorGUILayout.LabelField("  说明", config.namingDescription);
                EditorGUILayout.LabelField("  忽略扩展名", config.namingIgnoreExtensions);
                EditorGUILayout.Space(2);
            }

            if (config.enableAddressable)
            {
                EditorGUILayout.LabelField("Addressable 规则", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("  命名模板", config.addressableNameTemplate);
                EditorGUILayout.LabelField("  分组", string.IsNullOrEmpty(config.addressableGroupName) ? "（默认）" : config.addressableGroupName);
                EditorGUILayout.LabelField("  标签", string.IsNullOrEmpty(config.addressableLabels) ? "（无）" : config.addressableLabels);
                EditorGUILayout.LabelField("  目标扩展名", config.addressableTargetExtensions);
                EditorGUILayout.Space(2);
            }

            if (config.enableTextureRule)
            {
                EditorGUILayout.LabelField("贴图规则", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("  纹理类型", config.textureType.ToString());
                EditorGUILayout.LabelField("  压缩方式", config.textureCompression.ToString());
                EditorGUILayout.LabelField("  Filter Mode", config.textureFilterMode.ToString());
                EditorGUILayout.LabelField("  Max Cap Size", config.textureMaxCapSize.ToString());
                EditorGUILayout.LabelField("  Alpha", config.textureAlphaIsTransparency ? "是" : "否");
                EditorGUILayout.LabelField("  UI 关键词", string.IsNullOrEmpty(config.textureUiKeywords) ? "（无）" : config.textureUiKeywords);
                if (!string.IsNullOrEmpty(config.textureUiKeywords))
                {
                    EditorGUILayout.LabelField("  UI 类型", config.textureUiType.ToString());
                    EditorGUILayout.LabelField("  UI Sprite Mode", config.textureUiSpriteMode.ToString());
                    EditorGUILayout.LabelField("  UI Mipmap", config.textureUiMipmapEnabled ? "启用" : "禁用");
                    EditorGUILayout.LabelField("  UI Wrap Mode", config.textureUiWrapMode.ToString());
                }
                EditorGUILayout.LabelField("  目标扩展名", config.textureTargetExtensions);
                EditorGUILayout.Space(2);
            }

            // 快捷按钮
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("选中配置文件", GUILayout.Height(22)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GetAssetPath(config));
                    if (asset != null) Selection.activeObject = asset;
                }
                if (GUILayout.Button("扫描此配置违规", GUILayout.Height(22)))
                {
                    ScanSingleConfig(config);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    // ── 右侧下：违规列表 ─────────────────────────────────────
    private void DrawViolationList()
    {
        EditorGUILayout.Space(6);
        _showViolations = EditorGUILayout.Foldout(_showViolations,
            $"违规资源（{_violations.Count} 项）", true);
        if (!_showViolations) return;

        // 过滤工具栏
        EditorGUILayout.BeginHorizontal();
        {
            GUILayout.Label("筛选:", GUILayout.Width(30));
            int filterIdx = Array.IndexOf(RuleTypeFilters, _filterRuleType);
            int newIdx = EditorGUILayout.Popup(filterIdx < 0 ? 0 : filterIdx, RuleTypeFilters, GUILayout.Width(80));
            _filterRuleType = RuleTypeFilters[newIdx];

            GUILayout.FlexibleSpace();

            if (_violations.Count > 0 && GUILayout.Button("全部修复", GUILayout.Width(70)))
            {
                FixAllViolations();
            }
        }
        EditorGUILayout.EndHorizontal();

        // 过滤后的违规列表
        var filtered = _filterRuleType == "全部"
            ? _violations
            : _violations.Where(v => v.ruleType == _filterRuleType).ToList();

        _violationScroll = EditorGUILayout.BeginScrollView(_violationScroll, GUILayout.MaxHeight(250));
        {
            if (filtered.Count == 0)
            {
                EditorGUILayout.LabelField(_scanned ? "无违规项 ✓" : "尚未扫描，请点击「扫描违规」",
                    EditorStyles.centeredGreyMiniLabel);
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                var v = filtered[i];
                var vObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(v.assetPath);

                EditorGUILayout.BeginVertical("box");
                {
                    // 顶行：类型标签 + 资源缩略图 ObjectField
                    EditorGUILayout.BeginHorizontal();
                    {
                        var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            fontStyle = FontStyle.Bold,
                            normal = { textColor = GetViolationColor(v.ruleType) }
                        };
                        GUILayout.Label($"[{v.ruleType}]", labelStyle, GUILayout.Width(74));

                        // 可拖拽、可点击选中的资源字段
                        var newObj = EditorGUILayout.ObjectField(vObj, typeof(UnityEngine.Object), false);
                        if (newObj != vObj && newObj != null)
                            Selection.activeObject = newObj;
                    }
                    EditorGUILayout.EndHorizontal();

                    // 路径小字
                    EditorGUILayout.LabelField(v.assetPath, EditorStyles.miniLabel);

                    // 违规原因
                    EditorGUILayout.LabelField(v.message, EditorStyles.wordWrappedMiniLabel);

                    // 操作按钮
                    EditorGUILayout.BeginHorizontal();
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("选中", GUILayout.Width(44), GUILayout.Height(18)))
                        {
                            if (vObj != null) Selection.activeObject = vObj;
                        }
                        if (GUILayout.Button("修复", GUILayout.Width(44), GUILayout.Height(18)))
                        {
                            FixViolation(v);
                        }
                        if (GUILayout.Button("忽略", GUILayout.Width(44), GUILayout.Height(18)))
                        {
                            _violations.RemoveAt(_violations.IndexOf(v));
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private Color GetViolationColor(string ruleType)
    {
        switch (ruleType)
        {
            case "命名": return new Color(0.9f, 0.6f, 0.1f);
            case "Addressable": return new Color(0.2f, 0.7f, 0.3f);
            case "贴图": return new Color(0.5f, 0.4f, 0.9f);
            default: return Color.white;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  扫描逻辑
    // ══════════════════════════════════════════════════════════

    private void ScanViolations()
    {
        _violations.Clear();
        _scanned = true;

        foreach (var config in _configs)
        {
            if (config == null || !config.enabled) continue;
            ScanSingleConfigInternal(config);
        }

        SetStatus($"扫描完成，发现 {_violations.Count} 项违规");
    }

    private void ScanSingleConfig(FolderRuleConfig config)
    {
        // 仅清除该配置的违规
        _violations.RemoveAll(v => v.config == config);
        _scanned = true;
        ScanSingleConfigInternal(config);
        SetStatus($"扫描 [{config.name}] 完成");
    }

    private void ScanSingleConfigInternal(FolderRuleConfig config)
    {
        string folder = config.NormalizedFolderPath;
        if (!AssetDatabase.IsValidFolder(folder))
        {
            SetStatus($"文件夹不存在: {folder}");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath)) continue;
            // 跳过文件夹自身
            if (AssetDatabase.IsValidFolder(assetPath)) continue;
            // 跳过忽略列表中的资源
            if (config.IsIgnored(assetPath)) continue;

            CheckNamingViolation(config, assetPath);
            CheckAddressableViolation(config, assetPath);
            CheckTextureViolation(config, assetPath);
        }
    }

    // ══════════════════════════════════════════════════════════
    //  应用规则（手动刷新）
    // ══════════════════════════════════════════════════════════

    /// <summary>对所有配置范围内的资源重新应用规则（Addressable + 贴图设置）</summary>
    private void ApplyAllRules()
    {
        int totalApplied = 0;
        bool anyAddressableChange = false;

        foreach (var config in _configs)
        {
            if (config == null || !config.enabled) continue;

            string folder = config.NormalizedFolderPath;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[FolderRule] 文件夹不存在，跳过: {folder}");
                continue;
            }

            string[] guids = AssetDatabase.FindAssets("", new[] { folder });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;
                if (AssetDatabase.IsValidFolder(assetPath)) continue;
                if (config.IsIgnored(assetPath)) continue;

                bool applied = false;

                // 命名检查（仅警告）
                CheckAndWarnNaming(config, assetPath);

                // Addressable 自动添加
                if (config.enableAddressable)
                {
                    if (ApplyAddressableToAsset(config, assetPath))
                        applied = true;
                }

                // 贴图导入设置
                if (config.enableTextureRule)
                {
                    if (ApplyTextureSettingsToAsset(config, assetPath))
                        applied = true;
                }

                if (applied) totalApplied++;
            }
        }

        if (anyAddressableChange)
            AssetDatabase.SaveAssets();

        RefreshConfigs();
        SetStatus($"规则已应用，共处理 {totalApplied} 个资源");
    }

    /// <summary>对单个资源应用 Addressable 规则（返回是否有变更）</summary>
    private bool ApplyAddressableToAsset(FolderRuleConfig config, string assetPath)
    {
#if ADDRESSABLES
        if (!config.IsTargetExtension(assetPath, config.addressableTargetExtensions))
            return false;

        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return false;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return false;

        var existingEntry = settings.FindAssetEntry(guid);
        if (existingEntry != null)
        {
            // 已存在，检查地址名称是否需要更新
            string expectedName = config.ResolveAddressableName(assetPath);
            if (!string.IsNullOrEmpty(expectedName) && existingEntry.address != expectedName)
            {
                existingEntry.address = expectedName;
                settings.SetDirty(
                    UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryModified,
                    existingEntry, true);
                Debug.Log($"[FolderRule] 更新 Addressable 地址: {assetPath} → {expectedName}");
                return true;
            }
            return false;
        }

        // 不存在，创建条目
        string groupName = config.addressableGroupName;
        var group = settings.FindGroup(groupName);
        if (group == null && !string.IsNullOrEmpty(groupName))
        {
            group = settings.CreateGroup(groupName, false, false, false, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
        }
        if (group == null)
            group = settings.DefaultGroup;

        var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: true);

        string address = config.ResolveAddressableName(assetPath);
        if (!string.IsNullOrEmpty(address))
            entry.address = address;

        var labels = config.GetAddressableLabels();
        foreach (string label in labels)
        {
            if (!settings.GetLabels().Contains(label))
                settings.AddLabel(label);
            entry.SetLabel(label, true);
        }

        settings.SetDirty(
            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved,
            entry, true);

        Debug.Log($"[FolderRule] 已添加 Addressable: {assetPath} → {entry.address} (分组: {group.Name})");
        return true;
#else
        return false;
#endif
    }

    /// <summary>对单个资源应用贴图导入设置（返回是否有变更）</summary>
    private bool ApplyTextureSettingsToAsset(FolderRuleConfig config, string assetPath)
    {
        if (!config.IsTargetExtension(assetPath, config.textureTargetExtensions))
            return false;

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return false;

        bool isUi = config.IsUiTexture(assetPath);
        bool changed = false;

        // 纹理类型
        var expectedType = isUi ? config.textureUiType : config.textureType;
        if (importer.textureType != expectedType) { importer.textureType = expectedType; changed = true; }

        // 公共参数
        if (importer.alphaIsTransparency != config.textureAlphaIsTransparency) { importer.alphaIsTransparency = config.textureAlphaIsTransparency; changed = true; }
        if (importer.filterMode != config.textureFilterMode) { importer.filterMode = config.textureFilterMode; changed = true; }
        if (importer.textureCompression != config.textureCompression) { importer.textureCompression = config.textureCompression; changed = true; }

        int expectedMaxSize = config.GetRecommendedMaxSize(importer);
        if (importer.maxTextureSize != expectedMaxSize) { importer.maxTextureSize = expectedMaxSize; changed = true; }

        // UI 贴图额外参数
        if (isUi)
        {
            if (importer.spriteImportMode != config.textureUiSpriteMode) { importer.spriteImportMode = config.textureUiSpriteMode; changed = true; }
            if (importer.mipmapEnabled != config.textureUiMipmapEnabled) { importer.mipmapEnabled = config.textureUiMipmapEnabled; changed = true; }
            if (importer.wrapMode != config.textureUiWrapMode) { importer.wrapMode = config.textureUiWrapMode; changed = true; }
        }

        if (changed)
        {
            importer.SaveAndReimport();
            Debug.Log($"[FolderRule] 已应用贴图规则: {assetPath}" + (isUi ? " [UI]" : ""));
        }
        return changed;
    }

    /// <summary>命名检查（仅警告）—— 供 ApplyAllRules 调用</summary>
    private static void CheckAndWarnNaming(FolderRuleConfig config, string assetPath)
    {
        if (!config.enableNamingRule) return;
        if (config.IsNamingIgnored(assetPath)) return;

        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrEmpty(fileName)) return;

        try
        {
            if (!Regex.IsMatch(fileName, config.fileNamePattern))
            {
                Debug.LogWarning(
                    $"[FolderRule] 文件名不符合规范 [{config.name}]\n" +
                    $"  文件: {assetPath}\n" +
                    $"  规则: {config.namingDescription}\n" +
                    $"  正则: {config.fileNamePattern}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FolderRule] 正则表达式错误 ({config.name}): {ex.Message}");
        }
    }

    // ── 命名违规检查 ──────────────────────────────────────────
    private void CheckNamingViolation(FolderRuleConfig config, string assetPath)
    {
        if (!config.enableNamingRule) return;
        if (config.IsNamingIgnored(assetPath)) return;

        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrEmpty(fileName)) return;

        try
        {
            if (!Regex.IsMatch(fileName, config.fileNamePattern))
            {
                _violations.Add(new ViolationEntry
                {
                    assetPath = assetPath,
                    configName = config.name,
                    ruleType = "命名",
                    message = $"文件名「{fileName}」不符合规范：{config.namingDescription}",
                    config = config
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FolderRuleManager] 正则表达式错误 ({config.name}): {ex.Message}");
        }
    }

    // ── Addressable 违规检查 ──────────────────────────────────
    private void CheckAddressableViolation(FolderRuleConfig config, string assetPath)
    {
#if ADDRESSABLES
        if (!config.enableAddressable) return;
        if (!config.IsTargetExtension(assetPath, config.addressableTargetExtensions)) return;

        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.FindAssetEntry(guid);
        if (entry == null)
        {
            _violations.Add(new ViolationEntry
            {
                assetPath = assetPath,
                configName = config.name,
                ruleType = "Addressable",
                message = "资源未添加到 Addressable",
                config = config
            });
        }
        else
        {
            // 检查命名是否符合模板
            string expectedName = config.ResolveAddressableName(assetPath);
            if (!string.IsNullOrEmpty(expectedName) && entry.address != expectedName)
            {
                _violations.Add(new ViolationEntry
                {
                    assetPath = assetPath,
                    configName = config.name,
                    ruleType = "Addressable",
                    message = $"Addressable 命名不匹配，期望「{expectedName}」，实际「{entry.address}」",
                    config = config
                });
            }
        }
#endif
    }

    // ── 贴图违规检查 ──────────────────────────────────────────
    private void CheckTextureViolation(FolderRuleConfig config, string assetPath)
    {
        if (!config.enableTextureRule) return;
        if (!config.IsTargetExtension(assetPath, config.textureTargetExtensions)) return;

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        bool isUi = config.IsUiTexture(assetPath);
        bool violated = false;
        var issues = new List<string>();

        // 纹理类型
        var expectedType = isUi ? config.textureUiType : config.textureType;
        if (importer.textureType != expectedType)
        {
            issues.Add($"类型: {importer.textureType} → {expectedType}");
            violated = true;
        }

        // 公共参数
        if (importer.alphaIsTransparency != config.textureAlphaIsTransparency)
        {
            issues.Add($"Alpha: {(importer.alphaIsTransparency ? "开" : "关")} → {(config.textureAlphaIsTransparency ? "开" : "关")}");
            violated = true;
        }

        if (importer.filterMode != config.textureFilterMode)
        {
            issues.Add($"Filter: {importer.filterMode} → {config.textureFilterMode}");
            violated = true;
        }

        if (importer.textureCompression != config.textureCompression)
        {
            issues.Add($"压缩: {importer.textureCompression} → {config.textureCompression}");
            violated = true;
        }

        // 自动分档 MaxSize
        int expectedMaxSize = config.GetRecommendedMaxSize(importer);
        if (importer.maxTextureSize != expectedMaxSize)
        {
            issues.Add($"MaxSize: {importer.maxTextureSize} → {expectedMaxSize}");
            violated = true;
        }

        // UI 贴图额外参数
        if (isUi)
        {
            if (importer.spriteImportMode != config.textureUiSpriteMode)
            {
                issues.Add($"SpriteMode: {importer.spriteImportMode} → {config.textureUiSpriteMode}");
                violated = true;
            }

            if (importer.mipmapEnabled != config.textureUiMipmapEnabled)
            {
                issues.Add($"Mipmap: {(importer.mipmapEnabled ? "开" : "关")} → {(config.textureUiMipmapEnabled ? "开" : "关")}");
                violated = true;
            }

            if (importer.wrapMode != config.textureUiWrapMode)
            {
                issues.Add($"Wrap: {importer.wrapMode} → {config.textureUiWrapMode}");
                violated = true;
            }
        }

        if (violated)
        {
            _violations.Add(new ViolationEntry
            {
                assetPath = assetPath,
                configName = config.name,
                ruleType = "贴图",
                message = (isUi ? "[UI] " : "") + string.Join("；", issues),
                config = config
            });
        }
    }

    // ══════════════════════════════════════════════════════════
    //  修复逻辑
    // ══════════════════════════════════════════════════════════

    private void FixAllViolations()
    {
        if (_violations.Count == 0)
        {
            SetStatus("无违规项可修复");
            return;
        }

        int fixedCount = 0;
        var toFix = new List<ViolationEntry>(_violations);

        foreach (var v in toFix)
        {
            if (FixViolationInternal(v))
                fixedCount++;
        }

        _violations.Clear();
        SetStatus($"已修复 {fixedCount}/{toFix.Count} 项违规");
    }

    private void FixViolation(ViolationEntry v)
    {
        if (FixViolationInternal(v))
        {
            _violations.Remove(v);
            SetStatus($"已修复: {v.assetPath}");
        }
        else
        {
            SetStatus($"修复失败: {v.assetPath}");
        }
    }

    private bool FixViolationInternal(ViolationEntry v)
    {
        try
        {
            switch (v.ruleType)
            {
                case "命名":
                    return FixNamingViolation(v);
                case "Addressable":
                    return FixAddressableViolation(v);
                case "贴图":
                    return FixTextureViolation(v);
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FolderRuleManager] 修复失败 ({v.assetPath}): {ex.Message}");
            return false;
        }
    }

    // ── 修复命名违规 ──────────────────────────────────────────
    private bool FixNamingViolation(ViolationEntry v)
    {
        string assetPath = v.assetPath;
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string ext = Path.GetExtension(assetPath);
        string dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "";

        // 尝试转换为小写+下划线格式
        string fixedName = fileName.ToLower()
            .Replace(" ", "_")
            .Replace("-", "_");

        // 移除不合法字符
        fixedName = Regex.Replace(fixedName, @"[^a-z0-9_]", "");

        // 确保以字母开头
        if (fixedName.Length > 0 && char.IsDigit(fixedName[0]))
            fixedName = "n" + fixedName;

        if (string.IsNullOrEmpty(fixedName)) fixedName = "unnamed";

        // 检查是否已符合规范
        try
        {
            if (Regex.IsMatch(fixedName, v.config.fileNamePattern))
            {
                string newPath = dir + "/" + fixedName + ext;
                if (newPath != assetPath)
                {
                    string result = AssetDatabase.RenameAsset(assetPath, fixedName + ext);
                    if (!string.IsNullOrEmpty(result))
                    {
                        // RenameAsset 可能不包含扩展名
                        result = AssetDatabase.RenameAsset(assetPath, fixedName);
                        if (!string.IsNullOrEmpty(result))
                        {
                            Debug.LogWarning($"[FolderRuleManager] 重命名失败: {result}");
                            return false;
                        }
                    }
                    AssetDatabase.SaveAssets();
                    return true;
                }
                return true; // 已经符合
            }
        }
        catch { }

        Debug.LogWarning($"[FolderRuleManager] 无法自动修复命名: {fileName}（建议手动重命名）");
        return false;
    }

    // ── 修复 Addressable 违规 ─────────────────────────────────
    private bool FixAddressableViolation(ViolationEntry v)
    {
#if ADDRESSABLES
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[FolderRuleManager] AddressableSettings 不存在，请先初始化 Addressable");
            return false;
        }

        string guid = AssetDatabase.AssetPathToGUID(v.assetPath);
        var entry = settings.FindAssetEntry(guid);

        if (entry == null)
        {
            // 添加到 Addressable
            string groupName = v.config.addressableGroupName;
            var group = settings.FindGroup(groupName);

            if (group == null && !string.IsNullOrEmpty(groupName))
            {
                // 创建分组
                group = settings.CreateGroup(groupName, false, false, false, null,
                    typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
            }

            if (group == null)
                group = settings.DefaultGroup;

            entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: true);
        }

        // 设置 Addressable 名称
        string expectedName = v.config.ResolveAddressableName(v.assetPath);
        if (!string.IsNullOrEmpty(expectedName))
            entry.address = expectedName;

        // 设置标签
        var labels = v.config.GetAddressableLabels();
        foreach (string label in labels)
        {
            if (!settings.GetLabels().Contains(label))
                settings.AddLabel(label);
            entry.SetLabel(label, true);
        }

        settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        return true;
#else
        Debug.LogWarning("[FolderRuleManager] 未安装 Addressables 包，无法修复");
        return false;
#endif
    }

    // ── 修复贴图违规 ──────────────────────────────────────────
    private bool FixTextureViolation(ViolationEntry v)
    {
        var importer = AssetImporter.GetAtPath(v.assetPath) as TextureImporter;
        if (importer == null) return false;

        bool isUi = v.config.IsUiTexture(v.assetPath);

        // 纹理类型
        importer.textureType = isUi ? v.config.textureUiType : v.config.textureType;

        // 公共参数
        importer.alphaIsTransparency = v.config.textureAlphaIsTransparency;
        importer.filterMode = v.config.textureFilterMode;
        importer.textureCompression = v.config.textureCompression;
        importer.maxTextureSize = v.config.GetRecommendedMaxSize(importer);

        // UI 贴图额外参数
        if (isUi)
        {
            importer.spriteImportMode = v.config.textureUiSpriteMode;
            importer.mipmapEnabled = v.config.textureUiMipmapEnabled;
            importer.wrapMode = v.config.textureUiWrapMode;
        }

        importer.SaveAndReimport();
        return true;
    }

    // ══════════════════════════════════════════════════════════
    //  创建新配置
    // ══════════════════════════════════════════════════════════

    private void CreateNewConfig()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "创建文件夹规则配置", "FolderRuleConfig", "asset", "选择保存位置", "Assets");
        if (string.IsNullOrEmpty(path)) return;

        var config = CreateInstance<FolderRuleConfig>();
        config.folderPath = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
        AssetDatabase.CreateAsset(config, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RefreshConfigs();
        _selectedConfigIndex = _configs.FindIndex(c => AssetDatabase.GetAssetPath(c) == path);
        SetStatus($"已创建配置: {path}");
    }

    // ── 工具方法 ──────────────────────────────────────────────
    private void SetStatus(string msg)
    {
        _statusMessage = msg;
        _statusTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    // ══════════════════════════════════════════════════════════
    //  静态 API — 供 Postprocessor 调用
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 获取所有有效的 FolderRuleConfig（静态缓存，避免每次都扫描 AssetDatabase）
    /// </summary>
    private static List<FolderRuleConfig> _cachedConfigs;
    private static double _cacheTime;
    private const double CacheDuration = 5.0; // 缓存5秒

    internal static List<FolderRuleConfig> GetAllConfigs()
    {
        if (_cachedConfigs != null && EditorApplication.timeSinceStartup - _cacheTime < CacheDuration)
            return _cachedConfigs;

        _cachedConfigs = new List<FolderRuleConfig>();
        string[] guids = AssetDatabase.FindAssets("t:FolderRuleConfig");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<FolderRuleConfig>(path);
            if (config != null && config.enabled) _cachedConfigs.Add(config);
        }
        _cacheTime = EditorApplication.timeSinceStartup;
        return _cachedConfigs;
    }

    /// <summary>强制刷新缓存</summary>
    internal static void InvalidateCache()
    {
        _cachedConfigs = null;
    }
}
#endif
