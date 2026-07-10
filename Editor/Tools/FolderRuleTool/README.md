# 📂 文件夹规则管理工具

> UnityToolsHub 子工具 — 统一管理文件夹命名规范、Addressable 自动添加、贴图导入规则

---

## 🎯 概述

文件夹规则管理工具用于对项目中的资源文件夹进行批量规范化管理。用户手动创建 **ScriptableObject 配置**，**SO 放在哪个文件夹就管理哪个文件夹**，工具会自动发现并统一管理所有配置，支持：

- ✅ **文件命名规范检查**（正则表达式匹配）
- ✅ **Addressable 自动添加**（命名模板、分组、标签）
- ✅ **贴图导入规则**（纹理类型、压缩格式、Max Size、Mipmap、UI 贴图参数）
- ✅ **违规扫描与一键修复**（支持多选配置批量扫描）
- ✅ **新文件导入自动应用规则**
- ✅ **预设系统**（保存/加载规则模板，含刷新设置）
- ✅ **忽略列表**（拖拽资源或文件夹，不受规则影响）
- ✅ **逐配置自动扫描**（每个配置独立开关和间隔）

---

## 📦 文件结构

```
FolderRuleTool/
├── FolderRuleConfig.cs              # ScriptableObject 配置资产（含逐配置自动扫描）
├── FolderRuleConfigEditor.cs        # 自定义 Inspector（预设 + 违规列表 + 手动刷新）
├── FolderRuleManager.cs             # 编辑器管理面板（多选扫描/修复/逐配置自动扫描）
├── FolderRulePostprocessor.cs       # 自动后处理器（新文件导入时触发）
├── FolderRulePreset.cs              # 规则预设 SO（保存/加载规则模板，含刷新设置）
├── AddressableGroupDropdown.cs      # Addressable 分组下拉（非 Odin）
├── AddressableGroupDropdownDrawer.cs# Addressable 分组下拉绘制器
├── FolderRuleTool.Editor.asmdef     # 程序集定义
└── README.md                        # 本文档
```

---

## 🚀 快速开始

### 1️⃣ 创建配置

**方式一：右键菜单**

在 Project 窗口中右键 → `Create` → `UnityToolsHub` → `文件夹规则配置`

**方式二：管理面板**

打开菜单 `UnityToolsHub` → `文件夹规则管理` → 点击右上角 `创建新配置`

### 2️⃣ 配置规则

选中创建的 SO 资产，在 Inspector 中设置：

| 配置项 | 说明 |
|--------|------|
| `enabled` | 是否启用此规则 |
| `recursive` | 是否递归监控子文件夹 |
| `ignoredAssets` | 忽略的资源列表（拖入资源或文件夹） |

> **生效范围** = 此 SO 资产所在的文件夹。把 SO 放到 `Assets/Art/Sprites/` 下，就管理该文件夹。开启 `recursive` 则包含所有子文件夹。

### 3️⃣ 使用预设（可选）

在 Inspector 的「📋 预设管理」区域：

1. 从下拉菜单选择已有预设 → 点击「应用预设」
2. 或修改规则后输入名称 → 点击「💾 保存为预设」

### 4️⃣ 扫描与修复

**方式一：Inspector 面板**

点击「🔍 扫描此配置违规」→ 查看违规列表 → 逐项修复

**方式二：管理面板**

打开 `UnityToolsHub` → `文件夹规则管理`：

- 点击 `🔍 扫描违规` — 检查所有配置的违规项
- 点击 `全部修复` — 一键修复所有违规
- 或在违规列表中逐项修复

---

## 📋 配置详解

### 🏷️ 基础设置

```csharp
// 生效范围 = 此 SO 资产所在的文件夹（自动获取，无需手动填写）
recursive = true                    // 递归监控子文件夹
enabled = true                      // 启用此规则
ignoredAssets = [资源A, 文件夹B]     // 忽略的资源（支持拖拽，支持文件夹）
autoScan = false                    // 自动扫描（每个配置独立开关）
scanInterval = 30f                  // 扫描间隔（秒，最小 5）
```

> **生效范围**：SO 资产放在哪个文件夹，就管理哪个文件夹。移动 SO = 改变生效范围。

**忽略列表说明：**
- 支持拖入**单个资源**（精确匹配路径）
- 支持拖入**文件夹**（忽略该文件夹下所有资源）
- Inspector 中以只读 ObjectField 显示，带图标标识（📁 文件夹 / 📄 资源）

**自动扫描说明：**
- 每个 FolderRuleConfig 独立配置自动扫描开关和间隔
- 开启后管理面板后台按间隔自动扫描该配置
- 配置列表中显示 🔄自动 标签
- 可通过预设批量设置多个配置的自动扫描参数

### 📝 文件命名规范

```csharp
enableNamingRule = true                                         // 启用命名检查
fileNamePattern = "^[a-z][a-z0-9_]*$"                          // 正则表达式
namingDescription = "文件名须为小写字母开头，仅含小写字母、数字、下划线"  // 违规提示
namingIgnoreExtensions = ".meta,.cs,.asmdef"                    // 忽略的扩展名
```

**常用正则示例：**

| 需求 | 正则表达式 |
|------|-----------|
| 小写字母+数字+下划线 | `^[a-z][a-z0-9_]*$` |
| 蛇形命名（强制下划线分隔） | `^[a-z]+(_[a-z0-9]+)*$` |
| 带前缀（如 `ui_`、`sfx_`） | `^(ui\|sfx\|bgm)_[a-z][a-z0-9_]*$` |
| 禁止中文 | `^[a-zA-Z0-9_]+$` |

### 📦 Addressable 配置

```csharp
enableAddressable = true                    // 启用自动添加
addressableNameTemplate = "{folder}/{name}" // 命名模板（下拉可选常用模板）
addressableGroupName = "Sprites"            // 分组名（下拉列出已有分组）
addressableLabels = "ui,character"          // 标签（逗号分隔）
addressableTargetExtensions = ".png,.jpg,.prefab,.asset"  // 生效扩展名
```

**命名模板变量：**

| 变量 | 说明 | 示例 |
|------|------|------|
| `{name}` | 文件名（无扩展名） | `hero_idle` |
| `{folder}` | 所在文件夹名 | `sprites` |
| `{path}` | 相对于规则文件夹的路径 | `characters/hero_idle` |

**常用模板（下拉可选）：**

| 模板 | 生成结果 |
|------|---------|
| `{folder}/{name}` | `sprites/hero_idle` |
| `{path}` | `characters/hero_idle` |
| `{name}` | `hero_idle` |
| `{folder}/{path}` | `sprites/characters/hero_idle` |
| `assets/{folder}/{name}` | `assets/sprites/hero_idle` |

### 🖼️ 贴图导入规则

```csharp
enableTextureRule = true                          // 启用贴图规则
textureTargetExtensions = ".png,.jpg,.jpeg,.tga,.psd"  // 生效扩展名
```

**公共参数（所有贴图）：**

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `textureType` | 纹理类型 | `Default` |
| `textureAlphaIsTransparency` | Alpha 透明 | `true` |
| `textureFilterMode` | 过滤模式 | `Bilinear` |
| `textureCompression` | 压缩方式 | `Compressed` |
| `textureMaxCapSize` | 最大尺寸上限（下拉可选） | `4096` |

**UI 贴图额外参数（路径包含关键词时生效）：**

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `textureUiKeywords` | UI 关键词（逗号分隔） | `/ui/,/sprite/,/sprites/,/icon/,/icons/` |
| `textureUiType` | UI 纹理类型 | `Sprite` |
| `textureUiSpriteMode` | Sprite 导入模式 | `Single` |
| `textureUiMipmapEnabled` | UI Mipmap | `false` |
| `textureUiWrapMode` | UI Wrap Mode | `Clamp` |

**Max Cap Size 下拉选项：** `512` / `1024` / `2048` / `4096` / `8192`

---

## 📋 预设系统

### 创建预设

右键 → `Create` → `UnityToolsHub` → `文件夹规则预设`

或在 FolderRuleConfig Inspector 的「📋 预设管理」中，输入名称后点击「💾 保存为预设」。

### 使用预设

1. 在 FolderRuleConfig Inspector 中展开「📋 预设管理」
2. 从下拉菜单选择预设
3. 点击「应用预设」→ 预设参数复制到当前配置

### 预设原理

- 预设存储**规则参数**（命名/Addressable/贴图/刷新设置），不含文件夹路径、递归、忽略列表等基础字段
- 应用预设 = **值拷贝**，之后预设和配置互不影响
- 同名保存时会提示是否覆盖
- 可通过预设批量设置多个配置的自动扫描参数

### 预设存储位置

`Assets/UnityFramework/Editor/UnityToolsHub/Presets/`

---

## 🔄 刷新设置

### 每配置独立自动扫描

每个 FolderRuleConfig 在「基础设置」中独立配置：

| 字段 | 说明 |
|------|------|
| `autoScan` | 是否开启自动扫描 |
| `scanInterval` | 扫描间隔（秒，最小 5） |

- 管理面板后台自动扫描所有开启 `autoScan` 的配置
- 各配置按各自间隔独立运行，互不影响
- 配置列表中开启自动扫描的配置会显示 🔄自动 标签

### 管理面板工具栏

| 按钮 | 功能 |
|------|------|
| `刷新配置` | 重新扫描项目中所有 FolderRuleConfig |
| `🔍 扫描选中(N)` | 扫描多选选中的配置（无选中则扫描全部） |
| `应用选中(N)` / `应用全部` | 对选中配置（或全部）应用规则 |
| `全部修复(N)` / `全部修复` | 修复选中配置（或全部）的违规项 |
| `全选` / `取消` | 多选操作（勾选配置列表中的复选框） |
| `创建新配置` | 快速创建新的 FolderRuleConfig |

### 多选操作

- 在配置列表中勾选复选框选中多个配置
- 所有批量操作（扫描/应用/修复）都支持选中模式
- 未选中任何配置时，操作对全部配置生效
- 确认对话框会列出受影响的配置名称

---

## 🔧 工作流程

### 自动处理（新文件导入）

```
新文件导入 → OnPostprocessAllAssets
    ↓
遍历所有 FolderRuleConfig
    ↓
匹配文件夹范围 + 检查忽略列表
    ↓
┌─────────────────────────────────────────┐
│ 命名检查 → 控制台警告（不自动修改）       │
│ Addressable → 自动创建条目（含命名/标签） │
│ 贴图规则 → 自动修正导入设置并 Reimport    │
└─────────────────────────────────────────┘
```

### 手动扫描与修复

```
打开 FolderRuleManager 面板 / FolderRuleConfig Inspector
    ↓
点击「🔍 扫描违规」
    ↓
遍历所有配置的管辖文件（跳过忽略列表）
    ↓
┌─────────────────────────────────────┐
│ 检查命名违规 → 列入违规列表         │
│ 检查 Addressable 缺失 → 列入列表    │
│ 检查贴图设置不符 → 列入列表         │
└─────────────────────────────────────┘
    ↓
点击「全部修复」或逐项修复
```

---

## 📊 面板功能

### FolderRuleManager（管理面板）

**左侧：配置列表**
- 显示所有 FolderRuleConfig SO
- 每个配置显示：多选复选框 ☑、启用开关、名称、路径、规则标签（📝/📦/🖼/🔄自动）
- 点击名称选中查看详情，勾选复选框进行多选批量扫描

**右侧上：配置详情**
- 完整规则信息
- 忽略资源列表（只读 ObjectField，带图标）
- 快捷按钮：选中配置文件、扫描此配置违规

**右侧下：违规列表**
- 醒目标题：有违规时 `⚠ 违规资源（N 项）`（橙红色），无违规 `✓ 违规资源（0 项）`
- 支持按类型筛选：`全部` / `命名` / `Addressable` / `贴图`
- 每项显示：类型标签（颜色区分）+ 资源 ObjectField（可拖拽）+ 路径 + 违规描述
- 操作按钮：`选中`（定位资源）/ `修复`（自动修复）/ `忽略`（移出列表）

### FolderRuleConfig Inspector

**属性区域**
- 基础设置、命名规范、Addressable、贴图规则（Odin 折叠分组）

**📋 预设管理**
- 预设下拉选择 + 应用按钮
- 预设内容预览
- 保存为新预设

**违规区域**
- 扫描按钮 + 应用规则按钮 + 打开管理器按钮
- 违规列表（与管理面板相同）

---

## ⚠️ 注意事项

1. **命名修复**：自动修复仅尝试将文件名转为小写+下划线格式，复杂情况需手动重命名
2. **Addressable**：需要项目已初始化 Addressable 系统（`Window` → `Asset Management` → `Addressables`）
3. **贴图规则**：修改后会自动 `SaveAndReimport`，可能触发资源重新导入
4. **配置冲突**：同一文件夹有多个配置时，所有匹配的规则都会生效
5. **递归模式**：开启递归时，子文件夹中的文件也会被检查
6. **忽略列表**：支持文件夹忽略（忽略其下所有子资源），拖入后以只读 ObjectField 显示
7. **预设独立**：应用预设后，预设和配置的修改互不影响（值拷贝）
8. **逐配置自动扫描**：每个 FolderRuleConfig 独立配置自动扫描，管理面板后台按各自间隔运行
9. **多选扫描**：在配置列表勾选复选框后，点击「扫描选中」只扫描勾选的配置

---

## 🔗 与其他工具的关系

| 工具 | 关系 |
|------|------|
| `AssetImportFilter` | 互补 — AssetImportFilter 用于忽略特定文件，FolderRuleTool 用于规范化管理 |
| `FolderRulePostprocessor` | 集成 — 新文件导入时自动应用 FolderRuleConfig 中的规则 |
| UnityToolsHub | 集成 — 在 UnityToolsHub 主面板的「资产工具」分类中显示 |

---

## 💡 使用示例

### 示例 1：规范美术资源命名

```
// 将 SO 放到 Assets/Art/ 下
// 创建配置：Assets/Art/ArtNamingRule.asset
enableNamingRule = true
fileNamePattern = "^[a-z]+(_[a-z0-9]+)*$"
namingDescription = "美术资源须为蛇形命名（如 hero_idle、ui_button）"
namingIgnoreExtensions = ".psd,.clip"
```

### 示例 2：自动添加 Addressable

```
// 将 SO 放到 Assets/Art/Sprites/ 下
// 创建配置：Assets/Art/Sprites/SpriteAddressable.asset
enableAddressable = true
addressableNameTemplate = "sprites/{name}"  // 下拉可选
addressableGroupName = "Sprites"            // 下拉列出已有分组
addressableLabels = "ui"
addressableTargetExtensions = ".png"
```

### 示例 3：统一贴图导入设置

```
// 将 SO 放到 Assets/Art/Textures/ 下
// 创建配置：Assets/Art/Textures/TextureRule.asset
enableTextureRule = true
textureType = TextureImporterType.Default
textureCompression = TextureImporterCompression.Compressed
textureMaxCapSize = 2048                    // 下拉可选
textureFilterMode = FilterMode.Bilinear
```

### 示例 4：使用预设快速配置

```
1. 创建预设：右键 Create → UnityToolsHub → 文件夹规则预设
2. 配置预设参数（命名规范 + Addressable + 贴图规则 + 自动扫描）
3. 将 SO 放到目标文件夹 → 选择预设 → 应用预设
4. SO 放到哪就管理哪，无需手动填路径
```

### 示例 5：忽略特定资源

```
1. 在 FolderRuleConfig 的「忽略的资源」列表中
2. 拖入不需要检查的单个资源文件
3. 或拖入整个文件夹（忽略其下所有资源）
4. 被忽略的资源不会出现在违规列表中
```

### FolderRuleConfig

```csharp
// 判断资源是否在管辖范围
bool IsInScope(string assetPath)

// 判断扩展名是否匹配
bool IsTargetExtension(string assetPath, string targetExtensions)

// 生成 Addressable 名称
string ResolveAddressableName(string assetPath)

// 获取标签列表
List<string> GetAddressableLabels()
```

### FolderRuleManager（内部 API）

```csharp
// 获取所有启用的配置（带缓存）
static List<FolderRuleConfig> GetAllConfigs()

// 强制刷新缓存
static void InvalidateCache()
```

---

## 📝 更新日志

### v1.0.0 (2026-07-08)
- ✨ 初始版本
- ✅ 支持文件命名规范检查
- ✅ 支持 Addressable 自动添加
- ✅ 支持贴图导入规则
- ✅ 支持违规扫描与一键修复
- ✅ 支持新文件导入自动应用规则
