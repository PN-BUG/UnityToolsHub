#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityToolsHubCompat;

/// <summary>
/// 文件夹规则配置 —— ScriptableObject
/// 用户手动创建此 SO，放置在项目任意文件夹下。
/// FolderRuleManager 会自动扫描所有此类 SO 进行统一管理。
///
/// 支持的规则：
///   1. 文件命名规范（正则表达式匹配）
///   2. Addressable 自动添加（命名模板、分组名）
///   3. 贴图导入规则（可选）
/// </summary>
[CreateAssetMenu(fileName = "FolderRuleConfig", menuName = "UnityToolsHub/文件夹规则配置", order = 200)]
public class FolderRuleConfig : ScriptableObject
{
    // ══════════════════════════════════════════════════════════
    //  基础配置
    // ══════════════════════════════════════════════════════════

    [FoldoutGroup("基础设置")]
    [FolderPath(AbsolutePath = false)]
    [LabelText("文件夹路径")]
    public string folderPath = "Assets/";

    [FoldoutGroup("基础设置")]
    [LabelText("递归子文件夹")]
    public bool recursive = true;

    [FoldoutGroup("基础设置")]
    [LabelText("启用此规则")]
    public bool enabled = true;

    [FoldoutGroup("基础设置")]
    [LabelText("忽略的资源")]
    [AssetsOnly]
    [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, NumberOfItemsPerPage = 10)]
    public List<UnityEngine.Object> ignoredAssets = new List<UnityEngine.Object>();

    // ══════════════════════════════════════════════════════════
    //  文件命名规范
    // ══════════════════════════════════════════════════════════

    [FoldoutGroup("文件命名规范")]
    [LabelText("启用命名检查")]
    public bool enableNamingRule;

    [FoldoutGroup("文件命名规范")]
    [ShowIf("enableNamingRule")]
    [InfoBox("命名规范描述", InfoMessageType.None, "namingDescription")]
    [LabelText("文件名正则")]
    public string fileNamePattern = "^[a-z][a-z0-9_]*$";

    [FoldoutGroup("文件命名规范")]
    [ShowIf("enableNamingRule")]
    [LabelText("规范描述")]
    public string namingDescription = "文件名须为小写字母开头，仅含小写字母、数字、下划线";

    [FoldoutGroup("文件命名规范")]
    [ShowIf("enableNamingRule")]
    [LabelText("忽略的扩展名")]
    public string namingIgnoreExtensions = ".meta,.cs,.asmdef";

    // ══════════════════════════════════════════════════════════
    //  Addressable 配置
    // ══════════════════════════════════════════════════════════

    [FoldoutGroup("Addressable 配置")]
    [LabelText("启用 Addressable")]
    public bool enableAddressable;

    [FoldoutGroup("Addressable 配置")]
    [ShowIf("enableAddressable")]
    [InfoBox("变量: {name}=文件名  {folder}=所在文件夹名  {path}=相对路径", InfoMessageType.None)]
    [ValueDropdown("TemplateNameOptions")]
    [LabelText("命名模板")]
    public string addressableNameTemplate = "{folder}/{name}";

    [FoldoutGroup("Addressable 配置")]
    [ShowIf("enableAddressable")]
    [ValueDropdown("GetGroupNamesOdin")]
    [LabelText("分组名")]
    public string addressableGroupName = "";

    [FoldoutGroup("Addressable 配置")]
    [ShowIf("enableAddressable")]
    [LabelText("标签")]
    public string addressableLabels = "";

    [FoldoutGroup("Addressable 配置")]
    [ShowIf("enableAddressable")]
    [LabelText("目标扩展名")]
    public string addressableTargetExtensions = ".png,.jpg,.prefab,.asset";

    // ══════════════════════════════════════════════════════════
    //  贴图导入规则（可选）
    //  逻辑与 TextureImportAutoTool 保持一致
    // ══════════════════════════════════════════════════════════

    [FoldoutGroup("贴图导入规则")]
    [LabelText("启用贴图规则")]
    public bool enableTextureRule;

    [FoldoutGroup("贴图导入规则")]
    [ShowIf("enableTextureRule")]
    [LabelText("目标扩展名")]
    public string textureTargetExtensions = ".png,.jpg,.jpeg,.tga,.psd";

    // ── 公共参数（所有贴图） ──

    [FoldoutGroup("贴图导入规则/公共参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("纹理类型")]
    public TextureImporterType textureType = TextureImporterType.Default;

    [FoldoutGroup("贴图导入规则/公共参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("Alpha Is Transparency")]
    public bool textureAlphaIsTransparency = true;

    [FoldoutGroup("贴图导入规则/公共参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("Filter Mode")]
    public FilterMode textureFilterMode = FilterMode.Bilinear;

    [FoldoutGroup("贴图导入规则/公共参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("压缩方式")]
    public TextureImporterCompression textureCompression = TextureImporterCompression.Compressed;

    [FoldoutGroup("贴图导入规则/公共参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("最大尺寸上限")]
    [ValueDropdown("MaxCapSizeOptions")]
    public int textureMaxCapSize = 4096;

    // ── UI 贴图额外参数 ──

    [FoldoutGroup("贴图导入规则/UI 贴图额外参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("UI 贴图关键词")]
    public string textureUiKeywords = "/ui/,/sprite/,/sprites/,/icon/,/icons/";

    [FoldoutGroup("贴图导入规则/UI 贴图额外参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("UI 纹理类型")]
    public TextureImporterType textureUiType = TextureImporterType.Sprite;

    [FoldoutGroup("贴图导入规则/UI 贴图额外参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("UI Sprite 模式")]
    public SpriteImportMode textureUiSpriteMode = SpriteImportMode.Single;

    [FoldoutGroup("贴图导入规则/UI 贴图额外参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("UI Mipmap")]
    public bool textureUiMipmapEnabled;

    [FoldoutGroup("贴图导入规则/UI 贴图额外参数")]
    [ShowIf("enableTextureRule")]
    [LabelText("UI Wrap Mode")]
    public TextureWrapMode textureUiWrapMode = TextureWrapMode.Clamp;

    // ══════════════════════════════════════════════════════════
    //  辅助方法
    // ══════════════════════════════════════════════════════════

    /// <summary>获取规范化的文件夹路径</summary>
    public string NormalizedFolderPath
    {
        get
        {
            string p = folderPath.Replace('\\', '/').TrimEnd('/');
            if (!p.StartsWith("Assets")) p = "Assets/" + p.TrimStart('/');
            return p;
        }
    }

    /// <summary>判断某个资源路径是否属于此规则的管辖范围</summary>
    public bool IsInScope(string assetPath)
    {
        if (!enabled) return false;
        string folder = NormalizedFolderPath;
        if (recursive)
            return assetPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assetPath, folder, StringComparison.OrdinalIgnoreCase);
        else
        {
            // 非递归：仅匹配该文件夹下的直接子文件
            string dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            return string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>判断资源是否在忽略列表中（支持单个资源和文件夹忽略）</summary>
    public bool IsIgnored(string assetPath)
    {
        if (ignoredAssets == null || ignoredAssets.Count == 0) return false;

        foreach (var obj in ignoredAssets)
        {
            if (obj == null) continue;
            string objPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(objPath)) continue;

            // 文件夹：忽略其下所有资源
            if (AssetDatabase.IsValidFolder(objPath))
            {
                if (assetPath.StartsWith(objPath + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            // 单个资源：精确匹配
            else if (string.Equals(objPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>判断扩展名是否在目标列表中</summary>
    public bool IsTargetExtension(string assetPath, string targetExtensions)
    {
        if (string.IsNullOrWhiteSpace(targetExtensions)) return true;
        string ext = System.IO.Path.GetExtension(assetPath).ToLower();
        foreach (var e in targetExtensions.Split(','))
        {
            string t = e.Trim().ToLower();
            if (!string.IsNullOrEmpty(t) && ext == t) return true;
        }
        return false;
    }

    /// <summary>判断文件是否应忽略命名检查</summary>
    public bool IsNamingIgnored(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(namingIgnoreExtensions)) return false;
        string ext = System.IO.Path.GetExtension(assetPath).ToLower();
        foreach (var e in namingIgnoreExtensions.Split(','))
        {
            string t = e.Trim().ToLower();
            if (!string.IsNullOrEmpty(t) && ext == t) return true;
        }
        return false;
    }

    /// <summary>根据模板生成 Addressable 名称</summary>
    public string ResolveAddressableName(string assetPath)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
        string relPath = assetPath;
        // 相对于规则文件夹的路径
        string ruleFolder = NormalizedFolderPath;
        if (relPath.StartsWith(ruleFolder + "/"))
            relPath = relPath.Substring(ruleFolder.Length + 1);

        return addressableNameTemplate
            .Replace("{name}", name)
            .Replace("{folder}", folder ?? "")
            .Replace("{path}", relPath);
    }

    /// <summary>获取 Addressable 标签列表</summary>
    public List<string> GetAddressableLabels()
    {
        var labels = new List<string>();
        if (string.IsNullOrWhiteSpace(addressableLabels)) return labels;
        foreach (var l in addressableLabels.Split(','))
        {
            string t = l.Trim();
            if (!string.IsNullOrEmpty(t)) labels.Add(t);
        }
        return labels;
    }

    /// <summary>获取命名忽略扩展名列表</summary>
    public List<string> GetNamingIgnoreExtensions()
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(namingIgnoreExtensions)) return list;
        foreach (var e in namingIgnoreExtensions.Split(','))
        {
            string t = e.Trim().ToLower();
            if (!string.IsNullOrEmpty(t)) list.Add(t);
        }
        return list;
    }

    /// <summary>判断贴图路径是否为 UI 贴图（路径包含任一 UI 关键词）</summary>
    public bool IsUiTexture(string assetPath)
    {
        if (string.IsNullOrEmpty(textureUiKeywords)) return false;
        string path = assetPath.Replace('\\', '/').ToLowerInvariant();
        foreach (var kw in textureUiKeywords.Split(','))
        {
            string trimmed = kw.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(trimmed) && path.Contains(trimmed))
                return true;
        }
        return false;
    }

    /// <summary>根据原图尺寸自动分档推荐 MaxSize（与 TextureImportAutoTool 一致）</summary>
    public int GetRecommendedMaxSize(TextureImporter importer)
    {
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        int longest = Mathf.Max(width, height);
        if (longest <= 256) return 256;
        if (longest <= 512) return 512;
        if (longest <= 1024) return 1024;
        if (longest <= 2048) return 2048;
        return textureMaxCapSize;
    }

    /// <summary>Odin ValueDropdown 回调 —— 最大尺寸上限选项（与 TextureImportAutoTool 一致）</summary>
    private IEnumerable<int> MaxCapSizeOptions()
    {
        return new int[] { 512, 1024, 2048, 4096, 8192 };
    }

    /// <summary>Odin ValueDropdown 回调 —— 常用命名模板</summary>
    private IEnumerable<string> TemplateNameOptions()
    {
        return new string[]
        {
            "{folder}/{name}",
            "{path}",
            "{name}",
            "{folder}/{path}",
            "assets/{folder}/{name}",
        };
    }

    /// <summary>Odin ValueDropdown 回调 —— 获取所有 Addressable 分组名</summary>
    private IEnumerable<string> GetGroupNamesOdin()
    {
        var names = new List<string> { "" };
#if ADDRESSABLES
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings != null)
        {
            foreach (var group in settings.groups)
            {
                if (group != null) names.Add(group.Name);
            }
        }
#endif
        return names;
    }
}
#endif
