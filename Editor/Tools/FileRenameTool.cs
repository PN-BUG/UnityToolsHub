#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

[ToolInfo("批量重命名", "文件工具",
    Description = "批量替换文件夹内文件名中的指定字符串。\n\n支持预览修改和执行替换，递归处理所有子目录。",
    Icon = "📁", Tags = new[] { "重命名", "批量" })]
public class FileRenameTool : EditorWindow
{
    private DefaultAsset folder;
    private string oldStr = "";
    private string newStr = "";

    private Vector2 scroll;

    [MenuItem("UnityToolsHub/文件批量重命名工具")]
    public static void ShowWindow()
    {
        GetWindow<FileRenameTool>("文件重命名工具");
    }

    private void OnGUI()
    {
        GUILayout.Label("批量文件名替换", EditorStyles.boldLabel);

        folder = (DefaultAsset)EditorGUILayout.ObjectField("目标文件夹", folder, typeof(DefaultAsset), false);

        oldStr = EditorGUILayout.TextField("要替换的字符串", oldStr);
        newStr = EditorGUILayout.TextField("替换为", newStr);

        GUILayout.Space(10);

        if (GUILayout.Button("🔍 预览修改"))
        {
            Preview();
        }

        if (GUILayout.Button("🚀 执行替换"))
        {
            Rename();
        }
    }

    private void Preview()
    {
        if (folder == null) return;

        string path = AssetDatabase.GetAssetPath(folder);
        string fullPath = Path.GetFullPath(path);

        string[] files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);

        Debug.Log("====== 预览 ======");
        foreach (var file in files)
        {
            if (file.EndsWith(".meta")) continue;

            string fileName = Path.GetFileName(file);
            string newName = fileName.Replace(oldStr, newStr);

            if (fileName != newName)
            {
                Debug.Log($"{fileName}  →  {newName}");
            }
        }
    }

    private void Rename()
    {
        if (folder == null) return;

        string path = AssetDatabase.GetAssetPath(folder);
        string[] guids = AssetDatabase.FindAssets("", new[] { path });

        int count = 0;

        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileName(assetPath);

            string newName = fileName.Replace(oldStr, newStr);

            if (fileName != newName)
            {
                string result = AssetDatabase.RenameAsset(assetPath, newName);
                if (string.IsNullOrEmpty(result))
                {
                    count++;
                }
                else
                {
                    Debug.LogError($"重命名失败: {result}");
                }
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"✅ 完成，共修改 {count} 个文件");
    }
}
#endif