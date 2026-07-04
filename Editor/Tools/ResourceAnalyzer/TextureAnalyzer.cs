#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ResourceAnalyzer
{
    /// <summary>
    /// 纹理资源分析器 —— 分析图片资源的尺寸、压缩、Mipmap 等设置，给出优化建议
    /// </summary>
    public class TextureAnalyzer : IResourceAnalyzer
    {
        public string AnalyzerName => "纹理分析器";
        public string Description => "分析纹理资源的尺寸、压缩格式、Mipmap、读写等设置，针对不同平台给出优化建议";
        public string[] AssetSearchFilters => new[] { "t:Texture2D", "t:Sprite" };

        // UI 资源常见路径关键词
        private static readonly string[] UIPathKeywords =
        {
            "ui", "gui", "icon", "sprite", "atlas", "hud", "menu", "panel", "button", "image"
        };

        // Normal Map 常见命名关键词
        private static readonly string[] NormalMapKeywords =
        {
            "_normal", "_nrm", "_n", "normalmap", "bump"
        };

        /// <summary>
        /// 批量分析纹理资源
        /// </summary>
        public List<ResourceAnalysisResult> Analyze(List<string> assetPaths, TargetPlatform platform)
        {
            var results = new List<ResourceAnalysisResult>();
            var rules = PlatformOptimizationProfile.GetTextureRules(platform);

            int total = assetPaths.Count;
            for (int i = 0; i < total; i++)
            {
                string path = assetPaths[i];

                // 进度条
                if (i % 50 == 0 || i == total - 1)
                {
                    EditorUtility.DisplayProgressBar(
                        $"纹理分析 ({PlatformOptimizationProfile.GetPlatformName(platform)})",
                        $"分析中: {Path.GetFileName(path)} ({i + 1}/{total})",
                        (float)(i + 1) / total
                    );
                }

                var result = AnalyzeSingleTexture(path, rules);
                if (result != null)
                {
                    results.Add(result);
                }
            }

            EditorUtility.ClearProgressBar();
            return results;
        }

        /// <summary>
        /// 分析单张纹理
        /// </summary>
        private ResourceAnalysisResult AnalyzeSingleTexture(string assetPath, TexturePlatformRules rules)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return null;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) return null;

            // 判断是否为 UI 资源
            bool isUI = IsUIAsset(assetPath);
            // 判断是否为 Normal Map
            bool isNormalMap = importer.textureType == TextureImporterType.NormalMap
                               || HasNormalMapKeyword(assetPath);
            // 判断是否为 HDR
            bool isHDR = importer.textureType == TextureImporterType.Lightmap
                         || importer.textureType == TextureImporterType.GUI
                         || IsHDRFormat(importer);

            // 获取当前格式信息
            var buildTarget = PlatformOptimizationProfile.GetBuildTarget(rules.Platform);
            TextureImporterPlatformSettings platformSettings = importer.GetPlatformTextureSettings(buildTarget.ToString());

            string currentFormat = "Default";
            int currentMaxSize = importer.maxTextureSize;

            if (platformSettings != null && platformSettings.overridden)
            {
                currentFormat = platformSettings.format.ToString();
                currentMaxSize = platformSettings.maxTextureSize;
            }

            // 计算内存估算
            long memEstimate = EstimateMemorySize(tex.width, tex.height, tex.mipmapCount > 1, isHDR, hasAlpha: importer.DoesSourceTextureHaveAlpha());

            // 文件磁盘大小
            long diskSize = 0;
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", assetPath);
                if (File.Exists(fullPath))
                    diskSize = new FileInfo(fullPath).Length;
            }
            catch { /* 忽略文件读取异常 */ }

            // 构建结果
            var result = new ResourceAnalysisResult
            {
                AssetPath = assetPath,
                AssetName = Path.GetFileName(assetPath),
                ResourceType = "Texture",
                CurrentMemoryBytes = memEstimate,
                CurrentDiskBytes = diskSize,
                Width = tex.width,
                Height = tex.height,
                HasMipmap = importer.mipmapEnabled,
                CurrentFormat = currentFormat,
                CurrentMaxSize = currentMaxSize,
                IsNPOT = !IsPowerOfTwo(tex.width) || !IsPowerOfTwo(tex.height),
                HasAlpha = importer.DoesSourceTextureHaveAlpha(),
                HasTransparentAlpha = HasTransparentAlpha(importer),
                IsNormalMap = isNormalMap,
                IsUIAsset = isUI,
                IsHDR = isHDR,
                IsReadWriteEnabled = importer.isReadable,
                IsStreamingMipmaps = importer.streamingMipmaps,
                WrapMode = importer.wrapMode.ToString(),
                FilterMode = importer.filterMode.ToString(),
                TextureImporter = importer
            };

            // 推荐格式
            result.RecommendedFormat = GetRecommendedFormat(result, rules);
            result.RecommendedMaxSize = GetRecommendedMaxSize(result, rules);

            // 生成优化建议
            AnalyzeSuggestions(result, rules);

            return result;
        }

        // ══════════════════════════════════════════════════════
        //  建议生成
        // ══════════════════════════════════════════════════════

        private void AnalyzeSuggestions(ResourceAnalysisResult result, TexturePlatformRules rules)
        {
            // 1. 尺寸过大检查
            int sizeLimit = result.IsUIAsset ? rules.MaxUITextureSize : rules.MaxTextureSize;
            if (result.CurrentMaxSize > sizeLimit)
            {
                long saving = EstimateSizeSaving(result.Width, result.Height, sizeLimit, result.HasMipmap, result.HasAlpha, result.IsHDR);
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"纹理尺寸 {result.CurrentMaxSize} 超过 {PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 推荐上限 {sizeLimit}",
                    IssueSeverity.Warning,
                    $"建议将 MaxSize 降至 {sizeLimit}，{PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 平台{(result.IsUIAsset ? "UI 纹理" : "纹理")}推荐上限为 {sizeLimit}",
                    saving,
                    canAutoFix: true
                ));
            }

            // 2. 超大纹理警告
            if (result.CurrentMaxSize > rules.WarnAboveSize && result.CurrentMaxSize <= sizeLimit)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"纹理尺寸 {result.CurrentMaxSize} 较大",
                    IssueSeverity.Info,
                    $"当前尺寸在可接受范围内，但如无特殊需求建议使用 {rules.WarnAboveSize} 以下",
                    0,
                    canAutoFix: false
                ));
            }

            // 3. NPOT 检查
            if (result.IsNPOT)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    "非 2 的幂次纹理 (NPOT)",
                    result.IsUIAsset ? IssueSeverity.Warning : IssueSeverity.Error,
                    rules.NPOTAction,
                    0,
                    canAutoFix: false
                ));
            }

            // 4. Mipmap 检查
            if (result.HasMipmap && result.IsUIAsset)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    "UI 纹理不应开启 Mipmap",
                    IssueSeverity.Warning,
                    "UI 元素始终以原始分辨率渲染，Mipmap 会额外占用 33% 内存且无任何视觉收益。建议关闭 Mipmap。",
                    (long)(result.CurrentMemoryBytes * 0.33f),
                    canAutoFix: true
                ));
            }

            if (!result.HasMipmap && !result.IsUIAsset && !result.IsNormalMap)
            {
                // 3D 场景中的非 UI 纹理应该开 Mipmap
                result.Suggestions.Add(new OptimizationSuggestion(
                    "3D 纹理未开启 Mipmap",
                    IssueSeverity.Info,
                    "用于 3D 物体的纹理开启 Mipmap 可以减少远处渲染时的锯齿和带宽消耗（但会增加 33% 内存）。UI 和 Normal Map 可忽略。",
                    0,
                    canAutoFix: false
                ));
            }

            // 5. Read/Write 检查
            if (result.IsReadWriteEnabled)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    "Read/Write 已开启",
                    IssueSeverity.Warning,
                    "Read/Write Enabled 会导致纹理内存翻倍（CPU 和 GPU 各一份副本）。如果不需要在运行时通过脚本读写像素数据，建议关闭。",
                    result.CurrentMemoryBytes,
                    canAutoFix: true
                ));
            }

            // 6. Streaming Mipmaps 检查（移动端）
            if (rules.RecommendStreaming && !result.IsStreamingMipmaps && !result.IsUIAsset)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    "建议开启 Streaming Mipmaps",
                    IssueSeverity.Info,
                    $"{PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 平台显存有限，开启 Streaming Mipmaps 可按需加载 Mipmap 层级，减少实际显存占用。",
                    0,
                    canAutoFix: true
                ));
            }

            // 7. 压缩格式检查
            string recommended = result.RecommendedFormat;
            if (!string.IsNullOrEmpty(recommended) && result.CurrentFormat != recommended && result.CurrentFormat != "Default")
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"压缩格式不匹配: 当前 {result.CurrentFormat}，推荐 {recommended}",
                    IssueSeverity.Warning,
                    $"建议在 {PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 平台使用 {recommended} 格式以获得更好的压缩比和兼容性。",
                    (long)(result.CurrentMemoryBytes * 0.3f),
                    canAutoFix: true
                ));
            }

            // 8. 内存大小警告
            float memMB = result.CurrentMemoryBytes / (1024f * 1024f);
            if (memMB > rules.ErrorMemoryMB)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"内存占用过大: {memMB:F1}MB",
                    IssueSeverity.Error,
                    $"单张纹理内存超过 {rules.ErrorMemoryMB}MB，严重影响 {PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 平台性能。请大幅降低分辨率或使用更高压缩比的格式。",
                    (long)(result.CurrentMemoryBytes * 0.5f),
                    canAutoFix: false
                ));
            }
            else if (memMB > rules.WarnMemoryMB)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"内存占用较高: {memMB:F1}MB",
                    IssueSeverity.Warning,
                    $"单张纹理内存超过 {rules.WarnMemoryMB}MB 建议阈值，考虑降低分辨率或优化压缩格式。",
                    (long)(result.CurrentMemoryBytes * 0.25f),
                    canAutoFix: false
                ));
            }

            // 9. Alpha 通道检查
            if (result.HasAlpha && !result.HasTransparentAlpha && !result.IsNormalMap)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    "纹理有 Alpha 通道但无透明像素",
                    IssueSeverity.Info,
                    "纹理包含 Alpha 通道但所有像素均为不透明。如果不需透明度，可移除 Alpha 通道以减小体积（使用不含 Alpha 的压缩格式）。",
                    (long)(result.CurrentMemoryBytes * 0.25f),
                    canAutoFix: false
                ));
            }

            // 10. Wrap Mode 检查
            if (result.IsUIAsset && result.WrapMode != "Clamp")
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    "UI 纹理 Wrap Mode 不是 Clamp",
                    IssueSeverity.Info,
                    "UI 元素通常不需要 Repeat 模式，使用 Clamp 可避免边缘接缝问题。",
                    0,
                    canAutoFix: true
                ));
            }
        }

        // ══════════════════════════════════════════════════════
        //  自动修复
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 应用优化建议（自动修复）
        /// </summary>
        public bool ApplyOptimization(ResourceAnalysisResult result, TargetPlatform platform)
        {
            if (result.TextureImporter == null) return false;

            var rules = PlatformOptimizationProfile.GetTextureRules(platform);
            var importer = result.TextureImporter;
            bool changed = false;

            // 获取或创建平台设置
            string platformName = PlatformOptimizationProfile.GetBuildTarget(platform).ToString();
            var platformSettings = importer.GetPlatformTextureSettings(platformName);
            if (platformSettings == null)
            {
                platformSettings = new TextureImporterPlatformSettings
                {
                    name = platformName,
                    overridden = true
                };
            }

            // 1. 修复 MaxSize
            int recommendedMaxSize = result.RecommendedMaxSize;
            if (recommendedMaxSize > 0 && platformSettings.maxTextureSize != recommendedMaxSize)
            {
                platformSettings.maxTextureSize = recommendedMaxSize;
                changed = true;
            }

            // 2. 修复压缩格式
            string recommendedFormat = result.RecommendedFormat;
            if (!string.IsNullOrEmpty(recommendedFormat) && Enum.TryParse<TextureImporterFormat>(recommendedFormat, out var fmt))
            {
                platformSettings.format = fmt;
                platformSettings.overridden = true;
                changed = true;
            }

            // 3. 关闭 UI 的 Mipmap
            if (result.IsUIAsset && importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            // 4. 关闭 Read/Write
            if (result.IsReadWriteEnabled)
            {
                importer.isReadable = false;
                changed = true;
            }

            // 5. 开启 Streaming Mipmaps（移动端非 UI 纹理）
            if (rules.RecommendStreaming && !result.IsStreamingMipmaps && !result.IsUIAsset)
            {
                importer.streamingMipmaps = true;
                changed = true;
            }

            // 6. 修复 Wrap Mode
            if (result.IsUIAsset && importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                changed = true;
            }

            if (changed)
            {
                importer.SetPlatformTextureSettings(platformSettings);
                importer.SaveAndReimport();
                Debug.Log($"[ResourceAnalyzer] 已优化: {result.AssetName} ({PlatformOptimizationProfile.GetPlatformName(platform)})");
            }

            return changed;
        }

        // ══════════════════════════════════════════════════════
        //  辅助方法
        // ══════════════════════════════════════════════════════

        private string GetRecommendedFormat(ResourceAnalysisResult result, TexturePlatformRules rules)
        {
            if (result.IsNormalMap) return rules.NormalMapFormat.ToString();
            if (result.IsHDR) return rules.HDRFormat.ToString();
            if (result.IsUIAsset)
                return result.HasAlpha ? rules.UIFormat.ToString() : rules.UIFormatNoAlpha.ToString();
            return result.HasAlpha ? rules.DefaultFormat.ToString() : rules.DefaultFormatNoAlpha.ToString();
        }

        private int GetRecommendedMaxSize(ResourceAnalysisResult result, TexturePlatformRules rules)
        {
            // 如果纹理实际尺寸小于推荐上限，保持不变
            int limit = result.IsUIAsset ? rules.MaxUITextureSize : rules.MaxTextureSize;
            if (result.CurrentMaxSize <= limit) return result.CurrentMaxSize;

            // 找到最接近的合法值
            int[] validSizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
            int best = limit;
            foreach (int size in validSizes)
            {
                if (size >= result.Width && size <= limit)
                {
                    best = size;
                    break;
                }
            }
            return Math.Min(best, limit);
        }

        private bool IsUIAsset(string assetPath)
        {
            string lower = assetPath.ToLowerInvariant();
            foreach (string keyword in UIPathKeywords)
            {
                if (lower.Contains(keyword)) return true;
            }
            return false;
        }

        private bool HasNormalMapKeyword(string assetPath)
        {
            string lower = assetPath.ToLowerInvariant();
            foreach (string keyword in NormalMapKeywords)
            {
                if (lower.Contains(keyword)) return true;
            }
            return false;
        }

        private bool IsHDRFormat(TextureImporter importer)
        {
            // 通过 TextureImporter 判断是否 HDR
            return importer.textureType == TextureImporterType.Lightmap
                || importer.textureType == TextureImporterType.SingleChannel;
        }

        private bool HasTransparentAlpha(TextureImporter importer)
        {
            // 通过 alphaSource 判断
            return importer.alphaSource == TextureImporterAlphaSource.FromInput
                && importer.DoesSourceTextureHaveAlpha();
        }

        private bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        /// <summary>估算纹理内存大小</summary>
        private long EstimateMemorySize(int width, int height, bool hasMipmap, bool isHDR, bool hasAlpha)
        {
            // 简化估算：每像素字节数
            int bpp = isHDR ? 8 : (hasAlpha ? 4 : 3);
            long size = (long)width * height * bpp;
            if (hasMipmap) size = (long)(size * 1.33f); // Mipmap 额外 33%
            return size;
        }

        /// <summary>估算降低 MaxSize 后的内存节省</summary>
        private long EstimateSizeSaving(int currentWidth, int currentHeight, int newMaxSize, bool hasMipmap, bool hasAlpha, bool isHDR)
        {
            int newWidth = Mathf.Min(currentWidth, newMaxSize);
            int newHeight = Mathf.Min(currentHeight, newMaxSize);
            long currentMem = EstimateMemorySize(currentWidth, currentHeight, hasMipmap, isHDR, hasAlpha);
            long newMem = EstimateMemorySize(newWidth, newHeight, hasMipmap, isHDR, hasAlpha);
            return Math.Max(0, currentMem - newMem);
        }
    }
}
#endif
