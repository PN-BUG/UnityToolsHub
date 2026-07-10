# Unity Tools Hub
<img width="1368" height="1222" alt="image" src="https://github.com/user-attachments/assets/19a5c76a-64e7-4302-8149-21b733f3518f" />

Unity编辑器工具集合管理器，提供工具自动发现、分类展示、快捷启动等功能。

## 功能特性

- **自动发现**：扫描所有程序集中带有 `[ToolInfo]` 特性的 `EditorWindow` 类
- **分类管理**：按功能分类展示工具，支持自定义分类图标和颜色
- **快速搜索**：支持关键字搜索和标签过滤
- **快捷键**：为常用工具绑定键盘快捷键
- **使用统计**：记录工具使用频率，常用工具自动置顶
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

### 3. ToolInfo 参数说明

| 参数 | 说明 |
|------|------|
| `Name` | 工具显示名称（必填） |
| `Category` | 所属分类（必填） |
| `Description` | 功能描述，显示在详情面板 |
| `Icon` | 工具图标（Emoji 或特殊字符） |
| `Tags` | 搜索标签数组 |
| `Shortcut` | 快捷键提示文本 |
| `Priority` | 排序优先级，数字越小越靠前 |

## 内置工具

| 工具 | 分类 | 说明 |
|------|------|------|
| 收藏夹 | 资产工具 | 收藏常用资源和场景对象，支持分组和搜索 |
| 资产导入过滤 | 资产工具 | 自动处理资源导入设置 |
| 组件参数复制 | 编辑器工具 | 批量复制粘贴组件参数 |
| 字体替换 | 字体工具 | 批量替换 UGUI / TextMeshPro 字体 |
| 富文本编辑器(网页版) | 文本工具 | 浏览器版富文本编辑器，解决选区丢失问题 |
| 编码转换 | 文件工具 | 文件编码批量转换 |
| 自动添加 Using | 文件工具 | 扫描 .cs 文件自动补全缺失的 using 语句 |
| 批量重命名 | 文件工具 | 批量重命名资源文件 |
| 视频首帧导出 | 媒体工具 | 导出视频文件的首帧图片 |
| 包创建器 | 项目工具 | 快速创建 UPM 包 |
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
    │   ├── UnityToolsHub.cs        # 主窗口
    │   ├── ToolDiscovery.cs        # 工具发现
    │   ├── LeftPanel.cs            # 左侧分类面板
    │   ├── RightPanel.cs           # 右侧详情面板
    │   ├── DataStructures.cs       # 数据结构定义
    │   ├── DrawingUtils.cs         # 绘图工具
    │   ├── ShortcutBinding.cs      # 快捷键绑定
    │   ├── ShortcutManager.cs      # 快捷键管理
    │   ├── Styles.cs               # 样式定义
    │   └── Theme.cs                # 主题配置
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
    │   ├── UsingAdder.cs           # 自动添加 Using
    │   ├── VideoFirstFrameExporter.cs  # 视频首帧导出
    │   ├── Unity Package Creator/  # 包创建器（子包）
    │   └── Test/                   # 测试工具
    ├── OdinDetector/               # 第三方插件检测（独立程序集）
    │   ├── OdinAutoDetector.cs     # 通用插件检测器（支持自定义规则）
    │   └── UnityToolsHub.OdinDetector.Editor.asmdef
    ├── Nodin/                     # Nodin 属性层（独立包）
    │   ├── Runtime/Attributes.cs  # Nodin 命名空间特性定义
    │   └── Editor/NodinDrawer.cs  # 反射绘制器
    ├── ToolInfoAttribute.cs        # 工具信息特性定义
    ├── UnityPathUtility.cs         # 路径工具
    └── _NewToolTemplate.cs.txt     # 新建工具模板
```

## Odin Inspector 兼容

工具内置对 [Odin Inspector](https://odininspector.com/) 的可选支持：

- **自动检测**：`OdinAutoDetector` 编辑器启动时自动扫描项目中是否存在第三方插件 DLL
- **自动宏管理**：检测到 Odin 自动添加 `ODIN_INSPECTOR` 宏定义，移除后自动清理
- **属性兼容**：`Nodin` 包提供 `Nodin` 命名空间下的属性定义（`[FoldoutGroup]`、`[LabelText]`、`[Button]` 等），通过反射自动绘制 Inspector
- **反射自动绘制**：`NodinEditorWindow` 桩和 `NodinEditor` 通过反射读取属性，自动绘制 Inspector UI
- **零配置**：无需手动设置宏，开箱即用

### 工作原理

```
项目启动 → OdinAutoDetector 扫描第三方插件 DLL
  ├─ 找到 → 添加 ODIN_INSPECTOR 宏 → 使用 Odin 原生属性渲染
  └─ 未找到 → 移除 ODIN_INSPECTOR 宏 → 使用桩类型 + 反射绘制器
```

### 扩展插件检测

`OdinAutoDetector` 支持注册自定义插件检测规则：

```csharp
[InitializeOnLoad]
public static class MyPluginDetector
{
    static MyPluginDetector()
    {
        OdinAutoDetector.AddPlugin(new OdinAutoDetector.PluginDefinition
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

### 编写兼容工具

工具直接使用 `Nodin` 命名空间的属性，无需条件编译：

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

## 包信息

| 属性 | 值 |
|------|-----|
| 包名 | `com.zko.unitytoolshub` |
| 版本 | 1.0.0 |
| Unity 版本 | 2021.3+ |
| 仓库地址 | https://github.com/PN-BUG/UnityToolsHub.git |

## 依赖关系

```
UnityToolsHub
  └── Nodin (com.zko.nodin) — package.json 自动解析
```

## 许可证

Apache License 2.0
