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

            // tex.width/height 是当前编辑器平台的导入结果，不一定等于源文件尺寸。
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                sourceWidth = tex.width;
                sourceHeight = tex.height;
            }

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

            bool hasPlatformOverride = platformSettings != null && platformSettings.overridden;
            var compression = platformSettings != null
                ? platformSettings.textureCompression
                : importer.textureCompression;
            bool isUncompressed = compression == TextureImporterCompression.Uncompressed;
            bool usesAutomaticFormat = !hasPlatformOverride ||
                                       platformSettings.format.ToString().StartsWith("Automatic", StringComparison.Ordinal);
            string currentFormat = isUncompressed ? "未压缩"
                : usesAutomaticFormat ? "自动压缩"
                : platformSettings.format.ToString();
            int currentMaxSize = importer.maxTextureSize;

            if (hasPlatformOverride)
            {
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
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                HasMipmap = importer.mipmapEnabled,
                CurrentFormat = currentFormat,
                HasPlatformOverride = hasPlatformOverride,
                UsesAutomaticFormat = usesAutomaticFormat,
                IsUncompressed = isUncompressed,
                CurrentMaxSize = currentMaxSize,
                IsNPOT = !IsPowerOfTwo(sourceWidth) || !IsPowerOfTwo(sourceHeight),
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
            // 1. 按源图实际尺寸和目标平台上限推荐 Max Size。
            int sizeLimit = result.IsUIAsset ? rules.MaxUITextureSize : rules.MaxTextureSize;
            if (result.CurrentMaxSize > result.RecommendedMaxSize)
            {
                int sourceLongSide = Math.Max(result.SourceWidth, result.SourceHeight);
                bool reducesPixels = sourceLongSide > result.RecommendedMaxSize;
                long saving = reducesPixels
                    ? EstimateSizeSaving(result.SourceWidth, result.SourceHeight,
                        result.CurrentMaxSize, result.RecommendedMaxSize,
                        result.HasMipmap, result.HasAlpha, result.IsHDR)
                    : 0;
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"源图 {result.SourceWidth}×{result.SourceHeight}，当前 Max Size {result.CurrentMaxSize} 高于建议值 {result.RecommendedMaxSize}",
                    reducesPixels ? IssueSeverity.Warning : IssueSeverity.Info,
                    $"将 {PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 平台 Max Size 设为 {result.RecommendedMaxSize}"
                    + (reducesPixels
                        ? $"（平台推荐上限 {sizeLimit}，会缩小此平台导入纹理的尺寸）。"
                        : "（匹配源图尺寸；仅收紧上限，当前纹理像素不会变小，通常也不会减少包体）。"),
                    saving,
                    action: OptimizationAction.MaxSize
                ));
            }

            // 2. 超大纹理警告
            int effectiveLongSide = Math.Min(Math.Max(result.SourceWidth, result.SourceHeight), result.CurrentMaxSize);
            if (effectiveLongSide > rules.WarnAboveSize && effectiveLongSide <= sizeLimit)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"此平台纹理导入尺寸可能达到 {effectiveLongSide}，相对较大",
                    IssueSeverity.Info,
                    $"当前尺寸在可接受范围内，但如无特殊需求建议使用 {rules.WarnAboveSize} 以下",
                    0,
                    action: OptimizationAction.None
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
                    action: OptimizationAction.None
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
                    action: OptimizationAction.Mipmap
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
                    action: OptimizationAction.None
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
                    action: OptimizationAction.ReadWrite
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
                    action: OptimizationAction.StreamingMipmaps
                ));
            }

            // 7. 压缩格式检查
            int effectivePixelsLongSide = Math.Min(Math.Max(result.SourceWidth, result.SourceHeight), result.CurrentMaxSize);
            bool recommendAutoCompression = result.IsUncompressed && result.UsesAutomaticFormat && !result.IsHDR
                && effectivePixelsLongSide >= 512
                && (long)result.SourceWidth * result.SourceHeight >= 256L * 256;
            bool explicitFormatMismatch = result.HasPlatformOverride && !result.UsesAutomaticFormat
                && !result.IsUncompressed && result.CurrentFormat != result.RecommendedFormat;
            if (recommendAutoCompression)
            {
                // 保留自动格式选择，仅将目标平台的压缩方式改为 Compressed。
                result.RecommendedFormat = "自动压缩";
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"较大的纹理未压缩：{result.SourceWidth}×{result.SourceHeight}",
                    result.IsUIAsset ? IssueSeverity.Info : IssueSeverity.Warning,
                    $"建议在 {PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 平台启用自动压缩，并检查纹理显示质量。具体格式由 Unity 按目标平台选择；节省的包体字节需以构建结果为准。",
                    0,
                    action: OptimizationAction.Format
                ));
            }
            else if (result.IsUncompressed)
            {
                // 小图允许用户主动试用自动压缩；HDR 或明确指定未压缩格式仍需人工判断。
                bool canTryAutoCompression = result.UsesAutomaticFormat && !result.IsHDR;
                result.RecommendedFormat = canTryAutoCompression ? "自动压缩" : result.CurrentFormat;
                string recommendation = result.IsHDR
                    ? "HDR/高精度纹理可能需要未压缩格式；先核对用途和目标平台支持，再决定是否调整。"
                    : canTryAutoCompression
                        ? $"当前 {PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 导入上限为 {result.CurrentMaxSize}。可选择试用自动压缩，但小图/UI 边缘可能出现画质变化；应用后请目视比较，并以构建结果判断包体收益。"
                        : "当前平台明确指定了未压缩格式；先核对用途和画质需求，再决定是否改为压缩格式。";
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"纹理未压缩：源图 {result.SourceWidth}×{result.SourceHeight}，导入上限 {result.CurrentMaxSize}",
                    IssueSeverity.Info,
                    recommendation,
                    0,
                    action: OptimizationAction.Format,
                    canAutoFix: canTryAutoCompression
                ));
            }
            else if (explicitFormatMismatch)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"压缩格式不匹配: 当前 {result.CurrentFormat}，参考 {result.RecommendedFormat}",
                    IssueSeverity.Warning,
                    $"可在 {PlatformOptimizationProfile.GetPlatformName(rules.Platform)} 平台改用 {result.RecommendedFormat}；应用前请检查目标设备的兼容性与显示质量。",
                    0,
                    action: OptimizationAction.Format
                ));
            }
            else
            {
                // 自动压缩不是格式错误；小尺寸的未压缩 UI 图也不必强制创建平台覆写。
                result.RecommendedFormat = result.CurrentFormat;
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
                    action: OptimizationAction.None
                ));
            }
            else if (memMB > rules.WarnMemoryMB)
            {
                result.Suggestions.Add(new OptimizationSuggestion(
                    $"内存占用较高: {memMB:F1}MB",
                    IssueSeverity.Warning,
                    $"单张纹理内存超过 {rules.WarnMemoryMB}MB 建议阈值，考虑降低分辨率或优化压缩格式。",
                    (long)(result.CurrentMemoryBytes * 0.25f),
                    action: OptimizationAction.None
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
                    action: OptimizationAction.None
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
                    action: OptimizationAction.WrapMode
                ));
            }
        }

        // ══════════════════════════════════════════════════════
        //  自动修复
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 只应用指定的优化建议
        /// </summary>
        public bool ApplyOptimization(ResourceAnalysisResult result, TargetPlatform platform, IReadOnlyCollection<OptimizationSuggestion> suggestions)
        {
            if (result?.TextureImporter == null || suggestions == null) return false;

            var actions = new HashSet<OptimizationAction>();
            foreach (var suggestion in suggestions)
            {
                if (suggestion != null && suggestion.CanAutoFix && result.Suggestions.Contains(suggestion))
                    actions.Add(suggestion.Action);
            }
            if (actions.Count == 0) return false;

            var rules = PlatformOptimizationProfile.GetTextureRules(platform);
            var importer = result.TextureImporter;
            bool platformChanged = false;
            bool importerChanged = false;

            // 平台覆写只在选中了 MaxSize 或格式建议时写入。
            string platformName = PlatformOptimizationProfile.GetBuildTarget(platform).ToString();
            var platformSettings = importer.GetPlatformTextureSettings(platformName);
            if (platformSettings == null)
            {
                platformSettings = new TextureImporterPlatformSettings
                {
                    name = platformName
                };
            }

            Undo.RecordObject(importer, "应用资源优化建议");

            if (actions.Contains(OptimizationAction.MaxSize) && result.RecommendedMaxSize > 0 &&
                (!platformSettings.overridden || platformSettings.maxTextureSize != result.RecommendedMaxSize))
            {
                platformSettings.maxTextureSize = result.RecommendedMaxSize;
                platformChanged = true;
            }

            if (actions.Contains(OptimizationAction.Format) && result.RecommendedFormat == "自动压缩" &&
                (!platformSettings.overridden || platformSettings.textureCompression != TextureImporterCompression.Compressed))
            {
                platformSettings.textureCompression = TextureImporterCompression.Compressed;
                platformChanged = true;
            }
            else if (actions.Contains(OptimizationAction.Format) &&
                     Enum.TryParse<TextureImporterFormat>(result.RecommendedFormat, out var fmt) &&
                     (!platformSettings.overridden || platformSettings.format != fmt ||
                      (!result.IsHDR && platformSettings.textureCompression != TextureImporterCompression.Compressed)))
            {
                platformSettings.format = fmt;
                if (!result.IsHDR)
                    platformSettings.textureCompression = TextureImporterCompression.Compressed;
                platformChanged = true;
            }

            if (actions.Contains(OptimizationAction.Mipmap) && result.IsUIAsset && importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                importerChanged = true;
            }

            if (actions.Contains(OptimizationAction.ReadWrite) && importer.isReadable)
            {
                importer.isReadable = false;
                importerChanged = true;
            }

            if (actions.Contains(OptimizationAction.StreamingMipmaps) && rules.RecommendStreaming &&
                !importer.streamingMipmaps && !result.IsUIAsset)
            {
                importer.streamingMipmaps = true;
                importerChanged = true;
            }

            if (actions.Contains(OptimizationAction.WrapMode) && result.IsUIAsset &&
                importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importerChanged = true;
            }

            if (platformChanged)
            {
                platformSettings.overridden = true;
                importer.SetPlatformTextureSettings(platformSettings);
            }

            if (platformChanged || importerChanged)
            {
                importer.SaveAndReimport();
                Debug.Log($"[ResourceAnalyzer] 已优化: {result.AssetName} ({PlatformOptimizationProfile.GetPlatformName(platform)})");
            }

            return platformChanged || importerChanged;
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
            int limit = result.IsUIAsset ? rules.MaxUITextureSize : rules.MaxTextureSize;
            int sourceLongSide = Math.Max(result.SourceWidth, result.SourceHeight);
            int sourceCeiling = Mathf.Max(32, Mathf.NextPowerOfTwo(Math.Max(1, sourceLongSide)));
            // 保留已有的更低上限，不会因分析建议而放大纹理。
            return Math.Min(result.CurrentMaxSize, Math.Min(sourceCeiling, limit));
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
        private long EstimateSizeSaving(int sourceWidth, int sourceHeight, int currentMaxSize,
            int newMaxSize, bool hasMipmap, bool hasAlpha, bool isHDR)
        {
            int sourceLongSide = Math.Max(sourceWidth, sourceHeight);
            if (sourceLongSide <= 0) return 0;
            float currentScale = Mathf.Min(1f, (float)currentMaxSize / sourceLongSide);
            float newScale = Mathf.Min(1f, (float)newMaxSize / sourceLongSide);
            int currentWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * currentScale));
            int currentHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * currentScale));
            int newWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * newScale));
            int newHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * newScale));
            long currentMem = EstimateMemorySize(currentWidth, currentHeight, hasMipmap, isHDR, hasAlpha);
            long newMem = EstimateMemorySize(newWidth, newHeight, hasMipmap, isHDR, hasAlpha);
            return Math.Max(0, currentMem - newMem);
        }
    }
}
#endif
