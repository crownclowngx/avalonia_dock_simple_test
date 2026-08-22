# Managed Plugin V2 G14：V2 封板记录

> 完成日期：2026-08-22  
> 状态：已完成  
> 正式入口：`scripts/Invoke-HostV2ReleaseGate.ps1`  
> 平台：Windows x64 / PowerShell 7 / .NET SDK 10.0.302

## 1. 结果与边界

G14 把 G0–G13 已经完成的 V2 实现、Core/UI SDK、四个业务插件、严格磁盘契约、测试、文档与
可重复制品签署为同一个 `2.0.0` 基线。当前生产路径只支持 manifest v2、Document envelope v2、
`layout-v2.json`、每插件独立 Provider、声明式贡献和 Host internal Dock/lifecycle 实现。

本轮没有增加业务功能，没有改变 C# public 签名，也没有读取、迁移、覆盖或删除用户 V1 数据。
门禁不读取或初始化 AIFLOW，不访问真实账号、Bilibili 网络或大媒体，不运行插件 ReleaseAcceptance，
不创建 CI、提交、标签、上传或公共 NuGet 发布。机器摘要固定记录 `aiflow=false`。

## 2. Plugin SDK 正式基线

G2–G13 期间，V2 public API 保存在 Unshipped，允许重构在同一未发布主版本内收敛。G14 将最终事实
原样移入 Shipped：

| 程序集 | Shipped | Unshipped |
| --- | ---: | ---: |
| `MyAvaloniaManagement.PluginSdk` | 85 | 0 |
| `MyAvaloniaManagement.PluginSdk.UI` | 46 | 0 |
| **合计** | **131** | **0** |

历史 `ApiCompatibility/v1` 的 243 条签名保持原样。以后对 V2 的兼容新增先进入对应程序集的
Unshipped；删除、改名、收窄可见性或修改参数/返回类型属于破坏性变化，必须进入新的 SDK 主版本，
不能改写本次 Shipped 文本掩盖差异。

## 3. 发布门禁设计

正式命令为：

```powershell
.\scripts\Invoke-HostV2ReleaseGate.ps1
```

入口只接受干净 Git 修订，在两个 `git clone --no-hardlinks` 克隆中依次执行：

1. G14 V2 发布门禁 Core 单元测试；
2. 文档门禁 Core 单元测试；
3. 解决方案 `--locked-mode` 还原；
4. Release、`-warnaserror`、`ContinuousIntegrationBuild=true` 构建；
5. V2 生产面全量门禁，包括 Host 覆盖率、SDK/插件测试、包消费、四插件确定性 ZIP、诊断与文档；
6. Core/UI `ApiCompatibility/v2` 文本与成员级变异门禁；
7. Windows 真实窗口 Opened/Closing 和唯一 `layout-v2.json` Smoke。

每轮拥有独立的 TEMP/TMP、NuGet 缓存、DOTNET_CLI_HOME、宿主数据根和插件物理构建目录。规范化
比较只忽略生成时间、耗时、绝对路径和 transcript；测试数、覆盖率、API、文档事实、阶段状态、
ZIP/manifest 长度与 SHA-256、Smoke 文件名和 schema 必须完全相等。只有两轮均成功且语义摘要一致，
顶层结果才会写出 `passed=true`、`repeatabilityVerified=true`、`releaseEligible=true`。

## 4. SOLID 与朴素设计取舍

- **SRP**：叶子脚本拥有领域断言；总入口只负责隔离和编排；Core 模块只负责路径安全、阶段状态、
  证据规范化与完整性检查；专项文档只解释已经通过的事实。
- **OCP**：新增门禁只需增加一个显式阶段和稳定摘要字段，不需要修改 Host/插件测试内部实现。
- **LSP**：既有叶子脚本的参数、成功/失败和产物语义保持不变；Smoke 只纠正为当前 V2 布局事实。
- **ISP**：正式入口无发布、上传、标签、网络或 AIFLOW 参数；Core 函数只接收完成自身职责所需的数据。
- **DIP**：总入口依赖叶子脚本退出码和 JSON/TRX/文件摘要，不依赖测试类、插件内部服务或 Dock 实现。

实现没有引入工作流框架、Pester、反射阶段发现、策略工厂、DI 容器或自定义 MSBuild Task。V1 门禁
代码保持历史原样；V2 使用独立的小型模块，避免修改旧脚本后让 V1 证据失去可解释性。少量重复是有意的
历史隔离边界，比抽取一个同时理解 V1/V2 的通用发布框架更容易审计和整体回滚。

## 5. 单元测试与失败语义

`Test-HostV2ReleaseGateCore.ps1` 使用系统 Temp 中的最小夹具验证：

- 时间、耗时和绝对路径不同仍视为同一发布结果；
- Host/插件测试数、覆盖率、API、文档、阶段、Smoke 或 ZIP 摘要漂移时报告精确 JSON 路径；
- 失败阶段落盘后立即停止，后续阶段不执行；
- 缺少任一 transcript、TRX、覆盖率、摘要、ZIP、manifest 或 Smoke 证据时失败；
- ZIP/manifest 的实际长度与 SHA-256 必须和摘要一致；
- Smoke 必须证明 `layout-v2.json`、schema 2 且没有生成 `layout-v1.json`；
- 递归清理只能发生在门禁明确拥有的 Temp/artifacts 子目录，且能处理只读 NuGet 文件。

任一正式阶段失败时，本轮后续阶段不再执行，但 transcript、阶段状态和已产生的证据继续保留。
临时目录清理失败只产生明确警告，不会把已经失败的发布结论改成成功，也不会让成功结论依赖清理竞态。

## 6. 封板证据

最终门禁从机器生成的 TRX、Cobertura、JSON 和包文件读取数量，不把本次实测数字误当成
未来可以降低的阈值。本次封板生产面实测为：Host Unit 169、Headless UI 53、Plugin 202，
合计 424；PluginSdk 34、DaTang 62、MySmallTools 184、BiliDownloader 718。所有套件均为
零失败、零跳过；Host 行覆盖率 83.24%，分支覆盖率 68.98%。

最终审计使用系统 Temp 中的一次性干净提交，未在当前工作区创建提交或标签：

- 证据目录：`artifacts/release-gate/v2/20260822-015143-2808ade2e7b6/`；
- 审计提交：`2808ade2e7b6e3f54c9819c2097e39017e525b57`；
- Git tree：`8a4e07a291ba2f2e934de87adde5794ac87b4837`；
- 顶层结论：`passed=true`、`repeatabilityVerified=true`、`releaseEligible=true`、
  `publishable=true`、`aiflow=false`。

两轮共同的 ZIP 与外置 manifest 摘要如下；每个长度和 SHA-256 都在复制证据后
对真实文件重算，并参与两轮规范化比较。

| 插件 | ZIP 字节数 / SHA-256 | manifest 字节数 / SHA-256 |
| --- | --- | --- |
| BiliDownloader | 2,492,739 / `41693DFCA61FC0938AF48A12C806457E1A90234E75225184DA87B94D750836A1` | 3,280 / `9758F2DD2E3E62C06D67590491281BCF85078E7782C3D3C424937A14A8CAF813` |
| DaTangAccountingHelpPlug | 2,397,755 / `2374E340A27B9C89501429220072060482B040E5DE7610D0874D4815EE5E915F` | 2,364 / `4AFC70649F0E290978C543CAFA39E3C7EA4D2C082455177C9E2E9EE837637EC0` |
| MyPlugTest | 2,388,720 / `56B384533CB43312D6D6F5A39A3739CDA52811169774715881C0AADE1A58F934` | 2,648 / `1C84E72E4D125E3192548FE069D941ABC52E5E88F375FEB6F468C7D825AC5BDB` |
| MySmallTools | 48,982,373 / `6E374E0BAD431D0D08CF185003771079381B4D5BCD428A2344F385D6915CFEC4` | 96,172 / `2C6147CA2F1660068AF3F6972654FD2BC72B722C7DB8427769617484CF5CF37A` |

本次 V2 封板还确认：

- Host Unit、Headless UI、Plugin 三套测试以及 PluginSdk、DaTang、MySmallTools、BiliDownloader 完整测试
  均为零失败、零跳过；MyPlugTest 由 Host Plugin/UI 套件覆盖；
- 既有 Host 总体与关键文件覆盖率门槛没有降低；
- Core/UI Shipped 分别为 85/46，Unshipped 均为 0，API 变异门禁通过；
- 四个 `2.0.0-win-x64` ZIP 各自完成两次确定性构建、外置 manifest 复核与最终 Host 真实加载；
- 诊断白名单扫描、V1 生产面负例、SDK 包正反消费和文档门禁通过；
- Windows 真实窗口正常打开、关闭、退出，只在隔离数据根保存 schema 2 的 `layout-v2.json`。

可重建证据位于 `artifacts/release-gate/v2/<UTC>-<revision>/`。该目录被 Git 忽略；发布判断以其中
`summary.json` 和两个 pass 目录为准。上表是本次时间点的人工评审投影，未来重跑
仍以新生成的机器摘要为准，不得把本次哈希写成永久白名单。

## 7. 文档与历史边界

根 README、文档导航、架构、设计方法论、兼容约束、快速开始、SDK README、Document/Layout 参考和
测试说明已统一为 G14 当前事实。V1 封板任务书、host-v1 阶段记录及 V1 格式参考增加“已由 V2 取代”
提示，但原始日期、命令、测试数、覆盖率、哈希和当时结论不改写。

## 8. 回滚

G14 的回滚单位是 API Shipped 分类、V2 发布门禁、Smoke V2 断言、文档门禁和当前文档。可以整体回滚
为 G13 的未发布候选状态，但回滚后不得继续宣称 V2 已封板，也不能恢复 V1 reader、Legacy 项目、
Host/Common 双区间或任何隐藏 fallback。历史 V1 数据始终原样保留。
