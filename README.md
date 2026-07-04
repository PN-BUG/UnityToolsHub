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
| 收藏夹 | 资产工具 | 收藏常用资源和场景对象 |
| 资源导入过滤 | 资产工具 | 自动处理资源导入设置 |
| 组件参数复制 | 组件工具 | 批量复制粘贴组件参数 |
| 字体替换器 | UI工具 | 批量替换项目中的字体 |
| 资源分析器 | 资产工具 | 分析资源引用和依赖 |
| 富文本编辑器 | 文本工具 | 可视化编辑富文本 |
| 编码转换器 | 文本工具 | 文件编码批量转换 |
| Using 添加器 | 代码工具 | 自动添加缺失的 using 语句 |
| 包创建器 | 项目工具 | 快速创建 UPM 包 |

## 目录结构

```
UnityToolsHub/
├── package.json
├── CHANGELOG.md
├── README.md
└── Editor/
    ├── Hub/                    # Hub 核心面板
    │   ├── UnityToolsHub.cs    # 主窗口
    │   ├── ToolDiscovery.cs    # 工具发现
    │   ├── LeftPanel.cs        # 左侧分类面板
    │   ├── RightPanel.cs       # 右侧详情面板
    │   └── ...
    ├── Tools/                  # 内置工具集合
    │   ├── AssetBookmarks.cs
    │   ├── FontReplacer.cs
    │   └── ...
    └── ToolInfoAttribute.cs    # 工具信息特性定义
```

## 许可证

MIT License
