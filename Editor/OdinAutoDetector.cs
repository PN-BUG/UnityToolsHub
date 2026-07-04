// ═══════════════════════════════════════════════════════════════
//  Odin Inspector 自动检测
//  扫描项目中是否存在 Sirenix DLL，自动添加/移除 ODIN_INSPECTOR 宏定义。
//  放在 Editor 目录下，仅编辑器运行时生效。
// ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class OdinAutoDetector
{
    private const string DefineSymbol = "ODIN_INSPECTOR";
    private const string OdinMarkerDll = "Sirenix.OdinInspector.Attributes.dll";

    static OdinAutoDetector()
    {
        // 延迟一帧执行，确保 Asset 数据库已就绪
        EditorApplication.delayCall += DetectAndSetSymbol;
    }

    /// <summary>
    /// 检测项目中是否存在 Odin Inspector DLL，自动设置宏定义。
    /// </summary>
    private static void DetectAndSetSymbol()
    {
        bool odinExists = FindOdinDll() != null;

        var buildTarget = EditorUserBuildSettings.activeBuildTarget;
        var targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
        var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup)
            .Split(';')
            .Select(d => d.Trim())
            .ToList();

        bool hasDefine = defines.Contains(DefineSymbol);

        if (odinExists && !hasDefine)
        {
            defines.Add(DefineSymbol);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, string.Join(";", defines));
            Debug.Log($"[UnityToolsHub] 检测到 Odin Inspector，已自动添加宏定义: {DefineSymbol}");
        }
        else if (!odinExists && hasDefine)
        {
            defines.Remove(DefineSymbol);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, string.Join(";", defines));
            Debug.Log($"[UnityToolsHub] 未检测到 Odin Inspector，已自动移除宏定义: {DefineSymbol}");
        }
    }

    /// <summary>
    /// 在项目中查找 Odin DLL 文件。
    /// 支持常见安装路径：Assets/Plugins/Sirenix、Assets/Sirenix 等。
    /// </summary>
    private static string FindOdinDll()
    {
        // 通过 Asset 数据库查找（最快）
        string[] guids = AssetDatabase.FindAssets("Sirenix.OdinInspector.Attributes t:DLL");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".dll"))
                return path;
        }

        // 兜底：直接搜索常见目录
        string[] searchPaths = { "Assets/Plugins/Sirenix", "Assets/Sirenix", "Assets/ThirdParty/Sirenix" };
        foreach (string dir in searchPaths)
        {
            if (Directory.Exists(dir))
            {
                string found = Directory.GetFiles(dir, OdinMarkerDll, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found;
            }
        }

        return null;
    }
}
#endif
