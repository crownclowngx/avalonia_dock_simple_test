# Plugin SDK API 兼容基线维护指南

> `managed-plugin-v1.0.0` 继续定位 SDK `1.0.0` 的历史正式源码基线。V2 G14 已将 Core/UI 的
> `2.0.0` public 表面正式冻结到 v2 Shipped：Core 85 条、UI 46 条。V3 G8 已删除 Host 通用事件总线并
> 破坏式收口全屏端口；V3 G9–G12 依次验收四插件最终运行链，G13 又完成旧生产面零残留和真实包负例，
> 这些阶段均未新增 public API。V3 G14 已将最终签名原样移入 Shipped：Core 127 条、UI 45 条，
> 两个 v3 Unshipped 均为空；`3.0.0` 已建立本地发布资格但没有上传或对外发布。
> 当前 v3 Shipped 为 Core 127 条、UI 45 条，这是后续兼容审阅的正式基线。
> Host V4 G8 已签署 Host internal 收口，但没有产生 SDK 4.0.0。Workflow Action G0 已重新签署 Run 与
> Consumer 进度出口，G1 已把兼容新增实现并发布为 Core/UI SDK `3.1.0`：v3 Shipped 仍为 Core 127、
> UI 45，v3 Unshipped 为 Core 72、UI 6；manifest、Document、Layout 和数据根协议不变。结论仍为
> `sdkRoute=3.1-compatible-addition`。
> Workflow Action G2 已进一步证明 Core/UI `3.1.0` nupkg、Templates `1.1.0`、三个生成项目 lock file
> 和外部双 ALC 调用能够闭合；两者现已发布到 NuGet.org。Build 协议不变并继续精确消费 `1.1.2`。
> 本次兼容次版本发布没有改写 G2 已签署的 v3 基线分类，仍为 Shipped 127/45、Unshipped 72/6。

## 1. 权威源与程序集边界

当前签名事实由五组只读文本表达：

```text
Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v1/    # v1 历史事实
Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v2/    # V2 Core
Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v2/ # V2 UI
Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/    # V3 Core 正式事实
Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/ # V3 UI 正式事实
```

每个目录均包含 `PublicAPI.Shipped.txt` 与 `PublicAPI.Unshipped.txt`。Core 包、程序集和根命名空间统一为
`MyAvaloniaManagement.PluginSdk`；UI 包、程序集和根命名空间统一为
`MyAvaloniaManagement.PluginSdk.UI`。Host 可执行程序集不是活动 SDK API 的事实源；
`MyAvaloniaManagement.LegacyPluginContracts` 已在 V2 G13 删除，不再形成可编译程序集。

活动主版本由根级 `Directory.Version.props` 的 `MyAvaloniaPluginSdkApiBaseline` 选择。该值必须与
`MyAvaloniaPluginSdkVersion`、`MyAvaloniaPluginSdkAssemblyVersion` 的主版本一致。Core 与 UI 必须使用
同一个包版本和程序集版本。

## 2. Shipped 与 Unshipped

`PublicAPI.Shipped.txt` 保存已经发布或正式冻结的完整签名。删除、改名、改可见性、改参数或改返回类型
都属于破坏既有承诺，不能通过重写基线掩盖。历史 v1/v2 Shipped 保持原样，只用于复核历史事实。

`PublicAPI.Unshipped.txt` 保存当前主版本已经评审、但尚未发布的 public 表面。新增成员时先由
PublicApiAnalyzers 报出 `RS0016`，确认所有权、依赖方向、异常与线程语义后，再按 Ordinal 顺序登记。
未登记删除会产生 `RS0017`，重复项与非法文本也会被门禁拒绝。

V2 G14 的正式状态是：Core Shipped 85 条、UI Shipped 46 条，两个 Unshipped 均为 0。V3 G1 的
两个 Shipped 均为 0，Core/UI Unshipped 为 85/46；G2 以破坏式替换保存方法并新增值对象后，当前
Core/UI Unshipped 为 101/46；G8–G13 收口为 Unshipped 127/45；G14 的正式状态为 Shipped 127/45、
Unshipped 0/0。两份基线分别描述各自程序集，不能合并，也不能与 v1 的 243 条历史表面要求相等。
各阶段数量只保留在对应记录中，不能用 G14 状态倒写 G1 或 V2 历史。

Workflow Action G1 的当前状态是 v3 Shipped 127/45、Unshipped 72/6。新增含 caller-bound
`IWorkflowActionGateway.CreateRun()`、`IWorkflowActionRun.InvokeAsync` 与 UI 注册扩展；旧 Shipped 没有
改写。内核维护入口为 `scripts/Test-WorkflowActionG1.ps1`；包、模板和外部传播入口为
`scripts/Test-WorkflowActionG2.ps1`。

## 3. 日常变更流程

仅修改 internal/private 实现时，不应修改 API 文本。新增 public API 时：

1. 先以最小契约表达真实插件用例，并补齐详细中文 XML 文档与设计原因；
2. 确认 `RS0016` 只包含预期新增，且没有 `RS0017`；
3. 核对 Core 不泄漏 Avalonia、DI、Dock、Newtonsoft 或 Host 类型，UI 不泄漏 Dock、Newtonsoft 或 Host 类型；
4. 把兼容新增签名加入对应项目的 v3 Unshipped，保持 Ordinal 排序且无重复；不得改写既有 v3 Shipped；
5. 运行 API 变异、真实 nupkg 消费、SDK 单元测试及受影响的 Host/插件测试；
6. 同步契约说明、示例和专项记录。

常见诊断如下：

| 变化 | 常见诊断 | 处理 |
| --- | --- | --- |
| 删除类型或成员 | `RS0017` | 保留契约，或进入新主版本流程 |
| public 改为 internal/private | `RS0017` | 拒绝无迁移的可见性收窄 |
| 修改参数名、类型、顺序、数量或返回类型 | 旧签名 `RS0017`，新签名 `RS0016` | 按破坏性变化处理 |
| 新增 public 成员 | `RS0016` | 完成评审后登记到对应 Unshipped |
| 重复或非法基线 | `RS0025` / `RS0024` | 修正文本文本，不关闭分析器 |
| 缺少 API 文件 | `RS0048` | 恢复对应程序集的活动基线 |

## 4. 禁止的绕过方式

- 不得删除 Shipped 或 Unshipped 条目后声称“基线已更新”。
- 不得使用 `*REMOVED*`、`NoWarn`、降低诊断级别或移除 `require_api_files` 接受破坏。
- 不得把 UI/Host/Legacy 类型塞入 Core，也不得把 Dock 或 Newtonsoft 塞入 UI。
- 不得把 Core 与 UI 合成一份基线，或让 Legacy Common 继续承担活动 SDK 基线职责。
- 不得只提高版本或替换文本，而没有包消费者、反向编译与真实仓库回归证据。

## 5. 当前兼容门禁与发布入口

在仓库根目录执行：

```powershell
dotnet restore MyAvaloniaManagement.sln --locked-mode -p:SkipPluginDeploy=true --nologo
dotnet build MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-restore --nologo -warnaserror
dotnet test Host/MyAvaloniaManagement.PluginSdk.Tests/MyAvaloniaManagement.PluginSdk.Tests.csproj -c Release --no-build --no-restore
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v3 -Configuration Release
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -NoRestore
```

`Test-PluginSdkCompatibility.ps1` 分别验证 Core/UI 的版本、排序、重复项与成员级变异；
`Test-PluginSdkPackage.ps1` 从真实 nupkg 验证 DLL/XML/nuspec/精确依赖图、两个正向消费者和旧 API/禁用依赖
反例。以上命令适合日常兼容检查，不运行 Windows Smoke、上传或标签。

Workflow Action G0 另有一个只建立兼容证据、不授予发布资格的入口：

```powershell
.\scripts\Test-WorkflowActionG0.ps1 -Configuration Release
```

它在固定 Git 输入的临时副本中登记并删除候选 API，生产 v3 API 文本始终保持 127/0、45/0；默认还会
复用 Host、四插件、SDK 包/API 和文档日常门禁。它不调用 AIFLOW、Windows CI/Smoke 或发布入口。

当前 Host V4 G8 正式本地入口为：

```powershell
.\scripts\Invoke-HostV4ReleaseGate.ps1
```

它在两个无硬链接隔离克隆中复用 G7 的完整生产事实、四插件专项、20 轮资源 Harness、API/包和文档，
再执行 Windows Smoke 与实体制品复核，并明确记录未上传、未打标签和 `aiflow=false`。V3 G14 的
`scripts/Invoke-HostV3ReleaseGate.ps1` 与以下 V2 入口只用于复核历史封板证据：

```powershell
.\scripts\Invoke-HostV2ReleaseGate.ps1
```

`Test-HostV3ProductionSurface.ps1` 继续作为不启动窗口的日常聚合入口；历史 ReleaseAcceptance、
外部账号、上传和标签不属于 G14 本地门禁。

## 6. 新主版本与评审清单

确需破坏性变化时，应在一个完整变更单元中说明用例和迁移，建立新的 Core/UI `ApiCompatibility/vN`，
同步包、文件和程序集版本、消费者及兼容区间，并保留旧目录作为历史事实。只新建目录或改版本号不构成
合法升级。

- [ ] 兼容新增分别登记在正确的 Unshipped，排序稳定、无重复、无 `*REMOVED*`；既有 Shipped 不改写。
- [ ] 所有 public 类型和成员具有详细中文 XML 文档，异常、线程和所有权边界明确。
- [ ] Core/UI 依赖白名单与临时 NuGet 正反消费者通过。
- [ ] Legacy 项目保持不可打包、无活动 API 基线且没有新增生产消费者。
- [ ] Analyzer 只作为私有构建依赖，没有进入 nuspec 或消费还原图。
- [ ] API 变异、SDK 单元测试、Host/插件回归与文档门禁有本次执行证据。
- [ ] 仅在真实发布变更中执行 Windows CI、Smoke、发布总门禁、上传和标签。
