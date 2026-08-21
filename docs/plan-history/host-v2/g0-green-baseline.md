# Managed Plugin V2 G0：绿色基线冻结记录

> 状态：已完成
>
> 完成日期：2026-08-21
>
> 分支：`dev-重构-2026年8月18日`
>
> 验证源提交：`abb8c262209f339acd7989767d240e2e93724ccf`
>
> 所属任务：[Managed Plugin V2 破坏式架构重构任务书](../../design/host-v2-breaking-refactor-plan.md#G0冻结绿色基线已完成)

## 1. 结论与边界

G0 已在不改变生产行为的前提下冻结可重复验证的绿色基线。解决方案锁定还原和 Release
`-warnaserror` 构建为 0 警告、0 错误；Host Unit、Headless UI、Plugin 共 **361/361** 通过，
行覆盖率 **81.12%**、分支覆盖率 **66.85%**。三个有独立测试项目的业务插件共 **967/967**
通过；Plugin SDK Shipped API 为 **243** 条、Unshipped 为 **0**；四个 Managed Plugin 均完成
两轮确定性构建并由最终 Host 加载 **4/4**。

本阶段只新增文档和扩展现有文档门禁的检查范围。没有修改 Host、SDK 或插件生产源码，没有改变
public API、版本、锁文件、manifest、Document、layout、数据根或插件兼容区间，也没有建立 V1/V2
双栈。`.aiflow` 未读取、未修改、未参与证据生成。

本轮明确没有执行 Windows Smoke、Windows CI、G14 发布门禁、发布验收项目、真实网络/媒体、上传或
发布。G0 是非发布基线，不能冒充 Windows 发布放行；G14 仍负责实际发布前的完整平台验收。

## 2. SOLID 优先与朴素设计

后续 V2 评审顺序固定为：先判断职责和所有权，再判断依赖方向与接口最小性，最后才考虑是否需要设计
模式。模式只能解决已经出现的变化点，不能为了展示技巧制造抽象层。

- **SRP**：Core SDK 只表达跨边界契约，UI SDK 只表达 UI 接入，Host 独占 Dock、持久化和生命周期
  编排，插件只拥有自身模型、View 和私有服务。
- **OCP**：新增插件和贡献应通过不可变 Descriptor 扩展；Host 核心不增加插件名分支，但也不建立通用
  规则引擎或动态注册框架。
- **LSP**：Document、Tool 和生命周期实现必须满足窄契约的完整语义；不使用空实现、类型判断或静默
  fallback 假装兼容 V1。
- **ISP**：Core、UI、生命周期、Document 内容和 Host Port 分开，消费者不被迫引用 Dock、Newtonsoft
  或任意 Host 实现。
- **DIP**：插件依赖 SDK 契约，Host Adapter 依赖插件契约并拥有框架细节；插件不得反向引用 Host 或
  通过任意 `IServiceProvider` 桥接其他容器。

G0 本身不新增聚合脚本、策略层、继承层级、DI 编排器或兼容适配器。证据由现有测试脚本、CLI 输出和
可读表格直接组成；文档门禁只增加两个显式 Host 历史目录和一个候选任务书入口。新增脚本注释使用中文，
解释“当前事实、候选设计、历史证据”为什么必须分开，而不是逐行复述 PowerShell 语法。

## 3. V2 删除面清单

以下是 G0 冻结的迁移输入，不表示对应类型已经删除。每一组必须在指定阶段用单一 V2 事实替换；G13
只负责清除残留，不得在最后阶段重新设计契约。

| 分组 | V1 删除面 | V2 唯一替代 | 主处理阶段 |
| --- | --- | --- | --- |
| 贡献注册 | `IDocumentCreationStrategy`、`IToolCreationStrategy`、`IDocumentCreationIntentProvider`、`DocumentCreationMenuEntry`、独立 `AddView`、`IPluginRegistrationContext` | Core/UI SDK 中的窄 `IPluginRegistration` 与一次性 `AddDocument`、`AddPersistableDocument`、`AddTool` Descriptor | G2、G5、G9–G12 |
| DI 保护 | `HostServiceDescriptorPolicy`、`PluginServiceRegistrationTransaction`、插件修改 Host 根集合的工作副本 | 每插件独立 `ServiceCollection`、Provider 和受控 Host Port | G4、G5、G13 |
| Dock 与 Scope | 插件 Strategy 返回 Dock `Document`/`Tool`、public `IDocumentScopeFactory`、Metadata `LegacyIds` | Host internal `ManagedDocumentDockable`/`ManagedToolDockable`，插件只提供普通模型与 View | G2、G6、G8、G13 |
| 生命周期 | public `PluginLifecycleManager`、Runner、PlanBuilder、状态/注册 DTO、`IPluginLifecycleDependencies` 和 `Order` | SDK 只保留启动/停止接口；Host internal coordinator 按 PluginId 确定性编排 | G2、G8、G11、G12、G13 |
| Document 持久化 | `ISavableDocument`、`IDocumentSaveState`、字符串 `DocumentContentSnapshot`、public Newtonsoft/STJ ID Converter | `IPersistablePluginDocument` 与构造时克隆 `JsonElement` 的 `DocumentContent` | G2、G6、G7、G9–G12、G13 |
| 兼容与磁盘 | manifest Host/Common 双区间、入口模块扫描、V1 reader、旧布局迁移、旧 ID 与兼容浮动字段 | manifest、Document、layout 和数据根只接受 V2；入口程序集和类型显式声明 | G1、G3、G7、G8、G13 |

源码检索的时间点规模也被保留：`IDocumentCreationStrategy` 出现在 36 个文件、
`IToolCreationStrategy` 21 个、`AddView` 19 个、`IDocumentScopeFactory` 28 个、
`PluginLifecycleManager` 14 个、`DocumentContentSnapshot` 21 个；两个 Host DI 保护实现各只位于 2 个
Host 文件。数量包含生产、测试、Harness 和脚本，只用于规划迁移批次，不作为以后必须维持的固定门槛。

## 4. 目标依赖白名单与当前差距

| 所有者 | V2 允许依赖 | V2 禁止依赖 | G0 当前差距 |
| --- | --- | --- | --- |
| `MyAvaloniaManagement.PluginSdk` | .NET BCL、`System.Text.Json`；Public API Analyzer 仅作构建门禁 | Avalonia、Dock、Newtonsoft、Microsoft DI、Host 实现 | 当前 Common 有 5 个直接包：Avalonia、Dock、DI.Abstractions、Newtonsoft 和构建期 Analyzer；前四项必须在 G2 移出 |
| `MyAvaloniaManagement.PluginSdk.UI` | Core SDK、Avalonia、DI.Abstractions、经 Host 明确支持的 UI Profile | Dock、Newtonsoft、Host 实现 | 当前仍是无运行时程序集的依赖 Profile，9 个直接 UI 包中包含 Dock，且传递得到 Newtonsoft；G2 必须改为真实窄契约程序集 |
| Host | Core/UI SDK、Avalonia、Dock、持久化和 Host internal 实现 | 被插件直接引用的 public Host 实现契约 | 当前依赖方向合法，但 Dock Adapter、独立插件 Provider 和 internal 生命周期尚未实现 |
| 四个插件 | Core/UI SDK 与各插件自身业务依赖 | Host 项目、Dock 模型、其他插件私有服务、任意跨容器 Provider 桥 | 当前四插件仍使用 Common 命名空间和 V1 Strategy；部分 ViewModel/Strategy 直接认识 Dock 或 public Host 编排面 |
| 测试与 Harness | 对应生产契约、明确 friend 边界和测试专用依赖 | 为测试方便扩大生产 public 面 | 当前大量测试直接构造 V1 Strategy、Scope 和生命周期类型；迁移时必须跟随所有权边界，而不是保留 shim |

完整包图包含 **17** 个解决方案项目、每个项目 1 个目标框架、**82** 条直接包引用记录和 **694** 条
传递包引用记录。这里的数量按“项目—包”关系计数，同一包被多个项目引用会分别计入。关键项目摘要：

| 项目/项目组 | 直接引用 | 传递引用 |
| --- | ---: | ---: |
| Host | 18 | 54 |
| 当前 Core SDK 项目（Common） | 5 | 6 |
| 当前 UI Profile | 9 | 14 |
| 四个生产插件合计 | 18 | 68 |
| 测试、Harness 与发布验收项目合计 | 32 | 552 |

原始项目清单和 JSON 包图位于 `artifacts/baseline/host-v2/g0/`，不纳入 Git：

| 证据 | SHA-256 |
| --- | --- |
| `solution-projects.txt` | `BDEFBAB6B4AE991DBD63C36A3BD55C6AEA6DD44886973FADC5885ACDA91242EE` |
| `package-graph.json` | `61D8BC8EF9E21345DFB7B499194152A542915F71B67663972803001C3273BE7F` |

## 5. 当前消费者矩阵

`有`表示存在需要迁移的生产消费者；“测试/工具”列同时记录测试替身、专项 Harness 或构建脚本。矩阵
冻结的是所有权和迁移范围，不承诺逐文件名称永久不变。

| 删除面分组 | Host | BiliDownloader | DaTang | MyPlugTest | MySmallTools | 测试/工具 |
| --- | --- | --- | --- | --- | --- | --- |
| Strategy、Intent、Metadata、`AddView` | 有：内建 Document/Tool、Registry、菜单和组合根 | 有：Document、Scheduler Tool、模块 | 有：两个 Document、模块 | 有：四个 Document、Tool、模块 | 有：四个安全视频 Document、模块 | Host 三套测试及 Bili/DaTang 测试替身 |
| Host 根 DI 保护事务 | 有：Catalog、注册 Context、保护策略 | 间接：模块向 Context 注册 | 间接：模块向 Context 注册 | 间接：模块向 Context 注册 | 间接：模块向 Context 注册 | PluginServiceProtection 与模块组合测试 |
| Dock、Scope、`LegacyIds` | 有：Scope Manager、ManagementFactory、Registry、布局 | 有：Document Strategy | 有：两个 Document Strategy | 有：四个 Document Strategy | 有：四个 Document Strategy | Scope 隔离、布局、UI 和播放器稳定性测试 |
| public 生命周期编排 | 有：启动、状态 Tool、HostRuntime | 有：Scheduler Tool 直接读取 Manager | 无直接 Manager 消费 | 无直接 Manager 消费 | 生产仅使用生命周期接口 | 生命周期测试、Bili 测试、MySmallTools Harness |
| V1 保存状态与字符串内容 | 有：保存、关闭、信封和 ManagementFactory | 有：下载方案保存/恢复 | 有：银行调节保存/恢复 | 有：欢迎文档保存/恢复 | 无 Host Document 保存消费者 | Host/Plugin 内容契约、Bili/DaTang 测试和 SDK 包负例 |
| manifest/构建双区间与 V1 reader | 有：Loader、版本政策、Document/layout reader | 有：项目属性和清单 | 有：项目属性和清单 | 有：项目属性和清单 | 有：项目属性和清单 | 包构建、SDK 包、兼容、布局和版本政策门禁 |

## 6. 验证命令与结果

### 6.1 非发布命令

在仓库根目录按顺序执行：

```powershell
dotnet restore .\MyAvaloniaManagement.sln --locked-mode --nologo
dotnet build .\MyAvaloniaManagement.sln -c Release --no-restore --nologo -warnaserror -p:SkipPluginDeploy=true
dotnet list .\MyAvaloniaManagement.sln package --include-transitive --format json --no-restore
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -NoRestore
dotnet test .\Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj -c Release --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug.Tests\DaTangAccountingHelpPlug.Tests.csproj -c Release --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj -c Release --no-restore -p:SkipPluginDeploy=true
.\scripts\Test-HostDiagnosticRedaction.ps1
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v1 -Configuration Release
.\scripts\Test-ManagedPluginPackages.ps1 -Configuration Release
.\scripts\Test-DocumentationCore.ps1
.\scripts\Test-Documentation.ps1
```

前三个包图命令的原始输出保存在 `artifacts/baseline/host-v2/g0/`；现有脚本继续把 TRX、Cobertura、
HTML、JSON、ZIP 和清单写到各自 `artifacts/test-results/` 子目录。没有新增只做转发的 G0 聚合入口。

### 6.2 测试和契约结果

| 门禁 | 结果 |
| --- | --- |
| 锁定还原与 Release `-warnaserror` | 通过；0 警告、0 错误 |
| Host Unit/UI/Plugin | 173 + 38 + 150 = **361/361**；失败 0、跳过 0 |
| Host 覆盖率 | 行 **81.12%**、分支 **66.85%**；总体及关键文件门槛未降低 |
| 插件单元测试 | BiliDownloader 720、DaTang 64、MySmallTools 183，共 **967/967**；失败 0、跳过 0 |
| 诊断脱敏源码门禁 | 检查 127 个生产 C# 文件，通过 |
| SDK 包消费 | 内容、事件和 UI 正例通过；G5/G8/G9/G11 删除契约负例正确失败 |
| SDK API 兼容 | Shipped **243**、Unshipped **0**；7 个破坏性负例和 1 组兼容新增流程通过 |
| 四插件包 | 16 个协议负例通过；每插件两轮确定性构建；最终 Host 加载 **4/4** |
| 文档核心单元测试与正式门禁 | 正反例通过；检查 37 份文档、229 个本地链接、73 个脚本路径和 38 个项目路径 |
| Windows/发布项 | **未执行**：Windows Smoke、CI、G14 发布门禁、发布验收、网络/媒体、上传和发布 |

API 文本基线仍是 V1 历史事实，G0 不改写它：

| 文件 | 条目 | SHA-256 |
| --- | ---: | --- |
| `PublicAPI.Shipped.txt` | 243 | `C546F14AFBA52918A529CBE9A241462215ED8EC0164C2E024B72A2564F2600E5` |
| `PublicAPI.Unshipped.txt` | 0 | `0570CF88EF7BA0638A95F61E904C349C0C00BD34F76241B5EA968CE31482606A` |

### 6.3 四插件包事实

| 插件 | ZIP | 文件 | 大小（字节） | SHA-256 |
| --- | --- | ---: | ---: | --- |
| BiliDownloader | `BiliDownloader-1.0.0-win-x64.zip` | 14 | 2,489,662 | `3BF653EB5908167011E4700551669CA4B58446DC7D1D694768AE1CCDDF99EF0B` |
| DaTangAccountingHelpPlug | `DaTangAccountingHelpPlug-1.0.0-win-x64.zip` | 9 | 2,393,352 | `64237FF2B4FB91F1B33A850453C1A135994A0DCF2151AA30A9C8E041B016DAA4` |
| MyPlugTest | `MyPlugTest-1.0.0-win-x64.zip` | 11 | 2,387,778 | `A9098ED207821DAA3BBCBFC9280239381FF585F6F55A834BBBB25E312E5B4106` |
| MySmallTools | `MySmallTools-1.0.0-win-x64.zip` | 431 | 48,981,675 | `1C82117C78113BBE52CC4F12AC05EC99A22CBB151C98DA3D22EA942178CF3EBD` |

这些 SHA-256 来自本轮第一份候选包，脚本已经证明第二次隔离构建与其逐字节一致。MyPlugTest 没有
独立单元测试项目，由解决方案 Release 构建、Managed Plugin 协议负例、两轮打包和最终 Host 加载覆盖。

## 7. 回滚与进入 G1 的条件

G0 回滚只允许删除本记录、导航入口和文档门禁的 Host V2 扫描范围；忽略目录中的证据可以重新生成。
不得改写 `docs/plan-history/host-v1/`、删除 G13 API 文本或移动 `managed-plugin-v1.0.0` 标签来伪装
回滚，也不得为了让后续阶段通过而降低覆盖率、跳过失败测试或保留隐藏兼容路径。

只有以下条件继续成立时才可进入 G1：

- [x] 生产源码、public API、版本、schema、数据根和锁文件未变化；
- [x] Release 构建为 0 警告、0 错误；
- [x] Host 361 项和插件 967 项测试全部通过且无跳过；
- [x] 覆盖率门槛、诊断脱敏、SDK 包/API 和四插件确定性包门禁通过；
- [x] 文档核心正反例和正式门禁通过，V2 任务书与 G0 专项记录已纳入路径检查；
- [x] 删除面、目标依赖白名单和消费者矩阵均已冻结；
- [x] Windows Smoke、CI 和发布门禁明确延后到发布阶段，没有被宣称为本轮证据；
- [x] V1 历史记录保持原样，G1–G14 仍明确标记为尚未实现。
