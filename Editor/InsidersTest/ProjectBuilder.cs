#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
/// <summary>
/// ═══════════════════════════════════════════════════════════════
///  项目打包工具 —— 多平台一键打包 & 批量构建
/// ═══════════════════════════════════════════════════════════════
///  
///  功能：
///  - 支持 Windows / Android / iOS / WebGL / Linux / Mac 多平台
///  - 场景勾选管理，自动同步 Build Settings
///  - 打包选项（Development Build、Script Debugging、压缩方式）
///  - Player Settings 快速配置
///  - 单平台打包 + 多平台批量构建
///  - 打包日志 & 输出目录快速打开
///  
/// ═══════════════════════════════════════════════════════════════
/// </summary>
[ToolInfo("项目打包", "构建工具",
    Description = "多平台项目打包工具，支持 Windows / Android / iOS / WebGL 等平台的一键打包与批量构建。\n\n可管理构建场景、配置打包选项、快速设置 Player Settings。",
    Icon = "📦",
    Tags = new[] { "打包", "构建", "Build", "发布" },
    Shortcut = "Ctrl+Shift+B",
    Priority = 10)]
public class ProjectBuilder : OdinEditorWindow
{
    #region 平台定义
    /// <summary>支持的构建平台</summary>
    [Serializable]
    public enum BuildPlatform
    {
        [LabelText("Windows (64-bit)")]
        Windows64,
        [LabelText("Windows (32-bit)")]
        Windows32,
        [LabelText("Android")]
        Android,
        [LabelText("iOS")]
        iOS,
        [LabelText("WebGL")]
        WebGL,
        [LabelText("Linux (64-bit)")]
        Linux64,
        [LabelText("macOS")]
        MacOS,
    }

    /// <summary>获取 BuildPlatform 对应的 Unity BuildTarget</summary>
    private static BuildTarget GetBuildTarget(BuildPlatform platform)
    {
        return platform switch
        {
            BuildPlatform.Windows64 => BuildTarget.StandaloneWindows64,
            BuildPlatform.Windows32 => BuildTarget.StandaloneWindows,
            BuildPlatform.Android    => BuildTarget.Android,
            BuildPlatform.iOS        => BuildTarget.iOS,
            BuildPlatform.WebGL      => BuildTarget.WebGL,
            BuildPlatform.Linux64    => BuildTarget.StandaloneLinux64,
            BuildPlatform.MacOS      => BuildTarget.StandaloneOSX,
            _                        => BuildTarget.StandaloneWindows64,
        };
    }

    /// <summary>获取 BuildPlatform 对应的 BuildTargetGroup</summary>
    private static BuildTargetGroup GetBuildTargetGroup(BuildPlatform platform)
    {
        return platform switch
        {
            BuildPlatform.Windows64 => BuildTargetGroup.Standalone,
            BuildPlatform.Windows32 => BuildTargetGroup.Standalone,
            BuildPlatform.Android    => BuildTargetGroup.Android,
            BuildPlatform.iOS        => BuildTargetGroup.iOS,
            BuildPlatform.WebGL      => BuildTargetGroup.WebGL,
            BuildPlatform.Linux64    => BuildTargetGroup.Standalone,
            BuildPlatform.MacOS      => BuildTargetGroup.Standalone,
            _                        => BuildTargetGroup.Standalone,
        };
    }

    /// <summary>获取平台的默认输出扩展名</summary>
    private static string GetDefaultExtension(BuildPlatform platform)
    {
        return platform switch
        {
            BuildPlatform.Windows64 or BuildPlatform.Windows32 => ".exe",
            BuildPlatform.Android                               => ".apk",
            BuildPlatform.iOS                                   => "",
            BuildPlatform.WebGL                                 => "",
            BuildPlatform.Linux64                               => ".x86_64",
            BuildPlatform.MacOS                                 => ".app",
            _                                                   => ".exe",
        };
    }
    #endregion

    #region 打包配置字段

    [FoldoutGroup("平台选择", expanded: true)]
    [LabelText("目标平台")]
    [OnValueChanged(nameof(OnPlatformChanged))]
    public BuildPlatform targetPlatform = BuildPlatform.Windows64;

    [FoldoutGroup("平台选择")]
    [LabelText("批量打包平台")]
    [InfoBox("勾选多个平台后，点击「批量打包」可一次性构建所有选中平台", InfoMessageType.Info)]
    public List<BuildPlatform> batchPlatforms = new List<BuildPlatform> { BuildPlatform.Windows64 };

    [FoldoutGroup("构建场景", expanded: true)]
    [LabelText("构建场景列表")]
    [ListDrawerSettings(ShowIndexLabels = true, DraggableItems = true, HideAddButton = false)]
    [OnInspectorGUI(nameof(DrawSceneButtons))]
    [InfoBox("场景必须添加到 Build Settings 才会被打包。拖拽或添加场景资源到列表中，或点击下方按钮同步。", InfoMessageType.Info)]
    public List<SceneAsset> buildScenes = new List<SceneAsset>();

    [FoldoutGroup("打包选项", expanded: true)]
    [LabelText("Development Build")]
    [Tooltip("开启后包含调试符号和 Profiler 支持")]
    public bool developmentBuild = false;

    [FoldoutGroup("打包选项")]
    [LabelText("允许 Script Debugging")]
    [Tooltip("仅在 Development Build 开启时生效")]
    public bool allowDebugging = false;

    [FoldoutGroup("打包选项")]
    [LabelText("压缩方式")]
    public CompressionType compressionType = CompressionType.None;

    [FoldoutGroup("打包选项")]
    [LabelText("增量构建")]
    [Tooltip("开启后使用 BuildOptions.AcceptExternalModificationsToPlayer 进行增量构建，加快重复打包速度")]
    public bool incrementalBuild = false;

    [FoldoutGroup("打包选项")]
    [LabelText("构建完成后打开文件夹")]
    public bool openFolderAfterBuild = true;

    [FoldoutGroup("输出路径", expanded: true)]
    [LabelText("输出目录")]
    [FolderPath(RequireExistingPath = true)]
    public string outputPath = "Builds";

    [FoldoutGroup("输出路径")]
    [LabelText("输出文件名")]
    [Tooltip("不含扩展名，如「MermaidsFall」。留空则使用 Product Name")]
    public string outputFileName = "";

    [FoldoutGroup("Player Settings 快速配置", expanded: false)]
    [LabelText("Company Name")]
    [OnValueChanged(nameof(OnPlayerSettingChanged))]
    public string companyName = "";

    [FoldoutGroup("Player Settings 快速配置")]
    [LabelText("Product Name")]
    [OnValueChanged(nameof(OnPlayerSettingChanged))]
    public string productName = "";

    [FoldoutGroup("Player Settings 快速配置")]
    [LabelText("版本号")]
    [OnValueChanged(nameof(OnPlayerSettingChanged))]
    public string bundleVersion = "";

    [FoldoutGroup("Player Settings 快速配置")]
    [LabelText("Bundle Version Code (Android)")]
    [Tooltip("Android 内部版本号，每次提交商店需递增")]
    [ShowIf(nameof(targetPlatform), BuildPlatform.Android)]
    public int bundleVersionCode = 1;

    [FoldoutGroup("Player Settings 快速配置")]
    [LabelText("Package Name (Android)")]
    [Tooltip("如 com.company.appname")]
    [ShowIf(nameof(targetPlatform), BuildPlatform.Android)]
    public string androidPackageName = "";

    #endregion

    #region 状态字段
    [FoldoutGroup("打包日志", expanded: true, Order = 99)]
    [LabelText("构建日志")]
    [MultiLineProperty(10)]
    [ReadOnly]
    [HideLabel]
    public string buildLog = "";

    [HideInInspector]
    private bool _isBuilding;

    [HideInInspector]
    private BuildPlatform _previousPlatform;
    #endregion

    #region 菜单入口
    [MenuItem("UnityFramework/项目打包工具")]
    public static void OpenFromMenu()
    {
        GetWindow<ProjectBuilder>("项目打包工具");
    }
    #endregion

    #region 生命周期
    protected override void OnEnable()
    {
        base.OnEnable();
        // 从 Player Settings 同步当前配置
        LoadFromPlayerSettings();
        // 从 Build Settings 同步场景列表
        LoadScenesFromBuildSettings();
        // 记录当前平台
        _previousPlatform = targetPlatform;
    }

    protected override void OnDisable()
    {
        _isBuilding = false;
        base.OnDisable();
    }
    #endregion

    #region Player Settings 同步
    private void LoadFromPlayerSettings()
    {
        companyName         = PlayerSettings.companyName;
        productName         = PlayerSettings.productName;
        bundleVersion       = PlayerSettings.bundleVersion;
        androidPackageName  = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        bundleVersionCode   = PlayerSettings.Android.bundleVersionCode;
    }

    private void ApplyPlayerSettings()
    {
        if (!string.IsNullOrEmpty(companyName))
            PlayerSettings.companyName = companyName;
        if (!string.IsNullOrEmpty(productName))
            PlayerSettings.productName = productName;
        if (!string.IsNullOrEmpty(bundleVersion))
            PlayerSettings.bundleVersion = bundleVersion;
        if (!string.IsNullOrEmpty(androidPackageName))
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, androidPackageName);
        }
        PlayerSettings.Android.bundleVersionCode = bundleVersionCode;

        AssetDatabase.SaveAssets();
        AppendLog("Player Settings 已更新");
    }

    private void OnPlayerSettingChanged()
    {
        // 字段变更时自动应用（用户可控制何时调用）
    }
    #endregion

    #region 场景管理
    private void LoadScenesFromBuildSettings()
    {
        buildScenes.Clear();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
            {
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
                if (asset != null)
                    buildScenes.Add(asset);
            }
        }
    }

    /// <summary>将场景列表同步到 Build Settings</summary>
    private void SyncScenesToBuildSettings()
    {
        var validScenes = new List<EditorBuildSettingsScene>();
        foreach (var sceneAsset in buildScenes)
        {
            if (sceneAsset == null) continue;
            var path = AssetDatabase.GetAssetPath(sceneAsset);
            if (string.IsNullOrEmpty(path)) continue;
            validScenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = validScenes.ToArray();
        AssetDatabase.SaveAssets();
        AppendLog($"已同步 {validScenes.Count} 个场景到 Build Settings");
    }

    private void DrawSceneButtons()
    {
        GUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("从 Build Settings 加载", GUILayout.Height(24)))
        {
            LoadScenesFromBuildSettings();
            AppendLog("已从 Build Settings 加载场景列表");
        }
        if (GUILayout.Button("同步到 Build Settings", GUILayout.Height(24)))
        {
            SyncScenesToBuildSettings();
        }
        if (GUILayout.Button("清空列表", GUILayout.Height(24)))
        {
            buildScenes.Clear();
            AppendLog("场景列表已清空");
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4);
    }
    #endregion

    #region 平台切换
    private void OnPlatformChanged()
    {
        if (_previousPlatform != targetPlatform)
        {
            // 自动更新批量平台列表中的首个（保持默认一致）
            if (batchPlatforms.Count == 1 && batchPlatforms[0] == _previousPlatform)
            {
                batchPlatforms[0] = targetPlatform;
            }
            _previousPlatform = targetPlatform;
        }
    }
    #endregion

    #region 按钮操作

    [FoldoutGroup("操作", Order = 50)]
    [Button(ButtonSizes.Large), GUIColor(0.3f, 0.7f, 0.4f)]
    [LabelText("▶ 开始打包")]
    [EnableIf(nameof(CanBuild))]
    private void BuildForCurrentPlatform()
    {
        SyncScenesToBuildSettings();
        ApplyPlayerSettings();
        Build(targetPlatform);
    }

    [FoldoutGroup("操作")]
    [Button(ButtonSizes.Medium), GUIColor(0.3f, 0.5f, 0.8f)]
    [LabelText("批量打包")]
    [EnableIf(nameof(CanBatchBuild))]
    private void BatchBuild()
    {
        SyncScenesToBuildSettings();
        ApplyPlayerSettings();
        var platforms = batchPlatforms.Distinct().ToList();
        AppendLog($"========== 批量打包开始 ({platforms.Count} 个平台) ==========");
        foreach (var platform in platforms)
        {
            Build(platform);
        }
        AppendLog("========== 批量打包结束 ==========");
    }

    [FoldoutGroup("操作")]
    [Button(ButtonSizes.Medium)]
    [LabelText("打开输出目录")]
    private void OpenOutputFolder()
    {
        var fullPath = GetFullOutputPath(targetPlatform);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
            {
                FileName = dir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        else if (Directory.Exists(outputPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
            {
                FileName = Path.GetFullPath(outputPath),
                UseShellExecute = true,
                Verb = "open"
            });
        }
        else
        {
            EditorUtility.DisplayDialog("提示", $"输出目录不存在：{outputPath}", "确定");
        }
    }

    [FoldoutGroup("操作")]
    [Button(ButtonSizes.Medium)]
    [LabelText("清空构建日志")]
    private void ClearBuildLog()
    {
        buildLog = "";
    }

    [FoldoutGroup("操作")]
    [Button(ButtonSizes.Medium)]
    [LabelText("应用 Player Settings")]
    [GUIColor(0.5f, 0.5f, 0.7f)]
    private void ApplyPlayerSettingsButton()
    {
        ApplyPlayerSettings();
        EditorUtility.DisplayDialog("提示", "Player Settings 已更新并保存", "确定");
    }
    #endregion

    #region 验证

    private bool CanBuild()
    {
        return !_isBuilding && buildScenes.Count > 0 && buildScenes.Any(s => s != null);
    }

    private bool CanBatchBuild()
    {
        return !_isBuilding && batchPlatforms.Count > 0 && buildScenes.Count > 0 && buildScenes.Any(s => s != null);
    }
    #endregion

    #region 核心构建逻辑

    /// <summary>获取完整的输出路径（含文件名和扩展名）</summary>
    private string GetFullOutputPath(BuildPlatform platform)
    {
        var basePath = Path.GetFullPath(string.IsNullOrEmpty(outputPath) ? "Builds" : outputPath);
        // 为每个平台创建子目录
        var platformDir = Path.Combine(basePath, platform.ToString());

        var fileName = string.IsNullOrEmpty(outputFileName)
            ? PlayerSettings.productName
            : outputFileName;

        if (string.IsNullOrEmpty(fileName))
            fileName = Application.productName;

        var ext = GetDefaultExtension(platform);
        return Path.Combine(platformDir, fileName + ext);
    }

    /// <summary>执行构建</summary>
    private void Build(BuildPlatform platform)
    {
        if (_isBuilding)
        {
            AppendLog("⚠ 正在构建中，请等待当前构建完成");
            return;
        }

        _isBuilding = true;

        try
        {
            var buildTarget      = GetBuildTarget(platform);
            var buildTargetGroup = GetBuildTargetGroup(platform);
            var fullPath         = GetFullOutputPath(platform);

            // 确保输出目录存在
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 检查并切换平台
            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                AppendLog($"正在切换平台: {EditorUserBuildSettings.activeBuildTarget} → {buildTarget} ...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget))
                {
                    AppendLog($"❌ 平台切换失败: {platform}");
                    return;
                }
                AppendLog($"✅ 平台切换完成: {buildTarget}");
            }

            // 构建场景路径列表
            var scenePaths = buildScenes
                .Where(s => s != null)
                .Select(s => AssetDatabase.GetAssetPath(s))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();

            if (scenePaths.Length == 0)
            {
                AppendLog($"❌ 没有有效的构建场景，请先添加场景到列表并同步到 Build Settings");
                return;
            }

            // 构建选项
            var options = BuildOptions.None;
            if (developmentBuild)
                options |= BuildOptions.Development;
            if (allowDebugging && developmentBuild)
                options |= BuildOptions.AllowDebugging;
            if (incrementalBuild)
                options |= BuildOptions.AcceptExternalModificationsToPlayer;

            // 压缩方式（通过 BuildOptions 设置）
            switch (compressionType)
            {
                case CompressionType.Lz4:
                    options |= BuildOptions.CompressWithLz4;
                    break;
                case CompressionType.Lz4HC:
                    options |= BuildOptions.CompressWithLz4HC;
                    break;
            }

            AppendLog($"");
            AppendLog($"══════════════════════════════════════");
            AppendLog($"  平台: {platform}");
            AppendLog($"  输出: {fullPath}");
            AppendLog($"  场景数: {scenePaths.Length}");
            AppendLog($"  Development: {developmentBuild}");
            AppendLog($"  压缩: {compressionType}");
            AppendLog($"══════════════════════════════════════");

            // 执行构建
            var report = BuildPipeline.BuildPlayer(
                scenePaths,
                fullPath,
                buildTarget,
                options
            );

            // 处理构建结果
            if (report.summary.result == BuildResult.Succeeded)
            {
                var sizeMB = report.summary.totalSize / 1024f / 1024f;
                AppendLog($"✅ 构建成功！耗时 {report.summary.totalTime.TotalSeconds:F1}s，大小 {sizeMB:F1} MB");
                AppendLog($"   输出路径: {fullPath}");

                if (openFolderAfterBuild)
                {
                    var openDir = Path.GetDirectoryName(fullPath);
                    if (Directory.Exists(openDir))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = openDir,
                            UseShellExecute = true,
                            Verb = "open"
                        });
                    }
                }
            }
            else
            {
                var errors = report.summary.totalErrors;
                var warnings = report.summary.totalWarnings;
                AppendLog($"❌ 构建失败！错误: {errors}，警告: {warnings}");

                // 输出详细错误信息
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        {
                            AppendLog($"   [{msg.type}] {msg.content}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 构建异常: {ex.Message}");
            Debug.LogException(ex);
        }
        finally
        {
            _isBuilding = false;
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }
    #endregion

    #region 日志
    private void AppendLog(string msg)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        buildLog += $"[{timestamp}] {msg}\n";
        // 限制日志长度，保留最近 5000 行
        var lines = buildLog.Split('\n');
        if (lines.Length > 5000)
        {
            buildLog = string.Join("\n", lines.Skip(lines.Length - 5000));
        }
        Repaint();
    }
    #endregion
}
#endif
