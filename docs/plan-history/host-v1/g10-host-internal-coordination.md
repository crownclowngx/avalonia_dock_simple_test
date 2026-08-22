# G10：Host 内部直接协调

> **历史说明：本 V1 阶段已由 Managed Plugin V2 G14 取代；以下日期、数量和结论保持原样。**

> 状态：已完成
>
> 完成日期：2026-08-20
>
> 边界：只删除 Host 自身的文件打开、布局刷新和 Tool 显隐广播；Plugin SDK 与插件业务事件保持不变

## 1. 结论

G10 删除了 Host 内三个内部消息类型以及全部发送、订阅和订阅令牌。文件树现在通过单方法 internal
端口直接进入既有文档持久化协调器；文件菜单与文件树共享每 HostRuntime 唯一的文档操作状态。
Dock 布局和 Tool 可见性则由本来就拥有 Dock 树的 `ManagementFactory` 与 `ToolDockCoordinator`
统一提交，Tool 管理器只投影最终事实。

公共 `IHostEventBus` 和 internal `HostEventBus` 没有删除或改签名。BiliDownloader、MyPlugTest 等
插件程序集内存在真实生产消费者的事件继续使用该契约；SDK 版本、插件兼容区间、Document 信封和
布局 schema 均未变化。

## 2. 调用流对照

| 场景 | G9 后旧流 | G10 最终流 |
| --- | --- | --- |
| 文件树打开文件 | 文件树发布消息，主窗口订阅后 fire-and-forget 打开 | 文件树调用 `IHostDocumentOpenService.OpenPathAsync`，生产实现复用 `DocumentPersistenceCoordinator` |
| 文档错误条 | 主窗口持有字段，消息入口单独捕获异常 | 根级 `DocumentOperationState` 保存状态，文件菜单与文件树共同提交结果 |
| Tool 管理器切换 | ViewModel 直接改 Dock，再发布布局消息 | ViewModel 只提交目标状态，Factory 完整修改 Dock 后统一通知 |
| 用户关闭 Tool | Factory 发布可见性消息，管理器订阅并重扫 | Factory 直接调用 `IToolVisibilityStateSink` 读取最终 Dock 树 |
| Welcome/其他入口显示 Tool | Tool 协调器发布布局消息 | Tool 协调器成功恢复或激活后调用 Factory 的布局提交入口 |

Host 的定向通知不接受任意事件类型、不进入 SDK，也不具备路由、重试或全局订阅能力，因此不是另一套
消息总线。瞬态主窗口在构造时订阅当前根 Factory 和文档状态，在 `Dispose` 及构造失败回滚时成对解除。

## 3. 所有权与行为

- `DocumentPersistenceCoordinator` 是根级单例，继续负责批量打开、恢复、保存和预期故障转换；
- `IHostDocumentOpenService` 只暴露路径打开，文件树不知道主窗口、Dock、保存或错误状态；
- `DocumentOperationState` 只保存脱敏错误文本并通知真正的状态变化，取消操作不会覆盖已有提示；
- 文件树入口的意外异常转换为固定中文提示，诊断只记录错误码和异常类型；
- `ManagementFactory` 是 Dock 状态事实源，隐藏期间抑制 Dock 原生回调的中间通知，完成活动项调整后只提交一次；
- 用户直接点击关闭按钮时，`OnDockableHidden` 是唯一提交点；恢复和 `ShowTool` 在成功附着后提交；
- 提交顺序固定为“Tool 管理器读取最终 Dock 树，然后主窗口刷新 Layout 绑定”。

## 4. SOLID 与朴素设计取舍

- **SRP**：文档工作流、文档提示状态、Dock 状态和 Tool 状态投影分别由不同对象持有；
- **ISP**：文件树只有一个打开方法，Factory 对 Tool 管理器也只有一个同步方法；
- **DIP**：文件树依赖 internal 窄接口，测试不需要构造主窗口或事件总线；
- **OCP**：新增 Host Tool 不需要新增消息类型或总线路由，只需继续由 Factory 管理 Dock 状态；
- **LSP**：记录路径的测试替身与生产打开服务遵守同一异步完成契约。

没有引入 Mediator、命令总线、事件聚合器、弱引用、异步队列或通用状态框架。Factory 已经是 Dock 的
唯一协调者，在它上面增加定向布局通知比创建第二个抽象层更直接；文档状态单独拆出，是为了避免瞬态
窗口和文件树维护两份用户提示。

## 5. 测试与结构门禁

新增或加强的保护包括：

- 文件树只对存在文件调用窄服务，并通过真实协调器打开 Document；
- 预期读取失败和意外异常都更新共享错误条，意外异常正文不会进入界面；
- Tool 管理器隐藏、恢复、固定、不可关闭项、Dock 关闭及 `ShowTool` 均以真实 Dock 树为准；
- 一次成功显隐只产生一次布局通知，失败不通知；
- 两个 HostRuntime 的布局和文档状态互不串扰，主窗口释放后不再接收通知；
- 反射门禁验证三个删除类型不存在、五个 Host 消费者不再注入 `IHostEventBus`，SDK Events 命名空间
  没有新增具体事件 DTO；
- 精确源码搜索对三个删除类型返回零结果。

2026-08-20 的已执行证据：

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 | 通过，所有项目均为最新 |
| 解决方案 Release 构建 | 0 警告、0 错误 |
| G10 MainWindow/Tool/结构专项 | **37/37** 通过 |
| Host 综合门禁 | Unit 167 + UI 37 + Plugin 146 = **350/350** 通过 |
| Host 覆盖率 | 行 **80.65%**、分支 **65.98%**；既有关键文件门槛全部通过 |
| Windows 真实启动 Smoke | 通过 |
| BiliDownloader 完整测试 | **719/719** 通过 |
| DaTangAccountingHelpPlug 完整测试 | **64/64** 通过 |
| MySmallTools 完整测试 | 最终无并行负载复跑 **182/182** 通过 |
| Plugin SDK 独立包消费 | 通过；新 API 正例成功，旧消息器等反例按预期失败 |
| 删除类型源码搜索 | Host 下零结果 |

MySmallTools 首轮与其他插件、SDK 门禁并行执行时，异步搜索投影测试因固定等待窗口出现 1 项时序失败；
专项复跑仍观察到投影尚未稳定，随后在无并行负载下完整复跑 182/182 通过。本次没有修改 MySmallTools
生产代码或测试，也不把首轮失败隐藏为一次性成功。该测试的固定等待敏感性不属于 Host G10 修改边界。

Host 综合报告位于 `artifacts/test-results/MyAvaloniaManagement`。数量和覆盖率是本次 TRX、Cobertura
与 `summary.json` 的时间点证据，不作为未来永久常量。

## 6. 回滚边界

G10 应整体回滚：窄文档入口、根级错误状态、Factory 布局提交、Tool 直接同步、测试和文档必须保持
同一版本。不能只恢复三个消息类或重新让 Host 消费 `IHostEventBus`，否则会再次把本地调用伪装成
跨消费者事件，并恢复两套所有权协议。

本任务没有修改 Plugin SDK public API、磁盘 schema、插件事件 DTO、`TestResults/G10` 或 `.aiflow`。
