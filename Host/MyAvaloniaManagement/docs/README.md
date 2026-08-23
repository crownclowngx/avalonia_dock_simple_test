# MyAvaloniaManagement 主项目文档

本目录描述桌面宿主 `MyAvaloniaManagement` 的当前内部设计。目标是让维护者能够区分：哪些行为是必须保持的外部契约，哪些类型只是可继续调整的内部实现，以及每项设计为什么采用当前方案。

## 阅读顺序

1. [内部架构](./design/architecture.md)：先理解启动、插件发现、Dock、文档和布局持久化的完整链路。
2. [设计方法论与取舍](./design/design-methodology-and-tradeoffs.md)：了解本轮重构如何应用 SOLID 和设计模式，以及没有采用哪些更复杂方案。
3. [兼容约束](./reference/compatibility-contracts.md)：修改代码前核对 public API、插件激活、稳定 ID 和 JSON 行为。

解决方案级材料：

- [项目文档导航](../../../docs/README.md)
- [Managed 插件快速开始](../../../docs/quick-start/README.md)
- [宿主—插件架构评审](../../../docs/design/host-plugin-architecture-review.md)
- [宿主专项测试说明](../../../docs/reference/myavalonia-management-tests.md)
- [Host V4 G0–G8 任务书](../../../docs/design/host-v4-breaking-refactor-plan.md)
- [V4 G7 四插件、Harness 与文档回归](../../../docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md)
- [V4 G8 封板](../../../docs/plan-history/host-v4/g8-v4-sealing.md)
- [Dock 布局快照 V2](../../../docs/reference/dock-layout-snapshot-v2.md)
- [G4 Managed-only 插件加载记录](../../../docs/plan-history/host-v1/g4-managed-only-plugin-loading.md)
- [G5 显式贡献与 Plugin Registry](../../../docs/plan-history/host-v1/g5-explicit-contributions-and-plugin-registry.md)
- [V2 G5 声明式贡献目录](../../../docs/plan-history/host-v2/g5-declarative-contribution-catalog.md)
- [V2 G6 Host Dock Adapter](../../../docs/plan-history/host-v2/g6-host-dock-adapter.md)
- [Document envelope v2 + V3 G2 修订保存当前设计](../../../docs/design/document-persistence-v2-design.md)
- [V2 G7 Document V2](../../../docs/plan-history/host-v2/g7-document-v2.md)
- [V2 G8 布局与生命周期 V2](../../../docs/plan-history/host-v2/g8-layout-and-lifecycle-v2.md)
- [V2 G14 封板](../../../docs/plan-history/host-v2/g14-v2-sealing.md)
- [V3 G1 版本与数据边界](../../../docs/plan-history/host-v3/g1-version-and-data-boundaries.md)
- [V3 G2 修订化 Document 保存](../../../docs/plan-history/host-v3/g2-revisioned-document-save.md)
- [V3 G6 Workspace Session 与 Dock Factory](../../../docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md)
- [V3 G7 Host Catalog 与 Plugin Registry](../../../docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md)
- [V3 G8 全屏租约与 Host V3 骨架](../../../docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md)
- [V3 G9 MyPlugTest 最终验收](../../../docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md)
- [V3 G10 DaTangAccountingHelpPlug 最终验收](../../../docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md)
- [V3 G11 MySmallTools 最终验收](../../../docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md)
- [V3 G12 BiliDownloader 最终验收](../../../docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md)
- [V3 G13 删除 V2 生产面](../../../docs/plan-history/host-v3/g13-remove-v2-production-surface.md)
- [V3 G14 封板](../../../docs/plan-history/host-v3/g14-v3-sealing.md)
- [G16 文档与 v1 基线（历史）](../../../docs/plan-history/host-v1/g16-documentation-and-v1-baseline.md)

## 文档定位

这些文档描述的是当前实现，不是新功能路线图。当前明确保持以下边界：

- V2 G14 已将 Core/UI public API 冻结到 v2 Shipped；V3 G14 已将最终表面冻结到 v3 Shipped 127/45，Host 自有实现不属于插件 API；
- `managed-plugin-v1.0.0` 只定位 Managed Plugin v1 历史基线；当前版本与运行语义以 V3 G14、Document 保存细节以 V3 G2 为准；
- 插件只支持严格清单、必需 `.deps.json` 和唯一 `IPluginModule` 的 Managed 模型；
- manifest 是插件身份唯一事实源，插件 Document、Tool、View 和 Lifecycle 只通过 Context 显式登记；
- Host Welcome/Tool 由 `HostWorkspaceCatalog` 声明；插件贡献才通过 `IPluginRegistration` 发布到不可变 `PluginRegistry`；
- Document 只接受六字段 V2 信封与原生 JSON `DocumentContent`，保存确认绑定插件修订；布局只接受严格 `layout-v2.json`；
- Plugin Registry 只保存真实插件声明，生命周期状态和插件贡献可用性由 Host internal 状态存储与只读投影拥有；
- 每个 HostRuntime 只有一个 WorkspaceSession；HostDockFactory 只适配 Dock 协议，Tool 管理只消费无 Dock 类型的只读投影；
- 全屏端口只接受一个 Control 并返回 `IDisposable` 租约；MainWindow 委托具体会话维护唯一活动租约，插件不接触 Window 或 Dock；
- 四插件的创建、发布、关闭和 Tool 显隐只经 Workspace Session；最终测试 ZIP 分别使用真实 Loader 重放各自声明的 Document、Tool 与 Lifecycle；
- Host V4 G6 的文件树先由 `FileSystemPath` 规范化并分类，再由 `IHostStorageService.DirectoryExists`
  判断存在性；UNC 共享根可作为唯一自定义根展示，失败选择不会提交半成品 UI 状态；
- 分类菜单是构造期只读快照，只有展开状态可变；Document 创建仍直接进入真实 Coordinator；
- Host V4 G7 只复用 SDK、诊断、四插件和真实媒体既有开发门禁，不增加生产接口；测试 ZIP、
  Harness 与 Release 编译配置均不代表发布资格；
- Host V4 G8 通过两轮无硬链接隔离、实体 ZIP/manifest 复核和 Windows Smoke 建立本地发布资格；
  产品与 SDK 保持 3.0.0，未上传、未打 tag、未对外发布且未使用 AIFLOW；
- 不新增插件市场、热加载、沙箱或新的用户可见诊断通道；
- 不要求插件跟随宿主内部协作者重编写业务逻辑。

主项目内部类型默认可继续演进，但外部可观察行为必须由测试和兼容文档共同保护。

## 快速验证

在仓库根目录运行：

```powershell
.\scripts\Test-Documentation.ps1
.\scripts\Invoke-HostV4ReleaseGate.ps1
.\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G7
.\scripts\Test-HostV3ProductionSurface.ps1 -Configuration Release
.\scripts\Test-DocumentV2.ps1 -Configuration Release
.\scripts\Test-RevisionedDocumentSave.ps1 -Configuration Release -NoRestore
.\scripts\Test-WorkspaceSessionDockFactory.ps1 -Configuration Release -NoRestore
.\scripts\Test-HostCatalogPluginRegistry.ps1 -Configuration Release -NoRestore
.\scripts\Test-MyPlugTestV3.ps1 -Configuration Release
.\scripts\Test-DaTangAccountingHelpPlugV3.ps1 -Configuration Release -NoRestore
.\scripts\Test-MySmallToolsV3.ps1 -Configuration Release -NoRestore
.\scripts\Test-BiliDownloaderV3.ps1 -Configuration Release -NoRestore
.\scripts\Test-LayoutLifecycleV2.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v3 -Configuration Release
```

文档门禁验证本地链接、脚本路径、关键类型、集中版本和四插件兼容区间；宿主综合门禁动态统计
Unit、Headless UI、Plugin 与覆盖率。带日期的具体数量只记录在各 G 阶段专用文档中，不作为永久阈值。
除 `Invoke-HostV4ReleaseGate.ps1` 外，以上均是日常非发布验证。当前正式本地复验由 V4 G8 入口执行
两轮隔离矩阵和 Windows Smoke；V3 G14 入口仅保留历史复核。两者都不运行 AIFLOW、历史
ReleaseAcceptance、上传或标签。
