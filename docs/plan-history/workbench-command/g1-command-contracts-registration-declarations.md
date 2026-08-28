# Workbench Command G1：兼容契约与注册声明实施记录

> 状态：已完成（2026-08-28；完整非发布门禁通过）。
>
> 输入提交：`9aa5c892f6c2114cd8a9406fd66ed1da2fbe7595`
>
> 输入 Git tree：`9dcb3d90bac913c946c4fa3e4da572c575601a11`
>
> 前置：[G0 基线、语义与 public API 决策](./g0-facts-semantics-public-api.md)
>
> 总设计：[Workbench Command 引入评审与实施任务书](../../design/workbench-command-introduction-plan.md#g1建立兼容新增的-command-契约与注册声明)

## 1. 实施边界

G1 只增加 Core/UI public 候选与 Host 注册冻结能力。Registry 能保存 Command、菜单位置和快捷键的
不可变声明，但当前仍没有 Catalog、Executor、Context、活动 Document 路由、Avalonia 菜单/快捷键投影或
Command Palette。插件不能通过 G1 执行命令，也不能贡献 `MenuItem`、`KeyBinding`、Control 或回调。

SDK 源码版本继续为 `3.2.0`，Host 产品版本继续为 `3.0.0`。新增 API 只进入 v3 Unshipped；3.3.0
候选包、模板和外部仓库传播留到 G6。manifest schema 2、Document envelope schema 2、layout schema 2、
`layout-v2.json` 和数据根 `v2` 均保持不变。

## 2. 契约与设计思路

Core SDK 新增 `CommandId`、`WorkbenchCommandStateChangedEventArgs` 和
`IWorkbenchDocumentCommandTarget`。Target 每次只通知一个 CommandId，状态查询保持短小，异步执行必须真实
可等待并观察取消；接口不接收 Context、Provider、Control、Dock 或任意参数对象。

UI SDK 新增独立 Placement/Location 值对象、四个 Host 共享菜单末端位置、Command/Menu/KeyBinding
Descriptor，以及 `IWorkbenchCommandRegistration` 可选能力和兼容扩展。`IPluginRegistration` 已发布的
四个方法没有改变；旧 Host 只在新插件实际调用扩展时返回固定 `NotSupportedException`。

Host 延续现有一次写入与两阶段提交：

```text
PluginRegistration
    → 插件局部 PluginRegistryBuilder
    → Seal：owner、Target、引用、重复和 Location 校验
    → Import
    → 全局 Build：CommandId / PlacementId 冲突 owner 整体隔离
    → 不可变 PluginRegistry
```

跨插件相同 Gesture 在 G1 保留两份事实，不提前执行 G5 的“双禁用”展示政策。Registry 只保存 Owner、
Descriptor 和 TargetDocumentTypeId，不保存 Target、Handler、模型实例、Provider、Scope、Control、Dock、
`ICommand` 或 callback。

## 3. 实际源码变化与兼容结论

- Core SDK 新增 `WorkbenchCommandContracts.cs`，公开三项 BCL-only 候选契约；没有引入 Avalonia、Dock、DI、
  `ICommand`、Provider、Context 或 JSON 参数。
- UI SDK 新增 `WorkbenchCommandDescriptors.cs` 和 `WorkbenchCommandRegistrationContracts.cs`，只保存稳定身份、
  展示元数据和 Avalonia Gesture 值；没有保存控件、绑定、回调或运行期目标。
- Host 的 `PluginRegistration` 以可选能力收集声明，局部 Builder 在 Seal 时集中校验，随后由全局 Builder 按
  Commit/Reject 模型隔离跨候选 Command/Placement 冲突；相同 Gesture 的跨插件事实均被保留。
- `PluginRegistry` 新增三组只读索引并在输入和读取两端防御性复制；Registry 数据记录只有 Owner、Descriptor
  和 TargetDocumentTypeId，不改变 `PluginServiceCommitGuard`，也不创建 Command invocation scope。
- `IPluginRegistration` 的原有四个成员保持原样。旧插件在新 Host 上正常注册且三组 Command 集合为空；旧 Host
  未实现可选接口时，只在新插件实际调用扩展后抛出固定兼容异常。
- 四个仓内插件、WorkflowStudio、ClassicGame、模板、包引用和 lock file 均未修改。

集中诊断代码覆盖 Command/Placement owner、目标 Document owner/未登记、未知 Command、非法 Location、本插件
重复 Command/Placement/Gesture。局部注册顺序不影响 Seal 结果；全局任一已有 Document、Tool、Workflow Action、
Command 或 Placement 冲突拒绝 owner 后，该 owner 的所有冻结事实一并移除。

## 4. SOLID 与朴素模式

| 原则 | G1 做法 |
| --- | --- |
| SRP | 值对象负责词法，Descriptor 负责轻量输入，Builder 负责注册关系，Registry 负责冻结快照 |
| OCP | 新增可选注册接口，不修改已发布 `IPluginRegistration` |
| LSP | 旧插件零 Command 声明继续加载，新增 Registry 集合为空 |
| ISP | Document Target 只有定向状态、单命令事件和可等待执行 |
| DIP | Host 后续只依赖 SDK Target，不取得插件 Provider 或具体模型实现 |

实现只使用值对象、Descriptor、可选能力和不可变 Registry。没有引入服务定位器、MediatR、CQRS、事件溯源、
反射发现、通用规则引擎或没有第二生产实现的抽象层。public 成员均提供中文 XML 注释；线程、取消、所有权、
兼容与防御性复制的非显然原因写入中文设计注释。

## 5. 测试、覆盖率与 API 证据

已经执行的定向测试：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginSdk.Tests/MyAvaloniaManagement.PluginSdk.Tests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~WorkbenchCommandContractTests

dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~WorkbenchCommandRegistrationTests
```

结果分别为 17/17 与 11/11，通过，失败 0、跳过 0。API baseline policy 4/4、SDK boundary 8/8 也已通过。
完整非发布门禁入口为：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG1.ps1 -Configuration Release
```

2026-08-28 已实际执行该入口，结果如下；全部失败 0、跳过 0：

| 门禁 | 真实结果 |
| --- | ---: |
| Host Unit / Headless UI / Plugin | 238 / 65 / 212，合计 515 |
| MyPlugTest V3 | 608 |
| DaTangAccountingHelpPlug V3 | 659 |
| MySmallTools V3 | 799 |
| BiliDownloader V3 | 1327 |
| 四插件回归聚合 | 3393 |
| Host 覆盖率 | 行 85.88%，分支 71.23% |
| 文档门禁 | 文档 95、本地链接 524、脚本路径 187、项目路径 47 |

Host 覆盖率没有低于 G0 的行 85.45%、分支 71.14%。真实 TRX、Cobertura 和机器可读摘要位于忽略目录
`artifacts/test-results/WorkbenchCommandG1/` 及其调用的四插件专项结果目录；G1 摘要为
`artifacts/test-results/WorkbenchCommandG1/summary.json`。最终入口在 G1 专项目录集中归档了 27 份真实 TRX
和 46 份原始/聚合覆盖率 XML，并在摘要中分别记录 `evidenceTrxFiles=27`、`evidenceCoverageFiles=46`。

API 事实：

| API 文件 | 条目数 | SHA-256 |
| --- | ---: | --- |
| Core v3 Shipped | 127 | `063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F` |
| Core v3 Unshipped | 91 | `D80D43C3F4EE6A2214A0DD3B5682402CC6FC6B62FD321E48A2608A4370DDD7AA` |
| UI v3 Shipped | 45 | `B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803` |
| UI v3 Unshipped | 66 | `C8B831D64C25615291FBFB99740EC633F07EA53B20326FA8CCD222EE6B564932` |

两份 Shipped 的条目数和哈希与 G0 完全一致。新增签名只进入 v3 Unshipped；没有 v4 baseline、删除标记或
3.3.0 版本提升。Release 零警告构建、locked restore、SDK/API、Host 三层、四插件、覆盖率和文档门禁均通过。

## 6. 非发布边界与回滚

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

G1 不使用 AIFLOW，不调用 Windows CI/Smoke、Release Acceptance、Host Release Gate、签名、上传、tag 或
发布命令。回滚单位是本阶段 Core/UI Unshipped API、Host 注册冻结、专项测试、门禁和本文档整体；回滚后
恢复为仅 G0 完成，不保留空接口、空集合或未被后续 Runtime 使用的值对象。
