# G10：DaTangAccountingHelpPlug 迁移至 Host V2

> 完成日期：2026-08-21<br>
> 状态：已完成<br>
> 阶段性质：开发期非发布迁移；`publishable=false`

## 1. 目标与边界

G10 将 DaTangAccountingHelpPlug 的发票信息导入和银行余额调节两个 Document 完整迁移到最终
Core/UI SDK、插件私有 Provider、声明式贡献与 Host Dock Adapter。计算、匹配、Excel 读写和业务 DTO
保持原有语义；本阶段不读取旧 Document 内容、不迁移两个历史 GUID，也不提供 Legacy 兼容入口。

MyPlugTest 与 DaTangAccountingHelpPlug 现在均由 V2 Host 真实加载；MySmallTools 和 BiliDownloader
仍由不可打包的阶段桥保持源码回归，分别等待 G11 和 G12。本轮未使用 AIFLOW，也未执行 Windows CI、
Windows Smoke、ReleaseAcceptance 或正式发布门禁。

## 2. SOLID 与朴素设计取舍

| 原则 | G10 落点 |
| --- | --- |
| 单一职责 | 模块只组合服务和贡献；窗口端口只封装原生窗口能力；Codec 只做内容编解码；Document 只编排当前 Scope 状态 |
| 开闭原则 | 复用最终 `AddDocument`、`AddPersistableDocument`、Activator 和 Adapter 链；Host 不按 DaTang 类型增加分支 |
| 里氏替换 | 发票模型完整遵守 `IPluginDocument`；只有银行模型增加 `IPersistablePluginDocument` 的捕获、恢复和提交语义 |
| 接口隔离 | 发票、银行文件和剪贴板分别依赖窄插件接口；共同实现细节才下沉到一个适配器，不把无关方法推给 ViewModel |
| 依赖倒置 | 插件只依赖 Core/UI SDK 的 `IDocumentLifetime` 与 `IPluginWindowInteraction`；不寻找主窗口、不引用 Host、Dock 或旧 Scope 工厂 |

实现只使用构造注入、Host Port/Adapter 和严格 Codec 三种直接结构。没有为两个页面建立抽象工厂、
策略继承树、消息基类或额外生命周期框架。窗口适配器虽然实现三个用途接口，但没有业务状态，
因此在插件 Provider 内保持 singleton；Document 及其当前标签页状态、运行服务和子模型保持 scoped。

## 3. 声明式贡献与依赖边界

`DaTangAccountingHelpPluginModule.Configure` 是贡献的唯一事实源。注册 API 自动把顶层 Document 模型
注册为 scoped，模块不重复注册模型，也不单独注册 View。

| 类型 | 稳定 ID | 模型 / View | 生命周期 |
| --- | --- | --- | --- |
| Document | `myavalonia.plugin.datang-accounting-help.document.invoice-info-import` | `InvoiceInfoImportViewModel` / `InvoiceInfoImportView` | 每实例 Scope；不可持久化 |
| Persistable Document | `myavalonia.plugin.datang-accounting-help.document.bank-balance-reconciliation` | `BankBalanceReconciliationViewModel` / `BankBalanceReconciliationView` | 每实例 Scope；内容 schema 1 |

插件显式引用 Core/UI SDK 且均为 `Private=false`，入口探针使用
`ManagedPluginUseV2EntryContract=true`。生产程序集不再引用 Legacy、Dock、Newtonsoft、旧 Strategy、
旧保存接口、旧 Scope 工厂或 Host 实现；两个 Legacy GUID 已随旧身份常量删除。

## 4. 受控窗口交互端口

UI SDK 新增 `IPluginWindowInteraction`，只提供打开文件、选择保存路径和尝试写入剪贴板三个操作。
Host 的 internal Avalonia 实现按调用时的当前主窗口取用 `StorageProvider` 或剪贴板，并把同一个实例注入
每个插件私有 Provider。端口只返回本地路径、`null` 或布尔结果，不暴露 `Window`、`TopLevel`、
`IStorageProvider`、剪贴板对象或 Host 实现。

调用必须发生在 Avalonia UI 线程，null 参数立即抛出参数异常。原生选择器无法可靠强制关闭，
所以 Host 在调用前和原生窗口返回后检查取消令牌；DaTang 子模型又把命令令牌与当前
`IDocumentLifetime.ClosingToken` 联合观察。这样关闭期间到达的路径、报告输出或剪贴板结果会被丢弃，
不会越过已关闭 Document 的所有权边界。

## 5. 银行对账内容 schema

内容 schema 固定为 `1`，payload 是大小写敏感的 camelCase 对象，根字段必须恰好为：

```text
configuration, selectedProfileId,
bankStatementPath, enterpriseLedgerPath, bankLedgerPath,
asOfDate, includeMatchedItems, includeUnmatchedItems,
previousUnreconciledDifference, lastOutputPath
```

独立 `ReconciliationDocumentContentCodec` 使用 System.Text.Json 原生 `JsonElement`，拒绝错误 schema、
非对象根、未知/重复/缺失根字段、错误字段类型、无效枚举和无效配置。配置继续复用既有业务 DTO，
并由 `ReconciliationProfileLoader.Validate` 执行业务不变量校验；匹配、读取和报告领域模型没有重构。

恢复先把全部内容解码并验证为临时状态，然后才一次提交给父模型。任一错误都保留原标题、配置、
路径、选项、输出路径和原脏状态。`CaptureContentAsync` 只捕获克隆内容，不改变标题或脏状态；只有 Host
完成原子保存后调用 `AcceptChanges` 才清除脏状态。

## 6. 所有权、取消与失败矩阵

关闭顺序固定为：Host 先取消 `ClosingToken`；迟到文件结果停止提交；运行任务协作退出；父模型用具名
处理器解除对子模型持久字段的订阅；子模型和 Document Scope 最终释放。具名订阅避免匿名委托形成
隐含所有权，重复 Dispose 保持幂等。

| 失败点 | 对外结果 | 不得发生 |
| --- | --- | --- |
| 插件配置、Provider 或 Descriptor 组合失败 | 整个 DaTang 候选不进入 Registry | 发布一个成功、一个失败的半成品贡献 |
| Document 初始化或 View 创建失败 | 临时模型、View 和 Scope 释放 | 发布无 DataContext 的 Adapter |
| 原生选择取消或主窗口缺失 | 空集合或 `null` | ViewModel 搜索全局窗口 |
| 剪贴板不可用 | 返回 `false`，业务模型保持可用 | 向插件泄漏剪贴板实现 |
| Document 关闭期间选择器迟到 | 联合令牌阻止路径提交 | 已释放 Scope 被重新写入 |
| payload 结构或配置无效 | 恢复失败且原模型完全不变 | 部分覆盖或清除原脏状态 |
| Host 保存失败 | 保持脏状态 | 插件提前调用 `AcceptChanges` |
| ZIP 携带共享程序集 | 静态包边界门禁失败 | 以加载顺序掩盖类型身份冲突 |

## 7. 自动化测试与非发布门禁证据

专项入口：

```powershell
.\scripts\Test-DaTangAccountingHelpPlugV2.ps1 -Configuration Release
```

专项实际通过 **151/151**：Plugin 60、Headless UI 15、Plugin SDK 13、DaTang 业务 62、最终 ZIP
真实加载 1。两次隔离构建均为 9 个文件，排序清单、长度、逐文件摘要和归档 SHA-256 完全一致；
首份归档 SHA-256 为
`ED5E5297024B0AEBEDD2EAF9560B9DCEA2676DAC0EC0B1841DA49F029165D3FD`。解压后通过真实
`PluginLoadContext`、模块预检、私有 Provider 组合与 Registry 验证，形成 2 Document、0 Tool。

覆盖内容包括两个 Descriptor、真实 Provider/Registry/Activator/Adapter 组合、多 Scope 状态与关闭令牌
隔离、两个生产 View 和子 View 的 DataContext/关键绑定、端口注入、文件取消与迟到结果、剪贴板边界、
payload 往返/克隆/脏状态/提交点、严格负例和原子恢复，以及生产程序集和 ZIP 的共享依赖扫描。
原有两个依赖旧注册上下文的 Scope 测试已删除，其所有权职责由真实 Host 组合测试覆盖；DaTang 独立
业务套件实际为 **62/62**。

机器摘要位于 `artifacts/test-results/DaTangAccountingHelpPlugV2/summary.json`，固定记录：

```json
{
  "aiflow": false,
  "windowsCi": false,
  "windowsSmoke": false,
  "releaseAcceptance": false,
  "releaseGate": false,
  "publishable": false
}
```

完整非发布回归实际通过：locked restore；Release `-warnaserror` 全解决方案零警告构建；Host Unit 172、
UI 50、Plugin 205，共 **427/427**；Host 行覆盖率 **83.15%**、分支覆盖率 **68.74%**，脚本内既有
总量、分支和关键文件覆盖率下限均满足；SDK 单元 **33/33**，Core/UI API v2 与真实 nupkg 消费门禁；
BiliDownloader **719/719**、DaTangAccountingHelpPlug **62/62**、MySmallTools **183/183**。

四插件两轮非发布包矩阵已通过：BiliDownloader 14、DaTang 9、MyPlugTest 11、
MySmallTools 431 个文件，各自两轮的清单与摘要一致。文档核心/完整门禁与
`git diff --check` 作为最后静态验收。上述测试 ZIP 只用于加载验证，不进行签名、上传、标签或发布。

## 8. 回滚边界

回滚单位为整个 G10：UI SDK 窗口 Host Port、Host internal 实现与注入、DaTang 生产迁移、测试、专项
脚本和当前事实文档一起回退。G0–G9 不回退；不恢复 DaTang V1 在 V2 Host 中的加载能力，不制作
Legacy 兼容包，也不读取或改写已有用户文件。
