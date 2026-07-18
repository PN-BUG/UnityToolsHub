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
        _defaultCategoryNames.Clear();

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
                    originalCategory = attr.Category,
                    typeName   = type.FullName,
                    icon       = string.IsNullOrEmpty(attr.Icon) ? GetCategoryIcon(attr.Category) : attr.Icon,
                    tags       = attr.Tags,
                    shortcut   = attr.Shortcut,
                    priority   = attr.Priority,
                    author     = attr.Author ?? "",
                    authorLink = attr.AuthorLink ?? "",
                    isThirdParty = attr.IsThirdParty
                });
            }
        }

        // ── 2. 同步第三方工具注册表 + 过滤禁用的 ────────────
        bool registryDirty = false;
        for (int i = discovered.Count - 1; i >= 0; i--)
        {
            var t = discovered[i];
            if (!t.isThirdParty) continue;

            // 同步到注册表
            var existing = _thirdPartyRegistry.Find(t.typeName);
            if (existing == null)
            {
                _thirdPartyRegistry.AddOrUpdate(new ThirdPartyToolState
                {
                    typeName = t.typeName,
                    toolName = t.name,
                    author = t.author,
                    authorLink = t.authorLink,
                    description = t.description,
                    category = t.category,
                    scriptPath = "",
                    isEnabled = false // 默认禁用
                });
                registryDirty = true;
            }
            else
            {
                // 更新元数据但保留 isEnabled
                existing.toolName = t.name;
                existing.author = t.author;
                existing.authorLink = t.authorLink;
                existing.description = t.description;
                existing.category = t.category;
                registryDirty = true;
            }

            // 未启用的第三方工具不加入分类列表
            if (!_thirdPartyRegistry.IsEnabled(t.typeName))
                discovered.RemoveAt(i);
        }
        if (registryDirty) SaveThirdPartyRegistry();

        // ── 3. 按分类分组 ────────────────────────────────
        // 排序规则：使用频率高的分类在前（频率相同则按 Priority）
        var groups = discovered.GroupBy(t => t.category)
            .OrderByDescending(g => _usageStats.GetCategoryCount(g.Key))
            .ThenBy(g => g.Min(t => t.priority))
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

            // 工具排序：使用频率高的在前（频率相同则按缓存的 Priority）
            var tools = group
                .OrderByDescending(t => _usageStats.GetToolCount(t.typeName))
                .ThenBy(t => t.priority)
                .ToList();
            var node = new CategoryNode { name = catName, icon = catIcon, accent = accent };
            node.tools.AddRange(tools);
            _categories.Add(node);
            _defaultCategoryNames.Add(catName);
        }

        // ── 4. 特殊工具（无 EditorWindow 类，仅菜单项）──
        AddSpecialTools();

        // ── 5. 构建工具索引 + 缓存总数 + 快捷键索引（避免每帧遍历）──
        _toolIndex.Clear();
        _shortcutIndex.Clear();
        _totalToolCount = 0;
        foreach (var cat in _categories)
        {
            foreach (var tool in cat.tools)
            {
                if (!string.IsNullOrEmpty(tool.typeName))
                    _toolIndex[tool.typeName] = tool;
                _totalToolCount++;

                // 构建快捷键索引
                var sc = GetEffectiveShortcut(tool.typeName);
                if (sc.IsValid)
                    _shortcutIndex[sc] = tool;
            }
        }
    }

    private static int GetPriority(ToolEntry t)
    {
        // Priority 在 DiscoverTools() 时已缓存到 ToolEntry.priority
        // 保留此方法供外部调用（如需要重新计算时）
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
            originalCategory = "路径工具",
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
            originalCategory = category,
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
            _defaultCategoryNames.Add(categoryName);
        }
        return cat;
    }
    #endregion

    #region 类型查找
    private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterTypeCacheCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () => _typeCache.Clear();
    }

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

    /// <summary>通过 MonoScript 查找类型对应的脚本资产路径</summary>
    private static string FindScriptPathForType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        var type = FindType(typeName);
        if (type == null) return null;

        var monoScripts = Resources.FindObjectsOfTypeAll<MonoScript>();
        foreach (var ms in monoScripts)
        {
            if (ms.GetClass() == type)
            {
                string path = AssetDatabase.GetAssetPath(ms);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".cs"))
                    return path;
            }
        }
        return null;
    }

    /// <summary>在代码编辑器中打开脚本文件</summary>
    private static void OpenScriptFile(string scriptPath)
    {
        if (string.IsNullOrEmpty(scriptPath) || !scriptPath.EndsWith(".cs"))
        {
            Debug.LogWarning("[UnityToolsHub] 无法打开脚本：路径无效");
            return;
        }
        var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (asset != null)
            AssetDatabase.OpenAsset(asset);
        else
            Debug.LogWarning($"[UnityToolsHub] 无法加载脚本资产：{scriptPath}");
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

        // 作者/链接
        if (!string.IsNullOrWhiteSpace(_createAuthor))
            content = content.Replace(
                "// Author = \"你的名字\",           // 可选: 工具作者",
                $"Author = \"{EscapeCSharpString(_createAuthor.Trim())}\",");
        if (!string.IsNullOrWhiteSpace(_createAuthorLink))
            content = content.Replace(
                "// AuthorLink = \"https://...\",     // 可选: 作者主页/仓库链接",
                $"AuthorLink = \"{EscapeCSharpString(_createAuthorLink.Trim())}\",");

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
        _createAuthor = "";
        _createAuthorLink = "";
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

    #region 扫描非 HubTool 编辑器扩展
    /// <summary>
    /// 扫描指定目录下的 .cs 文件，找出继承 EditorWindow 但没有 [ToolInfo] 特性的类。
    /// 使用正则解析源码，无需编译加载。
    /// </summary>
    private void ScanDirectoryForNonHubTools(string scanDir, bool recursive)
    {
        _addToolCandidates.Clear();
        _addToolSelectedIndex = -1;
        _addToolScanError = "";

        // 解析为绝对路径
        string absDir = scanDir;
        if (scanDir.StartsWith("Assets/"))
            absDir = Application.dataPath + "/" + scanDir.Substring("Assets/".Length);

        if (!System.IO.Directory.Exists(absDir))
        {
            _addToolScanError = $"目录不存在：{scanDir}";
            return;
        }

        var searchOption = recursive
            ? System.IO.SearchOption.AllDirectories
            : System.IO.SearchOption.TopDirectoryOnly;

        string[] csFiles;
        try
        {
            csFiles = System.IO.Directory.GetFiles(absDir, "*.cs", searchOption);
        }
        catch (System.Exception ex)
        {
            _addToolScanError = $"扫描失败：{ex.Message}";
            return;
        }

        // 收集已注册的 ToolInfo 类型名（用于排除）
        var registeredTypes = new HashSet<string>();
        foreach (var cat in _categories)
            foreach (var tool in cat.tools)
                if (!string.IsNullOrEmpty(tool.typeName))
                    registeredTypes.Add(tool.typeName);

        // 正则模式
        // 匹配 namespace（可选）
        var rxNamespace = new System.Text.RegularExpressions.Regex(
            @"namespace\s+([\w.]+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        // 匹配 class/struct 声明继承 EditorWindow
        var rxClass = new System.Text.RegularExpressions.Regex(
            @"(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+)*class\s+(\w+)\s*:\s*([\w.,\s]+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        // 匹配 [ToolInfo] 特性
        var rxToolInfo = new System.Text.RegularExpressions.Regex(
            @"\[ToolInfo\s*\(",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var filePath in csFiles)
        {
            string content;
            try
            {
                content = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            }
            catch { continue; }

            // 快速跳过：不包含 EditorWindow 的文件
            if (!content.Contains("EditorWindow")) continue;
            // 已有 [ToolInfo] 的跳过
            if (rxToolInfo.IsMatch(content)) continue;

            // 解析 namespace
            string ns = "";
            var nsMatch = rxNamespace.Match(content);
            if (nsMatch.Success)
                ns = nsMatch.Groups[1].Value;

            // 查找继承 EditorWindow 的类
            var classMatches = rxClass.Matches(content);
            foreach (System.Text.RegularExpressions.Match cm in classMatches)
            {
                string className = cm.Groups[1].Value;
                string bases = cm.Groups[2].Value;

                // 检查是否继承 EditorWindow
                var baseParts = bases.Split(',');
                bool isEditorWindow = false;
                string baseClassName = "";
                foreach (var bp in baseParts)
                {
                    var trimmed = bp.Trim();
                    if (trimmed == "EditorWindow")
                    {
                        isEditorWindow = true;
                        baseClassName = "EditorWindow";
                        break;
                    }
                    // 也支持继承自 ToolEditorWindow 等间接基类
                    if (trimmed == "ToolEditorWindow")
                    {
                        isEditorWindow = true;
                        baseClassName = "ToolEditorWindow";
                        break;
                    }
                }

                if (!isEditorWindow) continue;

                // 构建完整类型名
                string fullTypeName = string.IsNullOrEmpty(ns)
                    ? className
                    : $"{ns}.{className}";

                // 排除已注册的
                if (registeredTypes.Contains(fullTypeName)) continue;
                // 排除 UnityToolsHub 自身
                if (className == "UnityToolsHub") continue;

                // 提取已有注释（文件头部连续 // 行）
                string existingDesc = ExtractFileHeaderComment(content);

                // 相对路径
                string relPath = filePath;
                if (filePath.StartsWith(Application.dataPath))
                    relPath = "Assets" + filePath.Substring(Application.dataPath.Length);
                relPath = relPath.Replace('\\', '/');

                _addToolCandidates.Add(new AddToolCandidate
                {
                    filePath = relPath,
                    absPath = filePath,
                    className = className,
                    baseClass = baseClassName,
                    namespaceName = ns,
                    fullTypeName = fullTypeName,
                    existingDescription = existingDesc
                });
            }
        }

        // 按类名排序
        _addToolCandidates.Sort((a, b) => string.Compare(a.className, b.className,
            System.StringComparison.OrdinalIgnoreCase));

        // 扫描完成但未找到候选时给出提示
        if (_addToolCandidates.Count == 0)
            _addToolScanError = $"扫描完成，在 {scanDir} 中未发现可添加的 EditorWindow 扩展（{(recursive ? "含" : "不含")}子目录）";
    }

    /// <summary>提取文件头部的连续注释行（// 开头）</summary>
    private static string ExtractFileHeaderComment(string content)
    {
        var lines = content.Split('\n');
        var commentLines = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("//"))
            {
                var text = trimmed.TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(text))
                    commentLines.Add(text);
            }
            else if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
            {
                // 遇到非注释非预处理指令行停止
                break;
            }
            // 跳过空行和 #if 等预处理指令继续收集
        }
        if (commentLines.Count == 0) return "";
        // 最多取前3行
        int count = Mathf.Min(commentLines.Count, 3);
        return string.Join("\n", commentLines.Take(count));
    }
    #endregion

    #region 自动添加 [ToolInfo] 特性
    /// <summary>
    /// 给指定的 .cs 文件自动插入 [ToolInfo] 特性。
    /// 在 class 声明行的上方插入特性声明。
    /// </summary>
    private bool AddToolInfoToScript(AddToolCandidate candidate, string toolName,
        string category, string description, string icon, string[] tags, string shortcut,
        string author = "", string authorLink = "", bool isThirdParty = false)
    {
        try
        {
            string content = System.IO.File.ReadAllText(candidate.absPath, System.Text.Encoding.UTF8);
            var lines = content.Split('\n').ToList();

            // 找到目标 class 声明行
            int classLineIdx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                // 匹配 class ClassName : BaseClass
                if (System.Text.RegularExpressions.Regex.IsMatch(lines[i],
                    $@"(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+)*class\s+{System.Text.RegularExpressions.Regex.Escape(candidate.className)}\s*:"))
                {
                    classLineIdx = i;
                    break;
                }
            }

            if (classLineIdx < 0)
            {
                _addToolScanError = $"找不到类声明：{candidate.className}";
                return false;
            }

            // 获取 class 行的缩进
            string indent = "";
            foreach (char c in lines[classLineIdx])
            {
                if (c == ' ' || c == '\t') indent += c;
                else break;
            }

            // 构建 [ToolInfo] 特性字符串
            var attrParts = new List<string>();
            attrParts.Add($"Description = \"{EscapeCSharpString(description)}\"");

            if (!string.IsNullOrEmpty(icon) && icon != "⚙")
                attrParts.Add($"Icon = \"{icon}\"");

            if (tags != null && tags.Length > 0)
            {
                var tagEntries = tags.Select(t => $"\"{t}\"");
                attrParts.Add($"Tags = new[] {{ {string.Join(", ", tagEntries)} }}");
            }

            if (!string.IsNullOrEmpty(shortcut))
                attrParts.Add($"Shortcut = \"{shortcut}\"");

            if (!string.IsNullOrEmpty(author))
                attrParts.Add($"Author = \"{EscapeCSharpString(author)}\"");

            if (!string.IsNullOrEmpty(authorLink))
                attrParts.Add($"AuthorLink = \"{EscapeCSharpString(authorLink)}\"");

            if (isThirdParty)
                attrParts.Add("IsThirdParty = true");

            string attrLine;
            if (attrParts.Count <= 2)
            {
                // 短特性：单行
                attrLine = $"{indent}[ToolInfo(\"{EscapeCSharpString(toolName)}\", \"{EscapeCSharpString(category)}\", {string.Join(", ", attrParts)})]";
            }
            else
            {
                // 长特性：多行
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{indent}[ToolInfo(\"{EscapeCSharpString(toolName)}\", \"{EscapeCSharpString(category)}\",");
                for (int i = 0; i < attrParts.Count; i++)
                {
                    string comma = i < attrParts.Count - 1 ? "," : "";
                    sb.AppendLine($"{indent}    {attrParts[i]}{comma}");
                }
                sb.Append($"{indent})]");
                attrLine = sb.ToString();
            }

            // 在 class 行上方插入
            lines.Insert(classLineIdx, attrLine);

            // 写回文件
            string newContent = string.Join("\n", lines);
            System.IO.File.WriteAllText(candidate.absPath, newContent, System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[UnityToolsHub] 已为 {candidate.className} 添加 [ToolInfo] 特性：{candidate.filePath}");

            // 第三方工具注册到注册表（默认禁用）
            if (isThirdParty)
            {
                _thirdPartyRegistry.AddOrUpdate(new ThirdPartyToolState
                {
                    typeName = candidate.fullTypeName,
                    toolName = toolName,
                    author = author ?? "",
                    authorLink = authorLink ?? "",
                    description = description,
                    category = category,
                    scriptPath = candidate.filePath,
                    isEnabled = false
                });
                SaveThirdPartyRegistry();
                Debug.Log($"[UnityToolsHub] 第三方工具已注册（默认禁用）：{candidate.className}");
            }

            return true;
        }
        catch (System.Exception ex)
        {
            _addToolScanError = $"写入失败：{ex.Message}";
            Debug.LogError($"[UnityToolsHub] 添加 ToolInfo 失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>转义 C# 字符串中的特殊字符</summary>
    private static string EscapeCSharpString(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
    #endregion

    #region 从候选条目填充导入表单
    /// <summary>选中候选条目后，自动填充导入表单的默认值</summary>
    private void FillAddToolFormFromCandidate(int index)
    {
        _addToolSelectedIndex = index;
        if (index < 0 || index >= _addToolCandidates.Count)
        {
            _addToolName = "";
            _addToolClassName = "";
            _addToolDescription = "";
            return;
        }

        var c = _addToolCandidates[index];
        _addToolClassName = c.className;
        _addToolName = DeriveToolNameFromClass(c.className);
        _addToolDescription = c.existingDescription ?? "";
        _addToolCategory = "编辑器工具";
        _addToolIcon = "⚙";
        _addToolTags = "";
        _addToolShortcut = "";
        _addToolAuthor = "";
        _addToolAuthorLink = "";
        _addToolIsThirdParty = false;
    }

    /// <summary>从类名反推可读的工具名（驼峰 → 空格分隔）</summary>
    private static string DeriveToolNameFromClass(string className)
    {
        if (string.IsNullOrEmpty(className)) return "未命名工具";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < className.Length; i++)
        {
            char c = className[i];
            if (i > 0 && c >= 'A' && c <= 'Z')
                sb.Append(' ');
            sb.Append(c);
        }
        // 去除常见后缀
        string result = sb.ToString();
        result = result.Replace("Window", "").Replace("Editor", "").Replace("Tool", "").Trim();
        return string.IsNullOrEmpty(result) ? className : result;
    }
    #endregion

    #region 第三方工具导入与卸载
    /// <summary>判断字符串是否为 Git URL</summary>
    private static bool IsGitUrl(string value)
    {
        return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从 Git URL 导入第三方工具包</summary>
    private void ImportThirdPartyFromGit(string gitUrl, string toolName, string author, string authorLink)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            _importStatus = "Git URL 不能为空";
            return;
        }

        // 去重检查
        var existing = _thirdPartyRegistry.FindByGitUrl(gitUrl);
        if (existing != null)
        {
            _importStatus = $"该 Git URL 已导入：{existing.toolName}";
            return;
        }

        _isImporting = true;
        _importStatus = "正在从 Git 导入，请稍候...";

        // 使用 Unity PackageManager Client.Add(gitUrl)
        var request = UnityEditor.PackageManager.Client.Add(gitUrl);
        WaitForPackageRequest(request, gitUrl, (success) =>
        {
            _isImporting = false;
            if (success)
            {
                // 注册到 ThirdPartyToolRegistry
                var state = new ThirdPartyToolState
                {
                    typeName = gitUrl,  // Git 导入在编译前用 URL 作为临时标识
                    toolName = string.IsNullOrWhiteSpace(toolName) ? System.IO.Path.GetFileNameWithoutExtension(gitUrl) : toolName,
                    author = author ?? "",
                    authorLink = authorLink ?? "",
                    description = "从 Git 导入的第三方工具",
                    category = "第三方工具",
                    scriptPath = "",
                    isEnabled = false,
                    importSource = "git",
                    gitUrl = gitUrl,
                    packagePath = gitUrl,
                    installPath = "",
                    isInstalled = true
                };
                _thirdPartyRegistry.AddOrUpdate(state);
                SaveThirdPartyRegistry();

                _importStatus = $"✅ 已从 Git 导入：{state.toolName}";
                _showImportForm = false;
                _importGitUrl = "";
                _importToolName = "";

                // 等待编译完成后重新发现工具
                DiscoverTools();
                Repaint();
            }
            else
            {
                _importStatus = "❌ Git 导入失败，请检查 URL 或网络连接";
            }
        });
    }

    /// <summary>从本地路径导入第三方工具包</summary>
    private void ImportThirdPartyFromLocal(string localPath, string toolName, string author, string authorLink)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            _importStatus = "本地路径不能为空";
            return;
        }

        // 规范化路径
        localPath = localPath.Replace('\\', '/');

        // 判断是 UPM 包（含 package.json）还是纯 .cs 文件
        bool isDirectory = System.IO.Directory.Exists(localPath);
        bool isFile = System.IO.File.Exists(localPath) && localPath.EndsWith(".cs");
        bool hasPackageJson = isDirectory && System.IO.File.Exists(System.IO.Path.Combine(localPath, "package.json"));

        if (!isDirectory && !isFile)
        {
            _importStatus = "路径无效：不是有效的目录或 .cs 文件";
            return;
        }

        // 去重
        var existing = _thirdPartyRegistry.FindByName(toolName);
        if (existing != null && existing.importSource == "local" && existing.packagePath == localPath)
        {
            _importStatus = $"该路径已导入：{existing.toolName}";
            return;
        }

        if (hasPackageJson)
        {
            // UPM 包：使用 Client.Add("file:" + path)
            _isImporting = true;
            _importStatus = "正在导入本地 UPM 包...";

            string identifier = "file:" + localPath;
            var request = UnityEditor.PackageManager.Client.Add(identifier);
            WaitForPackageRequest(request, localPath, (success) =>
            {
                _isImporting = false;
                if (success)
                {
                    var state = new ThirdPartyToolState
                    {
                        typeName = localPath,
                        toolName = string.IsNullOrWhiteSpace(toolName) ? System.IO.Path.GetFileName(localPath) : toolName,
                        author = author ?? "",
                        authorLink = authorLink ?? "",
                        description = "从本地导入的第三方工具包",
                        category = "第三方工具",
                        scriptPath = "",
                        isEnabled = false,
                        importSource = "local",
                        gitUrl = "",
                        packagePath = localPath,
                        installPath = localPath,
                        isInstalled = true
                    };
                    _thirdPartyRegistry.AddOrUpdate(state);
                    SaveThirdPartyRegistry();

                    _importStatus = $"✅ 已导入本地包：{state.toolName}";
                    _showImportForm = false;
                    _importLocalPath = "";
                    _importToolName = "";

                    DiscoverTools();
                    Repaint();
                }
                else
                {
                    _importStatus = "❌ 本地包导入失败，请检查 package.json";
                }
            });
        }
        else if (isFile)
        {
            // 纯 .cs 文件：注册记录，等待用户手动添加 [ToolInfo]
            var state = new ThirdPartyToolState
            {
                typeName = localPath,
                toolName = string.IsNullOrWhiteSpace(toolName) ? System.IO.Path.GetFileNameWithoutExtension(localPath) : toolName,
                author = author ?? "",
                authorLink = authorLink ?? "",
                description = "从本地导入的脚本文件",
                category = "第三方工具",
                scriptPath = localPath,
                isEnabled = false,
                importSource = "local",
                gitUrl = "",
                packagePath = localPath,
                installPath = localPath,
                isInstalled = true
            };
            _thirdPartyRegistry.AddOrUpdate(state);
            SaveThirdPartyRegistry();

            _importStatus = $"✅ 已导入脚本：{state.toolName}";
            _showImportForm = false;
            _importLocalPath = "";
            _importToolName = "";
            DiscoverTools();
            Repaint();
        }
        else if (isDirectory)
        {
            // 目录但无 package.json：扫描 .cs 文件
            _importStatus = "目录中未找到 package.json，将作为脚本目录导入";

            var state = new ThirdPartyToolState
            {
                typeName = localPath,
                toolName = string.IsNullOrWhiteSpace(toolName) ? System.IO.Path.GetFileName(localPath) : toolName,
                author = author ?? "",
                authorLink = authorLink ?? "",
                description = "从本地目录导入",
                category = "第三方工具",
                scriptPath = "",
                isEnabled = false,
                importSource = "local",
                gitUrl = "",
                packagePath = localPath,
                installPath = localPath,
                isInstalled = true
            };
            _thirdPartyRegistry.AddOrUpdate(state);
            SaveThirdPartyRegistry();

            _importStatus = $"✅ 已导入目录：{state.toolName}";
            _showImportForm = false;
            _importLocalPath = "";
            _importToolName = "";
            DiscoverTools();
            Repaint();
        }
    }

    /// <summary>异步等待 PackageManager 请求完成</summary>
    private void WaitForPackageRequest(UnityEditor.PackageManager.Requests.Request request, string identifier, Action<bool> onComplete)
    {
        EditorApplication.CallbackFunction onUpdate = null;
        onUpdate = () =>
        {
            if (request.IsCompleted)
            {
                bool success = request.Status == UnityEditor.PackageManager.StatusCode.Success;
                if (!success)
                {
                    var error = (request as UnityEditor.PackageManager.Requests.AddRequest)?.Error
                             ?? (request as UnityEditor.PackageManager.Requests.RemoveRequest)?.Error;
                    Debug.LogError($"[UnityToolsHub] 包操作失败: {identifier}\n{error?.message}");
                }
                EditorApplication.update -= onUpdate;
                onComplete?.Invoke(success);
            }
        };
        EditorApplication.update += onUpdate;
    }

    /// <summary>卸载第三方工具包</summary>
    private void UninstallThirdPartyTool(ThirdPartyToolState state)
    {
        if (state == null) return;

        // Git / UPM 本地包：使用 Client.Remove
        if (state.importSource == "git" || (state.importSource == "local" && !string.IsNullOrEmpty(state.packagePath) && state.packagePath != state.scriptPath))
        {
            // 从 packagePath 提取 UPM 包名
            string packageName = state.packagePath;
            if (IsGitUrl(packageName))
            {
                // Git URL 包：需要从已安装的包列表中查找包名
                var listRequest = UnityEditor.PackageManager.Client.List(true);
                EditorApplication.CallbackFunction onUpdate = null;
                onUpdate = () =>
                {
                    if (listRequest.IsCompleted)
                    {
                        EditorApplication.update -= onUpdate;
                        if (listRequest.Status == UnityEditor.PackageManager.StatusCode.Success)
                        {
                            // 找到匹配的包
                            string foundName = null;
                            foreach (var pkg in listRequest.Result)
                            {
                                if (pkg.source == UnityEditor.PackageManager.PackageSource.Git
                                    && !string.IsNullOrEmpty(pkg.resolvedPath)
                                    && state.gitUrl != null
                                    && pkg.packageId != null
                                    && pkg.packageId.Contains(state.gitUrl))
                                {
                                    foundName = pkg.name;
                                    break;
                                }
                            }
                            if (!string.IsNullOrEmpty(foundName))
                            {
                                var removeReq = UnityEditor.PackageManager.Client.Remove(foundName);
                                WaitForPackageRequest(removeReq, foundName, (success) =>
                                {
                                    if (success)
                                    {
                                        _thirdPartyRegistry.Remove(state.typeName);
                                        SaveThirdPartyRegistry();
                                        DiscoverTools();
                                        Repaint();
                                    }
                                });
                            }
                            else
                            {
                                // 找不到包名，仅从注册表移除
                                _thirdPartyRegistry.Remove(state.typeName);
                                SaveThirdPartyRegistry();
                                DiscoverTools();
                                Repaint();
                            }
                        }
                    }
                };
                EditorApplication.update += onUpdate;
            }
            else if (packageName.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                // 本地 file: 包
                var removeReq = UnityEditor.PackageManager.Client.Remove(packageName);
                WaitForPackageRequest(removeReq, packageName, (success) =>
                {
                    _thirdPartyRegistry.Remove(state.typeName);
                    SaveThirdPartyRegistry();
                    DiscoverTools();
                    Repaint();
                });
            }
            else
            {
                // 无法确定包名，仅从注册表移除
                _thirdPartyRegistry.Remove(state.typeName);
                SaveThirdPartyRegistry();
                DiscoverTools();
                Repaint();
            }
        }
        else
        {
            // 纯 .cs 文件或手动导入：仅从注册表移除（不删除文件）
            _thirdPartyRegistry.Remove(state.typeName);
            SaveThirdPartyRegistry();
            DiscoverTools();
            Repaint();
        }
    }
    #endregion
}
#endif
