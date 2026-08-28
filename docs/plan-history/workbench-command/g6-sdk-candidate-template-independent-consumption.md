# Workbench Command G6：SDK 3.3、模板与独立消费门禁

> 状态：已完成并已发布（2026-08-28）；Core/UI SDK `3.3.0` 与 Templates `1.3.0` 的本地候选、
> 模板生成、新旧兼容矩阵和 NuGet.org 纯公开源消费均已验证。
>
> 输入提交：`97732d21ad16676a38a298d6a8fda3140d467759`
>
> 输入 Git tree：`d81b79445283019b11e772d1103d98b2f5417886`
>
> 前置：[G5 声明式 Menu 与 KeyBinding Projection](./g5-declarative-menu-keybinding-projection.md)
>
> 总设计：[Workbench Command 引入评审与实施任务书](../../design/workbench-command-introduction-plan.md#g6完成-sdk-33-候选包模板和独立消费门禁)

## 1. 范围与版本

G6 把 G1–G5 已压测的 Command public 契约传播到真实 NuGet 候选和通用模板，不再让外部消费依赖仓库内
`ProjectReference`。Core/UI SDK 同步提升到 `3.3.0`，Templates 提升到 `1.3.0`；Host 产品保持
`3.0.0`，Workflow SDK 保持 `1.0.0`，Build 保持 `1.1.2`。manifest、Document envelope、layout schema
均保持 2，布局文件仍为 `layout-v2.json`，数据根仍为 `v2`。

仓内四个业务插件不提升自己的业务版本。它们从集中 SDK 版本得到 3.3 编译输入，生成 manifest 的最低 SDK
随之更新；这表示“当前源码构建物需要 3.3 Host”，并不把业务插件包伪装成新的业务发布。旧 3.0/3.1/3.2
插件包则继续按其原始 manifest 在新 Host 中验证。

## 2. 模板与设计思路

Templates `1.3.0` 精确锁定 Core/UI `[3.3.0]` 和 Build `[1.1.2]`，三个生成项目均携带
`packages.lock.json`。模板没有复制 Host、WorkflowStudio 或具体业务插件代码，只在原有 `MainDocument`
增加一个实例级 `IWorkbenchDocumentCommandTarget`：

```text
CommandDescriptor
    → ToolsShared MenuPlacement（无默认快捷键）
    → Host Catalog / Context / Executor
    → 当前 MainDocument 实例
    → 更新 Message，并只通知本 CommandId 的状态变化
```

命令 ID、菜单 Placement ID 和 Document Type ID 均集中在 `PluginIds`。模块只声明不可变描述符；文档模型
只处理已知 CommandId，尊重取消令牌，并用实例字段证明两个 Document 不共享执行状态。状态第一次执行后由
Enabled 变为 Disabled，避免示例退化成无条件成功的假命令。模板专用说明
`docs/workbench-commands.md` 解释职责、线程、生命周期、测试和扩展方式。

## 3. SOLID 与朴素模式

| 原则 | G6 做法 |
| --- | --- |
| SRP | `PluginIds` 管稳定身份，Module 管声明，Document Target 管实例行为，Host 门禁管外部消费 |
| OCP | 新 Command 继续追加 Descriptor/Placement/Target 分支，不修改 Host Executor 或注册协议 |
| LSP | 生成插件只实现既有 Target 契约；Host 通过接口执行，不依赖模板具体类型 |
| ISP | Target 只接收 CommandId 与取消令牌，不取得 Registry、Provider、Control、Dock 或 Host 服务 |
| DIP | 外部插件依赖 Core/UI nupkg；Host 只依赖不可变 Registry 和 Target 接口，不建立源码反向引用 |

使用的模式只有稳定身份常量、不可变描述符、窄 Target 接口和实例状态通知。没有引入事件总线、Mediator、
反射命令发现、字符串 `when` 表达式、服务定位或第二套执行管线。新增示例和测试使用详细中文 XML/设计注释，
说明状态所有权、取消语义和不注册默认快捷键的原因。

## 4. 独立消费与兼容矩阵

专项入口为：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG6.ps1 -Configuration Release
```

入口使用隔离 NuGet 缓存、临时 feed 和模板 hive，执行以下门禁：

1. 锁定还原根解决方案，并对 Core/UI 各打包两次；规范化 OPC 元数据后，主包和符号包必须逐字节确定；
2. 校验 SDK 包边界与 public API，Templates nupkg 不得含 `lib/`/`ref/`，必须含三份 lock file 和
   Workbench Command 专用文档；
3. 安装冻结模板，分别生成普通名称和带点号名称的两个解决方案，执行 locked restore、Release
   `warnaserror`、四项模板测试、Standalone 有界启动和两次确定性插件 ZIP；
4. 将两个生成插件同时放入真实 Host Loader，确认两个独立 `AssemblyLoadContext`、Core/UI 只来自
   `AssemblyLoadContext.Default`、Command/Menu 声明完整且命令作用于当前文档实例；
5. 从 NuGet.org 的 Templates 1.0.4/1.1.0/1.2.0 各生成一个真实旧插件包，确认新 Host 正常加载且命令
   贡献为空；
6. 从冻结 G5 输入提交生成旧 Host 测试快照，加载新 3.3 插件时必须在程序集执行前以
   `PLUGIN_SDK_INCOMPATIBLE` 拒绝，不能伪装兼容或退化成加载异常；
7. 最后执行当前文档门禁。精确测试数、ZIP/manifest 哈希和候选包 SHA-256 写入
   `artifacts/test-results/WorkbenchCommandG6/summary.json`，避免人工抄写成为第二事实源。

模板四项单元测试覆盖实例隔离、首次执行状态变化、定向通知、重复及并发执行拒绝、未知 CommandId 和预取消令牌。
Host 外部包测试覆盖双 ALC、共享 SDK、Registry 组合、菜单声明、无默认 KeyBinding、manifest 下限和真实执行。

## 5. 实测结果与发布证据

完整 G6 入口从冻结输入重新执行基础开发门禁，`baseGateReused=false`，最终退出码为 0。SDK 单元测试为
**81/81**；四个业务插件专项聚合分别为 MyPlugTest **663/663**、DaTang **714/714**、MySmallTools
**854/854**、BiliDownloader **1382/1382**。Host 行覆盖率为 **86.98%**，各插件专项记录的 Host 分支
覆盖率为 **72.39%–72.42%**。MySmallTools 另完成 20 轮真实媒体资源归零，BiliDownloader 自身测试
为 **729/729**。文档门禁验证 100 份文档、551 个本地链接、197 个脚本路径和 51 个项目路径。

本地候选门禁还通过两个生成解决方案各 **4/4** 单元测试、普通/点号名称、Standalone 有界启动、双次确定性
插件 ZIP、真实 Host 双 ALC、旧 Templates `1.0.4`/`1.1.0`/`1.2.0` 兼容矩阵和旧 3.2 Host 负例。
冻结候选哈希如下：

| 候选文件 | SHA-256 |
| --- | --- |
| `MyAvaloniaManagement.PluginSdk.3.3.0.nupkg` | `7A0F433D5250F672B955746607680D28BE532B700F62278189AB1879FE40743F` |
| `MyAvaloniaManagement.PluginSdk.3.3.0.snupkg` | `2975B848D1245973E6C2C899242997C7A196C995265E54FD0613945D51F65309` |
| `MyAvaloniaManagement.PluginSdk.UI.3.3.0.nupkg` | `6C7C874C795877AA9891B32ECF3ECD1A4DA151E493F323C6F6F36E7F00D75E5F` |
| `MyAvaloniaManagement.PluginSdk.UI.3.3.0.snupkg` | `95F57364223105C4C84DBA1E6A1EEB5C329B221E86C334324E35060240B271A5` |
| `MyAvaloniaManagement.Plugin.Templates.1.3.0.nupkg` | `3A8B85F0A5DB5CE41999D043629E55FF805C5620B644F2BD7D0EC3ED8B9F9B77` |

操作者明确授权后，上述三个主包及两个符号包按 Core → UI → Templates 顺序上传，NuGet.org 均返回
`Created`。公开包地址为 [Core 3.3.0](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk/3.3.0)、
[UI 3.3.0](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk.UI/3.3.0) 和
[Templates 1.3.0](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Templates/1.3.0)。NuGet.org 会给
主包追加 Repository 签名，因此公开文件整体哈希与未签名候选不同；公开源门禁先用 `dotnet nuget verify --all`
验证仓库签名，再排除 `.signature.p7s`，按 ZIP 路径、长度和内容哈希确认其余内容与冻结候选一致。

公开源专项入口为：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG6PublicFeed.ps1 -Configuration Release
```

该入口使用只含 NuGet.org 的配置与全新 NuGet 缓存，从公开源安装 Templates `1.3.0`，生成解决方案，执行
locked restore、Release `warnaserror`、**4/4** 测试和两轮确定性插件 ZIP；首次完整下载还原耗时约
9.85 分钟，最终退出码为 0。远端签名、候选/公开/非签名内容哈希、测试数与 ZIP 哈希记录在
`artifacts/test-results/WorkbenchCommandG6PublicFeed/summary.json`。

## 6. 发布与回滚边界

本地门禁本身不调用 Windows CI、Windows Smoke、Host Release Acceptance、Host Release Gate、签名或 tag。
操作者在候选门禁实施期间明确扩大范围，授权把门禁验证的同一批 Core/UI `3.3.0` 与 Templates `1.3.0`
内容上传到 NuGet.org；上传与远端纯公开源复验均已完成。Host 产品、Workflow SDK、Build 和四个业务插件
没有上传。

发布前可整体回滚到 G5，并删除本地候选 feed/模板 hive。版本一旦上传不可覆盖：如果公开消费发现问题，
只能提升修订版本并重新执行完整门禁，不能重打 3.3.0/1.3.0。源码回滚必须把集中版本、lock file、模板示例、
Host 新旧兼容测试、专项脚本和本文作为一个单元处理，不能留下声明与包版本不一致的半状态。

```text
aiflow=false
windowsCi=false
windowsSmoke=false
hostReleaseAcceptance=false
hostReleaseGate=false
hostProductPublished=false
sdkPackagesPublished=true
templatePackagePublished=true
publicOnlyVerification=true
uploaded=true
published=true
tagCreated=false
```
