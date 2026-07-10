// ═══════════════════════════════════════════════════════════════
//  第三方插件自动检测器
//  扫描项目中是否存在指定第三方 DLL，自动添加/移除对应宏定义。
//
//  独立程序集：不依赖任何外部类型，即使主程序集编译失败也能运行。
//  支持通过 PluginDefinition 配置新增插件检测规则。
// ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 第三方插件自动检测器
/// 启动时扫描项目，根据 DLL 是否存在自动管理预编译宏定义。
/// </summary>
[InitializeOnLoad]
public static class PluginAutoDetector
{
    #region 插件定义

    /// <summary>单个插件的检测配置</summary>
    public class PluginDefinition
    {
        /// <summary>预编译宏名称</summary>
        public string DefineSymbol;
        /// <summary>DLL 文件名（用于 AssetDatabase 搜索）</summary>
        public string MarkerDll;
        /// <summary>DLL 资产类型搜索关键词</summary>
        public string SearchKeyword;
        /// <summary>额外搜索目录（兜底用）</summary>
        public string[] FallbackDirectories;
        /// <summary>检测到时的日志消息</summary>
        public string FoundMessage;
        /// <summary>未检测到时的日志消息</summary>
        public string NotFoundMessage;
    }

    /// <summary>
    /// 已注册的插件检测列表。
    /// 可在静态构造函数之前通过 AddPlugin() 添加自定义规则。
    /// </summary>
    private static readonly List<PluginDefinition> Plugins = new()
    {
        new PluginDefinition
        {
            DefineSymbol       = "ODIN_INSPECTOR",
            MarkerDll          = "Sirenix.OdinInspector.Attributes.dll",
            SearchKeyword      = "Sirenix.OdinInspector.Attributes t:DLL",
            FallbackDirectories = new[] { "Assets/Plugins/Sirenix", "Assets/Sirenix", "Assets/ThirdParty/Sirenix" },
            FoundMessage       = "检测到 Odin Inspector，已自动添加宏定义: ODIN_INSPECTOR",
            NotFoundMessage    = "未检测到 Odin Inspector，已自动移除宏定义: ODIN_INSPECTOR",
        },
    };

    /// <summary>
    /// 添加自定义插件检测规则（在 [InitializeOnLoad] 之前调用）。
    /// </summary>
    public static void AddPlugin(PluginDefinition definition)
    {
        if (definition != null && !Plugins.Contains(definition))
            Plugins.Add(definition);
    }

    #endregion

    #region 初始化

    static PluginAutoDetector()
    {
        // 延迟一帧执行，确保 Asset 数据库已就绪
        EditorApplication.delayCall += DetectAllPlugins;
    }

    /// <summary>
    /// 检测所有已注册的插件，自动设置宏定义。
    /// </summary>
    private static void DetectAllPlugins()
    {
        var buildTarget = EditorUserBuildSettings.activeBuildTarget;
        var targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
        var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup)
            .Split(';')
            .Select(d => d.Trim())
            .Where(d => !string.IsNullOrEmpty(d))
            .ToList();

        bool changed = false;

        foreach (var plugin in Plugins)
        {
            bool exists = FindPluginDll(plugin) != null;
            bool hasDefine = defines.Contains(plugin.DefineSymbol);

            if (exists && !hasDefine)
            {
                defines.Add(plugin.DefineSymbol);
                Debug.Log($"[UnityToolsHub] {plugin.FoundMessage}");
                changed = true;
            }
            else if (!exists && hasDefine)
            {
                defines.Remove(plugin.DefineSymbol);
                Debug.Log($"[UnityToolsHub] {plugin.NotFoundMessage}");
                changed = true;
            }
        }

        if (changed)
        {
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, string.Join(";", defines));
        }
    }

    #endregion

    #region DLL 查找

    /// <summary>
    /// 在项目中查找指定插件的 DLL 文件。
    /// 先通过 AssetDatabase 搜索，再兜底搜索常见目录。
    /// </summary>
    private static string FindPluginDll(PluginDefinition plugin)
    {
        // 通过 Asset 数据库查找（最快）
        string[] guids = AssetDatabase.FindAssets(plugin.SearchKeyword);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".dll"))
                return path;
        }

        // 兜底：直接搜索常见目录
        if (plugin.FallbackDirectories != null)
        {
            foreach (string dir in plugin.FallbackDirectories)
            {
                if (Directory.Exists(dir))
                {
                    string found = Directory.GetFiles(dir, plugin.MarkerDll, SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) return found;
                }
            }
        }

        return null;
    }

    #endregion

    #region 菜单操作

    [MenuItem("UnityFramework/重新检测第三方插件")]
    private static void ForceDetect()
    {
        DetectAllPlugins();
        Debug.Log("[UnityToolsHub] 第三方插件检测完成");
    }

    #endregion
}
#endif
