# Plugin SDK API 兼容基线维护指南

> `managed-plugin-v1.0.0` 继续定位 SDK `1.0.0` 的历史正式源码基线。V2 G14 已将 Core/UI 的
> `2.0.0` public 表面正式冻结到 v2 Shipped：Core 85 条、UI 46 条，两个 Unshipped 均为空。

## 1. 权威源与程序集边界

当前签名事实由三组只读文本表达：

```text
Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v1/    # v1 历史事实
Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v2/    # V2 Core
Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v2/ # V2 UI
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
都属于破坏既有承诺，不能通过重写基线掩盖。历史 v1 Shipped 保持原样，只用于复核历史事实。

`PublicAPI.Unshipped.txt` 保存当前主版本已经评审、但尚未发布的 public 表面。新增成员时先由
PublicApiAnalyzers 报出 `RS0016`，确认所有权、依赖方向、异常与线程语义后，再按 Ordinal 顺序登记。
未登记删除会产生 `RS0017`，重复项与非法文本也会被门禁拒绝。

G14 的 V2 正式状态是：Core Shipped 85 条、UI Shipped 46 条，两个 Unshipped 均为 0。两份
Shipped 分别描述各自程序集，不能合并，也不能与 v1 的 243 条历史表面要求相等。G2–G13 的
Unshipped 数量仍保留在各阶段记录中，不能用今天的 131 条倒写历史。

## 3. 日常变更流程

仅修改 internal/private 实现时，不应修改 API 文本。新增 public API 时：

1. 先以最小契约表达真实插件用例，并补齐详细中文 XML 文档与设计原因；
2. 确认 `RS0016` 只包含预期新增，且没有 `RS0017`；
3. 核对 Core 不泄漏 Avalonia、DI、Dock、Newtonsoft 或 Host 类型，UI 不泄漏 Dock、Newtonsoft 或 Host 类型；
4. 把签名加入对应项目的 v2 Unshipped，保持 Ordinal 排序且无重复；
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

## 5. 当前兼容与发布门禁

在仓库根目录执行：

```powershell
dotnet restore MyAvaloniaManagement.sln --locked-mode -p:SkipPluginDeploy=true --nologo
dotnet build MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-restore --nologo -warnaserror
dotnet test Host/MyAvaloniaManagement.PluginSdk.Tests/MyAvaloniaManagement.PluginSdk.Tests.csproj -c Release --no-build --no-restore
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v2 -Configuration Release
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -NoRestore
```

`Test-PluginSdkCompatibility.ps1` 分别验证 Core/UI 的版本、排序、重复项与成员级变异；
`Test-PluginSdkPackage.ps1` 从真实 nupkg 验证 DLL/XML/nuspec/精确依赖图、两个正向消费者和旧 API/禁用依赖
反例。以上命令适合日常兼容检查，不运行 Windows Smoke、上传或标签。正式 V2 发布资格还必须在
干净修订上运行：

```powershell
.\scripts\Invoke-HostV2ReleaseGate.ps1
```

该入口在两个隔离克隆中重复执行全量测试、包、API、诊断、文档和真实窗口 V2 Smoke；它不调用
AIFLOW、真实账号或网络，也不会自动上传或创建标签。

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
