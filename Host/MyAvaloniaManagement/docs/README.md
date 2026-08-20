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
- [Dock 布局快照 V1](../../../docs/reference/dock-layout-snapshot-v1.md)
- [G4 Managed-only 插件加载记录](../../../docs/plan-history/host-v1/g4-managed-only-plugin-loading.md)
- [G5 显式贡献与 Plugin Registry](../../../docs/plan-history/host-v1/g5-explicit-contributions-and-plugin-registry.md)
- [G7 Document 信封 v1](../../../docs/plan-history/host-v1/g7-document-envelope-v1.md)
- [G8 保存契约与内容版本](../../../docs/plan-history/host-v1/g8-document-content-persistence-contract.md)
- [G16 文档与 v1 基线](../../../docs/plan-history/host-v1/g16-documentation-and-v1-baseline.md)

## 文档定位

这些文档描述的是当前实现，不是新功能路线图。当前明确保持以下边界：

- G5 已对封板前候选 Plugin SDK 做一次破坏式重定基线；此后的最终 v1 public 契约进入兼容治理，Host 自有实现不属于插件 API；
- G16 已完成最终文档签署，`managed-plugin-v1.0.0` 定位 Managed Plugin v1 基线；
- 插件只支持严格清单、必需 `.deps.json` 和唯一 `IPluginModule` 的 Managed 模型；
- manifest 是身份唯一事实源，Document、Tool、View 和 Lifecycle 只通过 Context 显式登记；
- 不改变七字段 Document 信封 v1 与 `layout-v1.json` 格式；插件内容仅通过 `DocumentContentSnapshot` 传递；
- 不新增布局版本、插件市场、热加载、沙箱或用户可见诊断功能；
- 不要求插件跟随宿主内部协作者重编写业务逻辑。

主项目内部类型默认可继续演进，但外部可观察行为必须由测试和兼容文档共同保护。

## 快速验证

在仓库根目录运行：

```powershell
.\scripts\Test-Documentation.ps1
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -WindowsSmoke
```

文档门禁验证本地链接、脚本路径、关键类型、集中版本和四插件兼容区间；宿主综合门禁动态统计
Unit、Headless UI、Plugin 与覆盖率。带日期的具体数量只记录在各 G 阶段专用文档中，不作为永久阈值。
`-WindowsSmoke` 是独立的 Windows 实窗验证，不属于 G16 文档基线门禁。
