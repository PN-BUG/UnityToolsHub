# Changelog
All notable changes to this package will be documented in this file.

## [1.4.0] - 2026-07-23
### Changed
- 移除 `HubCompat.cs` 兼容层：Hub 各文件直接引用 `Theme.*`、`Styles.*`、`Drawing.*`、`Palette.*`，消除间接别名层
- `ToolEditorWindow` 统一到 Nodin 基础设施：`CreateTex` 委托 `Palette.MakeTex`，`EnsureInit()` 先调用 `Styles.EnsureInit()`，消除重复的纹理创建与样式管理
- `UsageEntry` 新增 `lastUsed` 时间戳字段，`UsageStats` 新增 `GetToolLastUsed()` / `GetCategoryLastUsed()` 方法
- 排序模式区分：`ByRecent` 按 `lastUsed` 时间戳降序排序，`ByMostUsed` 按使用次数降序排序（此前两者逻辑完全相同）
- `Styles` 新增 5 个缓存样式（`SearchPlaceholder`、`SortButton`、`FoldButton`、`MiniLabelBoldCenter`、`CategoryTagSearch`），消除 LeftPanel / RightPanel 中每帧 `new GUIStyle()` 分配
- `Drawing.DrawRoundedRect` 在 IMGUI 下角纹理合成存在渲染伪影（角部白色噪点），回退为方形渲染

### Fixed
- 快捷键冲突检测 `FindShortcutConflict` 从 O(n) 线性遍历改为 O(1) `_shortcutIndex` 字典查找
- `DiscoverTools()` 批量预缓存所有工具的 `scriptPath`，消除 RightPanel 中每帧 `Resources.FindObjectsOfTypeAll<MonoScript>()` 调用

### Performance
- Nodin `Drawing` 纹理缓存加入上限保护：`_cornerTexCache` 上限 64 条目，`_gradientCache` 上限 32 条目，超限时清空重建，防止长时间编辑器会话中缓存无限增长
- LeftPanel 消除 8 处每帧 GUIStyle 分配（搜索占位符、排序按钮、折叠按钮、分类头、搜索标签、底部按钮 ×3）
- RightPanel 消除 4 处每帧 GUIStyle 分配（创建/添加/第三方 toggle 样式 ×3、拖放区域标签）

### Removed
- 删除 `Hub/HubCompat.cs` 及其 `.meta` 文件（兼容层别名已全部内联为直接引用）

## [1.3.0] - 2026-07-18
### Added
- 第三方工具管理系统：类似 Unity Package Manager 的左右分栏管理界面
- 从 Git URL 导入第三方工具包：通过 `PackageManager.Client.Add(gitUrl)` 异步安装
- 从本地路径导入第三方工具包：支持 UPM 包（含 package.json）和纯 .cs 文件
- 第三方工具卸载：Git/本地 UPM 包通过 `Client.Remove` 卸载，手动添加仅移除记录
- 第三方工具安全模型：`IsThirdParty=true` 的工具默认禁用，需手动启用后显示
- 工具详情页脚本信息区：显示脚本名称、路径，支持一键打开脚本
- `[ToolInfo]` 新增 `Author`、`AuthorLink`、`IsThirdParty` 属性
- 创建工具表单支持填写作者和作者链接
- 添加工具表单支持填写作者、作者链接和第三方工具标记
- 顶部 toggle 栏新增第三个标签「📦 第三方工具」
- 导入表单：Git URL / 本地路径输入、工具名、作者、作者链接
- 来源标签：左侧列表每项显示来源类型（Git/本地/手动）
- 第三方工具包目录结构规范文档

### Changed
- ThirdPartyToolState 扩展：新增 importSource、gitUrl、packagePath、installPath、isInstalled 字段
- ThirdPartyToolRegistry 新增 FindByName、FindByGitUrl 去重方法
- DrawThirdPartyManagerPanel 重写为 UPM 风格管理界面
- DrawThirdPartyToolDetails 新增来源信息、安装路径、卸载按钮
- _NewToolTemplate.cs.txt 模板新增 Author/AuthorLink/IsThirdParty 注释
- ToolInfoAttribute 注释新增第三方工具包规范

## [1.2.0] - 2026-07-18
### Added
- 文件夹式分类管理：支持拖拽工具切换分类、拖拽分类排序
- 自定义分类：新建分类（支持自定义图标和颜色）、重命名、删除
- 右键菜单：工具右键支持移动到指定分类、隐藏；分类右键支持重命名/删除/恢复默认
- 拖拽排序：工具拖拽到其他分类、分类拖拽改变顺序
- 恢复默认分类：右键菜单一键还原到自动发现的默认分类
- 分类排序：支持按名称（默认）、最近使用、最常使用排序
- 全部折叠/展开：一键折叠或展开所有分类
- 欢迎页仓库链接：点击在浏览器中打开 GitHub 仓库

### Changed
- 左侧面板全面美化：分类头深色背景、工具项缩进、分隔线、hover 高亮适配深色主题
- 底部按钮改为并排 "+工具" / "+分类" 按钮样式
- 新建分类对话框美化：深色背景、accent 色条、输入框样式优化
- 分类头视觉区分：深色背景 + 底部分隔线，与工具项层次分明
- 拖放坐标空间修复：统一使用 Event.current.mousePosition 与 GUILayoutUtility.GetRect 同一空间
- 空分类也显示在左侧面板中
- 隐藏项管理文本居中对齐

### Fixed
- 拖放工具到分类时坐标空间不一致导致无法正确放置
- 拖放高亮始终指向最后一个分类的问题
- 自动发现的分类错误显示为 [自定义]
- 创建新工具后点击其他工具无法切换的问题

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
