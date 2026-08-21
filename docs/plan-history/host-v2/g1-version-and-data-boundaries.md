# Managed Plugin V2 G1：版本与数据边界

> 状态：已完成
>
> 完成日期：2026-08-21
>
> 分支：`dev-重构-2026年8月18日`
>
> 前置基线提交：`372098d`
>
> 所属任务：[Managed Plugin V2 破坏式架构重构任务书](../../design/host-v2-breaking-refactor-plan.md#g1建立-v2-版本与数据边界已完成)

## 1. 结果摘要

G1 已把产品、Plugin SDK 和四个 Managed Plugin 切换到 `2.0.0`，Host 与 SDK 程序集版本为
`2.0.0.0`，并删除独立 Host API 版本事实。四插件在 G3 前仍需生成 V1 清单，但两组兼容字段只
投影同一个 SDK `[2.0.0, 3.0.0)` 区间，不再拥有两套可漂移的数字。

宿主默认数据根切换为：

```text
%LOCALAPPDATA%\MyAvaloniaManagement\v2\
```

`MYAVALONIA_DATA_DIRECTORY` 仍表示完整根目录，不追加产品名或 `v2`。旧 `v1` 根不读取、不迁移、
不改写、不删除。G1 同时集中声明 manifest、Document envelope、layout 的目标 schema `2` 和目标
`layout-v2.json`，但没有把 V1 字段形状仅改一个版本号后冒充 V2。

## 2. 版本与格式所有权

| 事实 | G1 当前值 | 所有者与阶段边界 |
| --- | --- | --- |
| 产品版本 | `2.0.0` | Host 发布身份；映射 Version、FileVersion、InformationalVersion |
| Host AssemblyVersion | `2.0.0.0` | 跟随产品；V2 不再提供独立 Host API 版本线 |
| Plugin SDK | `2.0.0` / `2.0.0.0` | 独立包与程序集身份；最终 Core/UI public API 由 G2 重建 |
| 四插件版本 | 均为 `2.0.0` | 每插件仍只维护一个 `PluginVersion` |
| 目标 manifest schema | `2` | G3 建立最终五字段格式；当前 reader 仍严格为 V1 |
| 目标 Document schema | `2` | G7 建立嵌套 JsonElement 内容；当前 reader 仍严格为 V1 |
| 目标 layout schema/文件 | `2` / `layout-v2.json` | G8 建立最终布局；当前运行文件仍为 `layout-v1.json` |
| 默认数据根 | `v2` | G1 已生效；与各磁盘 schema 独立演进 |

## 3. API 未发布边界

`ApiCompatibility/v1` 保持历史 Shipped 243 条、Unshipped 0 条。活动目录切换到 v2 后，Shipped
有意为空，当前 243 条 V1 形状签名全部登记到 Unshipped。这一安排同时满足三件事：

- 普通构建继续由 PublicApiAnalyzers 检查，不能意外扩大或收窄 public 面；
- 没有把尚待 G2 删除和重建的类型声明成已发布 V2 承诺；
- V1 历史基线无需改写，仍可复核从 v1 到最终 V2 的差异。

G1 不发布 NuGet，不建立 V2 外部消费者承诺。G2 必须在同一整改包中完成 Core/UI 依赖方向、最终
public API、编译正反例与文本基线，不能把当前 Unshipped 当成最终设计。

## 4. SOLID 与朴素设计取舍

- **SRP**：根级属性只保存版本和目标格式事实；`HostDataRootPolicy` 只计算路径；各 Store 继续只负责读写。
- **OCP**：未来格式由其所属 G 阶段一次替换 reader，不在当前 V1 DTO 中预埋无语义字段或探测分支。
- **LSP**：G1 不修改现有 SDK 接口和运行语义；阶段内四插件仍可按同一契约替换和完整回归。
- **ISP**：没有为版本或单一路径政策增加接口、工厂、事件或通用配置对象。
- **DIP**：项目文件从集中 SDK 属性投影兼容区间，插件不再复制 Host/Common 两套数字。

实现只使用根级 MSBuild 属性、一个纯路径 Policy、现有 API 文本和可读政策测试。没有引入策略模式、
兼容适配器、双 reader 或服务定位器。中文注释只解释所有权和阶段原因，不逐行复述赋值。

## 5. 数据隔离与失败语义

未配置覆盖时，Policy 规范化 `<LocalAppData>/MyAvaloniaManagement/v2`；显式覆盖直接规范化为绝对
完整根。Policy 不创建目录、枚举旧文件或决定 schema。测试在临时目录放置 V1 外观、布局和诊断
哨兵，再通过 V2 根执行加载、保存和诊断创建，证明新数据只进入 V2 且三类 V1 文件字节不变。

G1 的“拒绝 V1”只指默认发现边界。显式选择 `.mamdoc` 后的格式拒绝由 G7 负责；manifest v1 与
`layout-v1.json` 的格式级拒绝分别由 G3、G8 负责。这样不会在同一个 `schemaVersion=2` 下产生先后
两种线格式，也不会在 G1 提前吞并后续整改包。

## 6. 非发布验证

门禁串行执行共享 SDK 输出，避免多个 `dotnet test` 同时写 `obj` 造成文件锁假失败。执行入口为：

```powershell
dotnet restore .\MyAvaloniaManagement.sln --locked-mode --nologo
dotnet build .\MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-restore --nologo -warnaserror
dotnet test .\Host\MyAvaloniaManagement.PluginTests\MyAvaloniaManagement.PluginTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~VersionPolicyTests|FullyQualifiedName~PluginSdkApiBaselinePolicyTests"
dotnet test .\Host\MyAvaloniaManagement.Tests\MyAvaloniaManagement.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~HostDataRootPolicyTests"
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -NoRestore
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v2 -Configuration Release
.\scripts\Test-DocumentationCore.ps1
.\scripts\Test-Documentation.ps1
git diff --check
```

本阶段明确不运行 Windows Smoke、Windows CI、发布总门禁、发布验收、确定性发布 ZIP、真实媒体或
联网验收，也不创建标签、上传或发布产物。具体测试数量与覆盖率取自本轮生成的 TRX/Cobertura，
不作为后续阶段的固定数量门槛。

最终串行结果：

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 | 通过，锁文件无变化 |
| Release `-warnaserror` 全解决方案构建 | 0 警告、0 错误 |
| G1 版本/API 专项 | 8/8 通过 |
| G1 数据根专项 | 5/5 通过 |
| Host Unit / UI / Plugin | 173 / 38 / 151，共 362/362 |
| Host 覆盖率 | 行 81.12%，分支 66.85%，未降低既有门槛 |
| BiliDownloader / DaTang / MySmallTools | 720 / 64 / 183，共 967/967 |
| V2 API 文本与变异门禁 | Shipped 0、Unshipped 243；7 个破坏性负例和兼容新增流程通过 |
| 文档门禁 | 38 份文档、236 个本地链接、76 个脚本路径、40 个项目路径通过 |
| `git diff --check` | 通过 |

## 7. 回滚边界

G1 可以整体回到 G0 的源码和版本事实，但回滚不得删除已经产生的 `v2` 目录，也不得读取、移动、
覆盖或清理用户 `v1` 数据。不能只回滚版本数字而保留不匹配的程序集、插件清单或 API 活动目录；
版本属性、四插件声明、V2 API 过渡基线和数据根政策必须作为一个单元回滚。

## 8. 完成检查表

- [x] 产品、Host、SDK 与四插件版本统一进入 V2；
- [x] 独立 Host API 版本事实删除；
- [x] 双兼容字段只投影单一 SDK 区间；
- [x] 三种目标 schema 与现有 V1 reader 明确分离；
- [x] 默认数据根进入 v2，显式覆盖不追加代际；
- [x] V1 外观、布局和诊断文件保持原样；
- [x] V1 API 历史基线未改写，V2 当前表面全部为 Unshipped；
- [x] 未增加生产 public API、兼容 reader 或无必要抽象；
- [x] 当前文档和文档门禁已同步；
- [x] 未使用 AIFLOW，未执行 Windows/发布门禁或发布操作。
