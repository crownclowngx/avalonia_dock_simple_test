# V11-P2 Tool 浮窗关闭故障记录

> 日期：2026-09-16。起始基线：`d52600b`。计划见 [V11-P2](../../../roadmap/host-v11-p2-tool-window-close-fix-plan.md)。

## 原因及证据边界

用户确认点击 Tool 浮窗右上角关闭按钮。诊断夹具使用生产 HostWindow 模板与 Headless 鼠标事件，复现按钮 Click 和 Tool 命令双重入口：Tool 已隐藏，窗口、模型窗口和 HostWindows 仍存在。取消关闭后仍隐藏 Tool 的问题也成立。

窗口 Close 对照正常。只在诊断夹具中抑制同次点击的第二条命令后，按钮正常关闭和取消保留内容均通过；直接最后 Tool 命令仍需统一关闭协议。诊断记录位于本地 `artifacts/host-v11-p2-diagnosis/`，不能当成生产修复或真实 Windows 鼠标验收。

## 实施记录

先提交正式失败测试，修复前后专项和最终验证分别记录。不得删除 Coordinator 的 SameContents 检查来强行关闭，也不得先移除窗口模型再处理原生取消。最终 MD 定稿后仅回填非嵌入 JSON，避免改变已验证产物身份。

P2-1 正式红灯：`dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~DockToolWindowCloseUiTests` 共 8 项失败、0 通过、0 跳过。覆盖按钮、关闭命令、隐藏和工具中心四入口的成功/取消分支；本地 TRX 为 `artifacts/host-v11-p2/red/p2-red.trx`。此提交故意保留生产缺陷。

### P2-2 修复与保存时序

- `HostFloatingWindow` 在 Button.Click 冒泡到窗口时识别最后一个 Tool 的 Chrome 关闭按钮，消费同次点击，保留 Dock 已发起的窗口关闭，阻止随后重复隐藏的命令。多工具/混合窗口继续执行单项命令。
- `HostDockFactory.CloseDockable/HideDockable` 将最后一个 Tool 转交原生窗口关闭。窗口还可取消时执行 Tool 能力和 DockableClosing 检查；短期许可只供基类清理使用，结束、拒绝、异常和移除后撤销。隐藏及 DockableClosed 仍由基类执行，Session 独占原模型和 View。
- Session 以隐藏后的实际归属判断工具中心操作结果，取消时保留活动项并返回未接受；Coordinator 的 SameContents、文档确认、取消和迟到保护未改动。
- 回归发现原生 Closed 先注销位置跟踪，框架后续逐项隐藏又触发布局捕获，会把已记住的 bounds 覆盖成默认值。现将 CloseWindow 清理合并为一次批量变更：使用 Closing 获准时的完整位置记录，清理后只提交最终隐藏状态。多组顺序、比例和窗口位置均有断言；保存组件未加入关闭策略。

框架事件顺序依据锁定版本 [Dock HostWindow](https://raw.githubusercontent.com/wieslawsoltes/Dock/cc08602d02fde1b85067cec064da29f34785e505/src/Dock.Avalonia/Controls/HostWindow.axaml.cs)、[ToolChrome 模板](https://raw.githubusercontent.com/wieslawsoltes/Dock/cc08602d02fde1b85067cec064da29f34785e505/src/Dock.Avalonia.Themes.Fluent/Controls/ToolChromeControl.axaml) 和 [Avalonia Button](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.2/src/Avalonia.Controls/Button.cs)。不升级依赖、不改插件 SDK/V3、不重建业务实例。

### 专项自动化结果

新增 29 个案例（UI 24、单元 5），受影响专项结果如下；均为 Release、`--no-restore -m:1 -warnaserror`，0 失败、0 跳过。

| 入口与范围 | 通过 | 本地 TRX |
| --- | ---: | --- |
| UI：DockToolWindowCloseUiTests、DockLayoutV3UiTests、DockToolSplitUiTests、ToolCenterUiTests | 60 | artifacts/host-v11-p2/ui/p2-ui.trx |
| Unit：WorkspaceSessionAndDockFactoryTests、DockWindowCloseTests | 25 | artifacts/host-v11-p2/unit/p2-unit.trx |

覆盖真实模板按钮鼠标事件、菜单实际绑定命令、直接关闭/隐藏、工具中心、多工具逐项与多组整窗、原生及 Factory 取消、异步重试被取消后重试、能力/最后项约束、DockableClosing 拒绝、许可清理和异常。断言原窗口/模型窗口/HostWindows 同步回收，取消不隐藏、模型/View 不释放、文档原实例与令牌保持、V3 实际落盘及重启恢复。Headless 模板按就绪条件等待，关闭动作不重试，避免固定一帧造成夹具时序波动。

### P2-3 最终证据与交付边界

本 MD 与全部受影响指南定稿后运行一次完整 `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`；结果、源码/产物身份、TRX 计数和哈希仅回填 [p2-final-development-evidence.json](p2-final-development-evidence.json)。该 JSON 记录实际结果，不以专项通过预填最终通过。

真实 Windows 右上角关闭、显示器/焦点及外部 Bili 下载等业务验收仍待执行：当前计算机工具没有可操作的原生应用表面，Headless 不能代替桌面。操作清单见 [专项维护指南](../../../maintenance/floating-layout-verification.md)。本次交付源码补丁与开发验收，未覆盖 `D:\data\avalonia` 中的 P1 单文件自包含产物；未运行 Windows CI、正式发布门禁或 AIFLOW。
