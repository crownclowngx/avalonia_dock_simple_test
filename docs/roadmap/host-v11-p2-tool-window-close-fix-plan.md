# V11-P2：Tool 浮窗关闭黑框修复

> 分支：`codex/host-v11-floating-layout`。基线：`d52600b`。状态：修复与专项自动化已完成，最终 verify 另见非嵌入证据，真实桌面待验收。用户已授权修复 Tool 浮窗关闭；已先提交正式失败测试。安装目录部署另行交付，不使用 AIFLOW、Windows CI 或正式发布门禁。

## 故障与目标

ToolChrome 关闭按钮先触发浮窗关闭，随后执行 CloseDockable 命令。首次窗口关闭被协调器取消并排队重试，按钮命令却先隐藏了 Tool；内容集合变化使重试撤销，留下可见空窗。直接关闭或隐藏最后一个 Tool 同样缺少窗口回收。现有直接调用 Window.Close 的测试不能覆盖按钮联动。

成功时关闭窗口外壳、隐藏工具并保留原模型/View、窗口位置与分组；取消时保持工具、活动项和窗口原状。主窗口 Tool、仍有其他内容的浮窗、Document 关闭及其范围变化保护保持既有语义。

## 设计与 SOLID

1. HostFloatingWindow 适配框架按钮事件，浮窗关闭已处理同次点击时阻止后续重复 Tool 命令，不以鼠标专用过滤代替 Click 协议。
2. HostDockFactory 统一最后 Tool 的关闭/隐藏入口，在修改内容前请求已有窗口关闭协议；Tool 能力和 DockableClosing 在原生 Closing 中预检，获准许可只供后续清理消费并按结束路径撤销。框架最终关闭阶段仍执行原隐藏、Closed 事件和释放顺序，避免递归请求与重复询问。
3. Session 保持业务所有权及显隐结果语义；取消不得修改 ActiveDockable 或报告成功。Coordinator 保留取消、范围相同检查和迟到任务保护。
4. 保存组件独立，继续在原位置完整时捕获、最终状态提交后保存。CloseWindow 合并框架逐项隐藏，避免 Closed 后位置跟踪器已注销时再次捕获默认 bounds。不改 SDK、Dock 版本、V3 格式或插件业务寿命。

沿用现有窄边界，按窗口拓扑而非插件名称判断，不新增通用事件总线、事务或布局管理器。核心入口使用中文注释说明事件顺序、取消与原对象所有权。

## 验收与交付

- 正式失败测试覆盖真实按钮、CloseDockable、HideDockable、工具中心隐藏，分别检查成功和取消。
- 补多工具、最后一个工具、混合 Document、关闭能力约束、真实右键菜单命令、重新显示、V3 写入重启及布局转移回归。
- 检查原窗口关闭、模型窗口移除、HostWindows 清理、隐藏记录保留、模型/View 身份不变；不能只断言无异常。
- 同步专项记录、浮窗指南、V3 契约、Host 设计与导航；文档定稿后运行完整本地 verify，最终身份和哈希写非嵌入 JSON。
- 按失败复现、修复及文档、最终证据分阶段提交中文标题和说明。Headless 结果与真实桌面分别登记。

实施记录：[P2 修复记录](../archive/records/host-v11/p2-tool-window-close-fix.md)。

新增 24 项 Headless UI、5 项协议单元测试；连同受影响回归，UI 专项 60 项、单元专项 25 项通过。最终完整本地结果以 [P2 开发证据](../archive/records/host-v11/p2-final-development-evidence.json) 为准，专项不替代最终 verify。安装目录中的既有 P1 产物不作为本补丁的验证产物。
