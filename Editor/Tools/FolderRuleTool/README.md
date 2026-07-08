# 📂 文件夹规则管理工具

> UnityToolsHub 子工具 — 统一管理文件夹命名规范、Addressable 自动添加、贴图导入规则

---

## 🎯 概述

文件夹规则管理工具用于对项目中的资源文件夹进行批量规范化管理。用户手动创建 **ScriptableObject 配置**，工具会自动发现并统一管理所有配置，支持：

- ✅ **文件命名规范检查**（正则表达式匹配）
- ✅ **Addressable 自动添加**（命名模板、分组、标签）
- ✅ **贴图导入规则**（纹理类型、压缩格式、Max Size、Mipmap）
- ✅ **违规扫描与一键修复**
- ✅ **新文件导入自动应用规则**

---

## 📦 文件结构

```
FolderRuleTool/
├── FolderRuleConfig.cs          # ScriptableObject 配置资产
├── FolderRuleManager.cs         # 编辑器管理面板
├── FolderRulePostprocessor.cs   # 自动后处理器（新文件导入时触发）
└── README.md                    # 本文档
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
| `folderPath` | 监控的文件夹路径（如 `Assets/Art/Sprites`） |
| `recursive` | 是否递归监控子文件夹 |
| `enabled` | 是否启用此规则 |

### 3️⃣ 扫描与修复

打开 `UnityToolsHub` → `文件夹规则管理`：

- 点击 `扫描违规` — 检查所有配置的违规项
- 点击 `全部修复` — 一键修复所有违规
- 或在违规列表中逐项修复

---

## 📋 配置详解

### 🏷️ 基础设置

```csharp
folderPath = "Assets/Art/Sprites"  // 监控的文件夹路径
recursive = true                    // 递归监控子文件夹
enabled = true                      // 启用此规则
```

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
addressableNameTemplate = "{folder}/{name}" // 命名模板
addressableGroupName = "Sprites"            // 分组名（留空使用默认组）
addressableLabels = "ui,character"          // 标签（逗号分隔）
addressableTargetExtensions = ".png,.jpg,.prefab,.asset"  // 生效扩展名
```

**命名模板变量：**

| 变量 | 说明 | 示例 |
|------|------|------|
| `{name}` | 文件名（无扩展名） | `hero_idle` |
| `{folder}` | 所在文件夹名 | `sprites` |
| `{path}` | 相对于规则文件夹的路径 | `characters/hero_idle` |

**模板示例：**

| 模板 | 生成结果 |
|------|---------|
| `{folder}/{name}` | `sprites/hero_idle` |
| `assets/{path}` | `assets/characters/hero_idle` |
| `{name}` | `hero_idle` |

### 🖼️ 贴图导入规则

```csharp
enableTextureRule = true                          // 启用贴图规则
textureType = TextureImporterType.Sprite          // 纹理类型
textureFormat = TextureImporterFormat.RGBA32      // 压缩格式
textureMaxSize = 2048                             // 最大尺寸
textureMipmapEnabled = false                      // 是否启用 Mipmap
textureTargetExtensions = ".png,.jpg,.tga"        // 生效扩展名
```

---

## 🔧 工作流程

### 自动处理（新文件导入）

```
新文件导入 → OnPostprocessAllAssets
    ↓
遍历所有 FolderRuleConfig
    ↓
匹配文件夹范围
    ↓
┌─────────────────────────────────────────┐
│ 命名检查 → 控制台警告（不自动修改）       │
│ Addressable → 自动创建条目（含命名/标签） │
│ 贴图规则 → 自动修正导入设置并 Reimport    │
└─────────────────────────────────────────┘
```

### 手动扫描与修复

```
打开 FolderRuleManager 面板
    ↓
点击「扫描违规」
    ↓
遍历所有配置的管辖文件
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

### 左侧：配置列表

- 显示所有 `FolderRuleConfig` SO 配置
- 每个配置显示：启用开关、名称、路径、已启用的规则标签
- 点击选中查看详情

### 右侧：配置详情

- 显示选中配置的完整规则信息
- 快捷按钮：选中配置文件、扫描此配置违规

### 右侧：违规列表

- 显示所有扫描到的违规项
- 支持按类型筛选：`全部` / `命名` / `Addressable` / `贴图`
- 每项显示：违规类型、资源路径、违规描述
- 操作按钮：`修复`（自动修复）/ `忽略`（移出列表）

---

## ⚠️ 注意事项

1. **命名修复**：自动修复仅尝试将文件名转为小写+下划线格式，复杂情况需手动重命名
2. **Addressable**：需要项目已初始化 Addressable 系统（`Window` → `Asset Management` → `Addressables`）
3. **贴图规则**：修改后会自动 `SaveAndReimport`，可能触发资源重新导入
4. **配置冲突**：同一文件夹有多个配置时，所有匹配的规则都会生效
5. **递归模式**：开启递归时，子文件夹中的文件也会被检查

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

```csharp
// 创建配置：Assets/Config/ArtNamingRule.asset
folderPath = "Assets/Art"
enableNamingRule = true
fileNamePattern = "^[a-z]+(_[a-z0-9]+)*$"
namingDescription = "美术资源须为蛇形命名（如 hero_idle、ui_button）"
namingIgnoreExtensions = ".psd,.clip"
```

### 示例 2：自动添加 Addressable

```csharp
// 创建配置：Assets/Config/SpriteAddressable.asset
folderPath = "Assets/Art/Sprites"
enableAddressable = true
addressableNameTemplate = "sprites/{name}"
addressableGroupName = "Sprites"
addressableLabels = "ui"
addressableTargetExtensions = ".png"
```

### 示例 3：统一贴图导入设置

```csharp
// 创建配置：Assets/Config/TextureRule.asset
folderPath = "Assets/Art/Textures"
enableTextureRule = true
textureType = TextureImporterType.Default
textureFormat = TextureImporterFormat.ASTC_6x6
textureMaxSize = 1024
textureMipmapEnabled = false
```

---

## 🛠️ API 参考

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
