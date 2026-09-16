# V11-P2 Tool 浮窗关闭故障记录

> 日期：2026-09-16。起始基线：`d52600b`。计划见 [V11-P2](../../../roadmap/host-v11-p2-tool-window-close-fix-plan.md)。

## 原因及证据边界

用户确认点击 Tool 浮窗右上角关闭按钮。诊断夹具使用生产 HostWindow 模板与 Headless 鼠标事件，复现按钮 Click 和 Tool 命令双重入口：Tool 已隐藏，窗口、模型窗口和 HostWindows 仍存在。取消关闭后仍隐藏 Tool 的问题也成立。

窗口 Close 对照正常。只在诊断夹具中抑制同次点击的第二条命令后，按钮正常关闭和取消保留内容均通过；直接最后 Tool 命令仍需统一关闭协议。诊断记录位于本地 `artifacts/host-v11-p2-diagnosis/`，不能当成生产修复或真实 Windows 鼠标验收。

## 实施记录

先提交正式失败测试，修复前后专项和最终验证分别记录。不得删除 Coordinator 的 SameContents 检查来强行关闭，也不得先移除窗口模型再处理原生取消。最终 MD 定稿后仅回填非嵌入 JSON，避免改变已验证产物身份。

P2-1 正式红灯：`dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~DockToolWindowCloseUiTests` 共 8 项失败、0 通过、0 跳过。覆盖按钮、关闭命令、隐藏和工具中心四入口的成功/取消分支；本地 TRX 为 `artifacts/host-v11-p2/red/p2-red.trx`。此提交故意保留生产缺陷。
