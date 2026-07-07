#if UNITY_EDITOR 
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///  富文本编辑器（网页版启动器）
/// ═══════════════════════════════════════════════════════════════
///
///  这是一个轻量包装窗口，本身不绘制 UI，仅用于：
///  1. 携带 [ToolInfo] 特性，让 UnityToolsHub 自动发现并归类到「文本工具」
///  2. 打开时用系统默认浏览器启动 RichTextEditorWeb.html
///
///  为什么用网页版：
///  - HTML 的 textarea 原生 selectionStart/selectionEnd + mousedown preventDefault
///    能完美保持选区，彻底解决 IMGUI/UI Toolkit 点击按钮丢失选区的问题
///  - 单文件 HTML，浏览器打开即用，无需依赖 Unity 版本
///
///  HTML 文件位置：跟随包目录 Editor/Tools/RichTextEditorWeb.html
///
/// ═══════════════════════════════════════════════════════════════
/// </summary>
[ToolInfo("富文本编辑器(网页版)", "文本工具",
    Description = "网页版富文本编辑器，用浏览器打开，彻底解决选区丢失问题。\n\n• 点击按钮插入到选区或光标位置（核心修复）\n• 粗体/斜体/下划线/删除线\n• 字号预设 + 自定义字号\n• 12 色预设 + 自定义取色器\n• 左/中/右对齐\n• 上标/下标/换行/空格快捷标签\n• 8 种模板一键套用\n• 实时预览（Unity 标签→HTML 渲染）\n• 撤销/重做（Ctrl+Z/Y）\n• 复制到剪贴板\n• 可拖动分隔条调整编辑区/预览区",
    Icon = "📝",
    Tags = new[] { "富文本", "richtext", "网页", "文本编辑", "格式化", "颜色", "字体" },
    Shortcut = "Ctrl+Shift+Alt+R",
    Priority = 18)]
public class RichTextEditorWebLauncher : EditorWindow
{
    [MenuItem("UnityToolsHub/富文本编辑器 (网页版)", priority = 119)]
    public static void ShowWindow()
    {
        OpenInBrowser();
    }

    /// <summary>Hub 点击「打开」按钮时调用：直接启动浏览器</summary>
    private void CreateGUI()
    {
        // 包装窗口本身不显示 UI，直接打开浏览器并关闭自身
        OpenInBrowser();
        Close();
    }

    /// <summary>获取 HTML 文件的路径（基于脚本自身路径动态定位，兼容任意安装位置）</summary>
    private static string GetHtmlFilePath()
    {
        // 通过 MonoScript 反查脚本在 Assets / Packages 中的路径，
        // 再拼接同目录下的 HTML 文件，兼容 Assets、UPM、git submodule 等任意位置
        var monoScript = MonoScript.FromScriptableObject(CreateInstance<RichTextEditorWebLauncher>());
        if (monoScript != null)
        {
            string scriptPath = AssetDatabase.GetAssetPath(monoScript);
            if (!string.IsNullOrEmpty(scriptPath))
            {
                string scriptDir = Path.GetDirectoryName(scriptPath);
                string htmlPath = Path.Combine(scriptDir, "RichTextEditorWeb.html");
                if (File.Exists(htmlPath))
                    return Path.GetFullPath(htmlPath);
            }
        }

        // 回退：搜索项目中所有同名 HTML 文件
        string[] guids = AssetDatabase.FindAssets("RichTextEditorWeb t:TextAsset");
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (assetPath.EndsWith("RichTextEditorWeb.html"))
                return Path.GetFullPath(assetPath);
        }

        return string.Empty;
    }

    /// <summary>用系统默认浏览器打开 HTML 文件</summary>
    private static void OpenInBrowser()
    {
        var fullPath = GetHtmlFilePath();
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("错误",
                "找不到富文本编辑器 HTML 文件（RichTextEditorWeb.html）。\n\n" +
                "请确认 RichTextEditorWeb.html 与 RichTextEditorWebLauncher.cs 在同一目录下。", "确定");
            Debug.LogError("[RichTextEditorWeb] 找不到 RichTextEditorWeb.html，请确认它与启动器脚本在同一目录。");
            return;
        }

        // 转为 file:// URI（正斜杠，URL 编码）
        var uri = "file:///" + fullPath.Replace('\\', '/');

        try
        {
            // 用系统默认程序打开（Windows 上会调用默认浏览器）
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
            Debug.Log($"[RichTextEditorWeb] 已在浏览器中打开：{uri}");
        }
        catch (System.Exception e)
        {
            // 回退方案：用 Application.OpenURL
            Application.OpenURL(uri);
            Debug.LogWarning($"[RichTextEditorWeb] Process.Start 失败，已回退到 Application.OpenURL：{e.Message}");
        }
    }
}
#endif
