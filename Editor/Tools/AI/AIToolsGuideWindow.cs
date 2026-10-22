#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>AI 工具的固定入口；同时让尚未收录实用工具时的分类保持可见。</summary>
[ToolInfo("AI工具使用说明", "AI工具",
    Description = "存放 AI 编写、可重复使用的编辑器工具。先查找已有工具，再扩展或新增。\n\n提供工具目录、接入模板和可复制给 AI 的复用说明。",
    Icon = "⚙", Tags = new[] { "AI", "复用", "临时工具", "模板", "自动化" },
    Priority = -100)]
public class AIToolsGuideWindow : ToolEditorWindow
{
    protected override string ToolTitle => "AI工具使用说明";
    protected override string ToolIcon => "⚙";

    private string ToolsDirectory => Path.GetDirectoryName(
        AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this))).Replace('\\', '/');

    protected override void DrawToolContent()
    {
        DrawToolHelpBox("这里收录 AI 编写、后续还会使用的编辑器工具。下次处理相同任务时，先检查目录和工具说明，优先直接调用已有工具。");
        DrawSection("工具目录");
        EditorGUILayout.SelectableLabel(ToolsDirectory, EditorStyles.wordWrappedLabel,
            GUILayout.MinHeight(36));

        if (DrawFlatButton("定位工具目录"))
        {
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ToolsDirectory);
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }
        if (DrawFlatButton("打开工具索引与使用说明"))
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(ToolsDirectory + "/README.md"));

        EditorGUILayout.Space(12);
        DrawSection("添加可复用工具");
        DrawBody("将模板复制为新的 .cs 文件，修改类名、工具名称、描述和标签，实现工具逻辑后保存。Hub 会通过 [ToolInfo] 自动将其收录到「AI工具」。");
        if (DrawFlatButton("复制 AI 工具模板"))
            EditorGUIUtility.systemCopyBuffer = AssetDatabase.LoadAssetAtPath<TextAsset>(
                ToolsDirectory + "/_AIToolTemplate.cs.txt").text;

        EditorGUILayout.Space(12);
        DrawSection("让 AI 优先复用");
        DrawBody("工具应说明用途、输入范围、调用入口和副作用。将核心逻辑写成带参数的公开静态方法，窗口只负责收集参数与显示结果，方便 AI 下次直接调用。");
        if (DrawFlatButton("复制给 AI 的复用说明"))
            EditorGUIUtility.systemCopyBuffer =
                "请先阅读 " + ToolsDirectory + "/README.md 并搜索现有工具（包括其他 ToolsHub 分类）。" +
                "优先复用已有公开方法；能力不足时扩展已有工具，避免重复生成。" +
                "可复用的新工具保存在 " + ToolsDirectory +
                "，使用 [ToolInfo(\"工具名称\", \"AI工具\")]，并更新目录 README 的工具索引、参数、入口和副作用。";
    }
}
#endif
