#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 数据结构定义
/// 包含工具条目、分类节点、使用频率统计、隐藏项管理
/// </summary>
public partial class UnityToolsHub
{
    #region 工具条目
    [Serializable]
    private class ToolEntry
    {
        public string name;
        public string description;
        public string category;
        public string typeName;
        public string icon;           // 类别图标字符
        public string[] tags;         // 功能标签
        public string shortcut;       // 快捷键提示
    }
    #endregion

    #region 分类节点
    private class CategoryNode
    {
        public string name;
        public string icon;
        public Color accent;
        public bool expanded = true;
        public List<ToolEntry> tools = new List<ToolEntry>();
    }
    #endregion

    #region 使用频率统计
    /// <summary>使用频率统计（可序列化存储到 EditorPrefs）</summary>
    [Serializable]
    private class UsageStats
    {
        // 工具 typeName → 使用次数
        public List<UsageEntry> tools = new List<UsageEntry>();
        // 分类 name → 使用次数（= 该分类下所有工具使用次数之和，缓存用）
        public List<UsageEntry> categories = new List<UsageEntry>();

        public int GetToolCount(string typeName)
        {
            var e = tools.Find(x => x.key == typeName);
            return e?.count ?? 0;
        }

        public int GetCategoryCount(string categoryName)
        {
            var e = categories.Find(x => x.key == categoryName);
            return e?.count ?? 0;
        }

        public void IncrementTool(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            var e = tools.Find(x => x.key == typeName);
            if (e == null) tools.Add(new UsageEntry { key = typeName, count = 1 });
            else e.count++;
        }

        public void IncrementCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return;
            var e = categories.Find(x => x.key == categoryName);
            if (e == null) categories.Add(new UsageEntry { key = categoryName, count = 1 });
            else e.count++;
        }

        public void ClearTool(string typeName)
        {
            var e = tools.Find(x => x.key == typeName);
            if (e != null) tools.Remove(e);
        }
    }

    [Serializable]
    private class UsageEntry
    {
        public string key;
        public int count;
    }
    #endregion

    #region 隐藏项管理
    /// <summary>隐藏项管理（可序列化存储到 EditorPrefs）</summary>
    [Serializable]
    private class HiddenItems
    {
        public List<string> hiddenTools = new List<string>();      // 隐藏的工具 typeName
        public List<string> hiddenCategories = new List<string>(); // 隐藏的分类 name

        public bool IsToolHidden(string typeName)
            => !string.IsNullOrEmpty(typeName) && hiddenTools.Contains(typeName);

        public bool IsCategoryHidden(string categoryName)
            => !string.IsNullOrEmpty(categoryName) && hiddenCategories.Contains(categoryName);

        public void ToggleTool(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            if (hiddenTools.Contains(typeName)) hiddenTools.Remove(typeName);
            else hiddenTools.Add(typeName);
        }

        public void ToggleCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return;
            if (hiddenCategories.Contains(categoryName)) hiddenCategories.Remove(categoryName);
            else hiddenCategories.Add(categoryName);
        }
    }
    #endregion
}
#endif
