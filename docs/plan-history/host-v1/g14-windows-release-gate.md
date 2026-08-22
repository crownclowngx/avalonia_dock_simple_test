# G14：平台无关的 Windows 本地发布门禁

> **历史说明：本 V1 门禁已由 `Invoke-HostV2ReleaseGate.ps1` 取代；以下日期、数量和结论保持原样。**

> 完成日期：2026-08-20
> 状态：已完成
> 支持平台：Windows x64 / PowerShell 7 / .NET SDK 10.0.302
> 正式入口：`scripts/Invoke-HostV1ReleaseGate.ps1`

## 1. 结果与使用边界

G14 已把此前分散的宿主、Plugin SDK、API 兼容和真实插件包验证收敛为一个显式命令：

```powershell
.\scripts\Invoke-HostV1ReleaseGate.ps1
```

入口只接受干净 Git 提交，在两个独立本地克隆中连续执行完整 Release 门禁，并比较两轮机器可读
结果。它不读取当前工作目录已有的 `bin`、`obj`、`Controls` 或 `artifacts`，也不读取用户
LocalAppData。任一阶段失败会立即停止后续阶段，但已产生的 transcript、TRX、覆盖率和包证据会保留。

本项目明确选择不绑定 Gitee、GitHub、Jenkins 等托管平台，因此 G14 不提交 CI YAML、Git Hook，
也不创建或推送标签。当前完成的是可在本机、计划任务或任意未来 Windows CI 中原样调用的发布门禁，
不是服务器端分支保护；不能据此声称远端会自动拒绝绕过门禁的合并或标签。

## 2. 固定执行顺序

每个隔离克隆严格执行以下阶段，前一阶段失败时不进入后一阶段：

1. G14 核心 PowerShell 单元测试；
2. 解决方案 `--locked-mode` 还原；
3. Release、`-warnaserror`、`ContinuousIntegrationBuild=true` 构建；
4. Host Unit、Headless UI、Plugin 三套测试及覆盖率门槛；
5. Plugin SDK 基础包/UI Profile 的独立包消费正反例；
6. Plugin SDK v1 public API 文本和成员级变异兼容门禁；
7. 四个 Managed Plugin 的协议负例、两次确定性 ZIP 和最终 Host 加载；
8. Windows 真实主窗口 Opened/Closing Smoke。

Smoke 被拆为 `Invoke-MyAvaloniaManagementWindowsSmoke.ps1`，因此可以严格位于全部 API 和包门禁之后。
旧命令 `Invoke-MyAvaloniaManagementTests.ps1 -WindowsSmoke` 仍委托该脚本，调用兼容性没有改变。

## 3. 隔离模型与证据

总入口固定当前提交和 Git tree，使用 `git clone --no-hardlinks` 创建两份独立源树。每轮分别拥有自己的：

- `TEMP`/`TMP`；
- `DOTNET_CLI_HOME`、NuGet 包缓存和 HTTP 缓存；
- `MYAVALONIA_DATA_DIRECTORY`；
- 构建输出、测试结果和插件部署目录。

机器必须预装 Git、PowerShell 7 和 `global.json` 指定的 .NET SDK；仓库内 dotnet tool manifest 会在
隔离环境中重新还原，门禁不依赖开发机预装的 ReportGenerator 全局工具。

证据保存在 `artifacts/release-gate/<UTC>-<short-commit>/`：

```text
summary.json
pass-1/
├── pass.log
├── stage-state.json
├── summary.json
├── MyAvaloniaManagement/       # 三套 TRX、覆盖率和动态测试数
├── ManagedPluginPackages/      # 四个 ZIP、外置清单和聚合摘要
└── WindowsSmoke/summary.json
pass-2/                         # 与 pass-1 相同结构
```

规范化比较忽略生成时间、耗时、绝对路径和 transcript 文本，只比较提交/tree、平台、SDK、阶段状态、
三套动态测试数、覆盖率、SDK/API 基线、插件包文件数与 SHA-256、Smoke 结果。两轮任一发布事实不同，
顶层 `releaseEligible` 都不会成为 `true`。

## 4. SOLID 与朴素设计

- **SRP**：现有叶子脚本继续验证各自领域；G14 入口只负责隔离和编排；Core 模块只负责路径、阶段和证据。
- **OCP**：未来新增门禁只需增加一个显式阶段和对应稳定结果，不需要改写既有测试或打包算法。
- **LSP**：旧宿主测试及 `-WindowsSmoke` 参数保持原来的成功、失败和输出语义。
- **ISP**：正式入口没有发布、上传、标签或平台参数；Smoke 脚本只接收配置、还原和结果目录。
- **DIP**：总入口依赖叶子脚本退出码和 JSON/TRX/清单，不依赖测试类、MSBuild Target 或插件内部实现。

实现没有引入工作流框架、Pester、反射发现、策略工厂、自定义 MSBuild Task 或新的全局工具。
阶段顺序使用一张显式列表；证据比较使用一个小型递归比较器，避免为八个固定步骤建立抽象层级。
插件打包的物理输出仍分别位于每轮隔离的 `TEMP`；仅在编译期间，用命名互斥量保护一个稳定逻辑路径，
再通过 Junction 把它指向当轮物理目录，并用 `PathMap` 归一化仓库根和构建槽。这样既不让两轮共享
`bin`、`obj`、NuGet 或部署产物，也避免 portable PDB、CodeView 和 Avalonia XAML 后处理把不同克隆的
绝对路径写进 DLL。Junction 在递归清理物理目录前单独移除，避免清理边界越过临时目录进入真实仓库。
该处理确保“可重复”是跨独立源树成立，而不只是同一工作目录内成立。

## 5. 单元测试与失败语义

`Test-HostV1ReleaseGateCore.ps1` 不依赖第三方模块，以临时夹具覆盖：

- 时间、耗时和绝对路径不同仍视为同一发布结果；
- Unit/UI/Plugin 数量、覆盖率、API 条目、阶段状态、Smoke 或 ZIP SHA 漂移时打印具体 JSON 路径；
- 缺少 transcript、汇总、TRX、覆盖率、ZIP、外置清单或 Smoke 结果时失败；
- 中间阶段失败后后续阶段不执行，失败状态仍写入 JSON；
- 任何允许根之外的清理路径均被拒绝；门禁自有目录中的只读 NuGet 文件可以安全清理。

临时克隆和核心测试目录均使用 GUID 命名，并在删除前再次验证属于系统 Temp。正式证据目录使用唯一
时间和提交命名，不删除以前的门禁记录。Windows 上的常驻 MSBuild 节点会先被正常关闭，避免其持有
NuGet 构建任务 DLL 而阻止临时缓存清理；即使外部进程仍占用文件，清理警告也不会覆盖已落盘的门禁结论。

## 6. 验收证据

2026-08-20 的最终验证中，两轮隔离门禁均得到相同的发布事实：

- 证据目录：`artifacts/release-gate/20260820-091058-7a5d6196ff6c/`
- 验证提交：`7a5d6196ff6cff47a6a4fb34283adc6db97b85b2`
- Git tree：`928b56a8261d60a6cda13c207db42deef4ade29f`
- 顶层结论：`passed=true`、`repeatabilityVerified=true`、`releaseEligible=true`

验证提交是在一次性干净副本中为本次未提交工作树创建的审计快照；tree 精确对应本记录所述实现，
验证结束后副本已按安全清理边界删除，证据已复制回仓库的忽略目录。正式发布时仍必须从实际干净提交
重新执行无参数入口，不能把本次开发验收快照当作未来提交的放行凭据。

| 门禁 | 两轮结果 |
| --- | --- |
| 锁定还原与 Release 构建 | 通过；0 警告、0 错误 |
| Host Unit / UI / Plugin | 167 / 38 / 149，共 354/354，无跳过 |
| Host 覆盖率 | 行 80.62%，分支 65.91% |
| Plugin SDK 包与 API | 包消费正反例通过；v1 Shipped 243、Unshipped 0；成员级变异通过 |
| Managed Plugin 包矩阵 | 4 个插件；每插件 2 次确定性构建；16 个协议负例；最终 ZIP Host 加载通过 |
| Windows Smoke | 退出码 0；隔离目录生成 `layout-v1.json` |
| 两轮规范化比较 | 完全一致；`repeatabilityVerified=true` |

四个最终 ZIP 的两轮共同 SHA-256 为：

| 插件 | ZIP SHA-256 | 外置清单 SHA-256 |
| --- | --- | --- |
| BiliDownloader | `738F9EA40AAC51021A623CE79829A579DA16462A6E2077B00E0153473EC70463` | `E7F2756360169CA401FBADEAA8526AD16D6D3EABB9BC0181B64A9A5572023831` |
| DaTangAccountingHelpPlug | `F9774BF542909081546325F9DF850D39A84366459C6C267EEEC55004AA85C9D3` | `80A1122AF450D311E7BA6724A384A0B127E419A2E92DD9B815D3062D789AB44E` |
| MyPlugTest | `53C8083A87D8DD3550EEA51CE5B55365CF2A104F3538314D4A0A793C5D0BB4B0` | `1D9B6EA89738F75B2E51F038BD5E027F96ECA0DDE88BC6FEE191205A22A9A7FC` |
| MySmallTools | `4E0B5EE02B5DDE2CDFAF9BCA5DA4DA14754D4F922C9DA613518EA1BFEF28E57A` | `6F6EDEAB41676E346F0432288B9B918A975FD553FB4F6D030D72F86AEB30C9BE` |

测试数量和覆盖率来自每轮 TRX/Cobertura/`summary.json`，不是脚本中的固定阈值。表格只是本次时间点
证据；未来执行仍以新生成的机器结果为准。

## 7. 实现反馈与回滚

旧入口第一次委托独立 Smoke 时，数组展开让 PowerShell 把 `-Configuration` 误当成配置值。门禁在三套
测试通过后明确失败，没有继续冒充 Smoke 成功；改为命名参数映射后，旧入口复跑得到 354/354、
80.62%/65.91% 且真实窗口通过。这个记录证明失败即停止和旧接口回归都实际参与了验收。

一次补强验收在 SDK 包消费断言全部通过后，因 MSBuild 暂时持有隔离 NuGet 缓存中的 Avalonia DLL 而
在清理阶段失败。门禁保留失败证据并停止后续阶段；修正为禁止节点复用，并让叶子脚本只在首次删除
失败时关闭 build server、清除只读属性及执行最多 10 秒的有限重试。最终两轮复跑均正常清理，未把
清理竞态降级为警告或绕过门禁。

G14 可以整体回滚到分散执行脚本，但回滚后不能继续宣称具备两轮隔离、一致性比较或统一证据入口。
独立 Smoke 脚本可单独回滚为宿主测试内函数，但必须同时恢复旧 `-WindowsSmoke` 行为。G14 没有改变
生产 API、Plugin SDK、插件业务、发布上传或标签策略；G15/G16 仍分别负责诊断脱敏与最终封板。

## 8. G15 后续接入（2026-08-20）

本节记录 G14 完成后的流水线扩展，不改写第 2、6 节所述 G14 当时的八阶段、354/354、覆盖率和包
哈希历史事实。G15 在 `release-build` 之后、`host-tests` 之前新增独立失败即停止阶段
`host-diagnostic-redaction`，执行 `scripts/Test-HostDiagnosticRedaction.ps1`。因此当前发布入口每轮为
九个阶段；源码扫描失败时不会继续运行三套宿主测试、SDK/API、插件包或 Smoke。

G15 本地验收时，专项扫描检查 127 个生产 C# 文件并通过，宿主三套测试更新为 Unit 173、UI 38、
Plugin 150，共 361/361；行覆盖率 81.12%、分支覆盖率 66.85%。这些是 G15 时间点证据，最终两轮
隔离复验仍以新生成的 `artifacts/release-gate/<UTC>-<commit>/summary.json` 为准。G15 审计快照的
第 1 轮九阶段已完整通过，证据位于
`artifacts/release-gate/20260820-114346-a55a7f535772/pass-1/`；按维护者要求，独立 NuGet 包消费/API
核心门禁不在第 2 轮重复，因此没有把本次单轮结果写成新的“两轮一致”。脱敏设计、敏感调试开关和
回滚边界见 [G15 宿主诊断脱敏](./g15-host-diagnostic-redaction.md)。
