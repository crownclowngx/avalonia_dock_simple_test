# Managed 插件快速开始

本组文档面向两类读者：在当前仓库内增加插件的开发者，以及为既有宿主版本交付二进制插件的外部作者。主路径只介绍 **Managed Plugin**，目标是在约 10 分钟内让一个同时包含 Document 和 Tool 的最小插件被宿主发现并显示。

> 迁移提示：当前仓库已完成 V2 G5，Host 只接受最终 UI SDK `IPluginModule` 和
> `IPluginRegistration`。本组后续页面仍保存 G4 时期的 Legacy 示例，用于历史对照；这些模块会被 G5
> Host 拒绝，不能作为可运行教程。最终普通模型、Dock Adapter 和业务插件示例将在 G6/G9 后同步；
> 在此之前不要据此创建或发布 V2 插件。

## 完成后你将得到什么

- 一个具有稳定 `PluginId` 和严格 `plugin.manifest.json` 的插件程序集；
- 一个每次打开都会创建独立实例的 Document；
- 一个在宿主内保持单例、关闭后进入隐藏状态的 Tool；
- 一套可以定位清单、依赖、模块预检、显式贡献和扩展激活问题的验证方法。

当前不要直接复制后续 Legacy 片段；`MyPlugTest` 也要到 G9 才迁移为最终 V2 示例。

## 前置条件

- 使用 Windows 和 PowerShell 执行本文命令；
- 安装仓库 [`global.json`](../../global.json) 指定的 .NET SDK `10.0.302` 或满足其 `latestPatch` 规则的补丁版本；
- 能够构建 `Host/MyAvaloniaManagement`；
- 为插件选定一个不会变化的英文短名，例如 `quick-start`，并据此建立稳定 ID。

先在仓库根目录确认 SDK：

```powershell
dotnet --version
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
```

## 先判断能力放在哪里

| 能力 | 适合的场景 | 生命周期 | 最小教程是否包含 |
| --- | --- | --- | --- |
| Document | 可同时打开多个、具有各自输入和状态的工作会话 | 每个标签一个 DI Scope，关闭后由宿主释放 | 是 |
| Tool | 导航、状态、辅助操作等宿主级面板 | 宿主级单例，关闭表示隐藏，恢复仍是同一实例 | 是 |
| 普通服务 | 被 Document、Tool 或其他插件服务复用的业务能力 | 按状态所有权选择 Scoped、Transient 或 Singleton | 按需 |
| `IPluginLifecycle` | 插件级后台任务确实需要随宿主启动和停止 | 初始化幂等，退出前等待后台工作结束 | 否，按需扩展 |

如果拿不准，应先把“可独立打开的工作”放进 Document，把“持续可见的辅助状态”放进 Tool。不要为了执行一次普通业务操作而注册插件生命周期。

## 两种接入路径

### 仓库内开发

当前 Host 的仓库内插件暂时引用不可打包的
[`MyAvaloniaManagement.LegacyPluginContracts`](../../Host/MyAvaloniaManagement.LegacyPluginContracts/MyAvaloniaManagement.LegacyPluginContracts.csproj)，
构建后把入口程序集、清单和私有依赖部署到宿主输出目录下独立的 `Controls/<PluginFolder>/`。这是本文在 G3
可完整复现的运行路径，新生产项目不得继续增加 Legacy 引用。

### 外部二进制插件

G3 已能生成同版本 `MyAvaloniaManagement.PluginSdk` 与 `MyAvaloniaManagement.PluginSdk.UI` nupkg；UI
包提供 Avalonia、DI.Abstractions、Fluent/Semi/Ursa 支持，不提供 Dock。两个包当前用于契约编译与消费
门禁；manifest v2 Host 仍处于未发布分支。打包时只交付插件入口、清单和插件私有依赖，**不得**把
`MyAvaloniaManagementCommon.dll` 或宿主共享依赖闭包放进插件目录。

外部插件必须针对明确的 Host/SDK 版本组合编译和验证，兼容范围由清单如实声明。使用 G5 前候选
接口编译的插件不兼容最终 v1，必须重新编译并显式登记贡献。

## 推荐阅读顺序

1. [创建 Managed 插件](./create-managed-plugin.md)：建立项目、清单、模块和部署目录。
2. [添加 Document 与 Tool](./add-document-and-tool.md)：加入两个可见扩展并理解生命周期。
3. [验证与排错](./verification-and-troubleshooting.md)：检查加载结果、测试、日志和常见错误码。

需要了解完整边界时，再阅读：

- [宿主—插件架构评审](../design/host-plugin-architecture-review.md)
- [主项目兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)
- [主项目内部架构](../../Host/MyAvaloniaManagement/docs/design/architecture.md)
- [G5 显式贡献与 Plugin Registry](../plan-history/host-v1/g5-explicit-contributions-and-plugin-registry.md)

上述契约文档和实际 public 类型是详细规则的事实来源；Quick Start 只保留完成首次接入所需的最短路径。
