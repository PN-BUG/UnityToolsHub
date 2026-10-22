# AI工具

保存 AI 为临时任务编写、后续还可复用的 Unity 编辑器工具。此目录是工具源码的长期位置；不依赖聊天记录、TaskScratch 或临时生成脚本。

## 使用前先查找

1. 阅读下方索引，并搜索本目录和 `Editor/Tools` 下其他分类的已有工具。
2. 功能吻合时，打开对应窗口或直接调用其公开静态方法；先读参数和副作用说明。
3. 功能接近时扩展原工具，避免创建只有名字不同的重复脚本。
4. 确认没有可复用实现，再复制 `_AIToolTemplate.cs.txt` 为有明确用途的新 `.cs` 文件。

Hub 入口：`Window > Unity Tools Hub > AI工具`。窗口、搜索、标签、脚本定位等继续使用 Hub 的现有功能。

## 工具索引

| 工具 | 源码 | 入口与参数 | 副作用 |
| --- | --- | --- | --- |
| AI工具使用说明 | `AIToolsGuideWindow.cs` | Hub 中双击打开，无需参数 | 定位目录、更改编辑器选择、打开说明或复制文本；不修改场景和资产 |
| 本地化可选模块 | `LocalizationModuleWindow.cs` | Hub「可选模块 > 本地化模块」；`GetInstalledPackage()` 读取状态，`InstallOrUpdate(onComplete)` 显式安装/更新，`OpenPipeline()` 打开流水线 | 只有显式安装调用才发起 UPM Git 安装，可能下载依赖并重载程序集；不会覆盖本地/嵌入源码目录 |

本地化模块按用途显示在「可选模块」，源码在此目录复用。安装使用 `https://github.com/PN-BUG/UnityLocalization.git`；依赖已有 Nodin。当前只完成静态核对，UPM 安装结果及窗口布局仍需目标 Unity Editor 验证。

## 接入约定

- 文件保存在本目录，可按用途创建子目录；保持编辑器代码和运行时代码隔离。
- 使用 `[ToolInfo("明确的工具名称", "AI工具", Description = "用途、输入和副作用", Tags = new[] { "AI", "功能关键词" })]`。Hub 自动发现，无需修改分类注册代码。
- 优先继承 `ToolEditorWindow`，沿用现有界面样式。模板 `.cs.txt` 不参与编译，复制为 `.cs` 后才会被发现。
- 将核心逻辑暴露为带明确参数的公开静态方法，窗口仅收集输入、调用和展示结果。不要把 `Selection`、硬编码对象名或本机绝对路径当作核心方法的隐含输入。
- 避免在加载程序集、打开窗口或绘制界面时自动执行修改。批量处理先提供扫描/预览，再由明确操作执行。
- 修改场景/资产时支持 Undo，按需标记 dirty；说明不能撤销的文件操作。保留无关修改和已有 `.meta` GUID。
- 给工具写简短的源码说明，并同步更新上方索引；记录适用范围、必要依赖以及实际验证情况。
- 不保存密钥、账号令牌、一次性报告或只针对某个现场的破坏性脚本。

自定义程序集中的工具需要引用 `UnityToolsHub.SDK`（`ToolInfoAttribute`）；继承基类时还需引用 `UnityToolsHub.ToolWindowBase.Editor`。本目录已位于 Hub 的编辑器程序集内，无需新增 asmdef。
