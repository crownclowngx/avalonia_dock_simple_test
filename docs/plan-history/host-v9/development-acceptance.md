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
| G6 SDK/模板及本地验证 | 候选验证通过，完整 verify 待汇总 | 仓库外生成、锁定还原、构建、4 项测试及真实 ZIP Host 验收通过 |
| G7 旧插件 | 自动层通过；真实业务待验收 | 12 个旧 DLL / 60 个视图；部分真实 Workspace 回归 |
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

### G6：SDK 与模板本地候选

- Core/UI SDK 的包、FileVersion、AssemblyVersion 同步为 3.4.1 / 3.4.1.0，原 v3 API 基线未改写。产品保留 3.0.0。
- UI Profile 精确依赖 Avalonia 12.1.2；模板 1.4.2 精确引用 Core/UI 3.4.1、Desktop 12.1.2，新插件 manifest 最低 SDK 为 3.4.1。
- 本地 feed 使用独立 NuGet cache；package source mapping 将 Core/UI 仅映射到候选 feed，其他依赖从 NuGet.org 获取。三个嵌入模板锁文件重新生成并随源码提交。
- 安装本地 nupkg 到独立 hive，在仓库外临时目录生成点分名称 `Independent.V9`，排除了主仓 Directory.Build.targets 的隐式导入；locked-mode 还原成功，Release 构建 0 警告/错误，4/4 测试通过。
- `BuildManagedPluginPackage` 生成真实 ZIP，在独立 Controls 解压后通过新 Host：加载/共享身份/DI/全部声明视图，以及真实 Document 创建→分割→关闭、Tool 显示→隐藏→恢复共 2/2 外部用例。
- 证据：`artifacts/host-v9/feed/`、`template-tests/independent-template.trx`、`external-tests/generated-package.trx`、`generated-package/`；独立项目位置保存在 `template-workspace.txt`。全部是开发候选，未上传公开源。

### G7：原样保留的旧插件

冻结副本使用旧 SDK 3.4.0 编译。验收时未重编译这 12 个产物，也未提升它们的版本或清单。真实加载上下文与入口程序集路径均被断言；Avalonia 和 SDK 引用解析到 Default ALC 的 Host 程序集。

| 冻结插件目录 | 版本 | 文档声明 | 工具声明 | 视图创建/布局/复用/释放 |
| --- | --- | ---: | ---: | ---: |
| BaiduDiskPlugin | 1.0.1 | 1 | 1 | 2 |
| BiliDownloader | 3.0.1 | 1 | 1 | 2 |
| ClassicGamePlugin | 1.1.1 | 14 | 0 | 14 |
| DaTang | 3.0.1 | 2 | 0 | 2 |
| FractalArtPlugin | 1.0.1 | 1 | 0 | 1 |
| ImageLabPlugin | 1.0.1 | 21 | 0 | 21 |
| LayerUnpackPlugin | 1.0.1 | 3 | 0 | 3 |
| MyPlugTest | 3.0.0 | 4 | 1 | 5 |
| NovelGeneratePlugin | 1.3.0 | 1 | 2 | 3 |
| VideoSecurityPlayer | 3.1.1 | 4 | 0 | 4 |
| WorkflowStudio | 1.2.1 | 1 | 0 | 1 |
| XianCaiWorkerPlugin | 1.4.0 | 2 | 0 | 2 |
| 合计 | — | 55 | 5 | 60 |

- 12/12 通过 DLL、类型解析、真实 DI 组合及全部声明视图测试。视图在这一层未绑定业务模型，也未启动插件生命周期；不能由此推断原生播放/后台任务已通过。
- 另用旧 MyPlugTest 和 ClassicGame 的真实模型，通过 18 个 Document 创建、正文挂载、向右分割、同 View 复用和最终关闭；MyPlugTest 的 1 个 Tool 通过隐藏/恢复。2/2 用例通过。
- 合计 14/14，零跳过；证据 `artifacts/host-v9/external-tests/old-binaries.trx`。模板 ZIP 的 2 项验收另计，不混入旧插件结果。
- 外部验收夹具仅在 `HostExternalPluginAcceptance=true` 时编译，使用独立构建目录和显式路径输入；默认 Gate 不依赖个人安装目录。命令见 [V9 使用说明](../../quick-start/host-v9-upgrade.md)。
- G7 的原生播放、外部账号/数据库/下载/定时任务以及真实 Windows 操作仍待对应业务验收；本轮没有运行这些任务或将其标为通过。

### 完整开发验证中的修正

- 首次 verify 在 SDK 套件发现两个仍断言 AssemblyVersion 3.4.0.0 的历史断言；同步到已决策的 3.4.1.0，继续保留 Core 的 BCL-only、UI 无 Dock 以及公共 API 断言，没有降低门槛。
- 最终指针审查再复现“窗口外松开后只有无键移动返回”的自有捕获残留：新增先红后绿用例，取消分支改为释放自有捕获再恢复视觉状态。指针专项累计 10 项。
- 525 个冻结文件和安装目录中的对应原文件逐项 SHA-256 对比均无差异，见 `artifacts/host-v9/baseline/hash-verification.json`。
