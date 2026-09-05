using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[ToolInfo("复制并替换名称", "资产工具",
    Description = "复制 Project 窗口中选中的文件或文件夹，并将名称中的指定字符替换为新字符。\n\n支持多选、递归替换子文件夹和子文件名称；发生名称冲突或重命名失败时会自动清理本次副本。",
    Icon = "✎", Tags = new[] { "复制", "重命名", "批量替换", "文件夹", "文件" }, Priority = 30)]
public sealed class CopyAndReplaceNameWindow : EditorWindow
{
    private const string MenuPath = "Assets/复制并替换名称";

    private string searchText = string.Empty;
    private string replacementText = string.Empty;
    private bool replaceChildNames = true;
    private string validationMessage = string.Empty;

    [MenuItem(MenuPath, false, 2000)]
    public static void Open()
    {
        var window = CreateInstance<CopyAndReplaceNameWindow>();
        window.titleContent = new GUIContent("复制并替换名称");
        window.searchText = GetSelectedAssetName();
        window.minSize = new Vector2(420f, 180f);
        window.maxSize = new Vector2(700f, 180f);
        window.ShowUtility();
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateOpen()
    {
        return GetSelectedAssetPaths().Count > 0;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("复制选中的文件/文件夹，并递归替换副本中的名称。", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(6f);

        GUI.SetNextControlName("SearchText");
        searchText = EditorGUILayout.TextField("要替换的字符", searchText);
        replacementText = EditorGUILayout.TextField("替换为", replacementText);
        replaceChildNames = EditorGUILayout.Toggle("替换子文件夹/文件名称", replaceChildNames);

        if (!string.IsNullOrEmpty(validationMessage))
            EditorGUILayout.HelpBox(validationMessage, MessageType.Error);

        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消", GUILayout.Width(80f)))
                Close();

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(searchText)))
            {
                if (GUILayout.Button("复制并替换", GUILayout.Width(110f)))
                    CopySelectedAssets();
            }
        }

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && !string.IsNullOrEmpty(searchText))
        {
            CopySelectedAssets();
            Event.current.Use();
        }
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("复制并替换名称");
        if (string.IsNullOrEmpty(searchText))
            searchText = GetSelectedAssetName();

        EditorApplication.delayCall += FocusSearchField;
    }

    private void FocusSearchField()
    {
        if (this != null)
            EditorGUI.FocusTextInControl("SearchText");
    }

    private void CopySelectedAssets()
    {
        validationMessage = string.Empty;
        var sourcePaths = GetSelectedAssetPaths();
        if (sourcePaths.Count == 0)
        {
            validationMessage = "请先在 Project 窗口中选择文件或文件夹。";
            return;
        }

        if (!TryValidateReplacement(sourcePaths, out validationMessage))
            return;

        var copiedPaths = new List<string>();
        try
        {
            foreach (string sourcePath in sourcePaths)
            {
                string destinationPath = GetDestinationPath(sourcePath);
                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                    throw new InvalidOperationException($"复制失败：{sourcePath}");

                copiedPaths.Add(destinationPath);
                if (replaceChildNames)
                {
                    AssetDatabase.ImportAsset(
                        destinationPath,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ImportRecursive);
                    RenameCopiedChildren(destinationPath);
                }
            }
        }
        catch (Exception exception)
        {
            foreach (string copiedPath in copiedPaths)
                AssetDatabase.DeleteAsset(copiedPath);

            copiedPaths.Clear();
            Debug.LogException(exception);
            validationMessage = exception.Message;
            return;
        }
        finally
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Selection.objects = copiedPaths
            .Select(AssetDatabase.LoadMainAssetAtPath)
            .Where(asset => asset != null)
            .ToArray();
        Close();
    }

    private bool TryValidateReplacement(IReadOnlyCollection<string> sourcePaths, out string error)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            error = "“要替换的字符”不能为空。";
            return false;
        }

        if (searchText.IndexOfAny(new[] { '/', '\\' }) >= 0 || replacementText.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "查找或替换内容包含文件名不允许使用的字符。";
            return false;
        }

        foreach (string sourcePath in sourcePaths)
        {
            string rootName = GetNameWithoutExtension(sourcePath);
            bool rootMatches = rootName.Contains(searchText);
            bool childMatches = replaceChildNames && AssetDatabase.IsValidFolder(sourcePath) &&
                                Directory.EnumerateFileSystemEntries(sourcePath, "*", SearchOption.AllDirectories)
                                    .Any(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                                                 GetNameWithoutExtension(path).Contains(searchText));
            if (!rootMatches && !childMatches)
            {
                error = $"“{sourcePath}”及其子项中没有找到“{searchText}”。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private string GetDestinationPath(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
        string extension = AssetDatabase.IsValidFolder(sourcePath) ? string.Empty : Path.GetExtension(sourcePath);
        string replacedName = GetNameWithoutExtension(sourcePath).Replace(searchText, replacementText);
        if (string.IsNullOrWhiteSpace(replacedName))
            throw new InvalidOperationException($"替换后名称为空：{sourcePath}");

        string desiredPath = $"{directory}/{replacedName}{extension}";
        return AssetDatabase.GenerateUniqueAssetPath(desiredPath);
    }

    private void RenameCopiedChildren(string copiedRootPath)
    {
        if (!AssetDatabase.IsValidFolder(copiedRootPath))
            return;

        var paths = Directory.EnumerateFileSystemEntries(copiedRootPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Select(path => path.Replace('\\', '/'))
            .OrderByDescending(GetPathDepth)
            .ToArray();

        foreach (string path in paths)
        {
            string oldName = GetNameWithoutExtension(path);
            string newName = oldName.Replace(searchText, replacementText);
            if (newName == oldName)
                continue;
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidOperationException($"替换后名称为空：{path}");

            string moveError = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(moveError))
                throw new InvalidOperationException($"重命名失败：{path}\n{moveError}");
        }
    }

    private static int GetPathDepth(string path)
    {
        return path.Count(character => character == '/');
    }

    private static string GetNameWithoutExtension(string path)
    {
        return AssetDatabase.IsValidFolder(path) ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
    }

    private static string GetSelectedAssetName()
    {
        string activePath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!string.IsNullOrEmpty(activePath) && activePath.StartsWith("Assets/", StringComparison.Ordinal))
            return GetNameWithoutExtension(activePath);

        string firstPath = GetSelectedAssetPaths().FirstOrDefault();
        return string.IsNullOrEmpty(firstPath) ? string.Empty : GetNameWithoutExtension(firstPath);
    }

    private static List<string> GetSelectedAssetPaths()
    {
        var paths = Selection.assetGUIDs
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        // If a selected item is already inside another selected folder, copying the
        // parent is sufficient and avoids producing an unexpected second copy.
        return paths
            .Where(path => !paths.Any(other => other != path &&
                                              AssetDatabase.IsValidFolder(other) &&
                                              path.StartsWith(other + "/", StringComparison.Ordinal)))
            .ToList();
    }
}
