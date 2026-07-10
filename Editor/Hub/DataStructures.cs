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
        public int priority;          // 排序优先级（发现时缓存）
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

        // 运行时查找索引（序列化时不写入，OnEnable 后首次访问时重建）
        [NonSerialized] private Dictionary<string, int> _toolIndex;
        [NonSerialized] private Dictionary<string, int> _categoryIndex;
        [NonSerialized] private bool _indexDirty = true;

        private void RebuildIndexIfNeeded()
        {
            if (!_indexDirty) return;
            _toolIndex = new Dictionary<string, int>(tools.Count);
            for (int i = 0; i < tools.Count; i++)
                _toolIndex[tools[i].key] = tools[i].count;
            _categoryIndex = new Dictionary<string, int>(categories.Count);
            for (int i = 0; i < categories.Count; i++)
                _categoryIndex[categories[i].key] = categories[i].count;
            _indexDirty = false;
        }

        public int GetToolCount(string typeName)
        {
            RebuildIndexIfNeeded();
            return _toolIndex.TryGetValue(typeName, out var c) ? c : 0;
        }

        public int GetCategoryCount(string categoryName)
        {
            RebuildIndexIfNeeded();
            return _categoryIndex.TryGetValue(categoryName, out var c) ? c : 0;
        }

        public void IncrementTool(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            var e = tools.Find(x => x.key == typeName);
            if (e == null) tools.Add(new UsageEntry { key = typeName, count = 1 });
            else e.count++;
            _indexDirty = true;
        }

        public void IncrementCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return;
            var e = categories.Find(x => x.key == categoryName);
            if (e == null) categories.Add(new UsageEntry { key = categoryName, count = 1 });
            else e.count++;
            _indexDirty = true;
        }

        public void ClearTool(string typeName)
        {
            var e = tools.Find(x => x.key == typeName);
            if (e != null) { tools.Remove(e); _indexDirty = true; }
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

        [NonSerialized] private HashSet<string> _toolSet;
        [NonSerialized] private HashSet<string> _categorySet;
        [NonSerialized] private bool _indexDirty = true;

        private void RebuildIndexIfNeeded()
        {
            if (!_indexDirty) return;
            _toolSet = new HashSet<string>(hiddenTools);
            _categorySet = new HashSet<string>(hiddenCategories);
            _indexDirty = false;
        }

        public bool IsToolHidden(string typeName)
            => !string.IsNullOrEmpty(typeName) && Lookup(ref _toolSet, typeName);

        public bool IsCategoryHidden(string categoryName)
            => !string.IsNullOrEmpty(categoryName) && Lookup(ref _categorySet, categoryName);

        private bool Lookup(ref HashSet<string> set, string key)
        {
            if (_indexDirty) RebuildIndexIfNeeded();
            return set != null && set.Contains(key);
        }

        public void ToggleTool(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            if (hiddenTools.Contains(typeName)) hiddenTools.Remove(typeName);
            else hiddenTools.Add(typeName);
            _indexDirty = true;
        }

        public void ToggleCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return;
            if (hiddenCategories.Contains(categoryName)) hiddenCategories.Remove(categoryName);
            else hiddenCategories.Add(categoryName);
            _indexDirty = true;
        }
    }
    #endregion
}
#endif
