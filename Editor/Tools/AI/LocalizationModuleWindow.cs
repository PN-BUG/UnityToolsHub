#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>Explicit installation and reflection-only access to the optional localization package.</summary>
[ToolInfo("本地化模块", "可选模块",
    Description = "按需安装或更新独立本地化包；已安装时打开本地化流水线。保留 Nodin 依赖，输入图标由输入模块适配。",
    Icon = "文", Tags = new[] { "本地化", "Localization", "模块", "UPM", "安装", "翻译" },
    Author = "zko", AuthorLink = "https://github.com/PN-BUG/UnityLocalization")]
public sealed class LocalizationModuleWindow : ToolEditorWindow
{
    public const string PackageName = "com.zko.localization";
    public const string RepositoryUrl = "https://github.com/PN-BUG/UnityLocalization";
    public const string GitReference = RepositoryUrl + ".git";
    private const string PipelineTypeName = "LocalizationPipelineWindow";
    private static AddRequest installRequest;
    private static Action<bool, string> completion;
    private static string status = "";

    protected override string ToolTitle => "本地化模块";
    protected override string ToolIcon => "文";
    public static bool IsInstalling => installRequest != null;

    [MenuItem("UnityToolsHub/可选模块/本地化")]
    public static void ShowWindow()
    {
        var window = GetWindow<LocalizationModuleWindow>("本地化模块");
        window.minSize = new Vector2(400, 360);
        window.Show();
    }

    protected override void OnToolEnable() => EditorApplication.projectChanged += Repaint;
    protected override void OnToolDisable() => EditorApplication.projectChanged -= Repaint;

    protected override void DrawToolContent()
    {
        var package = GetInstalledPackage();
        var pipeline = FindPipeline();
        DrawToolHelpBox("语言配置、文本扫描、翻译和本地化组件按需安装。打开此窗口不会安装包或修改项目。");
        DrawSection("安装状态");
        DrawBody(package != null
            ? $"已安装：{package.displayName} {package.version}（{package.source}）"
            : pipeline != null ? "检测到项目内旧版本地化脚本" : "未安装本地化模块");

        var usesProjectDirectory = package != null &&
            (package.source == PackageSource.Local || package.source == PackageSource.Embedded);
        if (usesProjectDirectory)
            DrawToolHelpBox("当前从本地或嵌入目录加载。更新请在该目录的 Git 仓库中完成；此处不会覆盖本地源码。", MessageType.Info);
        if (package == null && pipeline != null)
            DrawToolHelpBox("请先迁移项目内的旧版脚本，避免与 UPM 包产生重复类或重复 GUID。", MessageType.Warning);

        using (new EditorGUI.DisabledScope(IsInstalling || usesProjectDirectory ||
            (package == null && pipeline != null) || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (DrawFlatButton(package == null ? "安装本地化模块" : "从 Git 更新本地化模块"))
                InstallOrUpdate();
        }
        using (new EditorGUI.DisabledScope(pipeline == null))
            if (DrawFlatButton("打开本地化流水线")) OpenPipeline();

        GUILayout.Space(10);
        DrawSection("公开仓库");
        EditorGUILayout.SelectableLabel(GitReference, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(38));
        if (DrawFlatButton("打开 GitHub 仓库")) Application.OpenURL(RepositoryUrl);
        if (DrawFlatButton("复制 Git 安装地址")) EditorGUIUtility.systemCopyBuffer = GitReference;
        DrawBody("要求 Unity 2023.2 或更高版本。必需依赖：Nodin、Unity Localization、uGUI / TMP、Newtonsoft JSON。ToolsHub 不引用本地化程序集；未安装时仍可正常使用其他工具。");
        if (!string.IsNullOrEmpty(status)) DrawToolHelpBox(status);
        if (IsInstalling) Repaint();
    }

    /// <summary>Reads registered UPM packages without changing the project.</summary>
    public static PackageInfo GetInstalledPackage()
    {
        return PackageInfo.GetAllRegisteredPackages().FirstOrDefault(package => package.name == PackageName);
    }

    /// <summary>Explicitly adds/updates the published Git package. UPM may resolve dependencies and reload assemblies.</summary>
    public static void InstallOrUpdate(Action<bool, string> onComplete = null)
    {
        if (IsInstalling)
        {
            onComplete?.Invoke(false, "本地化模块正在安装，请等待完成。");
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            onComplete?.Invoke(false, "请等待导入或编译完成，并退出 Play Mode 后再安装。");
            return;
        }
        var package = GetInstalledPackage();
        if (package != null && (package.source == PackageSource.Local || package.source == PackageSource.Embedded))
        {
            status = "当前模块由本地目录提供，请在对应目录更新源码。";
            onComplete?.Invoke(false, status);
            return;
        }
        if (package == null && FindPipeline() != null)
        {
            status = "检测到旧版本地化脚本，请先迁移后再安装 UPM 包。";
            onComplete?.Invoke(false, status);
            return;
        }
        completion = onComplete;
        try
        {
            status = "正在通过 Package Manager 安装本地化模块…";
            installRequest = Client.Add(GitReference);
            EditorApplication.update += PollInstall;
        }
        catch (Exception exception)
        {
            installRequest = null;
            status = "安装未能启动：" + exception.Message;
            completion = null;
            onComplete?.Invoke(false, status);
        }
    }

    private static void PollInstall()
    {
        if (installRequest == null || !installRequest.IsCompleted) return;
        EditorApplication.update -= PollInstall;
        var success = installRequest.Status == StatusCode.Success;
        status = success ? "本地化模块已安装。编译完成后可打开流水线。"
            : "安装失败：" + installRequest.Error?.message;
        installRequest = null;
        var callback = completion;
        completion = null;
        if (!success) Debug.LogError("[UnityToolsHub] " + status);
        callback?.Invoke(success, status);
        foreach (var window in Resources.FindObjectsOfTypeAll<LocalizationModuleWindow>()) window.Repaint();
    }

    /// <summary>Opens the optional EditorWindow without a compile-time reference to the package.</summary>
    public static bool OpenPipeline()
    {
        var type = FindPipeline();
        if (type == null) return false;
        GetWindow(type).Show();
        return true;
    }

    private static Type FindPipeline()
    {
        var type = Type.GetType(PipelineTypeName + ", UnityFramework.Localization.Editor");
        return type != null && !type.IsAbstract && typeof(EditorWindow).IsAssignableFrom(type) ? type : null;
    }
}
#endif
