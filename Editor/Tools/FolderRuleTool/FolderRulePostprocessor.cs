#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 文件夹规则后处理器 —— 新文件导入时自动应用规则
///
/// 工作流程：
///   1. 资源导入/更新时触发 OnPostprocessAllAssets
///   2. 遍历所有 FolderRuleConfig，找到匹配的规则
///   3. 自动应用命名检查（仅警告）、Addressable 添加、贴图设置
/// </summary>
public class FolderRulePostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // 获取所有启用的配置
        var configs = FolderRuleManager.GetAllConfigs();
        if (configs == null || configs.Count == 0) return;

        // 处理新导入和移动的资源
        var allChanged = new List<string>();
        if (importedAssets != null) allChanged.AddRange(importedAssets);
        if (movedAssets != null) allChanged.AddRange(movedAssets);

        if (allChanged.Count == 0) return;

        bool anyAddressableChange = false;

        foreach (string assetPath in allChanged)
        {
            if (string.IsNullOrEmpty(assetPath)) continue;
            if (AssetDatabase.IsValidFolder(assetPath)) continue;

            foreach (var config in configs)
            {
                if (!config.IsInScope(assetPath)) continue;
                if (config.IsIgnored(assetPath)) continue;

                // 命名检查（仅打印警告，不自动修改）
                CheckAndWarnNaming(config, assetPath);

                // Addressable 自动添加
                if (config.enableAddressable)
                {
                    if (TryApplyAddressable(config, assetPath))
                        anyAddressableChange = true;
                }

                // 贴图导入设置
                if (config.enableTextureRule)
                {
                    TryApplyTextureSettings(config, assetPath);
                }
            }
        }

        if (anyAddressableChange)
        {
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>命名检查（仅警告）</summary>
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

    /// <summary>自动添加到 Addressable</summary>
    private static bool TryApplyAddressable(FolderRuleConfig config, string assetPath)
    {
#if ADDRESSABLES
        if (!config.IsTargetExtension(assetPath, config.addressableTargetExtensions))
            return false;

        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return false;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return false;

        var existingEntry = settings.FindAssetEntry(guid);
        if (existingEntry != null) return false; // 已存在则跳过

        // 查找或创建分组
        string groupName = config.addressableGroupName;
        var group = settings.FindGroup(groupName);

        if (group == null && !string.IsNullOrEmpty(groupName))
        {
            group = settings.CreateGroup(groupName, false, false, false, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
        }

        if (group == null)
            group = settings.DefaultGroup;

        if (group == null)
        {
            Debug.LogWarning($"[FolderRule] 无法获取或创建 Addressable 分组，跳过: {assetPath}");
            return false;
        }

        // 创建条目
        var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: true);
        if (entry == null)
        {
            Debug.LogWarning($"[FolderRule] 创建 Addressable 条目失败，跳过: {assetPath}");
            return false;
        }

        // 设置地址名称
        string address = config.ResolveAddressableName(assetPath);
        if (!string.IsNullOrEmpty(address))
            entry.address = address;

        // 设置标签
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

    /// <summary>自动应用贴图导入设置（逻辑与 TextureImportAutoTool 一致）</summary>
    private static bool TryApplyTextureSettings(FolderRuleConfig config, string assetPath)
    {
        if (!config.IsTargetExtension(assetPath, config.textureTargetExtensions))
            return false;

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return false;

        bool changed = false;
        bool isUi = config.IsUiTexture(assetPath);

        // 纹理类型
        var targetType = isUi ? config.textureUiType : config.textureType;
        if (importer.textureType != targetType)
        {
            importer.textureType = targetType;
            changed = true;
        }

        // UI 贴图额外参数
        if (isUi)
        {
            if (importer.spriteImportMode != config.textureUiSpriteMode)
            {
                importer.spriteImportMode = config.textureUiSpriteMode;
                changed = true;
            }

            if (importer.mipmapEnabled != config.textureUiMipmapEnabled)
            {
                importer.mipmapEnabled = config.textureUiMipmapEnabled;
                changed = true;
            }

            if (importer.wrapMode != config.textureUiWrapMode)
            {
                importer.wrapMode = config.textureUiWrapMode;
                changed = true;
            }
        }

        // 公共参数
        if (importer.alphaIsTransparency != config.textureAlphaIsTransparency)
        {
            importer.alphaIsTransparency = config.textureAlphaIsTransparency;
            changed = true;
        }

        if (importer.filterMode != config.textureFilterMode)
        {
            importer.filterMode = config.textureFilterMode;
            changed = true;
        }

        if (importer.textureCompression != config.textureCompression)
        {
            importer.textureCompression = config.textureCompression;
            changed = true;
        }

        // 自动分档 MaxSize
        int maxSize = config.GetRecommendedMaxSize(importer);
        if (importer.maxTextureSize != maxSize)
        {
            importer.maxTextureSize = maxSize;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
            Debug.Log($"[FolderRule] 已应用贴图设置 [{config.name}]: {assetPath} (UI={isUi})");
        }

        return changed;
    }
}
#endif
