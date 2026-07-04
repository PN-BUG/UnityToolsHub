# Unity Tools Hub

Unity编辑器工具集合管理器，提供工具自动发现、分类展示、快捷启动等功能。

## 功能特性

- **自动发现**：扫描所有程序集中带有 `[ToolInfo]` 特性的 `EditorWindow` 类
- **分类管理**：按功能分类展示工具，支持自定义分类图标和颜色
- **快速搜索**：支持关键字搜索和标签过滤
- **快捷键**：为常用工具绑定键盘快捷键
- **使用统计**：记录工具使用频率，常用工具自动置顶

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
    ├── Tools/                      # 内置工具集合
    │   ├── AssetBookmarks.cs       # 收藏夹
    │   ├── AssetImportFilter.cs    # 资产导入过滤
    │   ├── ComponentParameterCopier.cs  # 组件参数复制
    │   ├── EncodingConverter.cs    # 编码转换
    │   ├── FileRenameTool.cs       # 批量重命名
    │   ├── FontReplacer.cs         # 字体替换
    │   ├── ResourceAnalyzer/       # 资源分析器（子模块）
    │   ├── RichTextEditorWeb.html  # 富文本编辑器网页
    │   ├── RichTextEditorWebLauncher.cs  # 富文本编辑器启动器
    │   ├── UsingAdder.cs           # 自动添加 Using
    │   ├── VideoFirstFrameExporter.cs  # 视频首帧导出
    │   ├── Unity Package Creator/  # 包创建器（子包）
    │   └── Test/                   # 测试工具
    ├── ToolInfoAttribute.cs        # 工具信息特性定义
    ├── UnityPathUtility.cs         # 路径工具
    └── _NewToolTemplate.cs.txt     # 新建工具模板
```

## 许可证

Apache License 2.0
