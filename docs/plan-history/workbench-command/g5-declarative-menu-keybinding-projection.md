# Workbench Command G5：声明式 Menu 与 KeyBinding Projection 实施记录

> 状态：已完成；生产投影、定向测试、Host/四插件回归及完整非发布门禁均已通过。
>
> 输入提交：`e233196d3c7c70bfb99ca69e9151b14bf158dc33`
>
> 输入 Git tree：`cee16d78489cf66374652fe5cc3156aa3ea16d30`
>
> 前置：[G4 Host 打开/保存 Presentation 真实闭环](./g4-host-open-save-presentation-loop.md)
>
> 总设计：[Workbench Command 引入评审与实施任务书](../../design/workbench-command-introduction-plan.md#g5建立声明式-menu-与-keybinding-projection)

## 1. 实施边界

G5 把 G1 已冻结的菜单与快捷键声明投影成 Host-owned Avalonia 对象。Host 继续拥有“文件、视图、工具、
帮助”四个顶级容器和静态“主题”项；插件只能进入四个既有共享末端位置。File 打开、保存和 `Ctrl+S`
由 Host 内建 Contribution 提供，G4 的静态 XAML 入口已经删除。

本阶段没有增加或修改 Core/UI SDK public API，没有改版本、NuGet 包、模板、外部插件仓库或磁盘协议。
产品仍为 `3.0.0`，SDK 仍为 `3.2.0`；manifest、Document envelope、layout 保持 schema 2，布局文件仍为
`layout-v2.json`，数据根仍为 `v2`。没有实现嵌套菜单、插件 Container、动态图标、Checked/Radio、动态标题、
用户快捷键设置、字符串 Gesture/菜单路径、`when` 表达式或 Command Palette。

## 2. 设计思路与源码变化

运行链保持单向：

```text
Host 内建声明 + PluginRegistry 不可变 Contribution
    → WorkbenchMenuProjection / WorkbenchKeyBindingProjection
    → WorkbenchPresentationCommandStore（每个 CommandId 唯一 Adapter）
    → MenuView / MainWindow 创建并拥有 MenuItem、Separator、KeyBinding
    → WorkbenchCommandStateQuery / WorkbenchCommandExecutor
```

`HostWorkbenchCommandProjectionCatalog` 只保存三个稳定声明：File 打开 Order 0、File 保存 Order 10、
保存 `Ctrl+S`。它不伪造 Host PluginId，也不持有控件。`WorkbenchCommandPresentation` 组合两个窄投影和
共享 Adapter Store；菜单与快捷键取得相同 CommandId 时得到同一个 `WorkbenchPresentationCommand`，退出时
按“投影观察者、Adapter”顺序统一退订。

菜单先输出 Host 内建条目，再按 Location、Group 的 Ordinal、Order 和 PlacementId 的 Ordinal 输出插件条目。
Separator 只从本轮可见快照计算：非空 Group 边界且前方已有可见项时插入；隐藏项不会遗留开头、结尾、连续
或悬空分隔符。Owner 不可用和 `Hide` 的 Target 缺失会移除条目；`Disable` 的 Target 缺失与
`CanExecute=false` 保留条目但由共享 Adapter 禁用。

快捷键直接比较 `(Key, KeyModifiers)`。Host 保留 Gesture 始终优先；不同插件共享 Gesture 时全部不激活，
命令和菜单仍保留。每个受影响 Placement 只写一条固定码 `WORKBENCH_KEY_GESTURE_CONFLICT` 的脱敏诊断，
只附 PluginId 和 PlacementId，不记录路径、异常正文或插件对象。冲突结果来自不可变 Registry，因此不随加载
和枚举顺序漂移。

`PluginAvailabilityReadModel` 新增 Host internal 只读通知。Store 只在布尔可用性真正变化时于锁外发布，
单个观察者失败不会中断其他观察者或状态写入。菜单和快捷键投影把通知合并到 UI Dispatcher；Dispose、重复
释放和排队中的迟到回调均安全失效。

`MenuView` 与 `MainWindow` 是 Avalonia 对象的唯一所有者：每个 View/Window 只记录并移除自己生成的对象，
不修改静态 Host 项。视觉树分离、DataContext 切换、Window Closed 和关闭后的迟到生命周期通知均幂等；
Descriptor、Registry、SDK 和投影内核从不持有 Control、DataContext、Provider、Dock 或插件模型。

## 3. SOLID 与朴素模式

| 原则 | G5 做法 |
| --- | --- |
| SRP | Catalog 保存声明；Menu Projection 负责排序/可见性/Separator；KeyBinding Projection 负责 Gesture 冲突；View 只创建控件 |
| OCP | 新菜单或快捷键继续通过既有不可变 Contribution 扩展，不修改 Executor、Target 或 View 控件协议 |
| LSP | 生产投影与纯内存设计数据满足相同的两个窄 internal 端口，View 不依赖具体实现 |
| ISP | MenuView 只取得菜单快照，MainWindow 只取得活动快捷键，不暴露 Catalog、Executor、Provider、Dock 或插件模型 |
| DIP | 组合根显式注入 Registry、Catalog、State Query、Executor、可用性、Dispatcher 和诊断端口，不运行期服务定位 |

采用的模式只有不可变投影条目、Catalog、ICommand Adapter 和显式 View 所有权。没有引入事件总线、CQRS、
MediatR、反射发现、表达式解释器、动态容器或第二套 Workflow Action Runtime。新增生产类型、成员、排序、
Separator、冲突治理、线程切换和释放边界均以中文 XML/设计注释说明原因。

## 4. 测试、覆盖率与兼容证据

专项入口为：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG5.ps1 -Configuration Release
```

最终 Release 结果如下：

| 验证层 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Workbench Command Unit 定向 | 57 | 0 | 0 |
| G5 Headless UI 定向 | 7 | 0 | 0 |
| Host Unit | 284 | 0 | 0 |
| Host Headless UI | 72 | 0 | 0 |
| Host Plugin | 212 | 0 | 0 |
| Host 三层合计 | 568 | 0 | 0 |
| 四个仓内插件门禁聚合 | 3605 | 0 | 0 |

单元测试覆盖 Host 内建身份/顺序、四个共享位置、注册顺序稳定性、空组与 Separator、Hide/Disable、目标切换、
同类型多实例、工作线程状态通知、CanExecute、Owner Ready/Unavailable、Host Gesture 优先、跨插件双禁用、
加载顺序无关、菜单保留、脱敏诊断、缓存唯一性、重复 Dispose、迟到通知和 shutdown 通知隔离。

Headless UI 使用真实 `MenuItem`、`Separator` 和 `Window.KeyBindings`，验证四个顶级容器与静态主题不变，
File 打开/保存来自内建投影，菜单/快捷键共享 Adapter，`Ctrl+S` 执行前重查，插件菜单执行当前活动实例，
冲突键不安装，Owner 撤回/恢复无重复对象，以及 View/Window 关闭后对象和订阅不复活。

最终合并 Host 行/分支覆盖率为 **86.97% / 72.39%**，高于 G4 的 **86.55% / 72.03%**。G5 关键
文件行覆盖率如下：

| 关键文件/类型 | 行覆盖率 |
| --- | ---: |
| `HostWorkbenchCommandPresentation.cs` / `HostWorkbenchCommandProjectionCatalog` | 100.00% |
| `WorkbenchCommandProjection.cs` | 96.61% |
| `PluginLifecycleStateStore.cs` | 98.72% |

Core/UI SDK API baseline 条目继续为 **127/91** 与 **45/66**，四份 SHA-256 分别保持：

- Core Shipped：`063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F`；
- Core Unshipped：`D80D43C3F4EE6A2214A0DD3B5682402CC6FC6B62FD321E48A2608A4370DDD7AA`；
- UI Shipped：`B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803`；
- UI Unshipped：`C8B831D64C25615291FBFB99740EC633F07EA53B20326FA8CCD222EE6B564932`。

机器摘要位于 `artifacts/test-results/WorkbenchCommandG5/summary.json`，定向 TRX 和 Host/四插件
TRX/Cobertura 快照位于同目录，共收集 **27** 份 TRX 和 **46** 份覆盖率 XML。文档门禁通过 **99** 份
文档、**547** 个本地链接、**195** 个脚本路径和 **51** 个项目路径。完整 Host V4 G7 本地开发门禁实际执行一次；最终 G5 复验使用
`-ReuseVerifiedBaseGate` 复用同一工作树已通过的基础证据，但仍重新执行 G5 定向测试、全部结构/API/版本/
覆盖率/文档断言和证据收集。

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
signed=false
tagCreated=false
```

G5 不读取或使用 AIFLOW，不调用 Windows CI/Smoke、Release Acceptance、Host Release Gate、上传、签名、tag
或发布命令。整体回滚单位是通用 Presentation、Host 内建 Contribution、Menu/KeyBinding View 接入、生命周期
通知、测试、专项门禁和本文档；回滚后完整恢复 G4 Host-only Presentation，不保留静态与声明式双入口。
