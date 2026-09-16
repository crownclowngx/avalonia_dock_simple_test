# V11 浮动窗口与 Layout V3 开发记录

> 状态：实施中。起始日期：2026-09-16。实施分支：`codex/host-v11-floating-layout`，从 `master` 的 `bbd4832` 创建；起始工作树干净。用户授权按阶段提交并写明标题与说明。
> 范围与约束见 [V11 计划](../../../roadmap/host-v11-floating-windows-and-layout-v3-plan.md)。不使用 AIFLOW、Windows CI、seal、发布门禁、发布 Smoke，不部署或发布。

## G0 开发基线

2026-09-16 在原始代码及已提交计划上执行 `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`，一轮 scope=all 通过。locked restore、Release 构建零警告零错误、契约与已发布 API 比较、MyPlugTest 打包及真实 ZIP 验收通过。

| 测试组 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- |
| SDK | 91 | 0 | 0 |
| Host Unit | 424 | 0 | 0 |
| Host Plugin | 218 | 0 | 0 |
| Host Headless UI | 117 | 0 | 0 |
| MyPlugTest Unit | 11 | 0 | 0 |
| MyPlugTest Package | 1 | 0 | 0 |
| 合计 | 862 | 0 | 0 |

证据运行 ID 为 `20260916-043048-bbd483255333`，摘要和 TRX 位于本地 `artifacts/gate/<run-id>/`；本节在完成后回填，不把记录文字本身算入原始构建输入。

## 锁定框架核查

Dock.Avalonia `12.1.0.6` 的本地 NuGet 元数据对应上游提交 `cc08602d02fde1b85067cec064da29f34785e505`。源码只下载到可清理的 `artifacts/host-v11/framework-source`，不加入产品或源码提交。

- `HostWindow.OnClosing` 先请求 `Factory.OnWindowClosing`，允许宿主在拆除前取消；随后会执行 Root.Close。
- `HostWindow.OnClosed` 继续调用 `CloseWindow`，遍历并关闭内容；因此子窗关闭必须提前汇总保护，不能等 Closed 再询问。
- `AddWindow` 执行 InitDockWindow，`RemoveWindow` 清除 Window/Owner/Layout 等引用；隐藏工具的恢复记录不能仅依赖即将被清除的原生窗口对象。
- 现有 HostRuntimeShutdown 已集中负责操作排空与 Provider 保留。浮窗只接入窗口与目标 Document 协调，不另建 Runtime 关闭路径。

具体 Float、隐藏和原生窗口时序将在相应实施阶段由 Host 测试验证。当前尚未完成浮动功能、V3 保存恢复、真实桌面或外部业务验收。

## G1 严格数据契约与迁移

新增 V3 有限节点记录、结构校验、严格 JSON 和 V2 纯转换。窗口正常坐标与屏幕参考、隐藏工具组、工具回停位置具有独立语义；没有保存 Document 实例或业务内容。生产仍使用旧布局链，待后续阶段整体切换。

执行 `dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~DockLayoutV3Tests`：13/13 通过，无失败、跳过或构建警告。覆盖往返、负坐标、重复/未知/缺失字段、错误类型、未来格式、超大文件、重复占位、非法活动项、循环和 V2 显隐/顺序/比例转换。

## G2 跨窗口查询

新增工作区级遍历与实际浮窗定位，保留稳定主布局的局部 FindDockById。工具显隐与页面查找覆盖浮窗，活动目标先还原实际窗口，Top/Bottom 的主骨架归一化跳过浮窗根。

运行 Host Unit 的 DockWorkspaceNavigationTests、WorkspaceSessionAndDockFactoryTests、ToolCenterTests 专项：28/28 通过。包含嵌套 Windows 回边去重、同名主/浮窗 Dock 局部查询，以及原 Workspace 与工具中心回归。浮动开关尚未开放，原生窗口回归继续在 G3/G4 完成。

## G3 前半：范围关闭与同步窗口回调适配

文档关闭协调先固定整组目标、汇总确认及保存，再排空目标命令；返回的短期许可只控制框架重入，不提前释放业务 Scope。新增 DockWindowCloseCoordinator 将首次同步关闭取消、异步准备和下一轮 UI 回调连接起来，内容集合变化、窗口移除、框架拒绝、重试异常及迟到任务均撤销许可。主窗口和浮窗不会同时进行两轮保存确认。

执行 `dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~DocumentCloseTests|FullyQualifiedName~DockWindowCloseTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests' --logger 'trx;LogFileName=v11-window-close-coordination.trx'`：33/33 通过，零失败、跳过或构建警告。TRX 在该测试项目本地 TestResults 目录。

新增测试使用可控重试队列，覆盖两份文档整组取消/文件选择取消、干净文档等待命令、等待期间新修改、范围关闭与单页/主窗口互斥、集合变化和移除后的迟到确认。它们验证协调基础，不等于真实 HostWindow 已验收；浮动策略、主窗口退出集成、视图跨 TopLevel 回归仍待后续完成，G3 尚未整体通过。

## G3/G4 交互适配基础

新增 HostFloatingWindow，保留锁定 Dock 的原生窗口、外框及拖动协议，仅组合 Dock OverlayHost 与工作台覆盖层。主窗和浮窗复用 WorkbenchWindowInteraction，各自拥有 KeyBinding、面板焦点与全屏租约；WorkbenchWindowContext 只登记窗口、活动 Owner 和唯一面板会话。对话框和选择器固定本次有效 Owner，关闭浮窗的异步确认通过调用链局部 Owner 范围定位，失效窗口的路径结果丢弃。

核查发现 Dock 原生 OnClosing 在 Window.Closing 被其他监听者取消后仍会调用 Factory 并可能执行 Root.Close。适配层保存同一 Closing 参数供 Factory 复核，取消时在任何内容拆除前退出。此行为已通过实际 IDockWindow.Present 创建的 HostFloatingWindow Headless 路径验证。

- `dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~WorkbenchCommandPresentationUiTests|FullyQualifiedName~CognitiveUxV8UiTests|FullyQualifiedName~ApplicationAndWindowTests' --logger 'trx;LogFileName=v11-floating-command-ui.trx'`：34/34 通过。
- `dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~DocumentCloseTests|FullyQualifiedName~DockWindowCloseTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests|FullyQualifiedName~WindowInteraction|FullyQualifiedName~StorageService' --logger 'trx;LogFileName=v11-window-coordination.trx'`：36/36 通过。

两组均零失败、零跳过、零构建警告。新增 UI 场景证明浮窗 Ctrl+S 一次主文件提交（另有正常的恢复备份写入）、共享命令且绑定实例隔离、跨窗面板互斥、异步明确 Owner、关闭取消保留内容及 Closed 清理绑定。浮动开关仍未开放；Float/FloatAll 真正迁移、主退出最终提交、全屏期间迁移限制、多屏与实际原生资源仍待后续验证，不能把本节记作 G3/G4 全部完成。
