using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity特殊路径枚举
/// </summary>
public enum UnitySpecialPath
{
    ProjectRoot,        // 项目根目录
    AssetsFolder,       // Assets文件夹
    ProjectSettings,    // ProjectSettings文件夹
    LibraryFolder,      // Library文件夹
    TempFolder,         // Temp文件夹
    BuildsFolder,       // Builds文件夹
    EditorLog,          // 编辑器日志文件
    PlayerLog,          // 玩家日志文件
    PersistentData,     // 持久化数据路径
    StreamingAssets,    // StreamingAssets路径
    TemporaryCache      // 临时缓存路径
}

/// <summary>
/// Unity特殊路径工具类
/// </summary>
public static class UnityPathUtility
{
    /// <summary>
    /// 打开Unity特殊文件夹
    /// </summary>
    /// <param name="pathType">路径类型</param>
    public static void OpenSpecialFolder(UnitySpecialPath pathType)
    {
        string path = GetSpecialFolderPath(pathType);

        if (string.IsNullOrEmpty(path))
        {
            UnityEngine.Debug.LogWarning($"无法找到路径: {pathType}");
            return;
        }

        if (!Directory.Exists(path) && !File.Exists(path))
        {
            UnityEngine.Debug.LogWarning($"路径不存在: {path}");
            return;
        }

        try
        {
            // 在Windows资源管理器中打开
            Process.Start(new ProcessStartInfo()
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"打开路径失败: {e.Message}");
        }
    }

    /// <summary>
    /// 获取特殊文件夹路径
    /// </summary>
    /// <param name="pathType">路径类型</param>
    /// <returns>完整路径字符串</returns>
    public static string GetSpecialFolderPath(UnitySpecialPath pathType)
    {
        switch (pathType)
        {
            case UnitySpecialPath.ProjectRoot:
                return Application.dataPath.Replace("/Assets", "");

            case UnitySpecialPath.AssetsFolder:
                return Application.dataPath;

            case UnitySpecialPath.ProjectSettings:
                return Path.Combine(Application.dataPath, "../ProjectSettings");

            case UnitySpecialPath.LibraryFolder:
                return Path.Combine(Application.dataPath, "../Library");

            case UnitySpecialPath.TempFolder:
                return Path.Combine(Application.dataPath, "../Temp");

            case UnitySpecialPath.BuildsFolder:
                return Path.Combine(Application.dataPath, "../Builds");

            case UnitySpecialPath.EditorLog:
                return GetEditorLogPath();

            case UnitySpecialPath.PlayerLog:
                return GetPlayerLogPath();

            case UnitySpecialPath.PersistentData:
                return Application.persistentDataPath;

            case UnitySpecialPath.StreamingAssets:
                return Application.streamingAssetsPath;

            case UnitySpecialPath.TemporaryCache:
                return Application.temporaryCachePath;

            default:
                throw new ArgumentOutOfRangeException(nameof(pathType), pathType, null);
        }
    }

    private static string GetEditorLogPath()
    {
        // Windows
        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Unity", "Editor", "Editor.log");
        }
        // macOS
        else if (Application.platform == RuntimePlatform.OSXEditor)
        {
            return Path.Combine(Environment.GetEnvironmentVariable("HOME"),
                "Library", "Logs", "Unity", "Editor.log");
        }
        // Linux
        else if (Application.platform == RuntimePlatform.LinuxEditor)
        {
            return Path.Combine(Environment.GetEnvironmentVariable("HOME"),
                ".config", "unity3d", "Editor.log");
        }

        return string.Empty;
    }

    private static string GetPlayerLogPath()
    {
        // Windows
        if (Application.platform == RuntimePlatform.WindowsPlayer ||
            Application.platform == RuntimePlatform.WindowsEditor)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Application.companyName, Application.productName, "Player.log");
        }
        // macOS
        else if (Application.platform == RuntimePlatform.OSXPlayer ||
                 Application.platform == RuntimePlatform.OSXEditor)
        {
            return Path.Combine(Environment.GetEnvironmentVariable("HOME"),
                "Library", "Logs", Application.companyName, Application.productName, "Player.log");
        }
        // Linux
        else if (Application.platform == RuntimePlatform.LinuxPlayer ||
                 Application.platform == RuntimePlatform.LinuxEditor)
        {
            return Path.Combine(Environment.GetEnvironmentVariable("HOME"),
                ".config", "unity3d", Application.companyName, Application.productName, "Player.log");
        }

        return string.Empty;
    }

    /// <summary>
    /// 在Unity编辑器中创建菜单项
    /// </summary>
    [MenuItem("UnityToolsHub/Open Special Folder/Project Root")]
    private static void MenuOpenProjectRoot() => OpenSpecialFolder(UnitySpecialPath.ProjectRoot);

    [MenuItem("UnityToolsHub/Open Special Folder/Assets Folder")]
    private static void MenuOpenAssetsFolder() => OpenSpecialFolder(UnitySpecialPath.AssetsFolder);

    [MenuItem("UnityToolsHub/Open Special Folder/Project Settings")]
    private static void MenuOpenProjectSettings() => OpenSpecialFolder(UnitySpecialPath.ProjectSettings);

    [MenuItem("UnityToolsHub/Open Special Folder/Library Folder")]
    private static void MenuOpenLibraryFolder() => OpenSpecialFolder(UnitySpecialPath.LibraryFolder);

    [MenuItem("UnityToolsHub/Open Special Folder/Temp Folder")]
    private static void MenuOpenTempFolder() => OpenSpecialFolder(UnitySpecialPath.TempFolder);

    [MenuItem("UnityToolsHub/Open Special Folder/Builds Folder")]
    private static void MenuOpenBuildsFolder() => OpenSpecialFolder(UnitySpecialPath.BuildsFolder);

    [MenuItem("UnityToolsHub/Open Special Folder/Editor Log")]
    private static void MenuOpenEditorLog() => OpenSpecialFolder(UnitySpecialPath.EditorLog);

    [MenuItem("UnityToolsHub/Open Special Folder/Player Log")]
    private static void MenuOpenPlayerLog() => OpenSpecialFolder(UnitySpecialPath.PlayerLog);

    [MenuItem("UnityToolsHub/Open Special Folder/Persistent Data")]
    private static void MenuOpenPersistentData() => OpenSpecialFolder(UnitySpecialPath.PersistentData);

    [MenuItem("UnityToolsHub/Open Special Folder/Streaming Assets")]
    private static void MenuOpenStreamingAssets() => OpenSpecialFolder(UnitySpecialPath.StreamingAssets);

    [MenuItem("UnityToolsHub/Open Special Folder/Temporary Cache")]
    private static void MenuOpenTemporaryCache() => OpenSpecialFolder(UnitySpecialPath.TemporaryCache);
}
