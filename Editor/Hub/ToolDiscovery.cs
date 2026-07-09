#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 工具自动发现与创建
/// 扫描 [ToolInfo] 特性、管理跨程序集工具、创建新工具文件
/// </summary>
public partial class UnityToolsHub
{
    #region 工具自动发现
    /// <summary>
    /// 扫描所有程序集，发现带有 [ToolInfo] 特性的 EditorWindow 类并自动归类。
    /// 新增工具只需在类上添加 [ToolInfo] 特性，无需修改 Hub。
    /// </summary>
    private void DiscoverTools()
    {
        _categories.Clear();

        // ── 1. 扫描 [ToolInfo] 特性 ──────────────────────
        var discovered = new List<ToolEntry>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(EditorWindow).IsAssignableFrom(type)) continue;

                var attr = (ToolInfoAttribute)Attribute.GetCustomAttribute(type, typeof(ToolInfoAttribute));
                if (attr == null) continue;

                discovered.Add(new ToolEntry
                {
                    name       = attr.Name,
                    description = attr.Description ?? "",
                    category   = attr.Category,
                    typeName   = type.FullName,
                    icon       = string.IsNullOrEmpty(attr.Icon) ? GetCategoryIcon(attr.Category) : attr.Icon,
                    tags       = attr.Tags,
                    shortcut   = attr.Shortcut
                });
            }
        }

        // ── 2. 按分类分组 ────────────────────────────────
        // 排序规则：使用频率高的分类在前（频率相同则按原 Priority）
        var groups = discovered.GroupBy(t => t.category)
            .OrderByDescending(g => _usageStats.GetCategoryCount(g.Key))
            .ThenBy(g => g.Min(t => GetPriority(t)))
            .ToList();

        int paletteIdx = 0;
        foreach (var group in groups)
        {
            var catName = group.Key;

            // 获取分类颜色：已知 → 固定色，未知 → 调色板循环
            if (!_categoryColors.TryGetValue(catName, out var accent))
            {
                accent = _defaultPalette[paletteIdx % _defaultPalette.Length];
                _categoryColors[catName] = accent;
                paletteIdx++;
            }

            // 获取分类图标（版本自适应，已知分类用字典，未知用工具自带）
            var catIcon = GetCategoryIcon(catName);

            // 工具排序：使用频率高的在前（频率相同则按原 Priority）
            var tools = group
                .OrderByDescending(t => _usageStats.GetToolCount(t.typeName))
                .ThenBy(t => GetPriority(t))
                .ToList();
            var node = new CategoryNode { name = catName, icon = catIcon, accent = accent };
            node.tools.AddRange(tools);
            _categories.Add(node);
        }

        // ── 3. 特殊工具（无 EditorWindow 类，仅菜单项）──
        AddSpecialTools();

        // ── 4. 构建工具索引 + 缓存总数（避免每帧 LINQ 遍历）──
        _toolIndex.Clear();
        _totalToolCount = 0;
        foreach (var cat in _categories)
        {
            foreach (var tool in cat.tools)
            {
                if (!string.IsNullOrEmpty(tool.typeName))
                    _toolIndex[tool.typeName] = tool;
                _totalToolCount++;
            }
        }

        // 使搜索过滤缓存失效
        _categoriesVersion++;
    }

    private static int GetPriority(ToolEntry t)
    {
        if (string.IsNullOrEmpty(t.typeName)) return 999;
        var type = FindType(t.typeName);
        if (type == null) return 999;
        var attr = (ToolInfoAttribute)Attribute.GetCustomAttribute(type, typeof(ToolInfoAttribute));
        return attr?.Priority ?? 0;
    }

    /// <summary>添加无 EditorWindow 类的特殊工具（仅菜单项）</summary>
    private void AddSpecialTools()
    {
        // ── 跨程序集工具（无法使用 [ToolInfo] 特性的 Editor 子文件夹工具）──
        AddCrossAssemblyTool("数据工具", "数据管理",
            "清理数据工具：可选择清空控制台日志、PersistentData 目录、PlayerPrefs。\n\n支持 GamingDataSO 配置管理，一键重置运行时数据。",
            "DataToolsWindow", "☰", new[] { "清理", "PlayerPrefs", "SO配置" });

        AddCrossAssemblyTool("事件中心调试", "调试工具",
            "ZEventSystem 事件中心调试窗口：搜索、查看已注册的事件监听器。\n\n支持手动发送测试事件，实时监控事件流，快速定位事件通信问题。",
            "ZEventSystem.Editor.EventCenterDebugWindow", "⌕", new[] { "事件系统", "调试", "监控" });

        AddCrossAssemblyTool("帧动画创建工具", "媒体工具",
            "从图片序列自动创建 Sprite 帧动画。\n\n支持按文件名数字排序、帧率/循环配置、批量子文件夹处理、角色分组、自动生成 AnimatorController 和预制体。",
            "FrameAnimationCreator", "▣", new[] { "帧动画", "Sprite", "动画创建" });

        AddCrossAssemblyTool("贴图导入规则配置", "资产工具",
            "配置贴图导入规则面板：UI 关键词匹配、忽略目录、自动导入开关。\n\n支持设置压缩格式、过滤模式、最大尺寸上限、Sprite 模式和 Mipmap 等参数。",
            "TextureImportSettingsWindow", "▤", new[] { "贴图", "导入规则", "配置" });

        AddCrossAssemblyTool("资源分析与优化", "资产工具",
            "资源分析与优化工具：扫描项目资产，检测纹理尺寸、格式、Read/Write 等问题。\n\n支持按平台分析、过滤/排序、批量定位问题资源，帮助优化包体和运行时性能。",
            "ResourceAnalyzer.ResourceAnalyzerWindow", "◷", new[] { "资源分析", "优化", "纹理", "包体" });

        // ── 路径快捷工具（无 EditorWindow）──────────────────
        var pathTools = GetOrCreateCategory("路径工具");
        pathTools.tools.Add(new ToolEntry
        {
            name        = "打开特殊目录",
            description = "快速打开项目常用目录：\n\n• Project Root  • Assets Folder\n• Project Settings  • Library Folder\n• Temp Folder  • Builds Folder\n• Editor Log  • Player Log\n• Persistent Data  • Streaming Assets\n• Temporary Cache",
            category    = "路径工具",
            typeName    = null,
            icon        = "◈",
            tags        = new[] { "目录", "快速访问" }
        });
    }

    /// <summary>添加跨程序集工具（Editor 子文件夹内、无法使用 [ToolInfo] 的工具）</summary>
    private void AddCrossAssemblyTool(string name, string category, string description,
        string typeName, string icon, string[] tags)
    {
        var cat = GetOrCreateCategory(category);
        cat.tools.Add(new ToolEntry
        {
            name        = name,
            description = description,
            category    = category,
            typeName    = typeName,
            icon        = icon,
            tags        = tags
        });
    }

    /// <summary>获取或创建分类节点（消除重复的 FirstOrDefault + 判空模式）</summary>
    private CategoryNode GetOrCreateCategory(string categoryName)
    {
        var cat = _categories.FirstOrDefault(c => c.name == categoryName);
        if (cat == null)
        {
            _categoryColors.TryGetValue(categoryName, out var accent);
            cat = new CategoryNode { name = categoryName, icon = GetCategoryIcon(categoryName), accent = accent };
            _categories.Add(cat);
        }
        return cat;
    }
    #endregion

    #region 类型查找
    private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

    private static Type FindType(string typeName)
    {
        if (_typeCache.TryGetValue(typeName, out var cached))
            return cached;

        var type = Type.GetType(typeName);
        if (type == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(typeName);
                    if (type != null) break;
                }
                catch { }
            }
        }

        _typeCache[typeName] = type;
        return type;
    }
    #endregion

    #region 工具窗口打开
    private void OpenToolWindow(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return;

        var type = FindType(typeName);
        if (type == null)
        {
            Debug.LogWarning($"[UnityToolsHub] 找不到类型：{typeName}");
            return;
        }

        if (!typeof(EditorWindow).IsAssignableFrom(type))
        {
            Debug.LogWarning($"[UnityToolsHub] {typeName} 不是 EditorWindow 类型");
            return;
        }

        // 反射调用 CreateWindow<T>(params Type[]) 将工具窗口停靠为同一标签页
        var createWindowMethod = typeof(EditorWindow)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "CreateWindow"
                && m.IsGenericMethod
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(Type[]));

        if (createWindowMethod != null)
        {
            var genericMethod = createWindowMethod.MakeGenericMethod(type);
            var window = (EditorWindow)genericMethod.Invoke(null, new object[] { new Type[] { typeof(UnityToolsHub) } });
            window?.Show();
        }
        else
        {
            // 回退：独立窗口
            EditorWindow.GetWindow(type);
        }
    }
    #endregion

    #region 创建工具文件
    /// <summary>从工具名生成合法的 C# 类名</summary>
    private static string DeriveClassName(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "NewTool";

        // 提取英文字母和数字作为类名基础
        var sb = new System.Text.StringBuilder();
        bool capNext = true;
        foreach (char c in toolName)
        {
            if (c >= 'a' && c <= 'z')
            {
                sb.Append(capNext ? char.ToUpper(c) : c);
                capNext = false;
            }
            else if (c >= 'A' && c <= 'Z')
            {
                sb.Append(capNext ? c : char.ToLower(c));
                capNext = false;
            }
            else if (c >= '0' && c <= '9')
            {
                if (sb.Length == 0) sb.Append('_');
                sb.Append(c);
                capNext = true;
            }
            else
            {
                // 空格/中文等 → 单词边界
                if (sb.Length > 0) capNext = true;
            }
        }

        string result = sb.ToString();
        if (string.IsNullOrEmpty(result)) return "NewTool";
        if (result[0] >= '0' && result[0] <= '9') result = "_" + result;
        return result;
    }

    /// <summary>
    /// 获取包内模板文件的绝对路径
    /// 通过当前程序集位置反推包根目录，兼容任意安装位置
    /// </summary>
    private static string GetPackageTemplatePath()
    {
        // 通过当前程序集定位包目录
        string asmPath = typeof(UnityToolsHub).Assembly.Location;
        string asmDir = System.IO.Path.GetDirectoryName(asmPath);
        // asmDir = .../Editor，包根目录 = 上一级
        string packageRoot = System.IO.Path.GetDirectoryName(asmDir);
        string templatePath = System.IO.Path.Combine(packageRoot, "Editor", "_NewToolTemplate.cs.txt");
        return templatePath;
    }

    private void CreateToolFile()
    {
        // 验证输入
        if (string.IsNullOrWhiteSpace(_createToolName))
        {
            EditorUtility.DisplayDialog("提示", "请输入工具名称", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(_createClassName))
        {
            EditorUtility.DisplayDialog("提示", "请输入类名", "确定");
            return;
        }

        // 确保目录存在
        string dir = _createDirectory;
        if (string.IsNullOrWhiteSpace(dir)) dir = "Assets/Editor/Tools";
        if (!dir.EndsWith("/")) dir += "/";

        string fullPath = Application.dataPath + "/" + dir.Substring("Assets/".Length);
        if (!System.IO.Directory.Exists(fullPath))
            System.IO.Directory.CreateDirectory(fullPath);

        string filePath = dir + _createClassName + ".cs";
        string absPath = Application.dataPath + "/" + filePath.Substring("Assets/".Length);

        if (System.IO.File.Exists(absPath))
        {
            EditorUtility.DisplayDialog("提示", $"文件已存在：{filePath}", "确定");
            return;
        }

        // 读取模板（从包目录定位）
        string templatePath = GetPackageTemplatePath();
        if (string.IsNullOrEmpty(templatePath) || !System.IO.File.Exists(templatePath))
        {
            EditorUtility.DisplayDialog("错误", "找不到模板文件：_NewToolTemplate.cs.txt", "确定");
            return;
        }

        string template = System.IO.File.ReadAllText(templatePath, System.Text.Encoding.UTF8);

        // 处理标签
        string tagsArray = "new[] { \"示例\" }";
        if (!string.IsNullOrWhiteSpace(_createTags))
        {
            var tagParts = _createTags.Split(',');
            var tagEntries = tagParts.Select(t => $"\"{t.Trim()}\"");
            tagsArray = "new[] { " + string.Join(", ", tagEntries) + " }";
        }

        // 转义描述中的换行
        string escapedDesc = _createDescription.Replace("\n", "\\n");
        if (string.IsNullOrWhiteSpace(escapedDesc))
            escapedDesc = $"{_createToolName} 工具";

        // 替换模板内容
        string content = template;
        content = content.Replace("示例工具", _createToolName);
        content = content.Replace("MyToolTemplate", _createClassName);
        content = content.Replace("编辑器工具", _createCategory);
        content = content.Replace(
            "Description = \"这是一个示例工具模板，展示如何创建新工具。\\n\\n功能说明写在这里，支持多行描述。\"",
            $"Description = \"{escapedDesc}\"");
        content = content.Replace("Icon = \"⚙\"", $"Icon = \"{_createIcon}\"");
        content = content.Replace("Tags = new[] { \"示例\", \"模板\" }", $"Tags = {tagsArray}");
        // 快捷键：有值则替换默认行，无值则移除该行
        if (!string.IsNullOrWhiteSpace(_createShortcut))
            content = content.Replace("Shortcut = \"Ctrl+Shift+T\"", $"Shortcut = \"{_createShortcut.Trim()}\"");
        else
            content = content.Replace("\n        Shortcut = \"Ctrl+Shift+T\",\n", "\n");
        content = content.Replace("Priority = 99)", "Priority = 0)");

        // 移除模板注释块（前25行左右）
        var lines = content.Split('\n').ToList();
        int start = lines.FindIndex(l => l.Contains("═══════════"));
        if (start >= 0)
        {
            int end = lines.FindIndex(start + 1, l => l.Contains("═══════════"));
            if (end >= 0 && end + 1 < lines.Count)
                lines.RemoveRange(start, end - start + 2); // 包含后面的空行
        }
        content = string.Join("\n", lines);

        // 写入文件
        System.IO.File.WriteAllText(absPath, content, System.Text.Encoding.UTF8);
        AssetDatabase.Refresh();

        Debug.Log($"[UnityToolsHub] 创建工具成功：{filePath}");

        // 刷新工具列表
        _showCreateForm = false;
        DiscoverTools();

        // 尝试选中新工具
        _toolIndex.TryGetValue(_createClassName, out var newTool);
        if (newTool != null)
        {
            _selectedTool = newTool;
            _selectedCategory = _categories.FirstOrDefault(c => c.tools.Contains(newTool));
        }

        // 重置表单
        _createToolName = "新工具";
        _createClassName = "NewTool";
        _createDirectory = "Assets/Editor/Tools";
        _createDescription = "";
        _createCategory = "编辑器工具";
        _createIcon = "⚙";
        _createTags = "";
        _createShortcut = "";
    }

    /// <summary>
    /// 删除工具脚本文件
    /// 通过 MonoScript 查找脚本资产路径，删除 .cs 文件和对应的 .meta 文件
    /// </summary>
    private void DeleteToolFile(ToolEntry tool)
    {
        if (tool == null || string.IsNullOrEmpty(tool.typeName))
        {
            EditorUtility.DisplayDialog("提示", "此工具没有关联的脚本文件，无法删除。", "确定");
            return;
        }

        // 查找类型
        var type = FindType(tool.typeName);
        if (type == null)
        {
            EditorUtility.DisplayDialog("提示", $"找不到类型 {tool.typeName}，可能已被删除。", "确定");
            return;
        }

        // 通过 MonoScript 查找脚本资产路径
        var monoScripts = Resources.FindObjectsOfTypeAll<MonoScript>();
        string assetPath = null;
        foreach (var ms in monoScripts)
        {
            if (ms.GetClass() == type)
            {
                assetPath = AssetDatabase.GetAssetPath(ms);
                break;
            }
        }

        if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".cs"))
        {
            EditorUtility.DisplayDialog("提示", $"无法定位 {tool.name} 的脚本文件。\n\n该工具可能来自插件包，不允许删除。", "确定");
            return;
        }

        // 确认删除
        bool confirmed = EditorUtility.DisplayDialog(
            "删除工具",
            $"确定要删除工具「{tool.name}」吗？\n\n文件：{assetPath}\n\n此操作不可撤销！",
            "删除", "取消");

        if (!confirmed) return;

        // 删除 .cs 文件和 .meta 文件
        string absPath = Application.dataPath + "/" + assetPath.Substring("Assets/".Length);
        string metaPath = absPath + ".meta";

        try
        {
            if (System.IO.File.Exists(absPath))
                System.IO.File.Delete(absPath);
            if (System.IO.File.Exists(metaPath))
                System.IO.File.Delete(metaPath);

            AssetDatabase.Refresh();
            Debug.Log($"[UnityToolsHub] 已删除工具：{tool.name} ({assetPath})");

            // 清除选中状态
            if (_selectedTool != null && _selectedTool.typeName == tool.typeName)
            {
                _selectedTool = null;
                _selectedCategory = null;
            }

            // 清除使用统计
            _usageStats.tools.RemoveAll(e => e.key == tool.typeName);
            SaveUsageStats();

            // 重新发现工具
            DiscoverTools();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UnityToolsHub] 删除工具失败：{ex.Message}");
            EditorUtility.DisplayDialog("错误", $"删除失败：{ex.Message}", "确定");
        }
    }
    #endregion
}
#endif
