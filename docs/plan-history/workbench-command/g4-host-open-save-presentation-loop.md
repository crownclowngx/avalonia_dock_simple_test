# Workbench Command G4：Host 打开/保存 Presentation 真实闭环实施记录

> 状态：已完成；生产闭环、定向测试、Host/四插件回归及完整非发布门禁均已通过。
>
> 输入提交：`4d284ec7f867a4b12e858c756c236d824195e602`
>
> 输入 Git tree：`08bcce5b72f615375a0dd2118f59de68bdf0fa4f`
>
> 前置：[G3 Context v1 与活动 Document Target 路由](./g3-context-active-document-target-routing.md)
>
> 总设计：[Workbench Command 引入评审与实施任务书](../../design/workbench-command-introduction-plan.md#g4迁移-host-打开保存完成第一个真实闭环)

## 1. 实施边界

G4 只把 Host File 菜单的打开/保存和 `Ctrl+S` 迁入既有 CommandId、State Query 与 Executor，建立第一个
真实用户闭环。通用插件 Menu/KeyBinding Contribution、Command Palette、主题、Document Creation、Tool
Toggle、错误条关闭及插件局部命令均未迁移，这些能力继续留在各自后续阶段或既有专用入口。

本阶段没有修改 Core/UI SDK public API、API baseline、版本、NuGet 包、模板或外部 WorkflowStudio、
ClassicGame。产品仍为 3.0.0，SDK 仍为 3.2.0；manifest、Document envelope、layout 保持 schema 2，
布局文件仍为 `layout-v2.json`，数据根仍为 `v2`。

## 2. 设计思路与源码变化

最终用户链只有一条：

```text
File 菜单 / Ctrl+S
    → HostWorkbenchCommandPresentation
    → WorkbenchPresentationCommand(CommandId)
    → WorkbenchCommandStateQuery / WorkbenchCommandExecutor
    → HostOpenDocumentCommandHandler / HostSaveDocumentCommandHandler
    → DocumentPersistenceCoordinator / DocumentOperationState
```

`WorkbenchPresentationCommand` 是 internal Avalonia Adapter：`CanExecute` 与只读 `IsEnabled` 每次查询统一状态，
`ExecuteAsync` 直接进入 Executor 的执行前重查。`ICommand.Execute` 只启动一个内部已观察任务，所有意外异常
都会被封闭并写稳定脱敏诊断。状态通知按 CommandId 过滤，工作线程事件经显式 Dispatcher 回到 UI 线程；
Dispose 会退订状态源并使排队中的迟到刷新失效。

显式 `IsEnabled` 是 Avalonia MenuItem 的确定性展示接缝：MenuItem 会在触发时检查 `CanExecute`，但初始挂载
并不保证把该值写入控件的 `IsEnabled`。该属性不缓存业务状态，只为 XAML 提供同一查询结果及
`PropertyChanged` 通知，因此没有形成第二套状态模型。

`HostWorkbenchCommandPresentation` 是根容器单例，只创建 Open/Save 两个 Adapter。File 菜单和 Window
KeyBinding 取得同一个 Save 实例。`MainWindowViewModel` 只暴露窄 `WorkbenchCommands` 绑定端口，已经删除
`DocumentPersistenceCoordinator` 依赖、`OpenDocument`/`SaveDocument` 方法及生成命令；设计器使用独立、
无副作用的纯内存实现。

## 3. SOLID 与朴素模式

| 原则 | G4 做法 |
| --- | --- |
| SRP | State Query 判定状态，Executor 执行，Presentation Adapter 只做 Avalonia 绑定/线程切换，ViewModel 只协调窗口 |
| OCP | 新展示入口只消费稳定 CommandId；后续 G5 可替换 Host-only 组合而不修改 Handler 或持久化用例 |
| LSP | 生产 Presentation 与设计数据满足同一 internal 窄端口，XAML 不区分具体实现 |
| ISP | 展示命令只公开 ICommand 与实时 IsEnabled；不暴露 Context、Executor、Provider、Dock 或 Document |
| DIP | 组合根显式注入 State Query、Executor、Dispatcher 和诊断端口；运行期不通过 Provider 定位依赖 |

采用的模式只有 Presentation Model、ICommand Adapter 与既有 Catalog/State/Executor。没有引入 MediatR、
CQRS、事件总线、事件溯源、反射发现、字符串条件、重试、队列、单飞、通用 Run Manager 或第二套 Workflow
Action Runtime。新增类型及状态重查、线程切换、异常观察、退订和所有权均有中文 XML/设计注释。

## 4. 测试、覆盖率与兼容证据

专项入口为：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~WorkbenchCommand

dotnet test Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~WorkbenchCommandPresentationUiTests

pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG4.ps1 -Configuration Release
```

最终专项结果如下，均为 2026-08-28 Release 实际输出：

| 验证层 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Workbench Command Unit 定向 | 50 | 0 | 0 |
| G4 Headless UI 定向 | 5 | 0 | 0 |
| Host Unit | 277 | 0 | 0 |
| Host Headless UI | 70 | 0 | 0 |
| Host Plugin | 212 | 0 | 0 |
| Host 三层合计 | 559 | 0 | 0 |
| 四个仓内插件门禁聚合 | 3569 | 0 | 0 |

新增 `WorkbenchCommandPresentationTests` 为 **3/3**；Headless 专项为 **5/5**。覆盖内容包括稳定身份与单例
共享、无/不可/可持久化目标、目标切换、执行前重查、定向/非相关/全量/工作线程通知、异常观察者隔离、
Dispose 后禁用与排队迟到事件、生产 File 菜单、真实 Headless `Ctrl+S`、打开取消、保存失败错误条和纯内存
设计数据。旧持久化用例改由 Executor 或直接用例所有者验证，不再调用已删除的 ViewModel 方法。

最终合并 Host 行/分支覆盖率为 **86.55% / 72.03%**，均不低于 G3 的 **86.51% / 71.76%**。专项摘要
直接读取的逐文件结果如下：

| G3/G4 关键文件 | 行覆盖率 |
| --- | ---: |
| `WorkbenchContextSnapshot.cs` | 100.00% |
| `WorkbenchContextStore.cs` | 96.43% |
| `WorkbenchCommandStateQuery.cs` | 94.30% |
| `WorkbenchCommandExecutor.cs` | 93.85% |
| `WorkbenchDocumentCommandLeaseStore.cs` | 99.03% |
| `DocumentCloseCoordinator.cs` | 94.89% |
| `WorkbenchPresentationCommand.cs` | 90.53% |
| `HostWorkbenchCommandPresentation.cs` | 100.00% |

Core/UI SDK API baseline 条目继续为 **127/91** 与 **45/66**，四份 SHA-256 与输入基线一致；SDK/Product
版本仍为 **3.2.0 / 3.0.0**。文档门禁通过 **98** 份文档、**542** 个本地链接、**193** 个脚本路径和
**51** 个项目路径。专项证据收集 **27** 份 TRX 与 **46** 份覆盖率 XML；机器摘要位于
`artifacts/test-results/WorkbenchCommandG4/summary.json`，证据快照位于同目录的 `evidence/`。

完整 Host V4 G7 本地开发门禁先实际执行并通过；补充测试线程边界覆盖后，仅测试源码变化，重新执行 Host
三层 559 项并用 `-ReuseVerifiedBaseGate` 完成最终 G4 结构、API、覆盖率、文档及定向复验。该参数没有跳过
G4 专项断言，也没有把失败的生产构建或插件回归标记为绿色。

## 5. 非发布边界与回滚

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
published=false
uploaded=false
tagCreated=false
```

G4 不读取或使用 AIFLOW，不调用 Windows CI/Smoke、Release Acceptance、Host Release Gate、上传、签名、
tag 或发布命令。整体回滚单位为 Presentation、组合根、主窗口绑定迁移、测试、专项门禁和本文档；失败时
整体回到输入 G3，不允许只恢复 ViewModel 转发而留下菜单/快捷键混用两条路径。
