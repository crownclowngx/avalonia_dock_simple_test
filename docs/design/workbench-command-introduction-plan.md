# MyAvaloniaManagement Workbench Command 引入评审与实施任务书

> 状态：实施中；G0–G5 已完成，G6–G10 尚未实施。当前已有 Command 候选契约、注册声明、
> Catalog/Executor、Context v1、活动 Document Target 路由、关闭门控、Host 打开/保存 Presentation，
> 以及 Host-owned 声明式菜单/快捷键投影闭环；本文不表示 SDK 候选包、外部插件命令或 Command Palette 已进入生产。
> 评审日期：2026-08-27。
> 事实基线：[主项目内部架构](../../Host/MyAvaloniaManagement/docs/design/architecture.md)、
> [主项目设计方法论与取舍](../../Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md)、
> [宿主—插件架构评审](./host-plugin-architecture-review.md)、
> [Host V4 内部收口任务书](./host-v4-breaking-refactor-plan.md)、
> [Workflow Action 总设计](./ai-workflow-plugin-exploration.md)及 2026-08-27 当前工作树代码。
> 计划性质：本轮建立的是 Workbench Action Model，不是把 Avalonia `ICommand` 换一个名字，
> 也不是第二套 Workflow Action Runtime。Command 只统一需要进入工作台菜单、快捷键和 Palette 的
> 用户语义行为；Document 内部格子点击、表单编辑、拖放和局部按钮默认继续使用插件自己的命令。
> 本文只定义目标、阶段、兼容边界、验证和回滚；每个 G 的真实提交、测试数量、覆盖率、包哈希、
> 外部插件 revision 和发布状态必须在实施时写入 `docs/plan-history/workbench-command/`，不得预填。
> 本计划不使用 AIFLOW，也不把 AIFLOW 作为实施、验证、发布或封板前提。

## 1. 目的与结论

当前 MyAvaloniaManagement 已经能让 Host 与插件声明并运行 Document、Tool 和 Workflow Action，
但工作台级用户行为仍主要附着在具体 ViewModel、XAML 和插件 View 内：

- `MainWindowViewModel` 直接拥有打开、保存和主题切换命令；
- `MenuView.axaml` 直接绑定这些 ViewModel 命令；
- `MainWindow.axaml` 的 `Ctrl+S` 再次直接绑定 `SaveDocumentCommand`；
- 插件能贡献 Document/Tool，却不能声明一个可同时出现在菜单、快捷键和 Palette 的稳定语义动作；
- WorkflowStudio 与 ClassicGame 已有大量实例级 `RelayCommand`，但 Host 不知道哪些行为应提升为工作台命令。

本轮固定结论为：

1. **Command 是有稳定身份的工作台语义动作**，核心身份是 `CommandId`，不是 `ICommand` 实例；
2. **Context 只表达 Host 能确认的工作台事实**，第一版只覆盖活动 Document，不泄漏模型、Control、Dock 或 Provider；
3. **Document Command 由当前活动 Document 实例执行**，通过窄 `IWorkbenchDocumentCommandTarget` 路由，
   不为 Command 重新开放 Document Scope 的任意服务解析；
4. **UI Contribution 只声明位置和展示政策**，Avalonia `MenuItem`、`KeyBinding` 和 Palette 项始终由 Host 创建；
5. **Visible 与 Enabled 分开**：位置贡献决定目标不成立时隐藏还是禁用，当前实例的 Target 决定能否执行；
6. **Command 与 WorkflowAction 永久分层**：Command 表达用户意图，Workflow Action 表达受治理的跨插件业务能力；
7. **Host 先成为 Command 系统的真实用户**：先迁移打开、保存，再允许外部插件贡献 Document Command；
8. **第一版不追求全能 Context 语言**，不引入字符串表达式、任意事实字典、反射方法发现或通用命令总线；
9. **只提升跨工作台有价值的行为**，不把 ClassicGame 十一个游戏和 WorkflowStudio 的全部局部按钮倾倒进 Catalog；
10. **Command Palette 是后续投影，不是内核成立的前置条件**。

### 1.1 实施范围

本任务书覆盖：

- Core/UI Plugin SDK 中兼容新增的 Command 身份、Document Target、描述符和可选注册能力；
- Host internal Command Catalog、Context Store、State Query、Executor、诊断和关闭门控；
- `WorkspaceSession` 的准确活动 Document 变化事实；
- Host 内建打开/保存命令及菜单、`Ctrl+S` 的统一执行路径；
- 声明式 Host Menu 末端共享位置、Menu Command 与 Key Binding Contribution；
- 当前插件注册 Seal、所有权验证、局部 Builder、全局冲突隔离和不可变 Registry 的扩展；
- 外部 WorkflowStudio 的验证/运行/取消工作台命令；
- 外部 ClassicGame 中一个真实游戏的重新开始/撤销工作台命令及多实例验收；
- Command Palette 的最小可用投影；
- SDK 候选包、模板、外部项目独立还原、真实 Host 加载、文档和最终封板。

本轮不要求仓库内四个业务插件把既有局部命令迁移为 Workbench Command。只有为回归、真实包兼容和
最终 Host 验收所必需的调整进入其范围，不得借 Command 计划改变下载、播放、加解密、会计或登录业务。

### 1.2 设计纪律

- Descriptor 构造后不可变，不保存 Avalonia Control、Dock 对象、Provider、Scope 或插件回调；
- Host 内建目录与 Plugin Registry 继续分离，Host 不伪装成一个 `PluginId`；
- 插件注册继续使用一次写入、Seal、所有权校验、全局冲突判断和 Commit/Reject；
- 插件只能把 Command 绑定到自己声明的 Document 类型，不能操作 Host 或其他插件的 Document；
- Executor 在真正执行前重新查询状态，不能相信菜单几毫秒前缓存的 `CanExecute`；
- 事件订阅必须与活动目标切换、Document 关闭和 Host 退出成对释放；
- Command Target 可以在插件内部适配既有 `RelayCommand` 或业务服务，但平台公共身份仍不是 `ICommand`；
- 没有第二个生产实现或真实替换边界时，Host internal 默认使用具体类型，不为每个小协作者增加接口；
- 每个 G 只建立一项可验收事实，SDK、Runtime、UI、外部插件和封板不能压成一个不可回滚提交；
- SOLID 是每个阶段的首要评审纪律：先证明职责、扩展、替换、接口隔离和依赖方向成立，再评审实现便利性；
- 只朴素使用值对象、Descriptor、Adapter、Catalog/Registry 等已经有真实职责的模式；没有第二个生产实现或真实替换边界时不增加接口；
- 新增 public 成员必须提供详细中文 XML 注释；所有权、线程切换、取消、兼容和资源释放等非显然取舍必须用中文设计注释说明原因；
- 真实结果来自命令、TRX、覆盖率和制品，不把计划数字写成完成证据；
- 不使用 AIFLOW、MediatR、事件溯源、CQRS、脚本解释器或反射自动暴露作为本计划捷径。

### 1.3 兼容与破坏边界

本轮允许破坏：

- Host internal 类型、构造函数、目录、绑定端口和测试接缝；
- `MainWindowViewModel` 的打开/保存生成命令及对应设计数据，在 G4 完成统一迁移后删除；
- Host Menu/KeyBinding 的 internal 展示模型与 XAML 结构；
- 外部 WorkflowStudio、ClassicGame 的候选版本和源码，在各自 G 中显式迁移；
- 尚未发布的 Command public API 候选，在 G6 正式冻结前按专项评审调整。

本轮默认不允许破坏：

- Core/UI SDK v3 已有 Shipped API；新增成员只能进入 v3 Unshipped，不能改写历史 Shipped 文本；
- 旧插件不声明 Command 时的加载、Document、Tool、Workflow Action、布局和关闭行为；
- manifest schema 2、Document envelope schema 2、layout schema 2、`layout-v2.json` 和数据根 `v2`；
- 插件独立 ALC/Provider、Document Scope、ClosingToken、生命周期和诊断脱敏边界；
- Workflow Action 的 caller-bound Gateway、Schema、授权、并发、超时、进度和 shutdown drain；
- 外部插件只通过真实 NuGet 包消费 SDK、不引用 Host 源码的独立仓库纪律。

若任一 G 发现必须修改 manifest schema、Document envelope、layout schema、数据根或 SDK 主版本，必须暂停，
新增独立格式/主版本评审；不得把这些变化隐藏在“Command 引入”名义下。

## 2. 当前基线与代码审查

### 2.1 Host 当前事实

| 当前对象 | 已有职责 | 与 Command 的真实缺口 |
| --- | --- | --- |
| `MainWindowViewModel` | 窗口绑定、布局协调、打开/保存、主题和错误条 | 用户行为仍由具体 ViewModel 方法定义 |
| `MenuView.axaml` | 静态文件/视图菜单 | 直接绑定 ViewModel，插件不能声明位置 |
| `MainWindow.axaml` | 窗口和单个 `Ctrl+S` KeyBinding | 快捷键没有稳定 CommandId，也没有冲突治理 |
| `WorkspaceSession` | 工作区唯一所有者，能取得活动 Document | 没有独立、准确的活动 Document 变化通知 |
| `ManagedDocumentDockable` | 持有当前 Document 模型、注册事实和 ClosingToken | 尚未把模型适配为工作台 Command Target |
| `DocumentScopeManager` | 创建、托管并释放 Document Scope | 刻意不暴露 Provider；不应为 Command 打开任意解析入口 |
| `PluginRegistration` | 一次性声明 Document/Tool/Lifecycle/Workflow Action | 没有兼容新增的 Command 注册能力 |
| `PluginRegistryBuilder` | 局部校验、所有权和跨插件冲突隔离 | 尚未收集 Command 与 UI Placement |
| `PluginRegistry` | 不可变插件贡献快照 | 尚未冻结 Command/Placement 注册事实 |
| `PluginProviderOwner` | 插件 Provider、Workflow invocation scope 和 Document Scope 所有权 | Command 不应复制 Workflow Action 的独立 invocation runtime |

`WorkspaceSession.LayoutChanged` 不能被改名后顺便承担全部 Context 变化。用户仅切换 Document 标签时，
布局结构不一定变化；Command 状态需要一个语义准确的 `ActiveDocumentChanged` 或等价窄通知。

### 2.2 真实外部插件事实

WorkflowStudio 是 Host 仓库之外的独立解决方案。其 `MainDocument` 当前已经拥有：

```text
ValidateCommand
RunCommand
CancelCommand
IsRunning
CanExecute
```

`RunCommand` 与 `CancelCommand` 的状态互斥，并且运行最终进入已有 Workflow Action Gateway。Command 引入后
不应重写 Runner 或 Gateway；只需把少量全局有价值的用户意图投影到当前 `MainDocument` 实例。

ClassicGame 同样是独立解决方案，但不参与 G0 基线签署；其 revision、版本、Document 数量、测试和包事实
统一留到 G8 开始时从干净输入独立冻结。已知其 Document 采用实例级 ViewModel，
多个游戏已经有 `RestartCommand`、`UndoCommand`、`HintCommand` 和 `NotifyCanExecuteChanged`。这证明：

- 命令状态属于当前游戏实例，不属于插件单例；
- 同一 DocumentType 的两个实例可以有不同的撤销状态；
- Host 只按 PluginId 或 DocumentType 路由不足以证明目标正确，必须路由到当前活动实例；
- 不需要把棋盘落子、格子点击、数字输入、拖牌等局部交互提升为 Workbench Command。

### 2.3 已确认问题与计划处置

| 问题 | 当前证据 | 计划处置 |
| --- | --- | --- |
| 行为身份缺失 | 菜单和快捷键直接引用 ViewModel 命令 | 建立 `CommandId` 和 Catalog |
| 活动目标通知缺失 | 只有布局提交通知 | 新增准确活动 Document 事实源 |
| Scope 解析不开放 | `DocumentScopeManager` 只返回模型和 ClosingToken | Document 模型实现窄 Target，不开放 Provider |
| 保存状态只在执行时判断 | 现有 Save 命令没有统一状态投影 | Context v1 提供 active/persistable，执行前重查 |
| 插件菜单不可贡献 | XAML 静态菜单；Document 创建另有专用 Tool | 只向 Host 冻结的末端共享位置建立数据化 Command Placement |
| 快捷键无冲突政策 | 当前只有 Host `Ctrl+S` | Host 保留优先，插件冲突只禁用绑定并诊断 |
| 插件动态业务状态 | Workflow `IsRunning`、游戏 `CanUndo` | 由当前实例 Target 查询和定向变更事件提供 |
| Command/WorkflowAction 易混 | 二者都叫“动作” | 固定用户意图与跨插件业务能力分层 |
| 自动创建命令 ID 不合法 | `host.document.create/...` 与现有 ID 规则冲突 | 参数化命令另行设计，本轮不迁移创建/Tool Toggle |
| Palette 尚不存在 | 无搜索、焦点和键盘导航实现 | G9 独立实现，不阻塞内核和菜单闭环 |

### 2.4 已排除的误判

- `RelayCommand` 不是需要全仓删除的旧架构。Document 内部局部交互继续使用它是合理的；
- Command 不是 Workflow Action 的短超时版本，不能复用 JSON Schema、授权、Run Manager 和长任务治理；
- `WorkspaceSession.GetActiveDocument()` 已经是正确原材料，不需要公开一个 Workspace Context 给插件；
- `DocumentScopeManager` 隐藏 Provider 是现有正确所有权边界，不因需要实例命令就退回服务定位器；
- `DocumentDescriptor.MenuCategory` 和 Creation Intent 是 Document 创建专用投影，不应在 G1 被强行删除；
- Tool 显隐、Document 创建和主题 Radio 状态都是真实后续候选，但不是第一个打开/保存闭环的前置；
- Command Catalog 不意味着所有命令必须显示在 Palette；安全、局部或仅上下文菜单动作可以声明不同投影；
- 插件 Command 失败不能让 Host 崩溃，但“捕获异常”也不能变成展示插件原始异常正文或路径的理由；
- 运行期热卸载仍未建立，Command 不得宣称已经解决可回收 ALC 或插件不停机更新。

## 3. 语义模型

### 3.1 四个核心概念

| 概念 | 定义 | 不负责 |
| --- | --- | --- |
| Command | 有稳定 `CommandId` 的用户语义动作 | UI 控件、菜单位置、任意业务 Payload |
| Context | Host 对当前 Workbench 状态的不可变事实快照 | 插件对象图、Control、Provider、任意对象字典 |
| Target | 当前实例对 Command 的状态查询和执行实现 | 全局 Catalog、菜单排序、快捷键冲突 |
| UI Contribution | Command 在菜单/快捷键/Palette 中的声明式投影 | 创建 Avalonia 对象、拥有插件生命周期 |

最小执行关系为：

```text
Menu / KeyBinding / Palette
            ↓
         CommandId
            ↓
 WorkbenchCommandExecutor
      ├─ Catalog / owner availability
      ├─ WorkbenchContextSnapshot
      ├─ target constraint
      └─ current instance state recheck
            ↓
 Host Handler 或 Active Document Target
```

### 3.2 Command 与 `ICommand` 的边界

平台不得把 Command 注册定义成：

```csharp
registration.AddCommand(new RelayCommand(...));
```

也不得把 `ICommand` 实例保存在不可变 Plugin Registry。`ICommand` 没有稳定身份、所有权、目标类型、
跨展示复用和组合冲突语义。

插件内部允许使用一个窄适配器把现有命令接到 Document Target。例如 ClassicGame Document 可以在
`CanExecute(CommandId)` 中委托 `ViewModel.UndoCommand.CanExecute(null)`，在执行方法中调用原有业务入口。
这只是插件内部实现复用，不会使 Workbench Command 的公共身份退化成 `ICommand`。

异步命令必须被真正 `await`。不得通过 `ICommand.Execute(null)` 启动 `async void` 或未观察任务，再让
Executor 误报已完成；WorkflowStudio 的 Run 必须通过可等待入口或 `IAsyncRelayCommand.ExecuteAsync` 适配。

### 3.3 Command 与 Workflow Action

| 维度 | Workbench Command | Workflow Action |
| --- | --- | --- |
| 核心语义 | 用户当前意图 | 可编排业务能力 |
| 典型入口 | Menu、KeyBinding、Palette | Plugin/Workflow Gateway |
| 目标 | Host 或当前 Document 实例 | Action 所有者 invocation scope |
| Context 依赖 | 强 | 通常弱 |
| 参数 | v1 无任意参数 | 受 Schema 验证的 JSON |
| 时间尺度 | 通常短；可启动业务会话 | 可长时间运行 |
| 授权/预算 | 轻量可用性与目标检查 | 已有完整治理 |
| 示例 | 保存、验证当前 Workflow、撤销当前棋局 | 下载、视频加密、格式化业务项 |

正确分层：

```text
用户选择“运行当前 Workflow”
        ↓
workflow-studio.command.run
        ↓
当前 MainDocument Target
        ↓
WorkflowRunSession
        ↓
IWorkflowActionGateway
        ↓
一个或多个受治理 Workflow Action
```

### 3.4 命令提升标准

一个现有局部命令只有满足至少一项时才进入 Workbench Catalog：

- 需要从 Document 外的菜单或快捷键调用；
- 需要被 Command Palette 发现；
- 同一语义需要多个工作台展示入口复用；
- 是用户对当前活动任务的高价值、可解释动作。

以下命令默认不提升：格子点击、棋盘落子、文本编辑、列表增删、拖放、局部弹层确认、动画手势、
仅对一个控件有意义的操作。插件 View 内原按钮也不必为了 Workbench Command 被强制改绑；在过渡期，
局部按钮和 Workbench Target 可以委托同一个业务用例或已有命令。

## 4. 目标架构

### 4.1 包与依赖方向

```text
MyAvaloniaManagement.PluginSdk
  ├─ CommandId
  ├─ IWorkbenchDocumentCommandTarget
  └─ Target state/change contracts
              ↑
MyAvaloniaManagement.PluginSdk.UI
  ├─ CommandDescriptor
  ├─ Menu/KeyBinding descriptors
  └─ optional registration extensions
              ↑
Host internal
  ├─ Commands/Catalog
  ├─ Commands/Context
  ├─ Commands/Execution
  ├─ Commands/Presentation
  └─ existing Plugin Registration / Workspace / Diagnostics
              ↑
Host Tests / UiTests / PluginTests
              ↑
real nupkg / template / external WorkflowStudio and ClassicGame
```

Core SDK 继续 BCL-only，不引用 Avalonia、Dock、Microsoft DI、CommunityToolkit 或 Host。UI SDK 可以保存
展示描述符，但不创建 Host 控件。插件不引用 Host implementation，Host 不引用外部插件具体类型。

### 4.2 稳定身份与所有权

新增 `CommandId` 继续复用当前稳定 ID 的小写点分/kebab-case 和最大 128 字符规则。

规范示例：

```text
myavalonia.host.command.document.open
myavalonia.host.command.document.save

myavalonia.plugin.workflow-studio.command.validate
myavalonia.plugin.workflow-studio.command.run
myavalonia.plugin.workflow-studio.command.cancel

myavalonia.plugin.classic.game.command.gomoku.restart
myavalonia.plugin.classic.game.command.gomoku.undo
```

插件 Command 必须位于：

```text
{PluginId}.command.{non-empty-suffix}
```

Host Command 位于 `myavalonia.host.command.*`，由独立 Host Catalog 拥有，不创建伪 PluginId。

UI Placement 使用独立稳定身份，建议命名空间为：

```text
{PluginId}.command-placement.{non-empty-suffix}
myavalonia.host.command-placement.{non-empty-suffix}
```

最终值对象名称可在 G1 public API 评审中确定，但不得用裸字符串同时表示 Command、Document、Tool 和
Placement。CommandId 碰撞属于语义贡献冲突；Key Gesture 碰撞属于展示资源冲突，两者不能采用同一失败政策。

### 4.3 Catalog 与注册事实

Host 内建 Command 与插件 Command 在查询层合并，但所有权仍分离：

```text
HostWorkbenchCommandCatalog ─┐
                             ├─ WorkbenchCommandCatalog
PluginRegistry Commands ─────┘
```

插件建议通过兼容扩展声明 Document Command：

```csharp
registration.AddDocumentCommand(
    new CommandDescriptor(
        commandId,
        displayName,
        description,
        iconPath),
    targetDocumentTypeId);
```

注册只冻结：

```text
OwnerId
CommandDescriptor
TargetDocumentTypeId
```

不保存 Handler 实例、模型、Scope、Provider、`ICommand` 或任意 callback。Seal 时必须验证目标 Document
由同一 PluginId 声明；插件不能为 Host Document 或其他插件 Document 注册命令。

Host 内建打开/保存使用 Host internal Handler/Invoker。Host Catalog 可以在组合根显式接收这些实现，
但不能把 Host 根 Provider 放进 CommandDescriptor 或 Catalog 供运行期任意定位。

### 4.4 Workbench Context v1

第一版快照固定为活动 Document 最小事实：

```text
HasActiveDocument
ActiveDocumentTypeId
ActiveDocumentOwnerId（Host 项可为空或使用显式 Host-owned 分支）
IsActiveDocumentPersistable
Revision
```

不进入 v1：

```text
ActiveDocument object
Control / FocusedControl
IDockable / RootDock
IServiceProvider / IServiceScope
Selection
ActiveToolTypeId
FocusedSurface
Dictionary<string, object>
插件自定义表达式事实
```

`Revision` 只表示 Context 快照代次，不是 Document 保存修订，也不写入磁盘。它用于丢弃迟到状态刷新，
不能被插件指定或解释。

活动 Document 事实必须来自 `WorkspaceSession` 拥有的 DocumentDock，并通过独立通知发布。Context Store
订阅这一个窄入口；不能遍历 RootDock、监听整个 VisualTree 或复用通用消息器。若 Dock 当前版本只提供
属性变化通知，Adapter 必须精确过滤 `ActiveDockable` 并在布局替换时成对重绑。

### 4.5 Document Target 与执行路由

Core SDK 候选协议表达以下语义：

```csharp
public interface IWorkbenchDocumentCommandTarget
{
    event EventHandler<WorkbenchCommandStateChangedEventArgs>?
        CommandStateChanged;

    bool CanExecute(CommandId commandId);

    ValueTask ExecuteAsync(
        CommandId commandId,
        CancellationToken cancellationToken);
}
```

G0 已冻结确切成员名和 EventArgs 形状：`CommandStateChanged` 每次只携带一个非空 `CommandId`。
多条命令因同一业务状态变化而失效时，Target 逐条发送事件；不接受集合、`null`、空值或“刷新全部”特殊值。
Host Presentation 可以在切换到 UI Dispatcher 时按 `CommandId` 去重，但不能把去重语义推回 public API。
此外必须满足：

- Target 不接收 Workbench Context、Provider、Control 或 Dock；
- 状态变化能指出受影响 CommandId，不要求 Host 每次刷新全部插件命令；
- 未声明/不属于当前目标的 CommandId 必须稳定拒绝；
- Target 事件可从工作线程发出，Host Presentation Adapter 负责切换 UI Dispatcher；
- Host 切换活动 Document 时退订旧 Target、订阅新 Target；关闭/退出路径幂等解除；
- Target 实例就是 `ManagedDocumentDockable.Model` 的可选能力，不另建第二个 Document 生命周期。

Executor 执行 Document Command 的固定顺序：

```text
Host 是否仍接受命令
        ↓
Catalog 是否存在 Command
        ↓
Owner 是否 available
        ↓
捕获当前 Context 与 ManagedDocumentDockable
        ↓
DocumentType 是否与注册 Target 匹配
        ↓
Model 是否实现 IWorkbenchDocumentCommandTarget
        ↓
重新调用 CanExecute(CommandId)
        ↓
链接 invocation + ClosingToken + Host shutdown token
        ↓
await ExecuteAsync
        ↓
映射结果、刷新状态、稳定诊断
```

Executor 不提供全局单飞、重试、超时或队列。WorkflowStudio 的 Run/Cancel、游戏 AI 搜索和其他业务
重入政策继续由目标实例负责。真正的长任务应由 Command 启动已有业务会话或 Workflow Action，不能把
Command Executor 扩张成第二个 Run Manager。

### 4.6 Enabled、Visible 与 Checked

v1 固定只实现：

- Command 语义状态：`CanExecute`；
- Menu Placement 政策：目标不可用时 `Hide` 或 `Disable`；
- KeyBinding：目标/状态不可用时不执行；
- Palette：G9 按同一 Placement/Context 政策筛选。

默认政策：

| 场景 | 展示 |
| --- | --- |
| `myavalonia.host.command.document.save` 无可保存活动 Document | 菜单保留，Disabled |
| WorkflowStudio Command 当前不是 Studio Document | Hidden |
| ClassicGame Gomoku Command 当前不是 Gomoku Document | Hidden |
| 当前是目标 Document，但 `CanUndo=false` | 菜单保留，Disabled |
| Target 状态变化 | 当前所有投影同步刷新 |

`Checked`、Radio Group、动态标题、动态图标和进度不进入 v1。因此主题 System/Light/Dark 不在首批迁移。
未来若有真实消费者，应新增可审阅的状态契约，不能用 `object State` 或字符串属性包绕过版本评审。

### 4.7 Menu Contribution

Menu v1 只包含一种插件可声明的不可变贡献：

```text
MenuCommandContribution
```

Host 继续拥有 File/View/Tools/Help 等保留位置，并只开放以下四个末端共享位置：

```text
myavalonia.host.menu.file.shared
myavalonia.host.menu.view.shared
myavalonia.host.menu.tools.shared
myavalonia.host.menu.help.shared
```

Host 内建项始终位于对应共享位置之前。共享位置只接收 Command Placement，不接收插件 Container、
嵌套菜单或 Host 项重排声明。插件只能：

- 向 Host 明确开放的共享位置贡献命令；

插件不能：

- 声明、接管或嵌套任何菜单 Container；
- 覆盖、删除或重排 Host 保留菜单；
- 贡献 `MenuItem`、DataTemplate、Control 或任意 ViewModel；
- 通过字符串路径如 `"文件/保存"` 绕过稳定位置 ID；
- 读取另一个插件的命令状态或模型。

排序固定为：

```text
LocationId → Group → Order → PlacementId
```

同序项最终按稳定 ID 排序，不依赖插件发现顺序、文件系统顺序或字典枚举顺序。Separator 由 Host 根据
非空 Group 投影，插件不贡献悬空 Separator 实例。

### 4.8 KeyBinding Contribution 与冲突政策

KeyBinding Descriptor 直接保存 UI SDK 已依赖的 `Avalonia.Input.Key` 与
`Avalonia.Input.KeyModifiers` 值，不复制第二套键枚举，也不解析字符串 Gesture；Descriptor 不保存已经绑定
Command 的 Avalonia `KeyBinding`、Control 或回调。Host 最终验证枚举值、保留项和冲突后创建 UI 对象。

冲突政策固定为：

1. Host 保留快捷键优先，`Ctrl+S` 等核心绑定不能被插件覆盖；
2. 同一插件内部重复 PlacementId、非法 Gesture 或重复声明属于候选注册错误，拒绝该插件候选；
3. 两个不同插件争用同一 Gesture 时，两个冲突绑定都不激活并产生稳定脱敏诊断；
4. Gesture 冲突不删除 Command，也不拒绝其菜单/Palette 投影；
5. 不使用“最后加载者胜出”、插件发现顺序或任意优先级数值抢占；
6. 用户自定义快捷键不进入 v1，不能把尚未设计的设置文件混入本轮。

### 4.9 错误、线程与关闭

- UI 触发统一进入可等待 Executor Adapter，未观察异常不能回到 Avalonia Dispatcher；
- Host 打开/保存继续复用 `DocumentOperationState` 的既有用户提示，不增加第二条错误条；
- 插件 Command 未处理异常固定映射为“插件命令执行失败；插件异常正文未写入诊断。”，并写稳定脱敏诊断，
  不展示异常正文、路径或 Payload；
- `CanExecute` 或事件访问器抛出异常时，该 Command 当前投影 Disabled，其他插件命令继续可用；
- Document 关闭触发 ClosingToken，Executor 等待协作取消；Host 不越过 Target 强制释放其资源；
- Host `BeginShutdown` 后拒绝新命令，已在途 Command 必须在 Runtime 释放插件 Provider 前完成或协作退出；
- shutdown wait 固定为独立的 10 秒协作退出宽限，不复制 Workflow Action 的六小时 long-running 策略；
  超时不得强杀同进程插件代码、伪装成功或继续不安全释放其 Provider，必须记录脱敏诊断；
- 状态查询默认在 UI 状态投影线程执行，插件实现必须短小无阻塞；耗时检查属于业务预检，不属于 `CanExecute`。

## 5. Public API 与 Host internal 落点

### 5.1 Core SDK 候选

建议在 `MyAvaloniaManagement.PluginSdk` 新增独立 `WorkbenchCommandContracts.cs`，包含：

- `CommandId`；
- `IWorkbenchDocumentCommandTarget`；
- `WorkbenchCommandStateChangedEventArgs`；
- 仅为状态/执行所需的 BCL-only 类型。

不得加入：

- Avalonia `ICommand`、KeyGesture、Control 或 Dispatcher；
- `IServiceProvider`、DI Scope、Host Context；
- 菜单位置、图标、快捷键等 UI Profile 类型；
- 任意 JSON 参数、用户授权或 Workflow Action 类型复制。

### 5.2 UI SDK 候选

建议在 `MyAvaloniaManagement.PluginSdk.UI` 新增：

- `CommandDescriptor`；
- `MenuCommandContributionDescriptor`；
- `KeyBindingContributionDescriptor`；
- 必要的稳定 Placement/Location 值对象和小型枚举；
- `IWorkbenchCommandRegistration` 可选能力；
- `WorkbenchCommandRegistrationExtensions`。

继续沿用 Workflow Action 的兼容模式：不向已经发布的 `IPluginRegistration` 直接追加抽象成员。

```csharp
public static void AddDocumentCommand(
    this IPluginRegistration registration,
    CommandDescriptor descriptor,
    DocumentTypeId targetDocumentTypeId)
```

扩展方法先要求 Host registration 实现可选接口；旧 Host 返回稳定 `NotSupportedException`，旧插件不调用
新扩展时完全不受影响。最终签名前必须建立旧 Host/新插件负例和新 Host/旧插件正例。

### 5.3 Host internal 目录

建议按变化原因落在：

```text
Business/Commands/
  Catalog/
    HostWorkbenchCommandCatalog.cs
    WorkbenchCommandCatalog.cs
  Context/
    WorkbenchContextSnapshot.cs
    WorkbenchContextStore.cs
  Execution/
    WorkbenchCommandExecutor.cs
    WorkbenchCommandExecutionResult.cs
    HostOpenDocumentCommandHandler.cs
    HostSaveDocumentCommandHandler.cs
  Presentation/
    WorkbenchCommandStateStore.cs
    WorkbenchMenuProjection.cs
    WorkbenchKeyBindingProjection.cs
    CommandPaletteQuery.cs
```

最终文件数可按实际职责合并，但不能重新放入 `Helpers/Common/Utils`，也不能把 Catalog、Context、Executor
和 Avalonia Projection 全塞入 `MainWindowViewModel`。

### 5.4 现有注册流水线扩展

| 当前类型 | Command 计划职责 |
| --- | --- |
| `PluginRegistration` | 实现可选注册接口，收集命令和 Placement，Seal 后不可写 |
| `PluginRegistryBuilder` | 所有权、局部重复、目标 Document、全局 CommandId/PlacementId 冲突 |
| `PluginRegistry` | 冻结 Command 和 UI Contribution；不保存运行实例 |
| `PluginServiceCommitGuard` | 禁止插件通过 Services 手工登记声明式贡献根；不注入通用 Host Command Service |
| `PluginProviderOwner` | 继续拥有 Provider；Document Command 不新增 invocation scope 工厂 |
| `PluginAvailabilityReadModel` | 复用 owner available 事实 |
| `WorkspaceSession` | 提供准确活动 Document 变化和当前 Adapter 捕获 |
| `ManagedDocumentDockable` | 继续拥有模型与 ClosingToken，不复制 Target 实例 |
| `ServiceCollectionExtensions` | 显式组合 Host Catalog、Context、Executor、Projection 和内建 Handler |

### 5.5 MainWindow 与 Presentation

G4 结束后目标关系为：

```text
MainWindow / MenuView
        ↓ binding
Workbench presentation models
        ↓
WorkbenchCommandExecutor
```

`MainWindowViewModel` 继续拥有布局、窗口关闭、主题和 Document 错误条绑定，但不再拥有
`OpenDocumentCommand`、`SaveDocumentCommand`。`IMainWindowViewBindings` 和设计数据同步删除对应成员。

Menu 容器可以保留 XAML 根外壳，但命令项必须来自 Host 投影；`Ctrl+S` 不能再直接绑定已删除的 ViewModel
命令。Headless UI 测试应验证最终 CommandId 和状态，而不是只断言 `KeyBindings.Count == 1`。

## 6. 版本、包与磁盘契约

### 6.1 SDK 版本计划

当前 Core/UI SDK 为 `3.2.0`，API baseline 为 `v3`。G0 已冻结 Command 兼容新增的候选版本：

| 包/产品 | 候选处理 |
| --- | --- |
| Core SDK | `3.3.0`，新增 BCL-only Command 身份/Target |
| UI SDK | `3.3.0`，新增描述符和可选注册扩展 |
| Workflow SDK | 保持 `1.0.0`，Command 不改变 Workflow 协议 |
| Plugin Build | 保持 `1.1.2`；只有后续独立包校验协议评审才允许升版 |
| Templates | 候选 `1.3.0`，精确引用 Core/UI `3.3.0` |
| Host 产品 | G0 保持 `3.0.0`，是否提升属于最终发布决策 |

使用 Command 的插件必须把 manifest 最低 SDK 提升到实际发布的 Command SDK 版本；不使用 Command 的旧插件
继续处于既有 `[3.0.0, 4.0.0)` 兼容线。G6 前所有新 API 保持 Unshipped 候选，不得上传或覆盖同版本包。

### 6.2 API 兼容矩阵

| 组合 | 预期 |
| --- | --- |
| 新 Host + 旧 3.0/3.1/3.2 插件 | 正常加载，命令贡献为空 |
| 新 Host + 新 Command 插件 | 注册、状态、执行和 UI 投影通过 |
| 旧 Host + 新插件但不调用 Command 扩展 | 由 manifest 最低版本政策决定，不伪装支持 |
| 旧 Host + 调用 Command 扩展的插件 | 还原/最低版本或稳定 `NotSupportedException` 明确失败 |
| 外部插件直接引用 Host 源码 | 门禁失败 |
| Core SDK 引入 Avalonia/Dock/DI | 包边界门禁失败 |

V3 Shipped 文本不得改写；3.3 新成员先进入 `ApiCompatibility/v3/PublicAPI.Unshipped.txt`，正式签署时再按
现有 API 政策处理，不能新建 v4 baseline 冒充兼容新增。

### 6.3 磁盘事实保持不变

| 事实 | 当前值 | Command 计划 |
| --- | --- | --- |
| manifest schema | 2 | 不修改字段；仅使用现有 SDK 区间表达最低版本 |
| Document envelope schema | 2 | 不修改 |
| layout schema | 2 | 不修改，不持久化 Command UI 状态 |
| layout 文件 | `layout-v2.json` | 不改名 |
| Host 数据根 | `v2` | 不新增 command-v1 数据目录 |
| 外观设置 | 既有路径/schema | 不修改 |
| 用户快捷键设置 | 不存在 | v1 不新增 |

CommandId、PlacementId 和 Menu Location 是运行时/注册稳定身份，不写入现有 Document 或 layout 文件。
未来用户自定义快捷键若需持久化，必须独立设计严格 schema、冲突和迁移政策。

## 7. 删除、迁移与保留清单

### 7.1 G4 计划删除

- `MainWindowViewModel.OpenDocument` 及生成的 `OpenDocumentCommand`；
- `MainWindowViewModel.SaveDocument` 及生成的 `SaveDocumentCommand`；
- `IMainWindowViewBindings` 对应两个成员；
- `MainWindowDesignData` 对应设计命令；
- `MenuView.axaml` 对这两个具体 ViewModel 命令的直接绑定；
- `MainWindow.axaml` 对 `SaveDocumentCommand` 的直接 KeyBinding；
- 完成消费者迁移后只验证旧绑定形状的测试断言。

删除必须在同一个 G 内完成消费者迁移，不能先保留两套入口。`DocumentPersistenceCoordinator`、
`DocumentOperationState` 和错误条继续保留并由 Host Handler 使用。

### 7.2 后续计划迁移

- Host File 菜单的打开/保存进入 Workbench Menu Projection；
- `Ctrl+S` 进入 Host KeyBinding Contribution；
- WorkflowStudio `MainDocument` 实现 Document Target，并声明三条命令及菜单/快捷键；
- ClassicGame 先迁一个游戏的 Restart/Undo，证明多实例状态；
- SDK/API/模板/快速开始和外部 lock file 在 G6–G8 同步更新。

### 7.3 明确保留

- `SetThemeCommand` 和主题 Radio 绑定，直到未来 checked/radio 状态契约建立；
- `DismissDocumentOperationErrorCommand`；
- `PlugGroupMenuViewModel.CreateDocumentEntryCommand` 和 Document Creation Intent 专用投影；
- `ToolManagementViewModel.ToggleToolVisibilityCommand`；
- 插件 View 内未提升的全部局部 `RelayCommand`；
- Workflow Action Runtime 与 Gateway；
- `DocumentScopeManager` 的 Provider 隐藏、Scope 释放和 ClosingToken 规则；
- Host/Plugin Catalog 分离、Plugin Registry 不可变和 Provider 两阶段提交。

### 7.4 明确不引入的兼容残留

- `LegacyWorkbenchCommand`、旧/新双注册或 CommandId 别名表；
- 把 `OpenDocumentCommand` 留在 MainWindow 只做 Executor 转发；
- 插件直接贡献 `MenuItem`/`KeyBinding` 的备用入口；
- 同时支持 Document Target 和从 Document Scope 任意解析 Handler 的双执行模型；
- 字符串 `when` 表达式、反射属性路径或 `Dictionary<string, object>` Context；
- `host.document.create::<id>`、斜杠 CommandId 或把参数拼入稳定 ID 的临时方案。

## 8. G0–G10 独立实施包

每个 G 必须在开始时新建 `docs/plan-history/workbench-command/gN-*.md`，记录目标、输入提交、源码变化、
删除面、SDK/插件影响、测试命令、实际结果、覆盖率、SOLID 取舍、AIFLOW=false、非发布声明和回滚边界。
本文不预建空记录，也不提前勾选最终签署项。

### G0：冻结基线、语义和 public API 决策

- **目标**：从干净可追溯提交冻结 Host/SDK/模板/四个仓内插件和 WorkflowStudio 的真实输入，签署本文 3–6 节语义。
- **生产变化**：无；只允许新增 G0 事实记录、经评审修正本文、G0 专项门禁和专项测试，不添加 Command 类型。
- **基线事实**：记录产品/SDK/Build/Templates 版本、Core/UI Shipped/Unshipped、Host 测试和覆盖率、
  四插件 ZIP/manifest、WorkflowStudio revision、当前菜单/快捷键和外部包引用；ClassicGame 明确记为未签署。
- **已决定**：Target 接口和单 `CommandId` 事件、四个 Host 共享菜单末端位置、Avalonia Key/Modifiers、
  固定脱敏失败文本、10 秒 shutdown wait、Core/UI `3.3.0` 与 Templates `1.3.0` 候选版本。
- **验证**：Host V4 G7 开发入口、SDK/API、四插件专项、WorkflowStudio 独立 locked restore/build/test/package、
  双 ZIP 确定性和真实 Host Loader/注册组合；不得调用发布入口或 Windows Smoke。
- **排除**：AIFLOW、ClassicGame 读取或门禁、Command 实现、Windows CI/Smoke、Release Acceptance、发布门禁、
  包上传、签名、tag、产品版本提升和外部插件业务修改。
- **回滚**：删除 G0 记录并回到输入提交；不得改写既有 V4/Workflow Action 历史数字。

### G1：建立兼容新增的 Command 契约与注册声明

- **状态**：已完成（2026-08-28）；实际证据见
  [G1 兼容契约与注册声明实施记录](../plan-history/workbench-command/g1-command-contracts-registration-declarations.md)。
- **目标**：加入最小 Core/UI public 候选和 Host 注册收集能力，但不创建 Menu、KeyBinding 或执行插件命令。
- **Core 变化**：`CommandId`、Document Target 和状态变更契约；保持 BCL-only。
- **UI 变化**：Command/Menu/KeyBinding Descriptor、可选注册接口和扩展方法；不修改 `IPluginRegistration` 签名。
- **Host 变化**：`PluginRegistration`、局部 `PluginRegistryBuilder` 收集声明；Seal 校验 owner、目标 Document、
  重复 ID、非法 Descriptor；`PluginRegistry` 可冻结但尚不投影执行。
- **所有权**：Command 必须属于 `{PluginId}.command.*`，Target Document 必须由同一插件声明；Placement 同理。
- **验证**：SDK contract/unit/API analyzer、旧插件模块、非法 ID、跨 owner target、重复、Seal 后写入和 defensive copy。
- **负例**：Core SDK 包图不出现 Avalonia/Dock/DI；Registry 不保存 Target/Handler/Provider/Scope/`ICommand`。
- **回滚**：整体回到 G0；删除全部 Unshipped 候选，不保留空接口或未消费值对象。

### G2：建立无 UI Command Catalog 与 Executor

- **状态**：已完成（2026-08-28）；实际证据见
  [G2 无 UI Catalog 与 Executor 实施记录](../plan-history/workbench-command/g2-command-catalog-executor.md)。
- **目标**：让 Host 内建 Command 能通过同一 Catalog/Executor 被直接测试执行，暂不改现有 XAML。
- **变更**：Host/Plugin 合并查询 Catalog、执行结果、shutdown rejection、owner availability、稳定诊断和
  `OpenDocumentCommandHandler`/`SaveDocumentCommandHandler`；注册 `myavalonia.host.command.document.open/save`。
- **执行边界**：Host Handler 直接依赖 `DocumentPersistenceCoordinator`、`DocumentOperationState`；Catalog
  不保存 root Provider，也不允许插件解析 Host Handler。
- **验证**：已知/未知 ID、重复 Host/Plugin ID、owner unavailable、Handler 异常、取消、shutdown、打开选择取消、
  保存成功/失败和 DocumentOperationState 结果复用。
- **负例**：现有菜单与 `Ctrl+S` 暂时仍走旧路径；本 G 不宣称用户界面已经 Command 化。
- **回滚**：整体回到 G1；不得留下未被任何生产入口消费的第二套 Host 行为实现。

### G3：建立 Context v1 与活动 Document Target 路由

- **状态**：已完成（2026-08-28）；实际证据见
  [G3 Context v1 与活动 Document Target 路由实施记录](../plan-history/workbench-command/g3-context-active-document-target-routing.md)。
- **目标**：准确捕获活动 Document，并让 Executor 可以路由、查询和执行当前实例 Target。
- **变更**：`WorkspaceSession` 活动 Document 变化通知、不可变 Context Snapshot/Store、Context Revision、
  当前 Adapter 捕获、Target 订阅切换、状态查询和 ClosingToken/shutdown 链接。
- **验证**：无 Document、Host Document、普通插件 Document、可持久化插件 Document、标签切换、同类型多实例、
  Target 缺失、类型不匹配、CanExecute false/true、状态事件、工作线程事件、关闭中执行和迟到事件。
- **资源要求**：反复切换/关闭后旧 Target 没有订阅；Document Scope、View 和 Adapter 按既有顺序释放。
- **负例**：Context 不出现 Control、Dock、Provider、Selection、ActiveTool 或对象字典；
  `DocumentScopeManager` 不增加通用 `GetService`。
- **回滚**：整体回到 G2；不得保留同时监听 LayoutChanged 和 ActiveDocumentChanged 的重复状态源。

### G4：迁移 Host 打开/保存，完成第一个真实闭环

- **状态**：已完成（2026-08-28）；实际证据见
  [G4 Host 打开/保存 Presentation 真实闭环实施记录](../plan-history/workbench-command/g4-host-open-save-presentation-loop.md)。
- **目标**：同一 `CommandId` 同时服务 File 菜单和 `Ctrl+S`，删除 MainWindow 的旧打开/保存命令。
- **变更**：建立 Host Presentation Command；Menu/KeyBinding 通过 Executor Adapter 调用；删除 7.1 列出的旧绑定面；
  保存状态由 Context/Executor 投影，无可保存 Document 时菜单 Disabled、快捷键无操作。
- **验证**：Headless UI 菜单、`Ctrl+S`、设计数据、打开取消、保存错误条、无活动 Document、不可保存 Document、
  可保存 Document、切换目标和执行前状态重查。
- **单一路径要求**：生产搜索只能找到一个打开用例和一个保存用例所有者；菜单与快捷键不得各保留一套 callback。
- **不迁移**：主题、Document Creation、Tool Toggle、错误条关闭、插件局部命令和 Palette。
- **回滚**：整体回到 G3；不得只恢复 ViewModel 转发而让菜单/快捷键继续混用两条路径。

### G5：建立声明式 Menu 与 KeyBinding Projection

- **状态**：已完成（2026-08-28）；实际证据见
  [G5 声明式 Menu 与 KeyBinding Projection 实施记录](../plan-history/workbench-command/g5-declarative-menu-keybinding-projection.md)。
- **目标**：从不可变 Command/Placement Catalog 确定性生成 Host 和插件菜单/快捷键对象。
- **变更**：Host 末端共享位置的 Command Projection、KeyBinding Projection、排序、Group Separator、状态绑定、
  owner availability 和 4.7/4.8 冲突政策；Host File 菜单也改由内建 Contribution 提供打开/保存。
- **验证**：Host 保留菜单、四个共享位置、插件 Container/嵌套菜单拒绝、同 owner/跨 owner 目标、顺序稳定、空组、冲突 ID、
  Host 快捷键优先、插件 Gesture 冲突双禁用、Command 仍可经菜单执行和插件不可用时移除投影。
- **UI 所有权**：插件停用/Host 退出时只有 Host 释放 MenuItem/KeyBinding；Descriptor 不持有控件或 DataContext。
- **负例**：生产 SDK/Registry 不出现 Avalonia `MenuItem`/`KeyBinding`；不解析字符串菜单路径或 when 表达式。
- **回滚**：整体回到 G4 的 Host-only Presentation；不得保留静态插件菜单与动态插件菜单双入口。

### G6：完成 SDK 3.3 候选包、模板和独立消费门禁

- **目标**：把 G1–G5 已压测的 public 契约形成可供外部插件独立消费的候选 Core/UI 包和模板。
- **版本**：按 G0 最终签署值提升 Core/UI；若仍采用本文建议则为 `3.3.0`，Templates 为 `1.3.0`，
  Workflow SDK/Build 默认不变；更新集中版本、Assembly/FileVersion、lock file 和 v3 Unshipped。
- **模板**：普通模板可包含一个不默认注册快捷键的最小 Document Command 示例，不能加入 WorkflowStudio/ClassicGame
  业务代码；生成项目最低 SDK 与精确包引用同步。
- **验证**：隔离临时 feed、locked restore、Release `warnaserror`、API package、模板安装/生成、带点号项目、
  Standalone、确定性 ZIP、manifest 最低版本和候选 Host 真实双 ALC 加载。
- **兼容**：用真实旧 3.0/3.1/3.2 插件包验证新 Host；用旧 Host/新扩展建立明确负例。
- **发布边界**：默认只生成候选，不上传、不覆盖同版本、不打 tag；外部发布需独立授权并记录不可变哈希。
- **回滚**：回到 G5 源码候选；删除本地候选 feed/模板安装，不能发布后覆盖同版本包。

### G7：迁移外部 WorkflowStudio 三条真实命令

- **目标**：在独立 WorkflowStudio 仓库提升 Validate/Run/Cancel，并保持 Runner/Workflow Action 分层不变。
- **变更**：`MainDocument` 实现 Document Target；声明三条 Command、指向 Host 末端共享位置的 Menu Placement
  和经 G0 确认的快捷键；内部继续复用现有编辑协调器、RunSession 和状态通知。
- **状态**：非 Studio Document 时 Hidden；Studio idle 时 Validate/Run 按现有状态启用、Cancel disabled；
  运行时 Validate/Run disabled、Cancel enabled；状态切换刷新全部投影。
- **验证**：Standalone 单元测试、Host Headless UI、真实 Workflow Action Fake/业务 Action、Run/Cancel、关闭取消、
  两个 Studio Document 独立状态、菜单/快捷键、独立 restore/build/test/package 和双 ZIP 确定性。
- **边界**：Studio 只引用真实候选/正式 NuGet，不使用 Host/SDK `ProjectReference`；Host 仓库不加入 Studio 项目。
- **回滚**：移除 Command Target/声明，保留原 Document View 内命令和 Workflow Action 功能；不得删除局部按钮作为回滚代价。

### G8：迁移外部 ClassicGame 多实例命令

- **目标**：用一个真实游戏证明 Restart/Undo 路由到当前实例，而不是插件单例或仅按 DocumentType 路由。
- **前置冻结**：G0 未读取或签署 ClassicGame。G8 修改前必须从干净提交独立记录 revision/tree、版本、
  精确 SDK 引用、Document 数量、测试、包、真实 Host 加载和实际游戏状态；任一事实不清楚时不得修改源码。
- **首选样本**：Gomoku；若 G8 基线证明其异步 AI 状态使首样本不稳定，可改用同样具备动态 Undo 的一个游戏，
  但必须在 G8 记录理由，不能选择永远 enabled 的假样本规避状态验证。
- **变更**：目标 Document 窄适配 Target；声明两条 Command 和指向 Host 末端共享位置的 Placement；
  View 内原按钮可继续使用现有命令/业务用例。
- **关键验收**：同时打开两个同类型 Document，使 A 可撤销、B 不可撤销；切换标签后 Menu/Palette/KeyBinding
  状态立即对应当前实例；Restart 只影响当前棋局；关闭 A 不影响 B，也不留下订阅。
- **回归**：十一个游戏全部 build/test/package，未提升的局部命令行为不变；真实 Host 加载和退出资源归零。
- **回滚**：只移除首样本 Command 适配和声明，十一个游戏原 UI 继续工作；不得把 Command 接到插件全局静态状态。

### G9：实现最小 Command Palette

- **目标**：把同一 Catalog/Context/State 投影为可搜索、可键盘执行的最小 Palette，不增加第二套执行逻辑。
- **功能**：按 canonical DisplayName/Description 搜索、确定性排序、显示快捷键、区分 Disabled、执行当前结果、
  Escape 关闭和打开时聚焦搜索框；Palette 自身使用 Host 保留快捷键。
- **过滤**：遵守 Menu 的目标隐藏政策；不可用 owner 和目标不匹配项不显示；当前目标匹配但 CanExecute=false
  可显示为 Disabled，不调用插件生成动态名称。
- **验证**：空查询、中文/英文、重复展示名、稳定 ID tie-break、状态实时变化、目标切换、执行异常、快速重复打开、
  焦点恢复、Host 保留快捷键冲突和 Headless UI 键盘路径。
- **非目标**：模糊搜索框架、最近使用持久化、用户排序、设置 UI、Toolbar、ContextMenu 和命令参数。
- **回滚**：整体删除 Palette View/Projection/入口，G8 的 Menu/KeyBinding/Executor 仍完整可用。

### G10：跨仓库集成回归、文档同步与封板

- **目标**：把 Host、SDK、模板、四仓内插件、WorkflowStudio、ClassicGame、UI 和制品签署为同一 Command v1 基线。
- **前置**：G0–G9 各有独立记录；工作树和两个外部仓库 revision 可追溯；无未解释 API/包/测试漂移。
- **生产变化**：原则上无；只允许修复 G1–G9 职责内真实回归，不新增 Toolbar、ContextMenu、参数或设置。
- **验证**：锁定还原、Release `warnaserror`、Core/UI/Workflow API、Host Unit/UI/Plugin、四插件、两个外部插件、
  模板生成、真实包、双 ALC、确定性 ZIP/manifest、Context 切换、Command 资源订阅、Windows Smoke、诊断和文档门禁。
- **两轮隔离**：从两个无硬链接独立副本执行，忽略时间/绝对路径后比较稳定事实；外部插件必须从包还原。
- **覆盖率**：不得降低现有 Host/插件门槛；Command Catalog/Context/Executor/Projection 和两个真实 Target 的
  关键分支覆盖写入 G10 记录，本文不预填数字。
- **版本/发布**：签署最终 SDK/模板/插件版本和哈希；默认只建立本地发布资格，不上传、不打 tag、不对外发布。
- **回滚**：回到最后一个绿色 G；若 public 包已经显式发布，只能发新修订版本，不能覆盖同版本或改写 Shipped 历史。

## 9. 执行顺序与合并纪律

```text
G0 → G1 → G2 → G3 → G4 → G5 → G6 → G7 → G8 → G9 → G10
```

- G0 只冻结 Host、仓内四插件和 WorkflowStudio 事实与决策；ClassicGame 在 G8 自行冻结，不构成进入 G1 的前提；
- G1 public API 保持候选 Unshipped，必须经过 G2/G3 Runtime 压测后才允许 G6 打包；
- G2 先建立无 UI 内核，G3 再加入活动实例路由，避免通过 UI 偶然行为验证 Executor；
- G4 是首个用户可见闭环，必须删除旧打开/保存双路径；
- G5 才开放插件 UI Contribution，不能在 G1 Descriptor 出现时就直接创建 MenuItem；
- G6 负责独立包/模板传播，外部插件不得用源码引用提前获得假绿色；
- G7/G8 分别验证异步运行状态和同类型多实例状态，不得合成一个大跨仓库提交；
- G9 Palette 只能消费已有 Catalog/State/Executor，不能自建命令列表或直接调用插件；
- G10 只封板已完成事实，不在发布门禁阶段设计新 API；
- 每个 G 从前一绿色提交开始，专项与受影响外部插件绿色后才能进入下一 G；
- 不使用 `--no-restore` 掩盖锁文件错误；开发阶段不调用 Windows CI/Smoke 或发布门禁，G10 发布封板范围另行执行；
- 所有阶段记录显式写 `aiflow=false`，AIFLOW 文件、命令和状态不参与本计划。

## 10. 最终验收矩阵

### 10.1 身份、SDK 与注册

- [x] `CommandId` 遵守稳定 ID 规则，Host/插件所有权和碰撞政策有正负例；
- [x] Core SDK 保持 BCL-only，UI 描述符不创建 Host 控件；
- [ ] v3 Shipped 未改写，新 API 按现有政策进入并最终签署；
- [x] `IPluginRegistration` 原签名不变，可选扩展在旧 Host 上明确失败；
- [x] 旧插件在新 Host 上正常加载且命令贡献为空；
- [x] 插件不能为 Host/其他插件 Document 注册命令或 Placement；
- [x] Registry 只保存不可变事实，不持有 Target、Provider、Scope、Control 或 `ICommand`。

### 10.2 Context、状态与执行

- [x] Host/Plugin Command 已通过同一无 UI Catalog 查询，且 Catalog 不保存 Provider、Scope 或插件 Target；
- [x] Executor 已覆盖未知 ID、owner unavailable、取消、异常、shutdown rejection 和 10 秒排空；
- [x] Command Runtime 没有复制 Workflow Action 的 Schema、授权、长超时和 invocation scope；
- [x] 活动 Document 切换有独立准确通知，不依赖布局偶然变化；
- [x] Context v1 不泄漏模型、Control、Dock、Provider、Selection 或对象字典；
- [x] 同类型多个 Document 的 CanExecute 与执行目标严格按当前实例变化；
- [x] Executor 执行前重查 Catalog、owner、target 和 CanExecute；
- [x] Target 缺失、抛错、关闭取消、shutdown 和迟到事件均被隔离；
- [x] 旧 Target 订阅在切换/关闭/退出后释放；

### 10.3 Host、菜单与快捷键

- [x] 打开/保存只有一条生产执行路径，MainWindow 旧转发命令已删除；
- [x] File 菜单和 `Ctrl+S` 使用同一 CommandId/Executor；
- [x] Save 在无可保存活动 Document 时 Disabled，执行前仍会重查；
- [x] Host 末端共享位置中的 Command Placement 按 Group、Order 和稳定 ID 确定性投影；
- [x] 插件不贡献 Avalonia `MenuItem`/`KeyBinding` 实例；
- [x] Host 快捷键优先，插件冲突双禁用且不删除 Command；
- [x] 插件不可用、Target 切换和状态事件会同步刷新全部投影。

### 10.4 WorkflowStudio 与 ClassicGame

- [ ] Workflow Validate/Run/Cancel 的 idle/running 状态与现有业务一致；
- [ ] Run 继续进入 WorkflowRunSession/WorkflowAction Gateway，没有绕过治理；
- [ ] 两个 Studio Document 的运行/取消状态彼此独立；
- [ ] ClassicGame 首样本的 Restart/Undo 只作用当前游戏实例；
- [ ] 两个同类型游戏 Document 可形成不同 CanUndo 并在切换后立即更新；
- [ ] 未提升的十一个游戏局部命令和 WorkflowStudio 编辑命令保持原 UI 行为；
- [ ] 两个外部插件只从真实包还原，不引用 Host/SDK 源项目。

### 10.5 Palette、资源、包与发布

- [ ] Palette 只消费 Catalog/Context/State/Executor，不建立第二套命令列表；
- [ ] 搜索、排序、Disabled、目标切换、键盘执行和焦点恢复有 Headless UI 证据；
- [ ] Document/Target/Menu/KeyBinding/Palette 订阅在关闭和 Runtime 退出后归零；
- [ ] Host、SDK、四插件和两个外部插件全部测试与覆盖率门槛通过；
- [ ] SDK/模板候选、lock file、独立生成项目、真实双 ALC 和确定性 ZIP/manifest 通过；
- [ ] manifest/envelope/layout/data root 未变化；
- [ ] 诊断和 UI 不泄漏异常正文、路径、URL、Payload、Secret 或插件对象；
- [ ] 两轮隔离结果稳定，最终摘要区分 publishable 与实际上传/tag/发布状态；
- [ ] 全过程 `aiflow=false`。

## 11. 明确延后

- Toolbar、ContextMenu、StatusBar、Settings Page 和任意插件 Control Contribution；
- Selection、FocusedSurface、ActiveTool、编辑器光标和插件自定义 Context Facts；
- 字符串 `when` 表达式、And/Or/Not 规则树或通用表达式解释器；
- Checked/Radio、动态标题、动态图标、进度和多状态命令；
- 带任意 Payload 的 Command；Document Creation、Tool Toggle 等参数化命令需独立类型化 Invocation 设计；
- 用户自定义快捷键、严格设置文件 schema、迁移和同步；
- 插件跨 owner 菜单协作、命令覆盖/别名、优先级抢占和用户重排；
- Undo/Redo 通用编辑协议、全局剪贴板命令和文本编辑器路由；
- 运行期热加载/热卸载、可回收 ALC、插件市场和在线更新；
- 进程外插件、恶意代码沙箱、操作系统级权限隔离；
- 把 Command 暴露为 Workflow Action 或让 Workflow 自动调用任意 UI Command；
- AI 命令推荐、自然语言 Palette 或遥测驱动排序；
- 为了 Command 合并 Core/UI SDK、替换 Avalonia/Dock/Microsoft DI 或重写现有 Registry。

## 12. 最终签署清单

Workbench Command v1 只有在以下问题全部回答“是”后才算完成：

1. [x] G0 从干净、可追溯的 Host 与 WorkflowStudio revision 开始；ClassicGame 明确未签署并留到 G8。
2. [x] Command、Context、Target、UI Contribution 与 Workflow Action 的语义边界已经冻结。
3. [x] Core/UI SDK 新 API 是兼容新增，旧 Shipped 未改写，旧插件继续正常加载。
4. [x] Command/Placement 身份、owner、目标 Document 和冲突政策有完整正负例。
5. [ ] Context v1 只包含 Host 可信活动 Document 事实，没有对象世界或服务定位器泄漏。
6. [ ] 活动 Document 切换、同类型多实例和 Target 状态变化都路由到正确当前实例。
7. [ ] Document Scope 隐藏、ClosingToken、Adapter/View/模型释放顺序保持不变。
8. [x] MainWindow 打开/保存旧命令已删除，菜单与 `Ctrl+S` 统一进入 Executor。
9. [x] Host 与插件菜单/快捷键均由不可变 Contribution 投影，插件不拥有 Avalonia UI 对象。
10. [x] Host 快捷键和插件冲突采用明确失败关闭政策，不依赖加载顺序。
11. [ ] WorkflowStudio Validate/Run/Cancel 保持 Workflow Action 治理和多实例隔离。
12. [ ] ClassicGame Restart/Undo 证明当前实例路由和动态 CanUndo，没有全局静态状态。
13. [ ] Command Palette 复用同一 Catalog/State/Executor，移除 Palette 不影响基础 Command 系统。
14. [ ] 主题、Document Creation、Tool Toggle、Toolbar、ContextMenu 和用户快捷键设置没有被偷偷提前实现。
15. [ ] SDK/模板/外部包、locked restore、双 ALC、确定性制品和版本区间全部一致。
16. [ ] Host、SDK、四插件、两个外部插件、资源、Windows Smoke、诊断和文档门禁全部通过。
17. [ ] manifest/envelope/layout/data root 保持既有格式，没有 Command 专用用户数据迁移。
18. [ ] 每个 G 都有实际记录、测试、覆盖率、回滚和发布边界，不以本文计划数字冒充结果。
19. [ ] 两轮隔离封板可重复，未通过降低门槛、跳过真实包或保留隐藏双路径获得绿色。
20. [ ] 最终记录明确 `aiflow=false`，并区分本地可发布资格与实际上传、tag、对外发布。

任一项未完成时，本计划只能保持候选或开发状态。不得通过把插件 `ICommand` 直接塞入 Registry、
公开 Workspace/Provider、保留新旧双执行路径、复制 Workflow Action Runtime、降低测试门槛或改写历史
API/验收记录来宣称 Command v1 已封板。
