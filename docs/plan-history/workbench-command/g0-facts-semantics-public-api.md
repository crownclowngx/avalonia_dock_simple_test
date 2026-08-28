# Workbench Command G0：基线、语义与 public API 决策冻结记录

> 状态：已完成（2026-08-28）。
>
> Host 输入提交：`b8def254b1ca76e481014b4075b0a60d155ec132`
>
> Host 输入 Git tree：`cc653631805ed0d09aa477d15c0fc5eeaaaae877`
>
> WorkflowStudio 输入提交：`0b3a3f55f43e66a914099f011dd344e7f556b56e`
>
> WorkflowStudio 输入 Git tree：`ad082df6a0445c84216ab9b76e785bdae0e644f3`
>
> 所属任务：[Workbench Command 引入评审与实施任务书](../../design/workbench-command-introduction-plan.md#g0冻结基线语义和-public-api-决策)

## 1. 生产边界与输入事实

G0 只冻结事实与后续实现决策，不增加 `CommandId`、Document Target、Descriptor、Catalog、Executor、
菜单投影或快捷键投影等生产类型。Host 和 WorkflowStudio 的生产源码保持输入提交状态；仓内仅新增或修改
总任务书、G0 记录、专项门禁、专项包验收测试和文档门禁导航。

当前版本事实如下：

| 对象 | G0 输入值 | 后续 Command 候选 |
| --- | --- | --- |
| Host 产品 | `3.0.0` | G0 不提升 |
| Core/UI Plugin SDK | `3.2.0` | `3.3.0` |
| Workflow SDK | `1.0.0` | 保持 |
| Plugin Build | `1.1.2` | 保持 |
| Templates | `1.2.0` | `1.3.0` |
| API baseline | `v3` | 继续使用 v3 Unshipped |

磁盘与兼容边界保持 manifest schema 2、Document envelope schema 2、layout schema 2、
`layout-v2.json` 和数据根 `v2`。G0 不修改任何版本文件、API 文本、锁文件、manifest 或用户数据。

## 2. API 基线

| API 文件 | 条目 | SHA-256 |
| --- | ---: | --- |
| Core Shipped | 127 | `063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F` |
| Core Unshipped | 72 | `3CAA366630A123B60C10E7E014FD39F711CF22BAC54B7554526CD73714B295C7` |
| UI Shipped | 45 | `B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803` |
| UI Unshipped | 6 | `D1BAC6F52B49E18E9814B98198372FE71362E3C5C9D2220B1933E3B0EF99E65F` |

这些文本是 G1 的保护线。Command public API 只能作为兼容新增进入 v3 Unshipped；不得改写 Shipped、
新建 v4 baseline 掩盖破坏，或在 G0 提前登记尚不存在的签名。

## 3. 已冻结的 public API 与运行语义

### 3.1 Document Target

G1 必须实现以下窄接口形状，不再重新讨论成员名称或事件批量语义：

```csharp
public interface IWorkbenchDocumentCommandTarget
{
    event EventHandler<WorkbenchCommandStateChangedEventArgs>? CommandStateChanged;

    bool CanExecute(CommandId commandId);

    ValueTask ExecuteAsync(
        CommandId commandId,
        CancellationToken cancellationToken);
}
```

`WorkbenchCommandStateChangedEventArgs` 每次只携带一个非空 `CommandId`。同一业务变化影响多条命令时逐条
发送；集合、空值、`null=全部` 和全量刷新标记均不进入 public API。Target 不接收 Context、Provider、
Control 或 Dock；Host 可在 UI Dispatcher 边界按 CommandId 去重重复刷新。

### 3.2 Menu 与 Gesture

Host 拥有 File/View/Tools/Help 顶级容器，并开放四个末端共享位置：

```text
myavalonia.host.menu.file.shared
myavalonia.host.menu.view.shared
myavalonia.host.menu.tools.shared
myavalonia.host.menu.help.shared
```

Host 内建项始终在共享位置之前。插件只能向共享位置放置自己的 Command，不能声明嵌套 Container、
删除或重排 Host 项，也不能写入其他插件的私有 Container。

KeyBinding Descriptor 使用 UI SDK 既有依赖中的 `Avalonia.Input.Key` 和 `KeyModifiers` 两个枚举值。
不增加自有键枚举，不解析 `Control+S` 字符串，也不保存 Avalonia `KeyBinding`、Control 或 callback。

### 3.3 失败与关闭

插件 Command 未处理异常的固定用户文本为：

```text
插件命令执行失败；插件异常正文未写入诊断。
```

Host 开始关闭后拒绝新命令，并给予在途命令独立的 10 秒协作退出宽限。超时后不得强杀同进程插件
代码、伪装完成或继续不安全释放对应 Provider；Host 必须记录不包含异常正文、路径或 Payload 的稳定诊断。

## 4. 当前 UI、仓内插件与 WorkflowStudio

当前 File 菜单的打开、保存仍直接绑定 `MainWindowViewModel`；`MainWindow.axaml` 的 `Ctrl+S` 仍直接绑定
`SaveDocumentCommand`。G0 只记录该事实，不迁移 XAML 或 ViewModel。

仓内 MyPlugTest、DaTangAccountingHelpPlug、MySmallTools、BiliDownloader 仍使用 SDK `[3.2.0, 4.0.0)`。
Host V4 G7 开发门禁的实际结果如下；每行均为失败 0、跳过 0，并完成两次确定性打包：

| 插件 | 通过数 | ZIP 文件数 | ZIP SHA-256 |
| --- | ---: | ---: | --- |
| MyPlugTest `3.0.0` | 580 | 11 | `F74BB2C66A7EC6FAD5842CEACB74E97A7D6042F8664B57C90C62F54799A21526` |
| DaTangAccountingHelpPlug `3.0.0` | 631 | 9 | `FDFC3052D804E93FA9525C75D3004865A6676C571EE0F2596EA486CF9C6EAA2C` |
| MySmallTools `3.1.0` | 771 | 431 | `627C8D3440539D6CEBD2FBECDD78B4D622D15B7B5FCA5665FD2510EE5E35F516` |
| BiliDownloader `3.0.0` | 1299 | 14 | `D0093EE5C008A05E0121F1C3A16C4DE0CB50B4A4F942624B9E9A047B0313B2D1` |

MyPlugTest 既有专项门禁原先仍断言 SDK 下界 `3.0.0`，与当前 manifest、Host V4 总门禁和本记录冻结的
`3.2.0` 事实冲突；G0 将该测试门禁期望修正为 `3.2.0`，未修改插件、SDK 或 Host 生产代码。

WorkflowStudio 的独立输入版本为 `1.1.0`，精确引用 Core/UI `3.2.0`、Workflow SDK `1.0.0` 和
Plugin Build `1.1.2`。locked restore、Release 零警告构建和全部 49 项单测均通过，失败 0、跳过 0；
两次本地 Managed Plugin ZIP 均为 4 个文件，SHA-256 均为
`1668CA6AAA9FA327726F13241A871BA68C6207C8A7F607EF623AA368468EC3B7`。真实 Host Loader/独立 ALC/
Provider/Registry 组合验收 1 项通过，失败 0、跳过 0；外部仓库在门禁前后均保持干净。

ClassicGame 明确为 **G0 未签署**。本阶段不读取、还原、构建、测试、打包或加载 ClassicGame，也不记录其
测试通过数、包哈希或兼容结论。G8 修改前必须自行冻结干净 revision/tree、版本、SDK 引用、Document 数量、
测试、包、真实 Host 加载和实际游戏状态。

## 5. SOLID、朴素模式与中文注释

- **SRP**：G0 只记录输入和决策；后续 Catalog、Context、Executor、Presentation 按变化原因分离。
- **OCP/LSP**：Command 通过可选注册能力扩展 v3；旧插件不声明 Command 时仍可正常加载。
- **ISP**：Document Target 只暴露定向状态、状态通知和可等待执行，不公开对象世界或服务解析。
- **DIP**：Host 依赖 SDK Target 契约，插件内部可用 Adapter 复用业务用例或 `RelayCommand`。

设计模式只使用值对象、Descriptor、Adapter、Catalog/Registry 等已有真实职责的普通模式。不引入
MediatR、CQRS、事件溯源、服务定位器、反射自动发现或无第二生产实现的接口。新增 public 成员必须具有
详细中文 XML 注释；线程、取消、所有权、兼容和释放等非显然代码必须用中文设计注释解释原因。

## 6. 专项验证

统一入口为：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG0.ps1 `
  -WorkflowStudioRoot <myavalonia-workflow-studio绝对路径>
```

该入口复用 Host V4 G7 开发门禁，执行 Host 三层测试与覆盖率、SDK/API、仓内四插件、包边界、诊断脱敏、
文档门禁，并独立验证 WorkflowStudio。脚本只接受 WorkflowStudio 外部仓库路径，内部固定使用本地
`Release` 编译配置。Host 基础三层测试共通过 504 项，失败 0、跳过 0；行覆盖率 `85.45%`、分支覆盖率
`71.14%`，分别高于冻结下限 `84.39%` 与 `70.58%`。实测摘要写入 Git 忽略的
`artifacts/test-results/WorkbenchCommandG0/summary.json`。

## 7. 非发布声明与回滚

Release 仅表示本地编译配置。本阶段不使用 AIFLOW，不运行 ClassicGame、Windows CI/Smoke、
Release Acceptance、Host Release Gate、签名、上传、tag 或外部发布。

```text
classicGameVerified=false
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

G0 回滚只删除本记录、总任务书修订、专项门禁、专项包测试和文档导航，不改写 Host V4、Workflow Action
或 ClassicGame 的既有历史，也不保留临时 ZIP 作为发布制品。
