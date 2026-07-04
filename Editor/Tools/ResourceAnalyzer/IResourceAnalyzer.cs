#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace ResourceAnalyzer
{
    /// <summary>
    /// 资源分析器接口 —— 所有资源类型分析器的基类
    /// 目前已实现: TextureAnalyzer
    /// 预留接口: MeshAnalyzer, AudioClipAnalyzer, AnimationClipAnalyzer ...
    /// </summary>
    public interface IResourceAnalyzer
    {
        /// <summary>分析器名称</summary>
        string AnalyzerName { get; }

        /// <summary>分析器描述</summary>
        string Description { get; }

        /// <summary>该分析器关注的资源类型过滤器（用于 AssetDatabase 搜索）</summary>
        string[] AssetSearchFilters { get; }

        /// <summary>
        /// 分析指定资源列表
        /// </summary>
        /// <param name="assetPaths">资源路径列表</param>
        /// <param name="platform">目标平台</param>
        /// <returns>分析结果列表</returns>
        List<ResourceAnalysisResult> Analyze(List<string> assetPaths, TargetPlatform platform);

        /// <summary>
        /// 对单个资源执行优化建议（自动修复）
        /// </summary>
        /// <param name="result">分析结果</param>
        /// <param name="platform">目标平台</param>
        /// <returns>是否成功应用优化</returns>
        bool ApplyOptimization(ResourceAnalysisResult result, TargetPlatform platform);
    }

    /// <summary>
    /// 目标平台枚举
    /// </summary>
    public enum TargetPlatform
    {
        PC,
        Android,
        WebGL
    }
}
#endif
