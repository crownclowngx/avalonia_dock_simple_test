# G0：基线、测试骨架与插件生命周期

> 实施日期：2026-07-21
>
> 适用范围：MyAvaloniaManagement 宿主、BiliDownloader 与 MyPlugTest 插件
>
> 兼容原则：插件逐个显式接入；未迁移插件继续使用历史初始化流程

## 1. 完成目标

G0 建立了后续安全、任务控制和恢复改造所依赖的基础边界：

- BiliDownloader 拥有可独立运行的 xUnit 测试项目。
- Coordinator 可以使用内存仓储和假下载执行器完成离线测试。
- 插件初始化与关闭不再依赖 Tool 或 Document 是否进入视觉树。
- 启动阶段只初始化 SQLite 和迁移本地任务状态，不自动恢复下载。
- 宿主只管理显式接入生命周期的插件，不改变历史插件的构造和初始化方式。

## 2. Legacy 与 Managed 双轨兼容

插件程序集分为两类：

| 类型 | 判定方式 | 策略创建 | 初始化与关闭 |
| --- | --- | --- | --- |
| Legacy Plugin | 程序集未实现 `IPluginModule` | 保留公共无参构造函数并使用 `Activator.CreateInstance` | 完全保留插件原有流程 |
| Managed Plugin | 程序集实现 `IPluginModule` | 使用 `ActivatorUtilities` 注入模块注册的服务 | 仅执行显式注册的 `IPluginLifecycle` |

当前 BiliDownloader 与 MyPlugTest 属于 Managed Plugin。只有 BiliDownloader 注册了
`IPluginLifecycle`；MyPlugTest 只使用依赖注入，不会进入生命周期状态列表。
DaTangAccountingHelpPlug 和 MySmallTools 仍是 Legacy Plugin，不会收到新的初始化或关闭回调。

公共策略接口 `IDocumentCreationStrategy` 和 `IToolCreationStrategy` 没有修改，历史插件无需重新设计。

## 3. 宿主生命周期

宿主启动顺序：

1. 按原有插件目录规则加载程序集。
2. 发现具有公共无参构造函数的 `IPluginModule`。
3. 按 `PluginId` 稳定排序并注册模块服务。
4. 构建根级依赖注入容器。
5. 按 `Order`、`PluginId` 串行初始化已注册的生命周期。
6. 创建 ManagementFactory、Tool 和主窗口。

宿主退出顺序：

1. Avalonia 桌面生命周期结束。
2. 只对成功初始化的 Managed Plugin 执行关闭。
3. 按初始化顺序反向等待插件关闭。
4. 最后释放根级依赖注入容器和其中的下载资源。

单个 Managed Plugin 初始化失败会记录 `Failed` 状态，不阻止其他插件初始化；失败插件不会进入关闭列表。

## 4. BiliDownloader 服务所有权

插件模块注册以下生命周期：

- Singleton：任务仓储、设置仓储、登录状态、凭据提供者、下载执行器、进度跟踪器、Coordinator 和 Tool ViewModel。
- Transient：每个 BiliDownloader Document ViewModel。
- 共享服务：直接复用宿主 `IMessengerService`，插件不注册第二个消息总线。

因此多个 Document、唯一 Tool 和宿主生命周期始终引用同一个 Coordinator 与 SQLite 任务事实源。

### 4.1 MyPlugTest 轻量 DI 示例

MyPlugTest 用于展示不需要后台生命周期时的最小 Managed Plugin 结构：

- Singleton：唯一的 `MyCustomToolViewModel` 和无状态 URL 内容请求服务。
- Transient：两个 Document ViewModel，以及每个欢迎 Document 独享的 URL 历史 ViewModel。
- 共享服务：直接注入宿主 `IMessengerService`，插件不创建第二个消息总线。
- 无生命周期：插件没有数据库、后台队列或退出清理工作，因此不注册空的 `IPluginLifecycle`。

两个 Document 策略只保存根级 `IServiceProvider`，在用户明确创建 Document 时解析瞬态
ViewModel；策略发现本身不会提前创建 Document。该示例说明 `IPluginModule` 与
`IPluginLifecycle` 是彼此独立的可选能力，插件不应为了形式完整而注册无职责生命周期。

## 5. 下载执行边界

`IDownloadTaskExecutor` 封装单个任务的副作用链路：

- Bilibili API 和 DASH 请求。
- 视频、音频下载。
- ffmpeg 合并。
- 字幕、弹幕和封面等 Extras。

Coordinator 不再直接创建上述服务，只负责：

- 初始化和迁移任务状态。
- 提交任务的先持久化、后执行顺序。
- 队列与并发槽位。
- 状态持久化和 UI 通知。
- 停止、取消以及宿主退出时的有序等待。

测试使用 Fake Executor，可以模拟立即成功、异常、阻塞和取消，全程不访问真实网络或媒体文件。

## 6. 状态与生命周期行为

启动：

- 创建或迁移本地任务表。
- 将 `fetching_metadata`、视频/音频下载、准备阶段和 `merging` 等运行中状态统一迁移为 `interrupted`。
- 保留 `pending`、`failed`、`done` 等其他状态。
- 不调用下载执行器，不自动启动 Ready 或 Interrupted 任务。

用户提交：

1. Document 发送明确的提交消息。
2. Coordinator 批量持久化任务。
3. 持久化成功后才启动执行器。

视觉树：

- Tool 附加时只加载设置并刷新全部任务投影。
- Tool 再次显示时重新读取 SQLite/Coordinator 投影，隐藏期间不停止下载。
- Document 附加时只恢复自身 `DocumentId` 的投影。
- Document 关闭不取消已提交任务。
- 登录远端校验只在用户点击登录后执行。

关闭：

- Coordinator 停止接受新执行命令。
- 取消队列和活动执行器并等待其退出。
- 因宿主关闭而取消的活动任务持久化为 `interrupted`。
- 注销共享消息总线订阅后再由 DI 容器释放底层资源。

## 7. 自动化测试

独立运行：

```powershell
dotnet test BiliDownloader.Tests\BiliDownloader.Tests.csproj -c Debug
```

当前测试覆盖：

- Managed 生命周期正序初始化、反序关闭、幂等和失败隔离。
- DaTangAccountingHelpPlug 与 MySmallTools 不被误判为 Managed Plugin。
- BiliDownloader 与 MyPlugTest 均能被识别为 Managed Plugin。
- Legacy 策略继续使用公共无参构造函数。
- Managed 策略使用 DI 构造路径。
- BiliDownloader 模块的 Singleton/Transient 注册和宿主消息服务复用。
- Coordinator 并发初始化只执行一次。
- 启动迁移运行状态但不调用下载执行器。
- 历史任务加载不自动下载。
- 提交先持久化再执行。
- 成功、失败、取消和宿主关闭状态。
- 重复关闭不会重复执行。

测试替身只使用内存数据，不连接 Bilibili、不写真实媒体文件、不启动 ffmpeg。

## 8. 构建基线与后续边界

- BiliDownloader 本体和 BiliDownloader.Tests 新增代码为 0 编译警告。
- G0 初次完成时全解决方案基线为 0 个错误、26 个历史警告。
- MyPlugTest 接入 DI 时自然消除了该项目原有的 4 个空值警告；当前仅保留
  DaTangAccountingHelpPlug 和 MySmallTools 的 22 个历史警告。
- 2026-07-21 完成宿主界面冒烟：主窗口正常启动，Legacy 插件菜单仍可见，
  BiliSchedulerTool 正常创建，BiliDownloader Document 可由用户菜单创建，宿主可通过关闭按钮有序退出。
  冒烟过程中未点击登录、解析或下载入口，未触发远端请求和媒体执行链路。
- 测试项目引用 BiliDownloader 和 MyPlugTest 时均通过 `SkipPluginDeploy=true`
  跳过插件发布，避免测试构建覆盖宿主插件目录。
- Cookie 字段和明文存储兼容代码仍保留，并以局部警告说明标注，由 G1 迁移。
- 进度最终 Flush、Range 完整性和临时文件恢复校验由 G3 实现。
- 并发数动态调整和单任务暂停/取消的竞争处理由 G2 实现。
