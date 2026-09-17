# 版本与交付边界

> 用途：集中解释当前版本、包职责和支持范围。状态：当前；核对日期：2026-09-16。事实源：[版本属性](../../Directory.Version.props)、[SDK 选择](../../global.json)、[模板依赖](../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/Directory.Packages.props)。

## 当前源码基线

| 事实 | 当前值 |
| --- | --- |
| .NET SDK / 目标框架 | 10.0.302，latestPatch / net10.0 |
| Host 产品 | 3.0.0 |
| 六个自有 NuGet 包 | 统一 3.4.1 |
| Avalonia / Dock | 12.1.2 / 12.1.0.6 |
| Host Dock 呈现层补丁 | Dock.Avalonia 12.1.0.7-area.5；程序集 12.1.0.6；[固定源码与重建](../../patches/dock-area-fill/README.md) |
| Semi / Ursa | 12.1.0 / 2.1.0 |
| manifest / Document envelope | schema 2 |
| Layout | schema 3；布局文件 layout-v3.json；V2 仅首次只读迁移 |
| 默认数据根 | `%LOCALAPPDATA%/MyAvaloniaManagement/v2/` |

`MYAVALONIA_DATA_DIRECTORY` 表示完整数据根，不再追加产品名或 v2。产品版本、SDK/API、插件业务版本、程序集身份及数据 schema 各有职责；Host V5–V11 等名称只是改造顺序。

V10–V11 工作树增加插件看板、兼容报告、浮窗与工具布局 V3，尚未公开发布，表中正式版本保持原样。报告绑定实际 Host 运行文件、规则及环境摘要；commit 和产品版本仅辅助定位，脏工作树不以 HEAD 代替内容身份。可选 `plugin.build.json` 与兼容报告独立使用 schema 1，不改变既有 schema 2。契约见[插件兼容证据](plugin-compatibility.md)。

## 六个包

下列包当前均为 `3.4.1`，唯一发布号来自 `MyAvaloniaPackageVersion`：

| 包 | 职责 | 插件 ZIP |
| --- | --- | --- |
| MyAvaloniaManagement.PluginSdk | BCL-only 身份、Document、生命周期、Action、Command 契约 | 不携带，由 Host 提供 |
| MyAvaloniaManagement.PluginSdk.UI | 模块、私有 DI、View、窗口端口及贡献声明 | 不携带，由 Host 提供 |
| MyAvaloniaManagement.PluginSdk.Workflow | Schema Profile、引用路径、类型兼容、双 revision | 不携带，由 Host 提供 |
| MyAvaloniaManagement.Icons | 无 UI/SDK 依赖的矢量数据 | 使用时作为声明过的私有依赖携带 |
| MyAvaloniaManagement.Plugin.Build | manifest、资产校验、部署与确定性打包 | 开发期依赖，不携带 |
| MyAvaloniaManagement.Plugin.Templates | 生成插件、Standalone、测试和文档 | 创建项目时使用 |

Core/UI 的包和程序集版本同步；Workflow 虽使用包号 3.4.1，仍保留 `AssemblyVersion=1.0.0.0` 与 v1 API 基线。Icons 属于插件私有资源，不加入共享 SDK 闭包。

## 新插件与旧二进制

当前模板精确锁定 Core/UI、Icons、Build `[3.4.1]`，生成插件业务版本从 `1.0.0` 开始，manifest 最低 SDK 为 `3.4.1`、上界为 `4.0.0`。使用可选 Workflow 包时同样采用当前包号。

旧 SDK 3.4.0 插件可以保留原 DLL 与 manifest，在新版 Host 回归；已经验证的范围见 [V9 使用与验证](../quick-start/host-v9-upgrade.md)。不能把加载/视图构造通过扩大为所有业务流程通过，也不能将新编译插件的最低 SDK 手动降回旧值。

`AddIcon` 首次提供于 SDK 3.4.0、共享 Workflow 协议首次提供于 SDK 3.2.0，这些是能力引入版本，不是新项目推荐依赖。

## 支持与发布状态

正式支持 Windows x64、内部可信进程内 Managed Plugin；不提供沙箱、热卸载、自动安装或在线市场。macOS 仅提供[实验打包与真机验收说明](../quick-start/macos-experiment.md)。

仓库的 [2026-09-16 发布记录](../archive/records/host-v9/nuget-unified-3.4.1-release.md)记录六包公共发布及模板消费验证。Host 安装程序发布、人工体验验收是独立事项，见[待办](../roadmap/README.md)。历史部署或旧版封板结果不授予当前工作树发布资格。

仓库许可证为 [MIT](../../LICENSE)。API 文本分类与 Host seal 的当前约束见 [API 维护](plugin-sdk-api-compatibility.md)。
