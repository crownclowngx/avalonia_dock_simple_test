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
