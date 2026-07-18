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
        public string category;       // 当前所属分类（可能被用户修改）
        public string originalCategory; // 发现时的原始分类（不可变，用于"还原默认分类"）
        public string typeName;
        public string icon;           // 类别图标字符
        public string[] tags;         // 功能标签
        public string shortcut;       // 快捷键提示
        public int priority;          // 排序优先级（发现时缓存）
        public string author;         // 工具作者
        public string authorLink;      // 作者链接/主页 URL
        public bool isThirdParty;      // 是否第三方工具
        public string scriptPath;      // 脚本资产路径 (Assets/.../*.cs)
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

    #region 文件夹分类配置（持久化）
    /// <summary>用户自定义文件夹分类配置，可序列化存储到 EditorPrefs</summary>
    [Serializable]
    private class FolderConfig
    {
        /// <summary>用户自定义的文件夹名称列表（包含自定义创建的 + 从 ToolInfo 同步来的），保持显示顺序</summary>
        public List<FolderItem> folders = new List<FolderItem>();

        /// <summary>工具 → 自定义所属文件夹名映射（仅存储用户手动移动过的工具）</summary>
        public List<ToolFolderAssignment> toolAssignments = new List<ToolFolderAssignment>();

        [NonSerialized] private Dictionary<string, string> _assignmentIndex;
        [NonSerialized] private bool _assignmentDirty = true;

        private void RebuildAssignmentIndex()
        {
            if (!_assignmentDirty) return;
            _assignmentIndex = new Dictionary<string, string>(toolAssignments.Count);
            foreach (var a in toolAssignments)
                _assignmentIndex[a.toolTypeName] = a.folderName;
            _assignmentDirty = false;
        }

        /// <summary>获取工具当前所属的文件夹名（null 表示未自定义）</summary>
        public string GetToolFolder(string toolTypeName)
        {
            if (string.IsNullOrEmpty(toolTypeName)) return null;
            RebuildAssignmentIndex();
            return _assignmentIndex.TryGetValue(toolTypeName, out var folder) ? folder : null;
        }

        /// <summary>设置工具所属的文件夹</summary>
        public void SetToolFolder(string toolTypeName, string folderName)
        {
            if (string.IsNullOrEmpty(toolTypeName)) return;
            RebuildAssignmentIndex();
            // 移除旧分配
            toolAssignments.RemoveAll(a => a.toolTypeName == toolTypeName);
            if (!string.IsNullOrEmpty(folderName))
                toolAssignments.Add(new ToolFolderAssignment { toolTypeName = toolTypeName, folderName = folderName });
            _assignmentDirty = true;
        }

        /// <summary>获取或创建文件夹项（保持顺序）</summary>
        public FolderItem GetOrCreateFolder(string name)
        {
            var item = folders.Find(f => f.name == name);
            if (item == null)
            {
                item = new FolderItem { name = name, icon = "📁", order = folders.Count };
                folders.Add(item);
            }
            return item;
        }

        /// <summary>重命名文件夹（同步更新所有工具分配）</summary>
        public void RenameFolder(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;
            var item = folders.Find(f => f.name == oldName);
            if (item != null) item.name = newName;
            foreach (var a in toolAssignments)
            {
                if (a.folderName == oldName) a.folderName = newName;
            }
            _assignmentDirty = true;
        }

        /// <summary>删除文件夹（将其中的工具释放到未分类）</summary>
        public void RemoveFolder(string name)
        {
            folders.RemoveAll(f => f.name == name);
            toolAssignments.RemoveAll(a => a.folderName == name);
            _assignmentDirty = true;
        }

        /// <summary>移动文件夹在列表中的位置</summary>
        public void MoveFolder(string name, int newIndex)
        {
            var item = folders.Find(f => f.name == name);
            if (item == null) return;
            folders.Remove(item);
            newIndex = Mathf.Clamp(newIndex, 0, folders.Count);
            folders.Insert(newIndex, item);
        }
    }

    [Serializable]
    private class FolderItem
    {
        public string name;
        public string icon;
        public int order;
        public bool isCustom; // true = 用户手动创建的文件夹
    }

    [Serializable]
    private class ToolFolderAssignment
    {
        public string toolTypeName;
        public string folderName;
    }
    #endregion

    #region 添加工具候选条目
    /// <summary>扫描发现的非 HubTool 编辑器扩展候选</summary>
    private class AddToolCandidate
    {
        public string filePath;        // .cs 文件相对路径（Assets/...）
        public string absPath;         // 绝对路径
        public string className;       // 类名
        public string baseClass;       // 基类名（如 EditorWindow）
        public string namespaceName;   // 命名空间（可为空）
        public string fullTypeName;    // 完整类型名（namespace.class）
        public string existingDescription; // 文件中已有的注释摘要（前3行）
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

    #region 第三方工具状态注册表
    /// <summary>单个第三方工具的持久化状态</summary>
    [Serializable]
    private class ThirdPartyToolState
    {
        public string typeName;       // 完整类型名
        public string toolName;       // 显示名
        public string author;
        public string authorLink;
        public string description;
        public string category;
        public string scriptPath;     // 脚本资产路径
        public bool isEnabled;        // false=禁用(默认), true=已启用

        // ── 导入来源信息 ──
        public string importSource;    // "local" | "git" | "manual"
        public string gitUrl;          // Git 仓库 URL
        public string packagePath;     // 本地包路径 或 UPM 包名
        public string installPath;    // 安装后的实际路径
        public bool isInstalled;       // 是否已安装
    }

    /// <summary>第三方工具注册表（可序列化存储到 EditorPrefs）</summary>
    [Serializable]
    private class ThirdPartyToolRegistry
    {
        public List<ThirdPartyToolState> tools = new List<ThirdPartyToolState>();

        [NonSerialized] private Dictionary<string, ThirdPartyToolState> _index;
        [NonSerialized] private bool _indexDirty = true;

        private void RebuildIndexIfNeeded()
        {
            if (!_indexDirty) return;
            _index = new Dictionary<string, ThirdPartyToolState>(tools.Count);
            foreach (var t in tools)
                if (!string.IsNullOrEmpty(t.typeName))
                    _index[t.typeName] = t;
            _indexDirty = false;
        }

        public ThirdPartyToolState Find(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            RebuildIndexIfNeeded();
            return _index.TryGetValue(typeName, out var s) ? s : null;
        }

        public ThirdPartyToolState FindByName(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return null;
            foreach (var t in tools)
                if (t.toolName == toolName) return t;
            return null;
        }

        public ThirdPartyToolState FindByGitUrl(string gitUrl)
        {
            if (string.IsNullOrEmpty(gitUrl)) return null;
            foreach (var t in tools)
                if (t.gitUrl == gitUrl) return t;
            return null;
        }

        public bool IsEnabled(string typeName)
        {
            var s = Find(typeName);
            return s?.isEnabled ?? false;
        }

        public void SetEnabled(string typeName, bool enabled)
        {
            var s = Find(typeName);
            if (s != null) { s.isEnabled = enabled; _indexDirty = true; }
        }

        public void AddOrUpdate(ThirdPartyToolState state)
        {
            if (state == null || string.IsNullOrEmpty(state.typeName)) return;
            var existing = Find(state.typeName);
            if (existing != null)
            {
                existing.toolName = state.toolName;
                existing.author = state.author;
                existing.authorLink = state.authorLink;
                existing.description = state.description;
                existing.category = state.category;
                existing.scriptPath = state.scriptPath;
                // 保留已有 isEnabled 状态
            }
            else
            {
                tools.Add(state);
                _indexDirty = true;
            }
        }

        public bool Remove(string typeName)
        {
            var s = Find(typeName);
            if (s != null) { tools.Remove(s); _indexDirty = true; return true; }
            return false;
        }

        public int EnabledCount
        {
            get
            {
                int c = 0;
                foreach (var t in tools)
                    if (t.isEnabled) c++;
                return c;
            }
        }
    }
    #endregion
}
#endif
