# Host V9 升级与定制整理：开发实施和验收记录

> 状态：实施中；本记录仅包含实际执行事实，未完成项不得写成通过。
> 日期：2026-09-16。方案：[Host V9](../../design/host-v9-avalonia-dock-upgrade-plan.md)。
> 分支：`codex/host-v9-avalonia-dock-upgrade`；起点：`bc3d877fc9b72810a060c2288b1d63cd6fdd0b19`。

## 执行范围

用户授权按 V9 实施并按阶段提交 Git；要求 SOLID、朴素设计、详细中文注释、完整单元/集成测试和同步文档。明确不使用 AIFLOW、Windows CI 或发布门禁。本轮使用本地开发 verify 和定向测试；不执行 seal、正式 Windows Smoke、公开 NuGet 上传、安装目录部署或产品发布升版。

SDK 3.4.1 / Templates 1.4.2 是本地开发候选；Host 产品保持 3.0.0，3.0.1 留待发布。SDK、插件清单及磁盘格式主版本不变。

## G0 基线冻结

- 在现有 master 基础上创建独立分支；已有 V9 方案、评估和导航文档来自本次会话，随基线提交保留。
- .NET SDK：10.0.302；Avalonia 12.1.0；Dock 12.0.0.2；Core/UI SDK 3.4.0。
- 从现有安装目录复制完整旧 Controls 到 `artifacts/host-v9/baseline/old-controls`，共 12 个插件、525 个文件。
- `old-plugin-files.json` 记录逐文件长度和 SHA-256；`old-plugin-manifests.json` 记录原始清单。测试仅使用副本，不重编译外部插件。
- 原始证据目录：`artifacts/host-v9/baseline/`。本轮不部署，完整 Host 发布备份与正式回退演练留待发布阶段。

## 阶段状态

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| G0 基线 | 已完成 | 分支、版本与旧插件产物已冻结 |
| G1 依赖升级对照 | 已完成 | 保留定制；构建及 713 项 Host/插件/UI 测试通过 |
| G2 浮动样式清理 | 已完成 | 保留禁止浮动能力和主窗口原生标题栏 |
| G3 指针保护 | 已完成（自动化） | 修正 Direct 订阅；移交后退出恢复职责 |
| G4 回收器 | 已完成（自动化） | 安全解绑、绑定保留、最终释放及实例传播 |
| G5 布局/Factory | 已完成（自动化） | 保留实现；新增集合及回调异常回归 |
| G6 SDK/模板及本地验证 | 待执行 | 候选打包不等于发布 |
| G7 旧插件 | 待执行 | 冻结 DLL 加载、组合与视图测试 |
| G8 发布/部署 | 不在本轮 | 按用户要求推迟 |

## SOLID 与设计约束

- Dock 适配仍留在 Host internal 边界，不向插件 SDK 暴露新 Dock 类型。
- 指针保护只负责指针所有权和结束后的恢复，不接管 Dock 排序/停靠决策。
- 回收器负责缓存与安全挂载；View 租约、Document Scope 和 Session 保持既有唯一所有者。
- 优先修改现有窄边界和补充行为测试，不为升级引入通用框架、全局单例或多余接口。

## 验证证据

### G1：仅升级依赖的对照

- Avalonia 家族与 Headless.XUnit 统一为 12.1.2；Dock 家族统一为 12.1.0.6，重新求值并提交锁文件。
- `dotnet restore MyAvaloniaManagement.sln --force-evaluate` 成功。
- `dotnet build MyAvaloniaManagement.sln -c Release --no-restore -warnaserror -m:1`：0 警告、0 错误。
- Release 下排除 `PackageAcceptance` 的三个测试项目：主程序 397/397、插件 217/217、UI 99/99；无跳过。
- 证据：`artifacts/host-v9/g1-build.log`、`artifacts/host-v9/g1-tests/*.trx`。
- 此结果只证明保留原定制的升级对照成立；不代替捕获移交、父级解绑及旧插件业务交互验证。

### G2–G5：定制处理与行为回归

| 定制 | 实际处理 | 依据与结果 |
| --- | --- | --- |
| App.axaml 浮动窗口覆盖 | 删除两个 HostWindow 样式 | 现有浮动能力已经禁止；保留四个 Float override、根策略、主窗口标题栏、三类标签保护及回收器样式 |
| DockTabPointerCaptureGuard | 保留并改造 | 捕获丢失是 Direct 事件，原 Tunnel 订阅无效；捕获移交后取消旧恢复任务，卸载/停用不清理接收方手势 |
| DocumentControlRecycling | 保留并改造 | 先摘除实际 Presenter，再处理逻辑父级；检查子项身份、SetCurrentValue 保留绑定、立即 UpdateChild；不能解绑则报错，不创建第二个 View |
| 最终关闭释放 | 保留所有权并加固 | Remove 的 finally 始终释放 View；Adapter 租约继续保证幂等，DockDocumentLifetime 继续释放 scope |
| HostDockFactory / 布局映射 / 工具协调器 | 保留实现 | 新框架的集合替换和回调行为通过回归，不引入新抽象或更改 layout-v2 格式 |

- 指针测试先红后绿：7 项真实 Headless 输入/路由用例，旧实现失败 3 项（移交后卸载、停用、捕获取消），修改后全部通过。测试输入显式携带左键状态，避免把无键移动误当成拖拽。
- 回收器最初 9 项中旧实现失败 3 项（仅逻辑父级、未知父级、最终关闭错误报告）；修复后增加“空内容模板仍返回旧 View”拒绝复用用例，10/10 通过。
- 已验证生产 Style 首次初始化取得 Host 回收器，同 Factory 的后续 DockControl 共享实例，不同 DI 容器隔离。
- Factory / Adapter 专项 25/25；新增普通集合转换、重复初始化、最后标签关闭、Closing 取消/异常、Closed 通知异常回归。
- 样式与禁浮动专项 14/14；最终完整 Headless UI 113/113，无失败或跳过。完整工具四向恢复、文档分割、活动目标、同 View 复用等已有回归保留。
- 证据：`artifacts/host-v9/regressions/` 中 `g3-before/after`、`g4-before/after`、`g2`、`g5`、`g2345-ui` TRX。
- 事件实现参考：[Avalonia InputElement](https://github.com/AvaloniaUI/Avalonia/blob/12.1.2/src/Avalonia.Base/Input/InputElement.cs)、[Pointer](https://github.com/AvaloniaUI/Avalonia/blob/12.1.2/src/Avalonia.Base/Input/Pointer.cs)。实际 Windows 多屏/DPI/失活焦点体验仍待人工验证，不能由 Headless 结果代替。
- 后续审查补充“捕获交给标签后代时，共同祖先不会收到 Lost”的两项回归；结束边界再次核对指针所有权，当前指针专项合计 9/9，见 `g3-final.trx`。
