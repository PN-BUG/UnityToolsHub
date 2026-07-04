#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
public static class UnityFrameworkInitWindowAutoOpen
{
    static UnityFrameworkInitWindowAutoOpen()
    {
        EditorApplication.delayCall += TryOpen;
    }

    private static void TryOpen()
    {
        if (EditorPrefs.GetBool(UnityFrameworkInitWindow.DoNotShowKey, false))
        {
            return;
        }

        if (SessionState.GetBool(UnityFrameworkInitWindow.SessionOpenedKey, false))
        {
            return;
        }

        SessionState.SetBool(UnityFrameworkInitWindow.SessionOpenedKey, true);
        UnityFrameworkInitWindow.ShowWindow();
    }
}

[ToolInfo("初始化设置", "框架初始化",
    Description = "完成首次初始化：导入 AssetsTemplate、安装缺失依赖包。\n\n支持一键安装 Addressables、Localization、Post Processing、Newtonsoft Json、UniTask 等必需依赖，可自定义 AssetsTemplate 路径。",
    Icon = "⚙", Tags = new[] { "初始化", "依赖安装" })]
public class UnityFrameworkInitWindow : EditorWindow
{
    [Serializable]
    private class DependencyItem
    {
        public string packageName;
        public string displayName;
        public string installSpec;
        public bool selected = true;
    }

    public const string DoNotShowKey = "UnityFramework.InitWindow.DoNotShow";
    public const string SessionOpenedKey = "UnityFramework.InitWindow.SessionOpened";
    private const string AssetsTemplatePathKey = "UnityFramework.InitWindow.AssetsTemplatePath";
    private const string AssetsTemplateFolderPathKey = "UnityFramework.InitWindow.AssetsTemplateFolderPath";

    private static readonly List<DependencyItem> RequiredDependencies = new List<DependencyItem>
    {
        new DependencyItem { packageName = "com.unity.addressables", displayName = "Addressables", installSpec = "com.unity.addressables" },
        new DependencyItem { packageName = "com.unity.localization", displayName = "Localization", installSpec = "com.unity.localization" },
        new DependencyItem { packageName = "com.unity.postprocessing", displayName = "Post Processing", installSpec = "com.unity.postprocessing" },
        new DependencyItem { packageName = "com.unity.nuget.newtonsoft-json", displayName = "Newtonsoft Json", installSpec = "com.unity.nuget.newtonsoft-json" },
        new DependencyItem { packageName = "com.cysharp.unitask", displayName = "UniTask", installSpec = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask" }
    };

    private Vector2 scrollPos;
    private bool doNotShowAgain;
    private string assetsTemplatePath;
    private string assetsTemplateFolderPath;

    private ListRequest listRequest;
    private AddRequest addRequest;
    private readonly List<DependencyItem> missingDependencies = new List<DependencyItem>();
    private readonly Queue<DependencyItem> installQueue = new Queue<DependencyItem>();
    private string installStatus = string.Empty;

    [MenuItem("UnityToolsHub/初始化设置", false, 0)]
    public static void ShowWindow()
    {
        var window = GetWindow<UnityFrameworkInitWindow>(true, "UnityFramework 初始化设置", true);
        window.minSize = new Vector2(560, 420);
        window.Show();
    }

    private void OnEnable()
    {
        doNotShowAgain = EditorPrefs.GetBool(DoNotShowKey, false);
        assetsTemplatePath = EditorPrefs.GetString(AssetsTemplatePathKey, "AssetsTemplate.unitypackage");
        assetsTemplateFolderPath = EditorPrefs.GetString(AssetsTemplateFolderPathKey, GetDefaultAssetsTemplateFolderPath());
        RefreshDependencies();
    }

    private void OnDisable()
    {
        EditorPrefs.SetBool(DoNotShowKey, doNotShowAgain);
        EditorPrefs.SetString(AssetsTemplatePathKey, assetsTemplatePath ?? string.Empty);
        EditorPrefs.SetString(AssetsTemplateFolderPathKey, assetsTemplateFolderPath ?? string.Empty);
        EditorApplication.update -= InstallNextDependency;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("UnityFramework 初始化设置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("可在此窗口完成首次初始化：导入 AssetsTemplate、安装缺失依赖包。", UnityEditor.MessageType.Info);

        EditorGUILayout.Space(8);
        bool newDoNotShowAgain = EditorGUILayout.ToggleLeft("勾选不再显示", doNotShowAgain);
        if (newDoNotShowAgain != doNotShowAgain)
        {
            doNotShowAgain = newDoNotShowAgain;
            EditorPrefs.SetBool(DoNotShowKey, doNotShowAgain);
        }

        EditorGUILayout.Space(10);
        DrawAssetsTemplateSection();

        EditorGUILayout.Space(10);
        DrawDependenciesSection();
    }

    private void DrawAssetsTemplateSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("AssetsTemplate 导入", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            assetsTemplatePath = EditorGUILayout.TextField("模板路径", assetsTemplatePath);
            if (GUILayout.Button("选择", GUILayout.Width(70)))
            {
                string selected = EditorUtility.OpenFilePanel("选择 AssetsTemplate 包", GetProjectRootPath(), "unitypackage");
                if (!string.IsNullOrEmpty(selected))
                {
                    assetsTemplatePath = selected;
                    EditorPrefs.SetString(AssetsTemplatePathKey, assetsTemplatePath);
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("导入 AssetsTemplate", GUILayout.Height(26)))
            {
                ImportAssetsTemplate();
            }
        }

        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            assetsTemplateFolderPath = EditorGUILayout.TextField("文件夹路径", assetsTemplateFolderPath);
            if (GUILayout.Button("选择", GUILayout.Width(70)))
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("选择要复制到 Assets 的文件夹", GetProjectRootPath(), string.Empty);
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    assetsTemplateFolderPath = selectedFolder;
                    EditorPrefs.SetString(AssetsTemplateFolderPathKey, assetsTemplateFolderPath);
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("复制文件夹到 Assets", GUILayout.Height(26)))
            {
                CopyFolderToAssets();
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawDependenciesSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("安装依赖包", EditorStyles.boldLabel);

        if (listRequest != null && !listRequest.IsCompleted)
        {
            EditorGUILayout.HelpBox("正在读取当前依赖包列表...", UnityEditor.MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        if (!string.IsNullOrEmpty(installStatus))
        {
            EditorGUILayout.HelpBox(installStatus, UnityEditor.MessageType.None);
        }

        if (missingDependencies.Count == 0)
        {
            EditorGUILayout.HelpBox("未发现缺失依赖包。", UnityEditor.MessageType.Info);
            if (GUILayout.Button("刷新依赖列表", GUILayout.Height(24)))
            {
                RefreshDependencies();
            }
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.LabelField("缺失依赖包（可勾选安装）：");
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(180));
        foreach (var item in missingDependencies)
        {
            item.selected = EditorGUILayout.ToggleLeft($"{item.displayName} ({item.packageName})", item.selected);
        }
        EditorGUILayout.EndScrollView();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新依赖列表", GUILayout.Height(26)))
            {
                RefreshDependencies();
            }

            using (new EditorGUI.DisabledScope(addRequest != null || !missingDependencies.Any(d => d.selected)))
            {
                if (GUILayout.Button("安装选中依赖", GUILayout.Height(26)))
                {
                    StartInstallSelectedDependencies();
                }
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void ImportAssetsTemplate()
    {
        string fullPath = ResolveFullPath(assetsTemplatePath);
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("导入失败", "未找到 AssetsTemplate 文件，请先选择正确的 .unitypackage 文件。", "确定");
            return;
        }

        AssetDatabase.ImportPackage(fullPath, true);
    }

    private void CopyFolderToAssets()
    {
        string sourcePath = ResolveFullPath(assetsTemplateFolderPath);
        if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
        {
            EditorUtility.DisplayDialog("复制失败", "未找到指定文件夹，请先选择有效目录。", "确定");
            return;
        }

        string[] childDirectories = Directory.GetDirectories(sourcePath);
        if (childDirectories.Length == 0)
        {
            EditorUtility.DisplayDialog("复制失败", "所选目录下没有可复制的子文件夹。", "确定");
            return;
        }

        int copiedCount = 0;
        foreach (string childDirectory in childDirectories)
        {
            string folderName = Path.GetFileName(childDirectory);
            if (string.IsNullOrEmpty(folderName))
            {
                continue;
            }

            string destinationPath = Path.Combine(Application.dataPath, folderName);
            if (Directory.Exists(destinationPath))
            {
                bool overwrite = EditorUtility.DisplayDialog("目标已存在", $"Assets/{folderName} 已存在，是否覆盖？", "覆盖", "跳过");
                if (!overwrite)
                {
                    continue;
                }

                FileUtil.DeleteFileOrDirectory(destinationPath);
                string metaFile = destinationPath + ".meta";
                if (File.Exists(metaFile))
                {
                    FileUtil.DeleteFileOrDirectory(metaFile);
                }
            }

            FileUtil.CopyFileOrDirectory(childDirectory, destinationPath);
            copiedCount++;
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("复制完成", $"已复制 {copiedCount} 个子文件夹到 Assets。", "确定");
    }

    private void RefreshDependencies()
    {
        installStatus = string.Empty;
        listRequest = Client.List(true);
        EditorApplication.update -= CheckListRequest;
        EditorApplication.update += CheckListRequest;
    }

    private void CheckListRequest()
    {
        if (listRequest == null || !listRequest.IsCompleted)
        {
            return;
        }

        EditorApplication.update -= CheckListRequest;

        missingDependencies.Clear();

        if (listRequest.Status == StatusCode.Success)
        {
            var installed = new HashSet<string>(listRequest.Result.Select(p => p.name));
            foreach (var dependency in RequiredDependencies)
            {
                if (!installed.Contains(dependency.packageName))
                {
                    missingDependencies.Add(new DependencyItem
                    {
                        packageName = dependency.packageName,
                        displayName = dependency.displayName,
                        installSpec = dependency.installSpec,
                        selected = true
                    });
                }
            }
        }
        else
        {
            installStatus = $"读取依赖包失败：{listRequest.Error.message}";
        }

        Repaint();
    }

    private void StartInstallSelectedDependencies()
    {
        installQueue.Clear();
        foreach (var item in missingDependencies.Where(d => d.selected))
        {
            installQueue.Enqueue(item);
        }

        if (installQueue.Count == 0)
        {
            return;
        }

        installStatus = "开始安装依赖包...";
        EditorApplication.update -= InstallNextDependency;
        EditorApplication.update += InstallNextDependency;
    }

    private void InstallNextDependency()
    {
        if (addRequest != null)
        {
            if (!addRequest.IsCompleted)
            {
                return;
            }

            if (addRequest.Status == StatusCode.Failure)
            {
                installStatus = $"安装失败：{addRequest.Error.message}";
                addRequest = null;
                installQueue.Clear();
                EditorApplication.update -= InstallNextDependency;
                Repaint();
                return;
            }

            addRequest = null;
        }

        if (installQueue.Count == 0)
        {
            installStatus = "依赖包安装完成。";
            EditorApplication.update -= InstallNextDependency;
            RefreshDependencies();
            return;
        }

        var item = installQueue.Dequeue();
        installStatus = $"正在安装：{item.displayName} ({item.packageName})";
        addRequest = Client.Add(item.installSpec);
        Repaint();
    }

    private static string ResolveFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(GetProjectRootPath(), path));
    }

    private static string GetDefaultAssetsTemplateFolderPath()
    {
        return Path.Combine(GetProjectRootPath(), "/UnityFramework/Editor/AssetsTemplate"); 
    }

    private static string GetProjectRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
#endif
