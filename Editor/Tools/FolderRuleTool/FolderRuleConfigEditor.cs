#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Nodin.Editor;

/// <summary>
/// FolderRuleConfig 自定义编辑器
/// 绘制属性后附加：违规扫描、违规列表、快捷操作
/// </summary>
[CustomEditor(typeof(FolderRuleConfig))]
public class FolderRuleConfigEditor : NodinEditor
{
    private List<ViolationItem> _violations = new List<ViolationItem>();
    private bool _scanned;
    private bool _showViolations = true;
    private Vector2 _scroll;

    private class ViolationItem
    {
        public string assetPath;
        public string ruleType;
        public string message;
    }

    private FolderRuleConfig Config => target as FolderRuleConfig;

    // ── 预设相关状态 ────────────────────────────────────────
    private string[] _presetNames = new string[0];
    private FolderRulePreset[] _presets = new FolderRulePreset[0];
    private int _selectedPresetIndex = -1;
    private string _newPresetName = "";
    private bool _showPresetSection = true;

    private void RefreshPresetList()
    {
        string[] guids = AssetDatabase.FindAssets("t:FolderRulePreset");
        _presets = guids
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .Select(p => AssetDatabase.LoadAssetAtPath<FolderRulePreset>(p))
            .Where(p => p != null)
            .ToArray();
        _presetNames = _presets.Select(p => p.name).ToArray();
    }

    public override void OnInspectorGUI()
    {
        // 1. 绘制默认属性（Nodin 自动绘制）
        base.OnInspectorGUI();

        // ── 预设区域 ──
        EditorGUILayout.Space(4);
        _showPresetSection = EditorGUILayout.Foldout(_showPresetSection, "📋 预设管理", true);
        if (_showPresetSection)
        {
            DrawPresetSection();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // 2. 操作按钮行
        EditorGUILayout.BeginHorizontal();
        {
            // 扫描按钮（高亮）
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button("🔍 扫描此配置违规", GUILayout.Height(28)))
            {
                ScanThisConfig();
            }
            GUI.backgroundColor = prev;

            // 应用规则按钮
            if (GUILayout.Button("应用规则", GUILayout.Height(28)))
            {
                ApplyRulesToThisConfig();
            }

            // 打开管理器
            if (GUILayout.Button("打开管理器", GUILayout.Height(28)))
            {
                FolderRuleManager.ShowWindow();
            }
        }
        EditorGUILayout.EndHorizontal();

        // 3. 违规列表
        DrawViolationList();
    }

    // ── 预设区域绘制 ────────────────────────────────────────
    private void DrawPresetSection()
    {
        EditorGUI.indentLevel++;

        // 预设选择行
        EditorGUILayout.BeginHorizontal();
        {
            // 刷新预设列表（每次绘制都刷新，开销很小）
            RefreshPresetList();

            GUILayout.Label("选择预设", GUILayout.Width(60));
            int newIndex = EditorGUILayout.Popup(
                _selectedPresetIndex < 0 ? 0 : _selectedPresetIndex,
                _presetNames.Length > 0 ? _presets.Select(p => p.name).ToArray() : new[] { "（无预设）" });
            if (newIndex != _selectedPresetIndex && newIndex < _presets.Length)
                _selectedPresetIndex = newIndex;

            // 应用预设
            EditorGUI.BeginDisabledGroup(_presets.Length == 0);
            if (GUILayout.Button("应用预设", GUILayout.Width(64)))
            {
                if (_selectedPresetIndex >= 0 && _selectedPresetIndex < _presets.Length)
                {
                    Undo.RecordObject(Config, "应用预设");
                    _presets[_selectedPresetIndex].ApplyToConfig(Config);
                    Debug.Log($"[FolderRule] 已应用预设「{_presets[_selectedPresetIndex].name}」→ {Config.name}");
                }
            }
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.EndHorizontal();

        // 预设预览（选中时显示简要信息）
        if (_selectedPresetIndex >= 0 && _selectedPresetIndex < _presets.Length)
        {
            var preset = _presets[_selectedPresetIndex];
            var summary = new List<string>();
            if (preset.enableNamingRule) summary.Add("📝命名");
            if (preset.enableAddressable) summary.Add("📦Addressable");
            if (preset.enableTextureRule) summary.Add("🖼贴图");
            if (summary.Count == 0) summary.Add("（未启用任何规则）");
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("预设内容", string.Join(" + ", summary));
            EditorGUI.EndDisabledGroup();
        }

        EditorGUILayout.Space(2);

        // 保存为新预设
        EditorGUILayout.BeginHorizontal();
        {
            _newPresetName = EditorGUILayout.TextField("新预设名称", _newPresetName);

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.5f, 0.7f, 1f);
            if (GUILayout.Button("💾 保存为预设", GUILayout.Width(100)))
            {
                SaveAsPreset();
            }
            GUI.backgroundColor = prev;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    // ── 保存为新预设 ────────────────────────────────────────
    private void SaveAsPreset()
    {
        string name = string.IsNullOrWhiteSpace(_newPresetName)
            ? $"Preset_{Config.name}"
            : _newPresetName.Trim();

        // 检查是否已存在同名预设
        string existingPath = AssetDatabase.FindAssets($"t:FolderRulePreset")
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);

        FolderRulePreset preset;
        if (existingPath != null)
        {
            // 覆盖已有预设
            preset = AssetDatabase.LoadAssetAtPath<FolderRulePreset>(existingPath);
            if (EditorUtility.DisplayDialog("覆盖预设",
                $"预设「{name}」已存在，是否覆盖？", "覆盖", "取消"))
            {
                preset.LoadFromConfig(Config);
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FolderRule] 已覆盖预设「{name}」");
            }
            return;
        }

        // 创建新预设
        string folder = "Assets/UnityFramework/Editor/UnityToolsHub/Presets";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/UnityFramework/Editor/UnityToolsHub", "Presets");

        string path = $"{folder}/{name}.asset";
        preset = ScriptableObject.CreateInstance<FolderRulePreset>();
        preset.LoadFromConfig(Config);
        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();

        _newPresetName = "";
        Debug.Log($"[FolderRule] 已创建预设「{name}」→ {path}");
    }

    // ── 扫描此配置 ──────────────────────────────────────────
    private void ScanThisConfig()
    {
        _violations.Clear();
        _scanned = true;

        var config = Config;
        string folder = config.NormalizedFolderPath;
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogWarning($"[FolderRule] 文件夹不存在: {folder}");
            return;
        }

        string configPath = AssetDatabase.GetAssetPath(config);
        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath)) continue;
            if (AssetDatabase.IsValidFolder(assetPath)) continue;
            if (string.Equals(assetPath, configPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (config.IsIgnored(assetPath)) continue;

            CheckNaming(config, assetPath);
            CheckAddressable(config, assetPath);
            CheckTexture(config, assetPath);
        }

        Debug.Log($"[FolderRule] [{config.name}] 扫描完成，发现 {_violations.Count} 项违规");
    }

    // ── 应用规则到此配置 ────────────────────────────────────
    private void ApplyRulesToThisConfig()
    {
        var config = Config;
        string folder = config.NormalizedFolderPath;
        if (!AssetDatabase.IsValidFolder(folder)) return;

        int count = 0;
        string configPath = AssetDatabase.GetAssetPath(config);
        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath)) continue;
            if (AssetDatabase.IsValidFolder(assetPath)) continue;
            if (string.Equals(assetPath, configPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (config.IsIgnored(assetPath)) continue;

            // Addressable
            if (config.enableAddressable)
                ApplyAddressable(config, assetPath);

            // 贴图
            if (config.enableTextureRule)
            {
                if (ApplyTexture(config, assetPath))
                    count++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FolderRule] [{config.name}] 规则已应用，处理 {count} 个资源");
    }

    // ── 违规列表绘制 ────────────────────────────────────────
    private void DrawViolationList()
    {
        EditorGUILayout.Space(6);

        int count = _violations.Count;
        var headerStyle = new GUIStyle(EditorStyles.foldoutHeader);
        if (count > 0)
        {
            headerStyle.normal.textColor = new Color(1f, 0.5f, 0.2f);
            headerStyle.fontStyle = FontStyle.Bold;
        }

        string title = _scanned
            ? (count > 0 ? $"⚠ 违规资源（{count} 项）" : "✓ 违规资源（0 项）")
            : "违规资源（未扫描）";
        _showViolations = EditorGUILayout.Foldout(_showViolations, title, true, headerStyle);
        if (!_showViolations) return;

        if (!_scanned)
        {
            EditorGUILayout.HelpBox("点击上方「🔍 扫描此配置违规」查看规则错误的资源。", MessageType.Info);
            return;
        }

        if (count == 0)
        {
            EditorGUILayout.HelpBox("当前配置无违规项。", MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(300));
        {
            for (int i = 0; i < _violations.Count; i++)
            {
                var v = _violations[i];
                var vObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(v.assetPath);

                EditorGUILayout.BeginVertical("box");
                {
                    // 类型标签 + 资源 ObjectField
                    EditorGUILayout.BeginHorizontal();
                    {
                        Color typeColor;
                        switch (v.ruleType)
                        {
                            case "命名": typeColor = new Color(0.9f, 0.6f, 0.1f); break;
                            case "Addressable": typeColor = new Color(0.2f, 0.7f, 0.3f); break;
                            case "贴图": typeColor = new Color(0.5f, 0.4f, 0.9f); break;
                            default: typeColor = Color.gray; break;
                        }
                        var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            fontStyle = FontStyle.Bold,
                            normal = { textColor = typeColor }
                        };
                        GUILayout.Label($"[{v.ruleType}]", labelStyle, GUILayout.Width(74));

                        // 可点击选中的资源字段
                        var newObj = EditorGUILayout.ObjectField(vObj, typeof(UnityEngine.Object), false);
                        if (newObj != vObj && newObj != null)
                            Selection.activeObject = newObj;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField(v.assetPath, EditorStyles.miniLabel);
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
                            ScanThisConfig(); // 修复后重新扫描
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    // ══════════════════════════════════════════════════════════
    //  违规检查（复制逻辑，避免依赖 Manager 内部方法）
    // ══════════════════════════════════════════════════════════

    private void CheckNaming(FolderRuleConfig config, string assetPath)
    {
        if (!config.enableNamingRule) return;
        if (config.IsNamingIgnored(assetPath)) return;

        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrEmpty(fileName)) return;

        try
        {
            if (!Regex.IsMatch(fileName, config.fileNamePattern))
            {
                _violations.Add(new ViolationItem
                {
                    assetPath = assetPath,
                    ruleType = "命名",
                    message = $"文件名「{fileName}」不符合规范：{config.namingDescription}"
                });
            }
        }
        catch { }
    }

    private void CheckAddressable(FolderRuleConfig config, string assetPath)
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
            _violations.Add(new ViolationItem
            {
                assetPath = assetPath,
                ruleType = "Addressable",
                message = "资源未添加到 Addressable"
            });
            return;
        }

        // 检查分组是否匹配
        string expectedGroup = config.addressableGroupName;
        string actualGroup = entry.parentGroup?.Name ?? "";
        if (!string.IsNullOrEmpty(expectedGroup) &&
            !string.Equals(actualGroup, expectedGroup, StringComparison.Ordinal))
        {
            _violations.Add(new ViolationItem
            {
                assetPath = assetPath,
                ruleType = "Addressable",
                message = $"分组不匹配，期望「{expectedGroup}」，实际「{actualGroup}」"
            });
        }

        // 检查命名是否符合模板
        string expectedName = config.ResolveAddressableName(assetPath);
        if (!string.IsNullOrEmpty(expectedName) && entry.address != expectedName)
        {
            _violations.Add(new ViolationItem
            {
                assetPath = assetPath,
                ruleType = "Addressable",
                message = $"命名不匹配，期望「{expectedName}」，实际「{entry.address}」"
            });
        }
#endif
    }

    private void CheckTexture(FolderRuleConfig config, string assetPath)
    {
        if (!config.enableTextureRule) return;
        if (!config.IsTargetExtension(assetPath, config.textureTargetExtensions)) return;

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        bool isUi = config.IsUiTexture(assetPath);
        var issues = new List<string>();

        var expectedType = isUi ? config.textureUiType : config.textureType;
        if (importer.textureType != expectedType)
            issues.Add($"类型: {importer.textureType} → {expectedType}");

        if (importer.alphaIsTransparency != config.textureAlphaIsTransparency)
            issues.Add($"Alpha: {(importer.alphaIsTransparency ? "开" : "关")} → {(config.textureAlphaIsTransparency ? "开" : "关")}");

        if (importer.filterMode != config.textureFilterMode)
            issues.Add($"Filter: {importer.filterMode} → {config.textureFilterMode}");

        if (importer.textureCompression != config.textureCompression)
            issues.Add($"压缩: {importer.textureCompression} → {config.textureCompression}");

        int expectedMaxSize = config.GetRecommendedMaxSize(importer);
        if (importer.maxTextureSize != expectedMaxSize)
            issues.Add($"MaxSize: {importer.maxTextureSize} → {expectedMaxSize}");

        if (isUi)
        {
            if (importer.spriteImportMode != config.textureUiSpriteMode)
                issues.Add($"SpriteMode: {importer.spriteImportMode} → {config.textureUiSpriteMode}");
            if (importer.mipmapEnabled != config.textureUiMipmapEnabled)
                issues.Add($"Mipmap: {(importer.mipmapEnabled ? "开" : "关")} → {(config.textureUiMipmapEnabled ? "开" : "关")}");
            if (importer.wrapMode != config.textureUiWrapMode)
                issues.Add($"Wrap: {importer.wrapMode} → {config.textureUiWrapMode}");
        }

        if (issues.Count > 0)
        {
            _violations.Add(new ViolationItem
            {
                assetPath = assetPath,
                ruleType = "贴图",
                message = (isUi ? "[UI] " : "") + string.Join("；", issues)
            });
        }
    }

    // ── 修复单条违规 ────────────────────────────────────────
    private void FixViolation(ViolationItem v)
    {
        var config = Config;
        try
        {
            switch (v.ruleType)
            {
                case "命名":
                    // 命名违规无法自动修复，仅提示
                    Debug.LogWarning($"[FolderRule] 命名违规需手动重命名: {v.assetPath}");
                    break;

                case "Addressable":
                    ApplyAddressable(config, v.assetPath);
                    break;

                case "贴图":
                    ApplyTexture(config, v.assetPath);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FolderRule] 修复失败 ({v.assetPath}): {ex.Message}");
        }
    }

    // ── Addressable 应用 ────────────────────────────────────
    private void ApplyAddressable(FolderRuleConfig config, string assetPath)
    {
#if ADDRESSABLES
        if (!config.IsTargetExtension(assetPath, config.addressableTargetExtensions)) return;

        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return;

        // 获取目标分组
        string groupName = config.addressableGroupName;
        var targetGroup = settings.FindGroup(groupName);
        if (targetGroup == null && !string.IsNullOrEmpty(groupName))
        {
            targetGroup = settings.CreateGroup(groupName, false, false, false, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
        }
        if (targetGroup == null) targetGroup = settings.DefaultGroup;
        if (targetGroup == null) return;

        var existingEntry = settings.FindAssetEntry(guid);
        if (existingEntry != null)
        {
            bool dirty = false;

            // 检查分组是否需要移动
            if (existingEntry.parentGroup != targetGroup)
            {
                settings.MoveEntry(existingEntry, targetGroup, false, true);
                dirty = true;
            }

            // 检查地址名是否需要更新
            string expectedName = config.ResolveAddressableName(assetPath);
            if (!string.IsNullOrEmpty(expectedName) && existingEntry.address != expectedName)
            {
                existingEntry.address = expectedName;
                dirty = true;
            }

            if (dirty)
            {
                settings.SetDirty(
                    UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryModified,
                    existingEntry, true);
            }
            return;
        }

        // 不存在，创建条目
        var entry = settings.CreateOrMoveEntry(guid, targetGroup, readOnly: false, postEvent: true);
        string address = config.ResolveAddressableName(assetPath);
        if (!string.IsNullOrEmpty(address)) entry.address = address;

        var labels = config.GetAddressableLabels();
        foreach (string label in labels)
        {
            if (!settings.GetLabels().Contains(label)) settings.AddLabel(label);
            entry.SetLabel(label, true);
        }

        settings.SetDirty(
            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved,
            entry, true);
#endif
    }

    // ── 贴图应用 ────────────────────────────────────────────
    private bool ApplyTexture(FolderRuleConfig config, string assetPath)
    {
        if (!config.IsTargetExtension(assetPath, config.textureTargetExtensions)) return false;

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return false;

        bool isUi = config.IsUiTexture(assetPath);
        bool changed = false;

        var expectedType = isUi ? config.textureUiType : config.textureType;
        if (importer.textureType != expectedType) { importer.textureType = expectedType; changed = true; }
        if (importer.alphaIsTransparency != config.textureAlphaIsTransparency) { importer.alphaIsTransparency = config.textureAlphaIsTransparency; changed = true; }
        if (importer.filterMode != config.textureFilterMode) { importer.filterMode = config.textureFilterMode; changed = true; }
        if (importer.textureCompression != config.textureCompression) { importer.textureCompression = config.textureCompression; changed = true; }

        int expectedMaxSize = config.GetRecommendedMaxSize(importer);
        if (importer.maxTextureSize != expectedMaxSize) { importer.maxTextureSize = expectedMaxSize; changed = true; }

        if (isUi)
        {
            if (importer.spriteImportMode != config.textureUiSpriteMode) { importer.spriteImportMode = config.textureUiSpriteMode; changed = true; }
            if (importer.mipmapEnabled != config.textureUiMipmapEnabled) { importer.mipmapEnabled = config.textureUiMipmapEnabled; changed = true; }
            if (importer.wrapMode != config.textureUiWrapMode) { importer.wrapMode = config.textureUiWrapMode; changed = true; }
        }

        if (changed) importer.SaveAndReimport();
        return changed;
    }
}
#endif
