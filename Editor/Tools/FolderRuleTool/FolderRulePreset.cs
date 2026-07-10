#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 文件夹规则预设 —— 保存一组规则参数，可快速应用到 FolderRuleConfig
///
/// 使用方式：
///   1. 右键 Create → UnityToolsHub → 文件夹规则预设
///   2. 在 FolderRuleConfig Inspector 中选择预设并应用
///   3. 也可将当前配置保存为新预设
/// </summary>
[CreateAssetMenu(fileName = "FolderRulePreset", menuName = "UnityToolsHub/文件夹规则预设", order = 201)]
public class FolderRulePreset : ScriptableObject
{
    // ══════════════════════════════════════════════════════════
    //  刷新设置
    // ══════════════════════════════════════════════════════════

    [Header("══ 刷新设置 ══")]
    [Tooltip("开启后管理面板将按间隔自动扫描此配置的违规项")]
    public bool autoScan;

    [Tooltip("自动扫描的时间间隔（秒）")]
    public float scanInterval = 30f;

    // ══════════════════════════════════════════════════════════
    //  文件命名规范
    // ══════════════════════════════════════════════════════════

    [Header("══ 文件命名规范 ══")]
    [Tooltip("是否启用文件命名检查")]
    public bool enableNamingRule;

    [Tooltip("文件名正则表达式")]
    public string fileNamePattern = "^[a-z][a-z0-9_]*$";

    [Tooltip("命名规范描述")]
    public string namingDescription = "文件名须为小写字母开头，仅含小写字母、数字、下划线";

    [Tooltip("忽略的文件扩展名")]
    public string namingIgnoreExtensions = ".meta,.cs,.asmdef";

    // ══════════════════════════════════════════════════════════
    //  Addressable 配置
    // ══════════════════════════════════════════════════════════

    [Header("══ Addressable 配置 ══")]
    [Tooltip("是否自动将资源添加到 Addressable")]
    public bool enableAddressable;

    [Tooltip("Addressable 命名模板")]
    public string addressableNameTemplate = "{folder}/{name}";

    [Tooltip("Addressable 分组名")]
    public string addressableGroupName = "";

    [Tooltip("Addressable 标签")]
    public string addressableLabels = "";

    [Tooltip("目标扩展名")]
    public string addressableTargetExtensions = ".png,.jpg,.prefab,.asset";

    // ══════════════════════════════════════════════════════════
    //  贴图导入规则
    // ══════════════════════════════════════════════════════════

    [Header("══ 贴图导入规则 ══")]
    [Tooltip("是否启用贴图导入规则")]
    public bool enableTextureRule;

    [Tooltip("目标扩展名")]
    public string textureTargetExtensions = ".png,.jpg,.jpeg,.tga,.psd";

    // 公共参数
    [Tooltip("纹理类型")]
    public TextureImporterType textureType = TextureImporterType.Default;

    [Tooltip("Alpha Is Transparency")]
    public bool textureAlphaIsTransparency = true;

    [Tooltip("Filter Mode")]
    public FilterMode textureFilterMode = FilterMode.Bilinear;

    [Tooltip("压缩方式")]
    public TextureImporterCompression textureCompression = TextureImporterCompression.Compressed;

    [Tooltip("最大尺寸上限")]
    public int textureMaxCapSize = 4096;

    // UI 贴图额外参数
    [Tooltip("UI 贴图关键词")]
    public string textureUiKeywords = "/ui/,/sprite/,/sprites/,/icon/,/icons/";

    [Tooltip("UI 纹理类型")]
    public TextureImporterType textureUiType = TextureImporterType.Sprite;

    [Tooltip("UI Sprite 模式")]
    public SpriteImportMode textureUiSpriteMode = SpriteImportMode.Single;

    [Tooltip("UI Mipmap")]
    public bool textureUiMipmapEnabled;

    [Tooltip("UI Wrap Mode")]
    public TextureWrapMode textureUiWrapMode = TextureWrapMode.Clamp;

    // ══════════════════════════════════════════════════════════
    //  内置预设
    // ══════════════════════════════════════════════════════════

    /// <summary>从配置中读取参数（仅读取规则相关字段，不含文件夹路径等基础字段）</summary>
    public void LoadFromConfig(FolderRuleConfig config)
    {
        autoScan = config.autoScan;
        scanInterval = config.scanInterval;

        enableNamingRule = config.enableNamingRule;
        fileNamePattern = config.fileNamePattern;
        namingDescription = config.namingDescription;
        namingIgnoreExtensions = config.namingIgnoreExtensions;

        enableAddressable = config.enableAddressable;
        addressableNameTemplate = config.addressableNameTemplate;
        addressableGroupName = config.addressableGroupName;
        addressableLabels = config.addressableLabels;
        addressableTargetExtensions = config.addressableTargetExtensions;

        enableTextureRule = config.enableTextureRule;
        textureTargetExtensions = config.textureTargetExtensions;
        textureType = config.textureType;
        textureAlphaIsTransparency = config.textureAlphaIsTransparency;
        textureFilterMode = config.textureFilterMode;
        textureCompression = config.textureCompression;
        textureMaxCapSize = config.textureMaxCapSize;
        textureUiKeywords = config.textureUiKeywords;
        textureUiType = config.textureUiType;
        textureUiSpriteMode = config.textureUiSpriteMode;
        textureUiMipmapEnabled = config.textureUiMipmapEnabled;
        textureUiWrapMode = config.textureUiWrapMode;
    }

    /// <summary>将预设参数写入配置（仅写入规则相关字段，不影响文件夹路径等基础字段）</summary>
    public void ApplyToConfig(FolderRuleConfig config)
    {
        config.autoScan = autoScan;
        config.scanInterval = scanInterval;

        config.enableNamingRule = enableNamingRule;
        config.fileNamePattern = fileNamePattern;
        config.namingDescription = namingDescription;
        config.namingIgnoreExtensions = namingIgnoreExtensions;

        config.enableAddressable = enableAddressable;
        config.addressableNameTemplate = addressableNameTemplate;
        config.addressableGroupName = addressableGroupName;
        config.addressableLabels = addressableLabels;
        config.addressableTargetExtensions = addressableTargetExtensions;

        config.enableTextureRule = enableTextureRule;
        config.textureTargetExtensions = textureTargetExtensions;
        config.textureType = textureType;
        config.textureAlphaIsTransparency = textureAlphaIsTransparency;
        config.textureFilterMode = textureFilterMode;
        config.textureCompression = textureCompression;
        config.textureMaxCapSize = textureMaxCapSize;
        config.textureUiKeywords = textureUiKeywords;
        config.textureUiType = textureUiType;
        config.textureUiSpriteMode = textureUiSpriteMode;
        config.textureUiMipmapEnabled = textureUiMipmapEnabled;
        config.textureUiWrapMode = textureUiWrapMode;

        EditorUtility.SetDirty(config);
    }
}
#endif
