# G13：Plugin SDK API 兼容基线

> 完成日期：2026-08-20
> 状态：已完成
> 适用范围：`MyAvaloniaManagement.PluginSdk` / `MyAvaloniaManagementCommon`
> 验收入口：`scripts/Test-PluginSdkCompatibility.ps1 -Baseline v1`

## 1. 结果

G13 已用可审阅文本替换 G2/G11 阶段的 Common SHA256。正式 v1 基线包含 **243 条**、启用
nullable 且按 Ordinal 排序的 Shipped 签名；Unshipped 当前为 0 条。基础 SDK 普通 build 即运行
Microsoft Public API Analyzer，新增、删除、重复或 nullable 不完整不能等到发布时才发现。

本次没有增加、删除或修改任何运行时 public API。SDK PackageVersion 仍为 `1.0.0`，AssemblyVersion
仍为 `1.0.0.0`，manifest schema、Document 信封、布局和插件业务内容 schema 均未改变。
Host 实现程序集和 dependency-only UI Profile 不进入基础 SDK 文本基线。

## 2. 基线和审阅模型

活动基线由 `Directory.Version.props` 的 `MyAvaloniaPluginSdkApiBaseline=v1` 选择，并由政策测试和
专项脚本同时验证它与包版本、程序集版本主版本一致。

- `PublicAPI.Shipped.txt` 保存 G11 收口后的完整 v1 承诺；
- `PublicAPI.Unshipped.txt` 只接受同一主版本内经过评审的兼容新增；
- 未登记新增产生 `RS0016`，登记后才通过；
- 删除或改签名产生 `RS0017`，不能通过编辑旧 Shipped 或登记 `*REMOVED*` 消除；
- 新主版本必须建立新的 vN 目录，并同步版本、四插件清单兼容区间、迁移说明和消费证据。

长期操作和 AI 阅读入口见
[Plugin SDK API 兼容基线维护指南](../../reference/plugin-sdk-api-compatibility.md)。

## 3. 专项变异门禁

`Test-PluginSdkCompatibility.ps1` 先锁定还原并构建真实 SDK，再把根级版本/包配置和完整 SDK 项目
复制到本轮系统 Temp GUID 目录。所有测试变异只发生在副本中，字符串替换必须恰好命中一次；脚本
结束时只删除该 GUID 子目录。

| 测试副本变异 | 预期与实际结果 |
| --- | --- |
| 删除 `DocumentLoadException` public 类型 | `RS0017` 阻断并打印类型 |
| 删除 `CreationIntentId.Parse(string)` | `RS0017` 阻断并打印成员和参数 |
| 将 `ToolTypeIdSystemTextJsonConverter` 收窄为 internal | `RS0017` 阻断并打印类型 |
| 把 `CreationIntentId.Parse` 参数名由 `value` 改为 `text` | 旧签名 `RS0017`，证明参数名也是可审阅契约 |
| 把 `CreationIntentId.Parse` 参数改为 `object` | 旧签名 `RS0017`，新签名不被误认为兼容替代 |
| 为 `CreationIntentId.Parse` 增加第二个参数 | 旧签名 `RS0017`，参数数量变化被阻断 |
| 把 `CreationIntentId.Parse` 返回类型改为 `object` | 旧返回签名 `RS0017` |
| 新增 `G13CompatibilityProbe` 但不登记 | `RS0016` 阻断并打印成员 |
| 把同一新增登记到测试副本 Unshipped | 0 警告、0 错误 |

专项脚本最终输出：Shipped 243、Unshipped 0，七个破坏性负例与一组兼容新增审阅流程全部通过。

## 4. SOLID 与朴素设计取舍

- **SRP**：Roslyn Analyzer 比较编译符号，API 文本保存契约事实，PowerShell 编排真实/变异验证，
  xUnit 只检查版本和仓库政策。
- **OCP**：v1 兼容新增只登记 Unshipped；未来 v2 新建目录，不修改 v1 历史事实。
- **ISP**：Analyzer 和 API 文件只属于基础 SDK；Host 与 UI Profile 不承担无关构建协议。
- **DIP**：兼容判断依赖微软维护的符号分析器，不继续扩写自制反射签名和哈希算法。
- **LSP**：接口实现者和既有插件依赖的类型、成员、参数与返回契约不能在同一主版本中收窄。

实现没有自定义 MSBuild Task、兼容 Facade、反射框架、策略工厂或额外运行时服务。一个集中属性、
两份文本、一个专项脚本和少量政策测试足以表达当前边界。

## 5. 单元测试与依赖门禁

- 删除 `PublicApiContractTests` 中的 SHA256、反射格式化和对应测试，保留事件总线不泄漏第三方消息器
  等行为语义断言；
- 新增基线政策测试，保护主版本对应关系、文件存在、nullable、Ordinal 排序、无重复、无
  `*REMOVED*`、分析器版本和 `require_api_files`；
- 扩展 SDK 依赖边界测试，区分四个运行时依赖与一个 private analyzer，证明 Host/UI Profile
  没有接入基础 SDK API 文件；
- SDK 包门禁确认 `Microsoft.CodeAnalysis.PublicApiAnalyzers` 不进入 nuspec，正向最小消费者和
  G5/G8/G9/G11 旧契约反例继续通过。

## 6. 验收证据

2026-08-20 执行：

| 门禁 | 结果 |
| --- | --- |
| `dotnet restore MyAvaloniaManagement.sln --locked-mode -p:SkipPluginDeploy=true --nologo` | 通过 |
| Release 解决方案 `-warnaserror` 构建 | 0 警告、0 错误 |
| `Test-PluginSdkCompatibility.ps1 -Baseline v1` | 243 条 Shipped；7 个破坏性负例和兼容新增登记正反例通过 |
| `Test-PluginSdkPackage.ps1 -Configuration Release` | 基础/UI 包、依赖图、正向消费者和 G5/G8/G9/G11 反例通过；Analyzer 未进入 nuspec |
| Host 综合门禁（含 Windows Smoke） | Unit 167、UI 38、Plugin 149，共 354/354；Smoke 通过 |
| Host 覆盖率 | 行 80.62%，分支 65.91% |
| `Test-ManagedPluginPackages.ps1 -Configuration Release` | 4 个独立插件各构建 2 次；16 个协议负例；最终 ZIP Host 加载通过 |

四个最终 ZIP 分别包含 BiliDownloader 14、DaTang 9、MyPlugTest 11、MySmallTools 431 个文件，
两次构建摘要一致。G13 没有沿用 G12 的旧 ZIP 哈希；本次机器可读结果保存在
`artifacts/test-results/ManagedPluginPackages/summary.json`。

## 7. 失败复跑记录

门禁实现阶段出现三组有效反馈：

1. Analyzer 生成了完整签名，但默认输出顺序不是严格 Ordinal。首轮脚本因此主动失败；基线机械排序后
   重新构建仍为 0 警告、0 错误。
2. 空 Unshipped 使初版排序辅助函数和 `Compare-Object` 收到空集合/null。脚本补充空集合语义后，
   “没有待发布新增”成为正式受支持状态。
3. 删除成员负例需要把替换文本设为空；初版参数绑定拒绝空字符串。辅助函数显式允许空替换后，
   删除成员负例按预期打印 `CreationIntentId.Parse`。

这些修正没有放宽 API 诊断，也没有手工跳过失败案例；最终完整专项脚本和全部发布门禁均重新执行。

## 8. 回滚与后续边界

若 Analyzer 本身导致构建环境问题，可以回滚本次构建依赖和脚本，但不能在没有等价成员级兼容门禁时
继续宣称 G13 或 v1 封板通过。API 文本属于已经建立的 v1 契约事实，不应随工具回滚而删除。

G14 负责把 G13、G12 和宿主门禁接入同一 Windows CI；G15/G16 分别处理诊断脱敏和最终封板文档。
G13 不创建发布标签、不推送 NuGet，也不改变外部安装或更新模型。
