# Managed 插件快速开始

本组文档面向两类读者：在当前仓库内增加插件的开发者，以及为既有宿主版本交付二进制插件的外部作者。主路径只介绍 **Managed Plugin**，目标是在约 10 分钟内让一个同时包含 Document 和 Tool 的最小插件被宿主发现并显示。

> G4 已删除 Legacy 二进制激活。插件必须携带严格清单、入口 `.deps.json` 和唯一
> `IPluginModule`。G5 已删除策略/View 隐式发现和模块自报身份；manifest 是唯一身份来源，
> Document、Tool、View 和 Lifecycle 必须通过 `IPluginRegistrationContext` 显式登记。
> 当前教程对应 `managed-plugin-v1.0.0` 基线：Plugin SDK 为 `1.0.0`，仓库插件的 Host API 与
> Common 兼容区间均为 `[1.0.0, 2.0.0)`。

## 完成后你将得到什么

- 一个具有稳定 `PluginId` 和严格 `plugin.manifest.json` 的插件程序集；
- 一个每次打开都会创建独立实例的 Document；
- 一个在宿主内保持单例、关闭后进入隐藏状态的 Tool；
- 一套可以定位清单、依赖、模块预检、显式贡献和扩展激活问题的验证方法。

最小实现可以直接复制本文档中的片段；完整的生产示例请对照 [`MyPlugTest`](../../Plugins/MyPlugTest/MyPlugTest/)。

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

插件项目直接引用 [`MyAvaloniaManagementCommon`](../../Host/MyAvaloniaManagementCommon/MyAvaloniaManagementCommon.csproj)，构建后把入口程序集、清单和私有依赖部署到宿主输出目录下独立的 `Controls/<PluginFolder>/`。这是本文可完整复现的路径。

### 外部二进制插件

宿主发布方应提供同版本 `MyAvaloniaManagement.PluginSdk` nupkg；需要直接使用 Semi、Ursa 或 Dock
UI 时改用同版本 `MyAvaloniaManagement.PluginSdk.UI`。仓库当前只生成可验证制品，尚未自动推送
公共 NuGet。打包时只交付插件入口、清单和插件私有依赖，**不得**把
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
