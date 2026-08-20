# MyAvaloniaManagement

MyAvaloniaManagement 是一个基于 **.NET 10、Avalonia 12 和 Dock 12** 的模块化桌面工作台，也是面向内部可信 Managed Plugin 的插件宿主。宿主提供统一窗口、可停靠工作区、布局持久化、文件打开与保存、依赖注入、插件生命周期和诊断入口；业务能力由独立插件贡献。

> 当前定位是同一团队维护的内部可信插件平台，主要支持 Windows x64。它不是第三方插件市场或安全沙箱，也不支持运行时热卸载；更新插件需要退出宿主、替换文件并重新启动。

> Managed Plugin v1 已完成 G0–G16 封板，源码基线由本地注解标签
> `managed-plugin-v1.0.0` 定位。签署内容、非发布门禁证据和回退边界见
> [G16 文档与 v1 基线](./docs/plan-history/host-v1/g16-documentation-and-v1-baseline.md)。

## 核心扩展模型

| 概念 | 语义 | 典型用途 |
| --- | --- | --- |
| Document | 中央工作区中的多实例工作会话，每个标签拥有独立状态和 DI Scope | 下载方案、视频播放与加解密、发票导入、银行余额调节 |
| Tool | 宿主级单例面板，可以停靠、隐藏和恢复 | 文件树、工具管理、插件状态、下载任务中心 |
| 插件服务 | 不依赖页面可见性的业务能力，由根 DI 和可选插件生命周期管理 | 仓储、下载协调、凭据和媒体运行时 |

宿主当前具备以下基础能力：

- Left、Right、Top、Bottom 四向 Dock 布局，以及经过校验和迁移的布局持久化；
- Document 多开、独立 Scope、关闭取消和资源释放；
- Tool 单例创建、关闭隐藏和状态恢复；
- Managed Plugin 服务注册、可选初始化与反向关闭生命周期；
- 严格 `plugin.manifest.json`、插件目录隔离和私有依赖解析；
- 严格 Document 信封 v1、文件原子保存与恢复备份、插件状态面板和默认脱敏的会话诊断日志。

## 现有插件

| 插件 | 当前职责 |
| --- | --- |
| [BiliDownloader](./Plugins/BiliDownloader/BiliDownloader/doc/reference/PRODUCT.md) | Bilibili 链接和个人内容来源、下载计划、任务调度及媒体处理 |
| [MySmallTools](./Plugins/MySmallTools/MySmallTools/docs/secret-video-player/README.md) | SECVID03 视频播放、媒体库、视频加密和安全解密 |
| [DaTangAccountingHelpPlug](./Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.csproj) | 发票信息综合计算和银行余额调节 |
| [MyPlugTest](./Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj) | Managed Plugin 的 Document、Tool、消息通信和依赖注入示例 |

四个当前插件均使用 Managed Plugin 接入。G4 已删除 Legacy 二进制激活路径；无模块程序集、
缺少入口 `.deps.json` 的目录以及依赖历史加载 Facade 的代码不会进入插件运行链。

## Managed Plugin v1 支持与版本边界

v1 正式支持 Windows x64 上同一进程内的可信 Managed Plugin。插件必须携带严格清单并位于
独立目录；更新时退出宿主、替换插件文件后重新启动。不支持运行时热卸载、恶意代码沙箱、
权限系统、第三方市场、跨进程 UI 或用户动态启停插件。

版本按所有者独立演进：产品版本、Plugin SDK 版本、每插件版本、manifest schema、每种宿主
持久化 schema 和插件内容 schema 不能互相代替。当前产品与 Plugin SDK 基线均为 `1.0.0`，
Host API 与 SDK 的程序集兼容身份均为 `1.0.0.0`；统一事实定义在
[`Directory.Version.props`](./Directory.Version.props)。普通进程内强类型消息不增加无迁移行为的
版本字段，发生破坏性语义变化时创建新消息类型或提升 SDK 主版本。

四个当前插件的 `PluginVersion` 均为 `1.0.0`，Host API 与 Common 兼容区间均为
`[1.0.0, 2.0.0)`；这些值由各插件项目文件拥有，G16 文档门禁直接读取并与集中 SDK 版本交叉验证。

宿主默认把布局、外观和诊断写入 `%LOCALAPPDATA%\MyAvaloniaManagement\v1\`。旧预发布目录
保持原样，不读取、迁移或删除。`MYAVALONIA_DATA_DIRECTORY` 仍表示完整数据根，不追加 `v1`，
以保持自动化和部署隔离语义。

诊断 JSONL 位于上述数据根的 `Diagnostics/session-*.jsonl`。内存记录、插件状态、启动失败摘要、
剪贴板和默认 Trace/stderr 只保留错误码、阶段、经校验身份、版本、异常类型及受控耗时等白名单信息。
原始异常只允许在用户明确确认后，通过进程级
`MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS=1` 临时输出到 Trace/stderr；它永远不进入 UI 或 JSONL。

Document 使用项目第一个且唯一的七字段信封 v1。宿主拥有 schema、插件和 Document 身份、标题、路径与
UTC 时间，插件只通过不可变 `DocumentContentSnapshot` 提供内容 schema 和字符串 payload。reader 严格拒绝任何非 v1 结构、历史 ID 别名
和插件所有权冲突；文件 UTF-8 上限为 8 MiB，JSON 最大深度为 8。项目没有旧 Document 信封兼容
或迁移路径，打开失败不会创建、迁移或覆盖文件。

## 快速运行

### 前置条件

- Windows x64；
- 仓库 [`global.json`](./global.json) 指定的 .NET SDK `10.0.302`，或满足 `latestPatch` 规则的补丁版本。

以下命令构建并启动最小的 **Host + MyPlugTest** 组合。请在仓库根目录执行：

```powershell
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

统一 Managed Plugin 构建协议会生成严格清单，并把入口、deps、PDB 和私有运行时依赖部署到 Host
输出目录的独立 `Controls/<PluginFolder>/`。需要体验其他插件时，先构建对应插件项目，再使用
`--no-build` 启动 Host；构建只清理当前插件目录。

创建新插件、编写严格清单及处理依赖部署时，请直接阅读 [Managed 插件快速开始](./docs/quick-start/README.md)，不要从本节推断完整打包规则。

## 仓库结构

```text
Host/         桌面宿主、公共插件契约及宿主测试
Plugins/      当前业务插件、插件测试和专项验收工具
docs/         解决方案级理论、设计、快速开始、历史与参考文档
scripts/      宿主测试和插件专项验收脚本
TestResults/  需要保留的阶段验收与人工验证记录
```

根 README 只提供项目概览。继续阅读时，从以下入口选择：

- [项目文档导航](./docs/README.md)：按用途浏览全部解决方案级文档；
- [Managed 插件快速开始](./docs/quick-start/README.md)：从零接入包含 Document 和 Tool 的新插件；
- [宿主—插件架构评审](./docs/design/host-plugin-architecture-review.md)：理解当前架构、成熟度和边界；
- [Plugin SDK API 兼容基线维护指南](./docs/reference/plugin-sdk-api-compatibility.md)：新增或修改 SDK public API 前阅读；
- [主项目兼容约束](./Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)：修改 public API、插件契约或稳定 ID 前核对；
- [MyAvaloniaManagement 测试说明](./docs/reference/myavalonia-management-tests.md)：查看测试层次、门禁和结果位置。

## 测试

修改宿主 v1 当前文档、脚本路径、版本事实或关键契约名称时先运行：

```powershell
.\scripts\Test-DocumentationCore.ps1
.\scripts\Test-Documentation.ps1
```

该门禁不启动窗口、不执行发布，也不替代 G14 的历史发布门禁。

在干净 Git 提交上执行完整的 G14 Windows 本地发布门禁：

```powershell
.\scripts\Invoke-HostV1ReleaseGate.ps1
```

该入口在两个独立克隆中重复执行锁定还原、Release 零警告构建、G15 诊断脱敏扫描、宿主三套测试、SDK 包/API、
四插件包矩阵和真实窗口 Smoke，并把日志、TRX、覆盖率、ZIP、清单及两轮比较写入
`artifacts/release-gate`。它不绑定代码托管平台，也不会创建或推送标签。

运行宿主标准门禁：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release
```

修改诊断、异常边界、Trace 或 Console 输出时，还必须运行：

```powershell
.\scripts\Test-HostDiagnosticRedaction.ps1
```

需要验证真实 Windows 窗口启动时运行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -WindowsSmoke
```

验证四个插件的独立确定性 ZIP、协议负例和最终包加载：

```powershell
.\scripts\Test-ManagedPluginPackages.ps1 -Configuration Release
```

维护 Plugin SDK public API 时，必须额外运行可读基线和成员级变异门禁：

```powershell
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v1 -Configuration Release
```

每个插件分别生成 `<AssemblyName>-<PluginVersion>-win-x64.zip`，不会生成四插件合集。

插件还包含各自的单元测试、集成 Harness 或发布验收项目；其专用前置条件和命令以插件目录中的当前文档为准。

## 当前边界

- 插件是进程内可信代码，不提供权限隔离或恶意代码防护；
- 插件加载上下文用于依赖解析隔离，不等同于安全沙箱；
- 插件目录快照和加载上下文以进程为边界，不支持热更新或运行时卸载；
- 仓库能生成正式 Plugin SDK/NuGet 制品，但不自动推送公共包源；当前没有插件市场或通用脚手架；
- G4 已完成：宿主只接受严格清单、入口 `.deps.json` 和唯一 `IPluginModule` 的 Managed Plugin；
- Host、`MyAvaloniaManagementCommon`、Avalonia、Dock 与插件需要按照兼容区间协同升级。

上述边界的详细规则以[架构评审](./docs/design/host-plugin-architecture-review.md)和[兼容约束](./Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)为准。
