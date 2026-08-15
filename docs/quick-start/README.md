# Managed 插件快速开始

本组文档面向两类读者：在当前仓库内增加插件的开发者，以及为既有宿主版本交付二进制插件的外部作者。主路径只介绍 **Managed Plugin**，目标是在约 10 分钟内让一个同时包含 Document 和 Tool 的最小插件被宿主发现并显示。

> 当前代码在 G4 完成前仍保留 Legacy 过渡激活，但 Managed Plugin v1 明确不承诺 Legacy
> 二进制兼容，新插件不得以该路径起步。Managed Plugin 可以在宿主构建根容器前注册依赖，
> Document 和 Tool 策略也可以使用构造注入。

## 完成后你将得到什么

- 一个具有稳定 `PluginId` 和严格 `plugin.manifest.json` 的插件程序集；
- 一个每次打开都会创建独立实例的 Document；
- 一个在宿主内保持单例、关闭后进入隐藏状态的 Tool；
- 一套可以定位清单、依赖、类型发现和扩展激活问题的验证方法。

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

仓库当前没有发布官方插件 SDK/NuGet 包或脚手架。外部作者需要从宿主提供方取得与目标宿主版本匹配的编译契约引用集；打包时只交付插件入口、清单和插件私有依赖，**不得**把 `MyAvaloniaManagementCommon.dll` 或宿主共享依赖闭包放进插件目录。

这表示当前支持的是“针对明确宿主版本编译并交付二进制插件”，不是独立于宿主版本的一键 SDK 开发体验。兼容范围必须由清单如实声明。

## 推荐阅读顺序

1. [创建 Managed 插件](./create-managed-plugin.md)：建立项目、清单、模块和部署目录。
2. [添加 Document 与 Tool](./add-document-and-tool.md)：加入两个可见扩展并理解生命周期。
3. [验证与排错](./verification-and-troubleshooting.md)：检查加载结果、测试、日志和常见错误码。

需要了解完整边界时，再阅读：

- [宿主—插件架构评审](../design/host-plugin-architecture-review.md)
- [主项目兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)
- [主项目内部架构](../../Host/MyAvaloniaManagement/docs/design/architecture.md)

上述契约文档和实际 public 类型是详细规则的事实来源；Quick Start 只保留完成首次接入所需的最短路径。
