#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ResourceAnalyzer
{
    /// <summary>
    /// 问题严重程度
    /// </summary>
    public enum IssueSeverity
    {
        Info,       // 信息提示
        Warning,    // 警告（可优化）
        Error       // 严重问题（必须修复）
    }

    /// <summary>
    /// 单条优化建议
    /// </summary>
    [Serializable]
    public class OptimizationSuggestion
    {
        /// <summary>问题描述</summary>
        public string Description;

        /// <summary>严重程度</summary>
        public IssueSeverity Severity;

        /// <summary>优化建议详情</summary>
        public string Recommendation;

        /// <summary>预估内存节省（字节）</summary>
        public long EstimatedSavingBytes;

        /// <summary>可自动修复</summary>
        public bool CanAutoFix;

        public OptimizationSuggestion() { }

        public OptimizationSuggestion(string description, IssueSeverity severity, string recommendation, long estimatedSaving = 0, bool canAutoFix = false)
        {
            Description = description;
            Severity = severity;
            Recommendation = recommendation;
            EstimatedSavingBytes = estimatedSaving;
            CanAutoFix = canAutoFix;
        }

        /// <summary>格式化预估节省大小</summary>
        public string FormattedSaving
        {
            get
            {
                if (EstimatedSavingBytes <= 0) return "-";
                if (EstimatedSavingBytes < 1024) return $"{EstimatedSavingBytes} B";
                if (EstimatedSavingBytes < 1024 * 1024) return $"{EstimatedSavingBytes / 1024f:F1} KB";
                return $"{EstimatedSavingBytes / (1024f * 1024f):F2} MB";
            }
        }
    }

    /// <summary>
    /// 单个资源的分析结果
    /// </summary>
    [Serializable]
    public class ResourceAnalysisResult
    {
        /// <summary>资源路径（相对于 Assets/）</summary>
        public string AssetPath;

        /// <summary>资源名称</summary>
        public string AssetName;

        /// <summary>资源类型（Texture, Mesh, AudioClip ...）</summary>
        public string ResourceType;

        /// <summary>当前内存占用（字节）</summary>
        public long CurrentMemoryBytes;

        /// <summary>当前磁盘大小（字节）</summary>
        public long CurrentDiskBytes;

        /// <summary>宽度</summary>
        public int Width;

        /// <summary>高度</summary>
        public int Height;

        /// <summary>是否启用 Mipmap</summary>
        public bool HasMipmap;

        /// <summary>当前压缩格式</summary>
        public string CurrentFormat;

        /// <summary>推荐压缩格式</summary>
        public string RecommendedFormat;

        /// <summary>当前 MaxSize 设置</summary>
        public int CurrentMaxSize;

        /// <summary>推荐 MaxSize</summary>
        public int RecommendedMaxSize;

        /// <summary>是否为 NPOT（非2的幂次）</summary>
        public bool IsNPOT;

        /// <summary>Alpha 通道信息</summary>
        public bool HasAlpha;

        /// <summary>Alpha 是否为透明（非纯白/纯黑）</summary>
        public bool HasTransparentAlpha;

        /// <summary>是否为 Normal Map</summary>
        public bool IsNormalMap;

        /// <summary>是否为 UI 资源（通过路径或用途判断）</summary>
        public bool IsUIAsset;

        /// <summary>是否为 HDR 贴图</summary>
        public bool IsHDR;

        /// <summary>读写开关状态</summary>
        public bool IsReadWriteEnabled;

        /// <summary>Streaming Mipmaps 状态</summary>
        public bool IsStreamingMipmaps;

        /// <summary>Wrap Mode</summary>
        public string WrapMode;

        /// <summary>Filter Mode</summary>
        public string FilterMode;

        /// <summary>问题和优化建议列表</summary>
        public List<OptimizationSuggestion> Suggestions = new();

        /// <summary>选中状态（UI 用）</summary>
        [NonSerialized] public bool IsSelected;

        /// <summary>原始 TextureImporter 引用（用于自动修复）</summary>
        [NonSerialized] public TextureImporter TextureImporter;

        /// <summary>严重问题数量</summary>
        public int ErrorCount
        {
            get
            {
                int count = 0;
                foreach (var s in Suggestions)
                    if (s.Severity == IssueSeverity.Error) count++;
                return count;
            }
        }

        /// <summary>警告数量</summary>
        public int WarningCount
        {
            get
            {
                int count = 0;
                foreach (var s in Suggestions)
                    if (s.Severity == IssueSeverity.Warning) count++;
                return count;
            }
        }

        /// <summary>可节省总字节数</summary>
        public long TotalEstimatedSaving
        {
            get
            {
                long total = 0;
                foreach (var s in Suggestions)
                    total += s.EstimatedSavingBytes;
                return total;
            }
        }

        /// <summary>格式化当前内存大小</summary>
        public string FormattedMemory
        {
            get
            {
                if (CurrentMemoryBytes < 1024) return $"{CurrentMemoryBytes} B";
                if (CurrentMemoryBytes < 1024 * 1024) return $"{CurrentMemoryBytes / 1024f:F1} KB";
                return $"{CurrentMemoryBytes / (1024f * 1024f):F2} MB";
            }
        }

        /// <summary>格式化磁盘大小</summary>
        public string FormattedDisk
        {
            get
            {
                if (CurrentDiskBytes < 1024) return $"{CurrentDiskBytes} B";
                if (CurrentDiskBytes < 1024 * 1024) return $"{CurrentDiskBytes / 1024f:F1} KB";
                return $"{CurrentDiskBytes / (1024f * 1024f):F2} MB";
            }
        }

        /// <summary>严重程度标签</summary>
        public string SeverityLabel
        {
            get
            {
                if (ErrorCount > 0) return "严重";
                if (WarningCount > 0) return "警告";
                return "正常";
            }
        }
    }
}
#endif
