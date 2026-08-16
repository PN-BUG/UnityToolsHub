# UnityToolsHub 第三方工具接入指南

本文面向两类读者：希望一键安装其他 Git 仓库工具的使用者，以及希望主动支持 UnityToolsHub 的工具作者。

## 一、使用者：零代码接入 Git 工具

### 前置条件

- 工具仓库是 Unity Package Manager（UPM）包，包目录中包含 `package.json`。
- 工具至少提供一个可编译的 `EditorWindow`。使用 SDK 的工具也可以声明菜单或静态方法入口。
- 仓库及其依赖支持当前 Unity 版本。

### 接入步骤

1. 打开 `UnityToolsHub > 主面板`。
2. 进入“第三方工具”，点击“从 Git 导入”。
3. 填入 Git URL，工具名、作者和作者链接均可留空。
4. 点击“导入”，等待 UPM 下载并完成 Unity 编译。
5. Hub 会自动扫描包内的 `EditorWindow`，为每个窗口生成外部注册记录。
6. 检查识别结果并手动启用需要的入口。

Hub 不会向第三方仓库写入 `[ToolInfo]`，也不会修改其脚本。

### 已经安装的 Git 包

无需重复安装。Hub 打开时会扫描 `Packages/manifest.json` 中已经解析完成的 Git UPM 包，并自动登记其中的 `EditorWindow`。识别出的入口默认禁用，在第三方工具管理页确认后启用即可。UnityToolsHub 自身会被排除。

对于以 `file:`、嵌入包或 Git submodule 方式放在 `Packages/` 下的直接依赖，UPM 会把来源标记为 Local/Embedded，而不是 Git。Hub 同样会扫描这些包。识别过程以 Unity 编译程序集的源码归属为准，因此能够发现同一个脚本文件中的多个 `EditorWindow` 类型。

纯运行时库、只包含 Inspector/Importer 的扩展或没有 `EditorWindow` 的包不会生成可打开的工具入口；这类包若需要出现在 Hub 中，应使用 SDK 声明 `menu` 或 `static` 入口。

### 批量隐藏与启用状态

“隐藏项管理”提供两个独立开关：

- **隐藏内置工具**：批量隐藏 UnityToolsHub 内置入口。
- **隐藏第三方工具**：批量隐藏当前已启用的第三方入口。

这些开关只控制 Hub 左侧列表的可见性，不会禁用工具、改变第三方管理页中的启用状态，也不会卸载 Git/本地包。关闭某个开关时，只恢复对应类型的工具，不影响另一类工具、隐藏分类或其他独立隐藏设置。

### Git URL 示例

```text
# 包在仓库根目录
https://github.com/example/unity-tool.git

# 固定 tag、分支或 commit
https://github.com/example/unity-tool.git#v1.2.0
https://github.com/example/unity-tool.git#main
https://github.com/example/unity-tool.git#a1b2c3d

# 包位于仓库子目录
https://github.com/example/unity-tools.git?path=/Packages/com.example.tool#v1.2.0
```

团队项目应优先固定 tag 或 commit，避免不同成员解析到不同代码。

### 本地包

在第三方工具页选择“从本地导入”，指向包含 `package.json` 的目录。Hub 使用 UPM `file:` 依赖安装并自动发现窗口。

### 当前边界

- 普通 Git 仓库如果没有 `package.json`，不能作为 Git UPM 包直接安装。
- 自动模式只可靠识别 `EditorWindow`；纯菜单工具建议作者使用 SDK 明确声明。
- Hub 不会自动修复第三方包缺失依赖、程序集冲突或 Unity 版本不兼容。
- 安装第三方包会让其编辑器代码在项目中执行，启用前应检查仓库来源和许可证。

## 二、作者：使用可选 SDK 主动支持

SDK 是独立包 `com.zko.unitytoolshub.sdk`，源码位于 UnityToolsHub 仓库的 `SDK~` 目录。SDK 仅包含元数据 Attribute：

- 不引用 UnityToolsHub；
- 不接管窗口生命周期；
- 不替代工具自己的菜单入口；
- 工具在没有 UnityToolsHub 时仍然正常使用。

作者应把 SDK 声明为自己的开发依赖或随包分发，并继续保留原有 `MenuItem`。

### EditorWindow 入口

```csharp
using UnityEditor;
using UnityToolsHub.SDK;

[UnityTool("Sprite Tool", "美术工具",
    Description = "批量处理 Sprite",
    Icon = "Art",
    Tags = new[] { "Sprite", "批处理" },
    Author = "Example Studio",
    AuthorLink = "https://github.com/example")]
public sealed class SpriteToolWindow : EditorWindow
{
    [MenuItem("Tools/Example/Sprite Tool")]
    public static void OpenStandalone()
    {
        GetWindow<SpriteToolWindow>("Sprite Tool");
    }
}
```

`EntryKind` 默认为 `window`，标记类型必须继承 `EditorWindow`。

### 菜单入口

适合工具已有复杂启动逻辑、不希望 Hub 直接创建窗口的情况：

```csharp
[UnityTool("Sprite Tool", "美术工具",
    EntryKind = "menu",
    MenuItem = "Tools/Example/Sprite Tool")]
public sealed class SpriteToolIntegration
{
}
```

菜单路径必须与工具自身 `[MenuItem]` 中的路径完全一致。

### 静态方法入口

```csharp
[UnityTool("Build Helper", "构建工具",
    EntryKind = "static",
    StaticMethod = "Example.Tools.BuildHelper.Open")]
public sealed class BuildHelperIntegration
{
}
```

目标方法必须是无参数静态方法，可以是 `public` 或非公开方法。推荐使用公开方法，便于测试和其他集成调用。

### SDK 字段

| 字段 | 必填 | 说明 |
|---|---:|---|
| `Name` | 是 | Hub 中的显示名称 |
| `Category` | 是 | 工具分类 |
| `Description` | 否 | 功能说明 |
| `Icon` | 否 | 文本或 Emoji 图标 |
| `Tags` | 否 | 搜索标签 |
| `Priority` | 否 | 排序优先级，越小越靠前 |
| `Author` | 否 | 作者或组织 |
| `AuthorLink` | 否 | 仓库或作者主页 |
| `EntryKind` | 否 | `window`、`menu` 或 `static` |
| `MenuItem` | menu | Unity 菜单路径 |
| `StaticMethod` | static | `命名空间.类型.方法` |

## 三、故障排查

### 安装成功但没有发现入口

1. 等待 Unity 完成编译后重新打开 Hub。
2. 确认窗口脚本位于已安装包中，并且继承 `EditorWindow`。
3. 检查 Console 是否有第三方包编译错误。
4. 对纯菜单或特殊启动方式，使用 SDK 的 `menu`/`static` 入口。

### Git 安装失败

1. 检查 URL、访问权限以及本机 Git。
2. 确认目标目录存在合法 `package.json`。
3. 子目录包使用 `?path=/包目录`。
4. 私有仓库需要预先配置 Git 凭据或 SSH key。

### 独立运行检查

发布前在未安装 UnityToolsHub 的测试工程中验证：工具自己的菜单仍能打开、SDK 不调用 Hub API、运行时代码不依赖编辑器程序集。
