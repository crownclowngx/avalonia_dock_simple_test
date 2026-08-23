# Managed Plugin V3 G14：V3 封板记录

> 状态：已完成；本记录签署 V3 `3.0.0` 本地可发布基线，不代表已经上传或对外发布。<br>
> 正式入口：`scripts/Invoke-HostV3ReleaseGate.ps1`<br>
> 历史边界：V1/V2 API 文本、发布脚本和阶段证据保持原样，不参与 V3 编译与运行。

## 1. 结论与范围

G14 把 G0–G13 已验证的实现、Core/UI SDK、四个业务插件、V2 线格式数据、测试、文档和
可复现制品签署为同一 V3 基线。封板没有新增业务功能，没有修改任何 C# public 签名，也没有提升
manifest、Document envelope、layout 或数据根版本。

本阶段只建立本地发布资格，不创建 CI、当前分支提交、标签、上传或公共 NuGet 发布，也不读取、
初始化或修改 AIFLOW。实际外部发布仍需独立授权。

## 2. API 正式签署

G1–G13 期间，V3 public API 保存在 Unshipped，允许同一未发布主版本内完成破坏式语义收口。
G14 将最终文本逐行原样移入 Shipped：

| 程序集 | Shipped | Unshipped |
| --- | ---: | ---: |
| `MyAvaloniaManagement.PluginSdk` | 127 | 0 |
| `MyAvaloniaManagement.PluginSdk.UI` | 45 | 0 |

最终签署简称为 **Core 127、UI 45**。V1 Core 243 条及 V2 Core/UI 85/46 条历史 Shipped 不改写。
以后兼容新增进入 v3 Unshipped；删除、改名、收窄可见性或修改参数/返回类型必须提升 SDK 主版本，
不能重写本次 Shipped 文本掩盖破坏。

## 3. SOLID 与朴素设计

- **SRP**：G9–G13 叶子脚本拥有领域断言；发布入口只负责隔离、顺序、证据复制和最终结论；
  Core 模块只负责路径安全、阶段状态、规范化比较和实体完整性。
- **OCP**：阶段清单显式可读，未来新增真正的发布事实时增加一个叶子阶段和稳定摘要字段，不修改
  Host 或插件测试内部实现。
- **LSP**：既有非发布脚本的参数、退出语义和摘要保持不变；外层 G14 只消费其结果，不把
  `releaseGate=false` 的阶段事实改写成历史上已经发布。
- **ISP**：正式入口没有上传、标签、外部账号、AIFLOW 或业务网络参数；Core 函数只接收自身职责所需数据。
- **DIP**：编排器依赖叶子退出码、TRX、Cobertura、JSON 和文件摘要，不依赖测试类、插件内部服务或 Dock 实现。

实现没有引入工作流框架、Pester、反射阶段发现、策略工厂、DI 容器或自定义 MSBuild Task。V1/V2
发布门禁作为历史证据保持原样；V3 使用独立的小型模块，少量结构重复是有意的主版本审计边界。

## 4. 两轮隔离门禁

在 Windows x64、PowerShell 7 和 `global.json` 指定 SDK 上，从干净提交执行：

```powershell
.\scripts\Invoke-HostV3ReleaseGate.ps1
```

入口使用两个 `git clone --no-hardlinks` 克隆，分别隔离 NuGet、TEMP、Host 数据根和构建目录。
每轮固定执行：

1. G14 V3 门禁 Core 单元测试；
2. 文档门禁 Core 单元测试；
3. 解决方案锁定还原；
4. Release CI `-warnaserror` 构建；
5. V3 唯一生产面、Host/SDK/四插件完整回归、覆盖率、API/包、诊断与文档门禁；
6. V3 API 变异和 Core/UI 独立 nupkg 消费；
7. MyPlugTest、DaTang、MySmallTools、BiliDownloader 最终专项验收；
8. MySmallTools 20 轮本地真实媒体资源归零 Harness；
9. Windows 真实窗口打开、关闭、退出和 `layout-v2.json` 保存。

两轮比较只忽略时间、耗时和绝对路径。测试数、覆盖率、API、文档、四插件专项摘要、ZIP/manifest、
V2 线格式和 Smoke 必须完全相等。只有两轮成功且实体证据复核通过，顶层才写出：

```text
passed=true
repeatabilityVerified=true
releaseEligible=true
publishable=true
published=false
uploaded=false
tagCreated=false
aiflow=false
```

`publishable=true` 只表示本地证据满足发布资格；后三个 false 明确证明没有执行外部发布动作。

## 5. Core 单元测试与失败矩阵

`Test-HostV3ReleaseGateCore.ps1` 使用系统 Temp 中的最小夹具验证：

| 失败点 | 预期行为 |
| --- | --- |
| 测试数、覆盖率、API、文档或阶段状态漂移 | 报告首个精确 JSON 路径 |
| 四插件专项摘要、20 轮 Harness 或资源归零漂移 | 两轮比较或实体断言失败 |
| 缺少 transcript、TRX、Cobertura、摘要、ZIP、manifest 或 Smoke | 明确指出缺失相对路径 |
| manifest 不是 schema 2 / 插件 3.0.0 / SDK `[3.0.0,4.0.0)` | 发布证据失败 |
| ZIP/manifest 长度或 SHA-256 与摘要不同 | 重新计算后失败 |
| Smoke 不是隔离 `layout-v2.json` / schema 2 | 发布证据失败 |
| 任一阶段失败 | 写出失败阶段并立即停止，后续阶段不执行 |
| 清理目标越界 | 删除前拒绝；不接受通配符或未验证路径 |
| 只读 NuGet 文件或临时清理失败 | 有限重试；清理警告不覆盖原始发布结论 |

## 6. 封板测试与制品事实

最终生产面回归为 Host Unit **189**、Headless UI **62**、Plugin/Dock **204**，Host 合计 **455**；
Plugin SDK **37**、MyPlugTest **11**、DaTang **62**、MySmallTools **192**、BiliDownloader **728**，
连同最终 ZIP Loader/Registry/Workspace **8** 项，生产面共 **1493/1493**、零失败、零跳过。
Host 行覆盖率 **84.39%**、分支覆盖率 **70.58%**，没有降低 G0 下限。

四个最终专项入口分别通过 **504/504**、**555/555**、**685/685**、**1222/1222**；MySmallTools
覆盖率为 **72.96% / 49.02%**，BiliDownloader 覆盖率为 **83.87% / 67.79%**。真实媒体 Harness
为 20 轮且最终原生资源、关闭 Document/View 和
加密流弱引用全部归零。为避免共享 Dispatcher、生命周期诊断和媒体资源的并行调度污染两轮证据，
三个相关测试程序集在测试边界串行采集；另以 6 个快捷键路由用例覆盖可执行、不可执行、未知键和
空参数分支，并补齐取消/完成收敛与缓存命中用例；这些调整只增强测试和证据确定性，不改变 public API。

四插件包矩阵均为 `3.0.0-win-x64`、manifest schema 2、SDK `[3.0.0,4.0.0)`，每个插件完成两次
确定性构建并从最终 ZIP 通过真实 Host Loader：

| 插件 | 文件数 | ZIP 字节数 / SHA-256 | manifest 字节数 / SHA-256 |
| --- | ---: | --- | --- |
| BiliDownloader | 14 | `2495143` / `6DF9E3B3FAF7B36CAE6A9D484634EE9A83269C62BF80817E9156F925171AFD3F` | `3280` / `8A468B268114385A3CD68FE4325E52AF049D4C244534BBA236D0FD983E15DFDC` |
| DaTangAccountingHelpPlug | 9 | `2398295` / `7ED03C1C761E5AED51612FD893B36F4648B5CBDDE84FE375191365E03172D20F` | `2364` / `A84A4F0F633FCEFF8D0A1202B6E169C40A743234FEF4671665F349D70447C936` |
| MyPlugTest | 11 | `2390874` / `CFB3D3B3CC83456B6FF591684D939E2C2B8EF7F9F6655F793B147B1120A21070` | `2648` / `08FB041EF087AF392EC0F29F0A67451AFFED2A66F6332AA906E61AC6C53A95A1` |
| MySmallTools | 431 | `48982470` / `1B105A5EF63A395D15263596D5EBB829F01EEE538DCC7C314AA26E6856C837F0` | `96172` / `6C49C43E2D4B485F3427F4C74312F9723573C30900E2B41C75F647B24E51A24C` |

本次门禁在一次性本地审计仓库的干净提交
`5de82ce4ba3c9c41de9f7b85053f32c7914dc14d`（tree
`02457ba0461843d5f6500e88a407b6820d8d117b`）上执行，机器证据已复制回
`artifacts/release-gate/v3/20260823-033330-5de82ce4ba3c/`。顶层 `summary.json` 记录两轮通过、
`repeatabilityVerified=true`、`releaseEligible=true`、`publishable=true`；两个 pass 目录保留 transcript、
阶段状态、TRX、Cobertura、文档/专项摘要、包、外置 manifest 和 Smoke 摘要。人工文档记录的是本次
审计投影，后续重跑以新目录中的摘要和真实文件复算结果为准，不把上述哈希作为永久白名单。

## 7. 数据、文档与发布边界

- manifest、Document envelope、layout 继续使用 schema 2，布局文件仍为 `layout-v2.json`，默认数据根仍为 `v2`；
- V3 可以读取、保存既有 V2 线格式用户文件，但不恢复 V2 SDK、旧 public API、双 Loader 或 fallback；
- 根 README、文档导航、架构、兼容约束、快速开始、SDK README、Document 设计和测试说明统一为 G14；
- G0–G13 专项记录保留当时的 `releaseGate=false` / `publishable=false`，因为它们确实是非发布阶段证据；
- 本阶段没有运行历史 ReleaseAcceptance、真实账号、真实 Bilibili、上传、标签或外部发布。

## 8. 回滚

G14 的回滚单位是 V3 API Shipped 分类、V3 发布门禁、政策测试、文档门禁和当前文档。可以整体回到
G13 的未发布候选状态，但回滚后不得继续宣称 V3 已封板，也不能恢复 V2 API、Host EventBus、owner
全屏入口、旧 Dock Locator、伪插件分支或隐藏 fallback。已经由 V3 保存且线格式仍为 v2 的用户文件
不得删除、降级或搬迁。
