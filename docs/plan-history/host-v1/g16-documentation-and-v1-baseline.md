# G16：同步文档并建立 Managed Plugin v1 基线

> **历史说明：本 V1 基线已由 Managed Plugin V2 G14 取代；以下日期、数量和结论保持原样。**

> 完成日期：2026-08-20
> 状态：已完成
> v1 基线标签：`managed-plugin-v1.0.0`（本地 annotated tag，不自动推送）
> 文档门禁：`scripts/Test-Documentation.ps1`

## 1. 结果与边界

G16 完成了 Managed Plugin v1 的文档签署与 Git 基线定位。根 README、文档导航、宿主架构、
兼容约束、快速开始、测试说明和封板任务书现在使用同一组当前事实；G0–G15 的历史命令、测试数量
和当时结论继续保留，不以今天的实现倒写过去证据。

本次没有修改生产 C#、Plugin SDK public API、Document/布局/诊断 schema、插件业务行为或包格式。
G13 的可读 API 文本仍是 SDK v1 的成员事实源，G12 的统一构建协议仍是四插件包布局事实源；G16
只验证并签署这些已经完成的契约，没有建立重复的版本文件或第二套打包规则。

按本任务的明确约束，G16 没有调用 `Invoke-HostV1ReleaseGate.ps1`，没有运行 Windows Smoke，
没有新增 Windows CI，也没有执行上传、NuGet 发布或真实网络/媒体验收。因此本记录是文档、单元测试、
SDK 与插件兼容基线，不是一次新的 G14 发布放行。G14 的两轮隔离证据和脚本作为历史能力原样保留。

## 2. 基线事实

版本只从 `Directory.Version.props` 读取。标签定位这些相互独立但在本次签署时共同为 v1 的事实，
不意味着以后必须同步提升它们：

| 所有者 | v1 签署值 | 后续演进规则 |
| --- | --- | --- |
| 产品 | `1.0.0` | 用户可见发布节奏独立演进 |
| Plugin SDK 包 | `1.0.0` | 兼容新增提升次版本，破坏性变化提升主版本 |
| Host API / SDK 程序集身份 | `1.0.0.0` | 同一主版本内保持二进制兼容身份 |
| SDK API 文本 | `ApiCompatibility/v1` | G13 Shipped/Unshipped 生命周期继续适用 |
| manifest schema | `1` | 只由插件清单 reader 解释 |
| Document 信封 | `schemaVersion = 1` | 宿主七字段信封；插件内容 schema 独立 |
| 宿主数据根代际 | `v1` | 默认目录为 `%LOCALAPPDATA%\MyAvaloniaManagement\v1\` |

旧预发布数据继续保留在原目录；宿主不读取、迁移、改写或删除。设置
`MYAVALONIA_DATA_DIRECTORY` 时，该值仍是完整数据根，不再追加 `v1`。

四个仓库插件均由各自项目文件声明版本和兼容区间，G16 门禁直接读取这些文件，不复制常量：

| 插件 | PluginVersion | Host API | Common |
| --- | --- | --- | --- |
| BiliDownloader | `1.0.0` | `[1.0.0, 2.0.0)` | `[1.0.0, 2.0.0)` |
| DaTangAccountingHelpPlug | `1.0.0` | `[1.0.0, 2.0.0)` | `[1.0.0, 2.0.0)` |
| MyPlugTest | `1.0.0` | `[1.0.0, 2.0.0)` | `[1.0.0, 2.0.0)` |
| MySmallTools | `1.0.0` | `[1.0.0, 2.0.0)` | `[1.0.0, 2.0.0)` |

## 3. 文档门禁与 SOLID 取舍

文档门禁采用三个朴素组成部分：

- `DocumentationGate.Core.psm1` 只实现链接、脚本路径、过期表述、源码符号和版本事实校验；
- `Test-Documentation.ps1` 只选择 G16 文档范围、组合规则并写出 JSON 摘要；
- `Test-DocumentationCore.ps1` 只在系统临时目录构造正反例，不修改仓库文件。

这种划分落实了单一职责（SRP），让入口依赖窄小的校验函数（ISP/DIP），并允许新增一条明确规则而
不修改其他校验流程（OCP）。没有引入 Markdown 框架、规则引擎、继承层级、DI 容器或通用发布流水线。
链接解析只解决当前问题：外部 URL 和纯页内锚点跳过；本地路径移除片段后必须存在，文件还必须使用
Git 候选路径中的精确大小写。历史文档参加链接检查，但不参加“当前状态”措辞禁令。

脚本和模块中的说明性注释使用中文，并解释边界选择、Windows 大小写陷阱、历史文档排除和临时目录
清理哨兵，而不是逐行复述语法。

## 4. 文档同步范围

| 文档组 | G16 处理 |
| --- | --- |
| 根 README 与文档导航 | 标记 v1 已封板，提供 G16 记录和文档门禁入口 |
| 宿主架构与兼容约束 | 固定 Managed-only、public API、Document、事件、诊断和版本所有权边界 |
| 快速开始 | 明确示例面向 `managed-plugin-v1.0.0`，外部插件必须使用匹配 SDK |
| 测试说明 | 增加 G16 非发布门禁，测试数量继续作为带日期证据而非永久阈值 |
| 封板任务书 | 将 G0–G16 和签署清单收口为已完成，并记录标签与回退边界 |
| G0–G15 历史记录 | 不改写原始证据，只通过导航与后续状态连接到最终基线 |

## 5. 自动化证据

本轮严格按非 Windows Smoke、非发布验收范围执行。所有数量均来自本轮控制台、TRX、Cobertura 或
JSON 摘要，不作为以后新增测试时需要手工维持的固定门槛。

| 门禁 | 结果 |
| --- | --- |
| G16 文档核心单元测试 | 通过；链接、脚本路径、过期表述、类型、版本、插件区间和路径安全正反例全绿 |
| G16 文档门禁 | 35 份文档、220 个本地链接、65 个脚本路径、35 个真实项目路径；4 个插件版本区间一致 |
| 锁定还原与 Release `-warnaserror` 构建 | 通过；0 警告、0 错误 |
| G15 诊断源码扫描 | 通过；检查 127 个生产 C# 文件 |
| Host Unit/UI/Plugin 与覆盖率 | Unit 173、UI 38、Plugin 150，共 361/361；行 81.12%、分支 66.85% |
| 插件单元测试 | BiliDownloader 720/720、DaTang 64/64、MySmallTools 183/183 |
| SDK 包消费 | 最小内容/事件/UI 正例通过；G5/G8/G9/G11 删除契约负例正确阻断 |
| SDK API v1 兼容 | Shipped 243、Unshipped 0；7 个破坏性负例和 1 组兼容新增审阅流程通过 |
| 四插件确定性包与最终包加载 | 4 个独立 `win-x64` ZIP 均完成两轮确定性构建；最终 Host 加载 4/4 |

未执行项：Windows Smoke、G14 总发布门禁、发布验收项目、真实网络/媒体、CI、上传和发布。

## 6. 标签、发布与回退

全部门禁通过后，G16 使用独立提交 `docs(host): 完成 G16 文档同步与 v1 基线`，并在该提交创建
本地 annotated tag `managed-plugin-v1.0.0`，注解为
`Managed Plugin v1.0.0 baseline: G0-G16 completed`。本任务不推送提交或标签。

若标签位置错误，只删除本地标签后在正确提交重建：

```powershell
git tag -d managed-plugin-v1.0.0
```

若需要撤销已经提交的 G16 文档或门禁，应使用新的 `git revert` 保留历史；不得重写 G0–G15 证据，
不得删除 G13 `ApiCompatibility/v1` 来伪装兼容，也不得用移动标签掩盖生产契约变化。
