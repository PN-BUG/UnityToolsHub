# Changelog
All notable changes to this package will be documented in this file.

## [1.1.0] - 2026-07-11
### Changed
- NodinDrawer 重构：构造时缓存所有字段/方法的 Attribute 元数据（`FieldMeta`/`MethodMeta`），消除每帧 5+ 次 `GetCustomAttribute` 反射调用
- NodinDrawer 预计算分组排序与子分组映射，避免每帧 LINQ 遍历
- NodinDrawer 缓存 ValueDropdown 选项，复用静态 GUIContent / GUIStyle
- ToolDiscovery 缓存 `Priority` 到 `ToolEntry`，排序时不再触发 O(n log n) 次反射
- ToolDiscovery 添加 `AssemblyReloadEvents` 清理 `_typeCache`，避免程序集重载后类型查找失效
- ToolDiscovery 构建快捷键索引 `_shortcutIndex`，快捷键导航从 O(n) 遍历 → O(1) 字典查找
- UsageStats 添加 `Dictionary<string,int>` 运行时索引，`GetToolCount()` / `GetCategoryCount()` 从 O(n) → O(1)
- HiddenItems 添加 `HashSet<string>` 运行时索引，`IsToolHidden()` / `IsCategoryHidden()` 从 O(n) → O(1)
- ToolEditorWindow 消除 `DrawColoredButton` / `DrawIconButton` / `DrawTag` 的每帧 GUIStyle / GUIContent 分配
- ToolEditorWindow 修复 `DrawStatCard` 零高度问题（`GetRect(0, 0)` → `GetRect(0, 60)`）
- ToolEditorWindow `MakePrefsKey` 改用 djb2 确定性哈希，替代非确定性的 `string.GetHashCode()`
- LeftPanel / RightPanel 添加 8 个缓存 GUIStyle，消除每帧 `new GUIStyle()` 分配
- LeftPanel 修复废弃的 `GUI.skin.FindStyle`，改用 `GUI.skin.GetStyle`
- UnityToolsHub 移除 4 个反射方法中约 15 条 `Debug.Log` / `Debug.LogWarning`
- UnityToolsHub 添加 `#if UNITY_EDITOR_WIN` 平台保护，macOS/Linux 使用安全回退值
- UnityToolsHub 修复过时类文档注释

### Removed
- 删除空的 `Editor/Nodin/` 目录及其 `.meta` 文件（死遗留产物，Nodin 已作为 UPM 包独立存在）

## [1.0.0] - 2024-01-01
### Added
- 工具自动发现系统：扫描[ToolInfo]特性自动注册工具
- 分类展示面板：按类别组织工具列表
- 工具详情面板：显示工具描述、快捷键、标签等信息
- 快捷键管理：支持自定义工具快捷键
- 使用统计：记录工具使用频率，优化排序
- 内置工具集：收藏夹、资源过滤器、组件复制器、字体替换器等
- 新建工具模板：快速创建自定义工具脚本
