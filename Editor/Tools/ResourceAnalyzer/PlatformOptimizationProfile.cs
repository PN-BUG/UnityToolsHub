#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ResourceAnalyzer
{
    /// <summary>
    /// 平台优化配置 —— 定义各平台的资源规范和推荐设置
    /// </summary>
    public static class PlatformOptimizationProfile
    {
        /// <summary>
        /// 获取平台的纹理分析规则
        /// </summary>
        public static TexturePlatformRules GetTextureRules(TargetPlatform platform)
        {
            return platform switch
            {
                TargetPlatform.PC => GetPCRules(),
                TargetPlatform.Android => GetAndroidRules(),
                TargetPlatform.WebGL => GetWebGLRules(),
                _ => GetPCRules()
            };
        }

        /// <summary>获取 BuildTarget（用于 TextureImporter 平台设置）</summary>
        public static BuildTarget GetBuildTarget(TargetPlatform platform)
        {
            return platform switch
            {
                TargetPlatform.PC => BuildTarget.StandaloneWindows64,
                TargetPlatform.Android => BuildTarget.Android,
                TargetPlatform.WebGL => BuildTarget.WebGL,
                _ => BuildTarget.StandaloneWindows64
            };
        }

        /// <summary>获取平台名称</summary>
        public static string GetPlatformName(TargetPlatform platform)
        {
            return platform switch
            {
                TargetPlatform.PC => "PC (Standalone)",
                TargetPlatform.Android => "Android",
                TargetPlatform.WebGL => "WebGL",
                _ => "Unknown"
            };
        }

        /// <summary>获取所有支持的平台列表</summary>
        public static TargetPlatform[] GetAllPlatforms()
        {
            return new[] { TargetPlatform.PC, TargetPlatform.Android, TargetPlatform.WebGL };
        }

        // ──────────────────────── PC ────────────────────────
        private static TexturePlatformRules GetPCRules()
        {
            return new TexturePlatformRules
            {
                Platform = TargetPlatform.PC,
                // 纹理尺寸
                MaxTextureSize = 4096,
                MaxUITextureSize = 2048,
                WarnAboveSize = 2048,
                // 推荐压缩格式
                DefaultFormat = TextureImporterFormat.DXT5,
                DefaultFormatNoAlpha = TextureImporterFormat.DXT1,
                NormalMapFormat = TextureImporterFormat.BC5,
                HDRFormat = TextureImporterFormat.BC6H,
                // UI 推荐
                UIFormat = TextureImporterFormat.DXT5,
                UIFormatNoAlpha = TextureImporterFormat.DXT1,
                // Mipmap 策略
                MipmapFor3D = true,
                MipmapForUI = false,
                MipmapForSkybox = false,
                // 读写策略
                DefaultReadWrite = false,
                // Streaming
                RecommendStreaming = false,
                // 内存警告阈值（MB）
                WarnMemoryMB = 8,
                ErrorMemoryMB = 32,
                // NPOT 策略
                NPOTAction = "建议修正为 2 的幂次，或使用 Scale To Nearest",
                // 推荐 Wrap Mode
                DefaultWrapMode = TextureWrapMode.Repeat,
                UIWrapMode = TextureWrapMode.Clamp,
                // 平台描述
                Description = "PC 平台支持所有主流压缩格式，显存充足，可使用较高分辨率。推荐 DXT/BC 系列压缩。"
            };
        }

        // ──────────────────────── Android ────────────────────────
        private static TexturePlatformRules GetAndroidRules()
        {
            return new TexturePlatformRules
            {
                Platform = TargetPlatform.Android,
                MaxTextureSize = 2048,
                MaxUITextureSize = 1024,
                WarnAboveSize = 1024,
                DefaultFormat = TextureImporterFormat.ETC2_RGBA8,
                DefaultFormatNoAlpha = TextureImporterFormat.ETC2_RGB4,
                NormalMapFormat = TextureImporterFormat.ETC2_RGB4,
                HDRFormat = TextureImporterFormat.RGBAHalf, // Android 不原生支持 BC6H
                UIFormat = TextureImporterFormat.ETC2_RGBA8,
                UIFormatNoAlpha = TextureImporterFormat.ETC2_RGB4,
                MipmapFor3D = true,
                MipmapForUI = false,
                MipmapForSkybox = false,
                DefaultReadWrite = false,
                RecommendStreaming = true, // 移动端推荐开启
                WarnMemoryMB = 4,
                ErrorMemoryMB = 16,
                NPOTAction = "移动端强烈建议修正为 2 的幂次，NPOT 纹理在部分设备上无法压缩",
                DefaultWrapMode = TextureWrapMode.Repeat,
                UIWrapMode = TextureWrapMode.Clamp,
                Description = "Android 平台显存有限，必须使用 ETC2/ASTC 压缩。纹理尺寸建议不超过 2048，UI 纹理不超过 1024。强烈建议开启 Streaming Mipmaps。"
            };
        }

        // ──────────────────────── WebGL ────────────────────────
        private static TexturePlatformRules GetWebGLRules()
        {
            return new TexturePlatformRules
            {
                Platform = TargetPlatform.WebGL,
                MaxTextureSize = 2048,
                MaxUITextureSize = 1024,
                WarnAboveSize = 1024,
                DefaultFormat = TextureImporterFormat.DXT5,
                DefaultFormatNoAlpha = TextureImporterFormat.DXT1,
                NormalMapFormat = TextureImporterFormat.BC5,
                HDRFormat = TextureImporterFormat.RGBAHalf,
                UIFormat = TextureImporterFormat.DXT5,
                UIFormatNoAlpha = TextureImporterFormat.DXT1,
                MipmapFor3D = true,
                MipmapForUI = false,
                MipmapForSkybox = false,
                DefaultReadWrite = false,
                RecommendStreaming = true,
                WarnMemoryMB = 4,
                ErrorMemoryMB = 16,
                NPOTAction = "WebGL 必须使用 2 的幂次纹理，NPOT 纹理将无法被压缩且可能导致渲染异常",
                DefaultWrapMode = TextureWrapMode.Repeat,
                UIWrapMode = TextureWrapMode.Clamp,
                Description = "WebGL 平台内存受限（通常 512MB-2GB），必须精简纹理。优先使用 DXT 压缩（S3TC 扩展支持广泛），严格限制纹理尺寸。"
            };
        }
    }

    /// <summary>
    /// 纹理平台规则配置
    /// </summary>
    public class TexturePlatformRules
    {
        public TargetPlatform Platform;
        // 尺寸
        public int MaxTextureSize;
        public int MaxUITextureSize;
        public int WarnAboveSize;
        // 压缩格式
        public TextureImporterFormat DefaultFormat;
        public TextureImporterFormat DefaultFormatNoAlpha;
        public TextureImporterFormat NormalMapFormat;
        public TextureImporterFormat HDRFormat;
        public TextureImporterFormat UIFormat;
        public TextureImporterFormat UIFormatNoAlpha;
        // Mipmap
        public bool MipmapFor3D;
        public bool MipmapForUI;
        public bool MipmapForSkybox;
        // 读写
        public bool DefaultReadWrite;
        // Streaming
        public bool RecommendStreaming;
        // 内存阈值
        public float WarnMemoryMB;
        public float ErrorMemoryMB;
        // NPOT
        public string NPOTAction;
        // Wrap
        public TextureWrapMode DefaultWrapMode;
        public TextureWrapMode UIWrapMode;
        // 描述
        public string Description;
    }
}
#endif
