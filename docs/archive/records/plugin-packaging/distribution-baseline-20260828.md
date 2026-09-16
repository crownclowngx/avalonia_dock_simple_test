# 2026-08-28 外部插件分发说明快照

> 归档说明（2026-09-16）：以下摘录原综合开发与发布指南的交付物部分，保存当时的版本、发布日期和传播入口；其中“当前”仅指原文基线。旧脚本已退役，当前六包政策见[NuGet 维护说明](../../../maintenance/nuget-release.md)。


外部插件开发由五个 NuGet 包组成：

| 包 | 版本 | 职责 | 进入插件 ZIP |
| --- | --- | --- | --- |
| `MyAvaloniaManagement.PluginSdk` | `3.3.0` | 平台无关身份、Document、内容、关闭、生命周期、Workflow Action 与 Workbench Command Target 契约 | 否，Host 提供 |
| `MyAvaloniaManagement.PluginSdk.UI` | `3.3.0` | Avalonia 模块入口、DI、Document/Tool/View、窗口端口、Action 与 Command 声明扩展 | 否，Host 提供 |
| `MyAvaloniaManagement.PluginSdk.Workflow` | `1.0.0` | 共享 Schema、引用路径、保守可赋值与 Catalog revision | 否，Host 提供 |
| `MyAvaloniaManagement.Plugin.Build` | `1.1.2` | 声明校验、manifest、资产部署和确定性 ZIP | 否，仅开发期 |
| `MyAvaloniaManagement.Plugin.Templates` | `1.3.0` | `dotnet new myavalonia-plugin` 解决方案模板、lock file、Command 示例与项目内置文档 | 否，仅创建时 |

以上五个当前版本已发布到 NuGet.org；Build `1.1.2` 发布于 2026-08-24，Workflow `1.0.0` 发布于
2026-08-26，Core/UI `3.3.0` 与 Templates `1.3.0` 发布于 2026-08-28：
[Core SDK](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk/3.3.0)、
[UI SDK](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk.UI/3.3.0)、
[Workflow SDK](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk.Workflow/1.0.0)、
[Build](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Build/1.1.2) 和
[Templates](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Templates/1.3.0)。

Core/UI SDK 同版本发布。Build 与 Templates 独立演进；模板固定一组经过端到端验证的精确版本，避免
外部插件还原到当前 Host 未验证的共享程序集组合。

### 1.1 Workflow Action G2

G2 已在隔离 feed 验证并正式发布 Core/UI SDK `3.1.0` 与 Templates `1.1.0`。模板精确锁定
SDK `[3.1.0]`，三个生成项目均提交 `packages.lock.json`，manifest SDK 区间为
`[3.1.0, 4.0.0)`。Build 协议没有变化，因此仍从 NuGet.org 精确使用 `1.1.2`，没有重打包同版本。

需要复核传播链时，运行 `scripts/Test-WorkflowActionG2.ps1`；外部项目直接从 NuGet.org 使用正式版本，
不要提交开发机 feed 绝对路径、Host `ProjectReference` 或源码链接。
