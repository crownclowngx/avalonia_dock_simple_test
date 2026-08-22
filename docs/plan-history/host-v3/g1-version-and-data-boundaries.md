# Managed Plugin V3 G1：版本与数据边界签署记录

> 状态：已完成
>
> 验证日期：2026-08-22
>
> 性质：未发布版本线的本地非发布验证
>
> 所属任务：[MyAvaloniaManagement V3 破坏式架构重构任务书](../../design/host-v3-breaking-refactor-plan.md#G1建立-V3-版本与数据边界已完成)

## 1. 结论与阶段边界

G1 已把产品、Host、Core/UI SDK 和 BiliDownloader、DaTangAccountingHelpPlug、MyPlugTest、
MySmallTools 四个插件统一切换到未发布的 `3.0.0` 版本线。程序集版本为 `3.0.0.0`，插件声明的
SDK 兼容区间为 `[3.0.0, 4.0.0)`，活动 API 基线目录为 `ApiCompatibility/v3`。

这只是版本和所有权边界切换，不是磁盘格式升级，也不是发布。当前生产行为仍是 V2 G14 基座：
manifest schema、Document envelope schema 和 layout schema 均保持 2，布局文件仍为
`layout-v2.json`，默认数据根仍为 `MyAvaloniaManagement/v2`。G1 没有复制、迁移、删除、隔离或重写
用户数据，也没有改变任何 C# public API 形状；G2 及后续协议尚未实施。

非发布标志固定为：`aiflow=false`、`windowsCi=false`、`windowsSmoke=false`、
`releaseAcceptance=false`、`releaseGate=false`、`publishable=false`。验证过程没有读取、初始化或调用
AIFLOW，没有设置 `ContinuousIntegrationBuild=true`，没有运行 Windows CI/Smoke、任何 V1/V2/V3
发布门禁、ReleaseAcceptance、签名、上传、标签或发布操作。确定性 ZIP 仅是本地协议验证制品。

## 2. 实际变更与设计思路

版本事实集中在根级 `Directory.Version.props`：产品、Core/UI SDK、FileVersion、AssemblyVersion、
下一主版本和活动 API baseline 均由这里拥有。四个插件只声明自身 `PluginVersion=3.0.0`，SDK 区间继续
由集中属性投影，避免各项目复制版本规则。生成清单继续使用 schema 2，构建诊断中的版本代称改为
“manifest schema 2”或“当前构建协议”，没有改变构建协议行为。

Core/UI 新增 `ApiCompatibility/v3`。V3 Shipped 仅含 nullable 头，V3 Unshipped 分别完整承接 V2
Shipped 的 85/46 条签名；V1/V2 历史基线原样保留。这样既明确当前源码已进入 V3 版本线，又避免在
G14 封板前把未发布 API 错记为已发布。政策测试逐行验证 V3 Unshipped 与 V2 Shipped 完全相同。

兼容夹具把最小 V3 插件改为 `[3.0.0, 4.0.0)`，把拒绝夹具固定为 V2 区间
`[2.0.0, 3.0.0)`。实际 Host 测试证明 V2 插件会在执行入口前被拒绝，其损坏 DLL 不会被加载；合法
V3 插件仍可加载，其他损坏插件只隔离自身。

数据测试在默认 `v2` 根下写入既有 V2 Document envelope 与 `layout-v2.json`，通过现有
Serializer/Store 读取后逐字节比较源文件，并确认没有迁移目录、重写文件或生成隔离备份。显式数据根
覆盖仍解释为完整根路径，不会被附加版本段。

## 3. SOLID 取舍

- **SRP**：根级属性只拥有版本事实，`HostDataRootPolicy` 继续只拥有路径规则，各 Serializer/Store 只负责既有格式读写，不承担升级编排。
- **OCP/LSP**：既有 V2 文件继续由原实现读取；没有增加双 reader、V2/V3 loader、fallback 或会改变替换语义的兼容适配器。
- **ISP/DIP**：没有新增版本接口、配置对象、Facade 或策略；插件项目仅消费集中 SDK 区间，不依赖额外抽象。
- **朴素实现**：本阶段只修改集中事实、基线文本、既有夹具和政策测试，没有为未来 G2–G14 预建框架。

新增和修改的测试注释使用中文解释所有权、兼容原因及阶段边界。构建脚本中的用户可见诊断保持简洁，
只去除容易把产品大版本与 manifest schema 混淆的措辞。

## 4. 实测测试、覆盖率与 API

以下数字来自本工作区的 Release 串行非发布验证，不沿用 G0 历史数字：

| 门禁 | 实际结果 |
| --- | --- |
| 锁定还原 | `dotnet restore --locked-mode` 通过；锁文件只出现本地项目版本 `2.0.0 -> 3.0.0` 的预期变化 |
| 全解决方案构建 | Release、`-warnaserror`、`SkipPluginDeploy=true` 通过；0 警告、0 错误 |
| Host Unit/UI/Plugin | 171 + 53 + 202 = **426/426**；失败 0、跳过 0 |
| Host 覆盖率 | 行 **83.24%**、分支 **68.98%**；既有覆盖率门槛通过 |
| Plugin SDK 单元测试 | **34/34**；失败 0、跳过 0 |
| BiliDownloader | **718/718**；失败 0、跳过 0 |
| DaTangAccountingHelpPlug | **62/62**；失败 0、跳过 0 |
| MySmallTools | **184/184**；失败 0、跳过 0 |
| Core/UI V3 API | Shipped 0/0、Unshipped 85/46；与 V2 Shipped 逐项相同；7 个破坏性负例和 1 组兼容新增流程通过 |
| SDK nupkg 消费 | Core/UI `3.0.0` 包通过 2 个正例和 10 个负例；仅作本地消费验证 |
| Host 兼容和数据边界 | V2 SDK 区间拒绝、最小 V3 接受、损坏插件隔离、V2 Document/layout 原地读取均通过 |
| 诊断脱敏 | 检查 102 个生产 C# 文件；无异常正文、自由技术详情或完整路径泄漏 |
| 文档核心/正式门禁 | 通过；正式门禁检查 56 份文档、323 个本地链接、120 个脚本路径、49 个项目路径 |

## 5. 四插件清单与确定性测试包

四个生成清单均为 `pluginVersion=3.0.0`、manifest schema 2、SDK `[3.0.0, 4.0.0)`。
构建协议的 25 个负例通过，每个插件执行两轮构建，最终 ZIP 内容、RID 白名单和 Host 加载 4/4 通过。

| 插件 | ZIP 文件数 | ZIP SHA-256 | 两轮确定性 |
| --- | ---: | --- | --- |
| BiliDownloader | 14 | `268022BBA4490CE793316D9CCFFCB418193DAEB11386C6F66D7431895C99D2BA` | 是 |
| DaTangAccountingHelpPlug | 9 | `3CBC93B9B7CF6B70AD04F03EFBCA8DC22A3DDD7A7E2B9A4F82B8FD95EF3AB0DB` | 是 |
| MyPlugTest | 11 | `93E1C07063EA3130D4557FD7A4BE9D7E8828911C49AAAD0EED574F51DAC75E5F` | 是 |
| MySmallTools | 431 | `1051B77946DD2FC2A3D21820B7C3F0358FBC967C155FE6954AD2FF9148932783` | 是 |

机器摘要位于 Git 忽略的 `artifacts/test-results/ManagedPluginPackages/summary.json`。这些 ZIP 不可发布，
也不能作为运行发布门禁的证据。

## 6. 验证命令

```powershell
dotnet restore .\MyAvaloniaManagement.sln --locked-mode --nologo
dotnet build .\MyAvaloniaManagement.sln -c Release --no-restore --nologo -warnaserror -p:SkipPluginDeploy=true
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -NoRestore
dotnet test .\Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj -c Release --no-build --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj -c Release --no-build --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug.Tests\DaTangAccountingHelpPlug.Tests.csproj -c Release --no-build --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj -c Release --no-build --no-restore -p:SkipPluginDeploy=true
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v3 -Configuration Release
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
.\scripts\Test-ManagedPluginPackages.ps1 -Configuration Release
.\scripts\Test-HostDiagnosticRedaction.ps1
.\scripts\Test-DocumentationCore.ps1
.\scripts\Test-Documentation.ps1
git diff --check
```

命令清单刻意不包含 Windows Smoke、ReleaseAcceptance 和任何 Release Gate；这些能力留到真正发布阶段。

## 7. 插件影响、数据兼容与整体回滚

四个插件需要与当前 Host/SDK 一起使用：V2 SDK 区间会被 V3 Host 拒绝，V3 插件清单仍遵守 schema 2。
这不是二进制 fallback 策略；G1 不承诺同时加载 V2 与 V3 SDK 插件。

磁盘数据边界与插件 SDK 边界相互独立。升级源码版本不会改变现有 `v2` 数据根和 schema 2 文件，用户
无需迁移数据。回滚必须整体恢复根级版本事实、四插件版本声明、活动 API baseline、锁文件、兼容夹具、
构建诊断和当前文档；不得只回退其中一部分，也不得触碰任何已有 `v2` 用户数据。

G1 完成后，V3 总任务状态为 **G0–G1 已完成，G2–G14 尚未实施**。下一阶段只能在本边界之上开展
G2，不能把尚未实现的协议写成当前能力。
