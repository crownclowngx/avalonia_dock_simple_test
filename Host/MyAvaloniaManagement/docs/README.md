# MyAvaloniaManagement 主项目文档

本目录描述桌面宿主 `MyAvaloniaManagement` 的当前内部设计。目标是让维护者能够区分：哪些行为是必须保持的外部契约，哪些类型只是可继续调整的内部实现，以及每项设计为什么采用当前方案。

## 阅读顺序

1. [内部架构](./architecture.md)：先理解启动、插件发现、Dock、文档和布局持久化的完整链路。
2. [设计方法论与取舍](./design-methodology-and-tradeoffs.md)：了解本轮重构如何应用 SOLID 和设计模式，以及没有采用哪些更复杂方案。
3. [兼容约束](./compatibility-contracts.md)：修改代码前核对 public API、插件激活、稳定 ID 和 JSON 行为。

解决方案级材料：

- [项目文档导航](../../../docs/README.md)
- [宿主—插件架构评审](../../../docs/host-plugin-architecture-review.md)
- [宿主专项测试说明](../../../docs/testing/myavalonia-management-tests.md)
- [Dock 布局快照 V1](../../../docs/upgrade/net10/dock-layout-snapshot-v1.md)

## 文档定位

这些文档描述的是当前实现，不是新功能路线图。本轮内部重构明确保持以下边界：

- 不改变 Host 与 `MyAvaloniaManagementCommon` 的 public 契约；
- 不改变 Managed/Legacy 插件双轨规则；
- 不改变 `DocumentSaveData` 与 `layout-v1.json` 格式；
- 不新增布局版本、插件市场、热加载、沙箱或用户可见诊断功能；
- 不要求插件跟随宿主内部协作者重编写业务逻辑。

主项目内部类型默认可继续演进，但外部可观察行为必须由测试和兼容文档共同保护。

## 快速验证

在仓库根目录运行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -WindowsSmoke
```

2026-08-10 基线为 157 项通过，Host 行覆盖率 80.74%，分支覆盖率 65.17%，Windows 真实窗口冒烟通过。
