# Unity Tools Hub
<img width="1368" height="1222" alt="image" src="https://github.com/user-attachments/assets/19a5c76a-64e7-4302-8149-21b733f3518f" />

Unity编辑器工具集合管理器，提供工具自动发现、分类展示、快捷启动等功能。

## 功能特性

- **自动发现**：扫描所有程序集中带有 `[ToolInfo]` 特性的 `EditorWindow` 类，Priority 在发现时缓存避免排序时反射
- **分类管理**：按功能分类展示工具，支持自定义分类图标和颜色
- **文件夹式分类**：支持拖拽工具切换分类、拖拽分类排序、新建/重命名/删除自定义分类
- **快速搜索**：支持关键字搜索和标签过滤
- **排序方式**：支持按名称（默认）、最近使用、最常使用排序
- **快捷键**：为常用工具绑定键盘快捷键，O(1) 字典查找导航
- **使用统计**：记录工具使用频率，常用工具自动置顶，O(1) 字典查找
- **隐藏管理**：支持隐藏不需要的工具和分类，一键恢复默认分类
- **第三方工具管理**：类似 Unity Package Manager 的管理界面，支持从本地文件夹或 Git URL 导入第三方工具包，默认禁用确保安全
- **脚本信息展示**：工具详情页显示脚本名称、路径，支持一键打开脚本
- **作者信息**：支持在 `[ToolInfo]` 中标注作者和链接，详情页可点击跳转
- **性能优化**：消除 OnGUI 每帧 GUIStyle/GUIContent 分配，反射元数据构造时缓存
- **Odin Inspector 兼容**：自动检测 Odin，有则使用原生属性渲染，无则通过兼容层桩类型+反射绘制器回退

## 快速开始

### 1. 打开 Hub

菜单：`Window > Unity Tools Hub`

### 2. 创建自定义工具

在 `EditorWindow` 类上添加 `[ToolInfo]` 特性：

```csharp
using UnityEditor;
using UnityEngine;

[ToolInfo("我的工具", "自定义工具",
    Description = "工具功能描述",
    Icon = "🔧",
    Tags = new[] { "关键词1", "关键词2" },
    Shortcut = "Ctrl+Shift+M",
    Priority = 0)]
public class MyCustomTool : EditorWindow
{
    [MenuItem("Window/My Custom Tool")]
    public static void ShowWindow()
    {
        GetWindow<MyCustomTool>("我的工具");
    }

    private void OnGUI()
    {
        // 工具界面代码
    }
}
```

### 3. 分类管理

左侧面板支持文件夹式分类管理：

- **拖拽工具**：按住工具项拖动到目标分类，可跨分类移动
- **拖拽分类**：按住分类色条拖动，可调整分类顺序
- **新建分类**：点击底部「+ 分类」按钮，输入名称、选择图标和颜色
- **重命名/删除**：右键分类头，选择对应操作
- **恢复默认**：右键分类头 →「还原默认分类」，将工具移回自动发现的默认分类
- **隐藏工具**：右键工具项 →「隐藏」，或点击底部「管理隐藏项」查看和恢复
- **排序切换**：左上角排序按钮切换按名称 / 最近使用 / 最常使用
- **折叠/展开**：点击分类头折叠，或使用顶部展开/折叠全部按钮

### 4. ToolInfo 参数说明

| 参数 | 说明 |
|------|------|
| `Name` | 工具显示名称（必填） |
| `Category` | 所属分类（必填） |
| `Description` | 功能描述，显示在详情面板 |
| `Icon` | 工具图标（Emoji 或 BMP 安全字符，默认 "⚙"） |
| `Tags` | 搜索标签数组 |
| `Shortcut` | 快捷键提示文本 |
| `Priority` | 排序优先级，数字越小越靠前 |
| `Author` | 工具作者（可选），显示在详情页和第三方管理面板 |
| `AuthorLink` | 作者主页/仓库 URL（可选），点击可跳转 |
| `IsThirdParty` | 是否为第三方工具（可选，默认 false），标记后默认禁用 |

### 5. 第三方工具管理

第三方工具管理面板提供类似 Unity Package Manager 的统一管理界面：

#### 打开方式
- 顶部 toggle 栏切换到「📦 第三方工具」标签
- 或在「添加工具」面板中导入脚本时勾选「标记为第三方工具」

#### 导入第三方工具

**从 Git URL 导入**：
1. 点击「＋ 从 Git 导入」
2. 输入 Git 仓库地址（如 `https://github.com/user/repo.git`）
3. 填写工具名、作者、作者链接（可选）
4. 点击「导入」，Hub 会通过 `PackageManager.Client.Add(gitUrl)` 异步安装

**从本地路径导入**：
1. 点击「＋ 从本地导入」
2. 选择本地文件夹路径（含 `package.json` 的 UPM 包目录或含 `.cs` 文件的目录）
3. 填写工具名、作者、作者链接（可选）
4. 点击「导入」

#### 管理第三方工具

- **启用/禁用**：第三方工具默认禁用，需在管理面板中手动启用后才显示在分类列表中
- **卸载**：Git/本地 UPM 包卸载会从 Unity Package Manager 中移除；手动添加的工具仅移除注册记录
- **查看详情**：点击左侧列表中的工具，右侧显示作者、来源、安装路径、脚本路径等详细信息
- **打开脚本**：详情页可一键打开对应的 `.cs` 脚本文件

#### 第三方工具包目录结构（Git / 本地 UPM 包）

```
MyToolPackage/
├── package.json          # UPM 包描述文件
├── Editor/
│   └── MyTool.cs          # 含 [ToolInfo(IsThirdParty=true)] 的工具脚本
└── Runtime/               # 可选运行时代码
```

#### 安全模型

- 第三方工具（`IsThirdParty = true`）默认在 Hub 中**禁用**
- 禁用状态下工具不出现在分类列表中，无法通过 Hub 打开
- 需在「第三方工具管理」面板中手动启用
- 启用后工具正常显示并可使用
- 已启用的工具可随时禁用

## 内置工具

| 工具 | 分类 | 说明 |
|------|------|------|
| 框架主页 | 框架初始化 | UnityFramework 主页窗口 |
| 收藏夹 | 资产工具 | 收藏常用资源和场景对象，支持分组和搜索 |
| 资产导入过滤 | 资产工具 | 自动处理资源导入设置 |
| 文件夹规则管理 | 资产工具 | 文件夹导入规则管理 |
| 组件参数复制 | 编辑器工具 | 批量复制粘贴组件参数 |
| 样式模板 | 编辑器工具 | 样式展示与参考 |
| 字体替换 | 字体工具 | 批量替换 UGUI / TextMeshPro 字体 |
| 富文本编辑器(网页版) | 文本工具 | 浏览器版富文本编辑器，解决选区丢失问题 |
| 编码转换 | 文件工具 | 文件编码批量转换 |
| Using 管理 | 文件工具 | 扫描 .cs 文件自动补全缺失的 using 语句 |
| 批量重命名 | 文件工具 | 批量重命名资源文件 |
| 精灵图集切分 | 媒体工具 | 精灵图集自动切分 |
| 视频首帧导出 | 媒体工具 | 导出视频文件的首帧图片 |
| Git 包切换器 | 包管理工具 | Git 包版本切换 |
| 包创建器 | 项目工具 | 快速创建 UPM 包 |
| 脚本默认值同步工具 | 序列化工具 | 脚本默认值同步 |
| 加密解密工具 | 数据处理 | 加密解密工具 |
| JSON 查看与编辑 | 数据处理 | JSON 格式化查看与编辑 |
| 项目打包 | 构建工具 | 项目批量打包 |
| 测试窗口 | 调试工具 | 聚合展示场景中标记了 [Test] 的方法和字段 |

> **注意**：所有工具均通过 `[ToolInfo]` 特性自动注册，无需手动维护此列表。

## 目录结构

```
UnityToolsHub/
├── package.json
├── CHANGELOG.md
├── LICENSE
├── README.md
└── Editor/
    ├── Hub/                        # Hub 核心面板
    │   ├── UnityToolsHub.cs        # 主窗口（状态管理、生命周期、使用频率/隐藏项管理、分类管理）
    │   ├── ToolDiscovery.cs        # 工具发现（反射缓存、快捷键索引、类型查找缓存、默认分类注册）
    │   ├── LeftPanel.cs            # 左侧分类面板（文件夹管理、拖拽排序、搜索、右键菜单、对话框）
    │   ├── RightPanel.cs           # 右侧详情面板（欢迎页/详情/创建表单/隐藏项管理/第三方工具管理）
    │   ├── DataStructures.cs       # 数据结构（ToolEntry、FolderConfig、UsageStats、HiddenItems、ThirdPartyToolRegistry）
    │   ├── HubCompat.cs            # 兼容层，别名引用已迁移到 Nodin 的 Theme/Styles/Drawing
    │   ├── ShortcutBinding.cs      # 快捷键绑定结构体（解析/序列化/Event 转换）
    │   ├── ShortcutManager.cs      # 快捷键管理（录制、导航、冲突检测）
    │   └── ToolEditorWindow.cs     # 工具编辑器基类（统一深色主题、绘图工具方法）
    ├── InsidersTest/               # 内部测试工具
    │   ├── CryptoUtility.cs        # 加密工具
    │   ├── JsonViewer.cs           # JSON 查看器
    │   └── ProjectBuilder.cs       # 项目构建器
    ├── Tools/                      # 内置工具集合
    │   ├── AssetBookmarks.cs       # 收藏夹
    │   ├── AssetImportFilter.cs    # 资产导入过滤
    │   ├── ComponentParameterCopier.cs  # 组件参数复制
    │   ├── EncodingConverter.cs    # 编码转换（支持 Odin 回退原生）
    │   ├── FileRenameTool.cs       # 批量重命名
    │   ├── FontReplacer.cs         # 字体替换
    │   ├── ResourceAnalyzer/       # 资源分析器（子模块）
    │   ├── RichTextEditorWeb.html  # 富文本编辑器网页
    │   ├── RichTextEditorWebLauncher.cs  # 富文本编辑器启动器
    │   ├── UsingManager.cs         # 自动添加 Using
    │   ├── VideoFirstFrameExporter.cs  # 视频首帧导出
    │   ├── FolderRuleTool/         # 文件夹规则工具
    │   ├── FrameAnimationTool/     # 帧动画工具
    │   ├── Unity Package Creator/  # 包创建器（子包）
    │   └── Test/                   # 测试工具
    ├── PluginDetector/              # 第三方插件自动检测（独立程序集）
    │   ├── PluginAutoDetector.cs   # 通用插件检测器（支持自定义规则）
    │   └── UnityToolsHub.PluginDetector.Editor.asmdef
    ├── Setup/                      # 自动配置（独立程序集，不引用 Nodin）
    │   ├── UnityToolsHub.Setup.asmdef
    │   └── NodinSetup.cs           # [InitializeOnLoad] 自动写入 manifest.json
    ├── ToolInfoAttribute.cs        # 工具信息特性定义
    ├── UnityPathUtility.cs         # 路径工具
    ├── CreateLegacyUIMenu.cs       # Unity 版本兼容辅助
    ├── UnityFrameworkHomeWindow.cs # 框架主页窗口
    └── _NewToolTemplate.cs.txt     # 新建工具模板
```

> **注**：原 `Hub/Styles.cs`、`Hub/Theme.cs`、`Hub/DrawingUtils.cs` 已迁移至 Nodin 包的 `Editor/EditorCore/` 目录，通过 `HubCompat.cs` 透明代理。原 `Editor/Nodin/` 空目录已删除。

## Nodin 属性系统

工具使用 [Nodin](https://github.com/PN-BUG/Nodin) 作为 Inspector 属性系统，提供轻量级的属性定义和自动绘制能力：

- **属性定义**：`Nodin` 命名空间提供 `[FoldoutGroup]`、`[LabelText]`、`[Button]` 等常用属性
- **反射自动绘制**：`NodinEditor` 通过反射读取属性，自动绘制 Inspector UI
- **元数据缓存**：`NodinDrawer` 在构造时一次性读取所有字段的 Attribute 元数据（`FieldMeta`/`MethodMeta`），后续 OnGUI 仅查询缓存，避免每帧重复反射调用
- **分组预计算**：分组排序与子分组映射在构造时完成，绘制时直接遍历预计算结果
- **零配置**：作为 UPM 包自动安装，开箱即用

### 编写工具

直接使用 `Nodin` 命名空间的属性即可：

```csharp
using Nodin;
using UnityEditor;
using UnityEngine;

public class MyTool : EditorWindow
{
    [FoldoutGroup("设置")]
    [LabelText("速度")]
    public float speed = 5f;

    // NodinDrawer 自动绘制 Inspector
}
```

### 第三方插件自动检测

项目内置通用插件检测器，编辑器启动时自动扫描第三方 DLL 并管理预编译宏。支持注册自定义检测规则：

```csharp
[InitializeOnLoad]
public static class MyPluginDetector
{
    static MyPluginDetector()
    {
        PluginAutoDetector.AddPlugin(new PluginAutoDetector.PluginDefinition
        {
            DefineSymbol    = "MY_PLUGIN",
            MarkerDll       = "MyPlugin.dll",
            SearchKeyword   = "MyPlugin t:DLL",
            FoundMessage    = "检测到 MyPlugin",
            NotFoundMessage = "未检测到 MyPlugin",
        });
    }
}
```

## 包信息

| 属性 | 值 |
|------|-----|
| 包名 | `com.zko.unitytoolshub` |
| 版本 | 1.2.0 |
| Unity 版本 | 2021.3+ |
| 仓库地址 | https://github.com/PN-BUG/UnityToolsHub.git |

## 依赖关系

```
UnityToolsHub
  └── Nodin (com.zko.nodin) — 自动安装，无需手动配置
```

Nodin 依赖通过 `Editor/Setup/NodinSetup.cs` 自动处理：
- 首次加载时自动将 `com.zko.nodin` 写入 `manifest.json`
- Unity 自动解析并下载 Nodin 包
- 独立 asmdef（`UnityToolsHub.Setup`），不引用 Nodin，确保即使 Nodin 未安装也能编译

Nodin 包同时提供 `Editor/EditorCore/` 模块（`Palette`、`Theme`、`Styles`、`Drawing`），为 Hub 和各工具窗口提供统一的深色主题配色、GUIStyle 缓存、纹理与绘图工具。

## 许可证

Apache License 2.0
