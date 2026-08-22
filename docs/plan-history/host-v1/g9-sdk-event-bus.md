# G9：收口 SDK 事件总线

> **历史说明：本 V1 阶段已由 Managed Plugin V2 G14 取代；以下日期、数量和结论保持原样。**

> 状态：已完成
>
> 完成日期：2026-08-18
>
> 边界：Managed Plugin v1 标签前的候选 API 重定基线；SDK/程序集版本保持 `1.0.0`

## 1. 结论

G9 用 SDK 自有的 `IHostEventBus` 取代旧消息器包装。每个 `HostRuntime` 的根容器创建一个
`HostEventBus`，不同运行时没有静态实例、默认 messenger 或全局 Reset，因此测试和多宿主对象图不会
串收事件。现有强类型事件 DTO 与业务行为保持不变；Host 内部广播的删除仍属于 G10。

这是正式 v1 标签前一次有意的候选 API 重定。旧接口、实现、处理器基类和底层 messenger 属性已直接
删除，没有保留 `Obsolete` 适配层。基础 SDK 不再直接声明 `CommunityToolkit.Mvvm` 依赖；四个仍用其
ViewModel 能力的真实插件显式拥有直接依赖，Host 也继续直接拥有并共享受支持版本。

## 2. API 对照与公共契约

| G8 候选 API | G9 v1 API | 处理结果 |
| --- | --- | --- |
| `IMessengerService` | `IHostEventBus` | 契约只表达发布与订阅 |
| `MessengerService` | internal `HostEventBus` | 实现留在 Host，不进入 SDK public 面 |
| `MessageHandler<T>` | `Action<TEvent>` | 删除基类和隐式注册协议 |
| `.Messenger` | `Publish` / `Subscribe` | 不再暴露 CommunityToolkit 类型 |
| `Unregister` / `UnregisterAll` | `IDisposable` 订阅令牌 | 每个消费者只拥有并释放自己的订阅 |

最终公共契约为：

```csharp
public interface IHostEventBus
{
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class;
}
```

`Publish<TEvent>` 只派发精确的泛型事件类型，不按基类或接口扩散。普通内存事件不增加版本字段；语义
发生破坏变化时创建新事件类型，或在正式发布后提升 SDK 主版本。

## 3. 同步、并发和异常语义

`HostEventBus` 使用一个普通锁保护“事件类型到订阅列表”的字典：订阅、退订和发布快照在锁内完成，
用户处理器始终在锁外、发布线程上按订阅顺序同步执行。这个实现支持处理器自释放和重入发布，也避免
在用户代码持有总线锁时产生死锁。

确定语义如下：

- 同一 `Publish` 不切换线程、不排队、不异步等待；
- 同一精确事件类型按订阅顺序派发，重复订阅是两个独立订阅；
- 处理器抛出的异常原样传播给发布者，并立即停止后续处理器；总线不吞、包装、重试；
- 订阅令牌可重复释放，只移除自身订阅；
- 发布使用快照，令牌释放不能撤回已经进入当前快照的调用；Document 仍检查
  `IDocumentLifetime.IsClosing`，以抑制关闭竞态中的最后一次迟到副作用；
- 空事件和空处理器抛 `ArgumentNullException`；总线释放后发布或订阅抛
  `ObjectDisposedException`；
- 根容器释放总线时先标记释放并清空订阅；已有发布快照仍按上述快照规则完成。

没有引入 Mediator、命令总线、事件溯源、弱引用、优先级、中间件、异步管线或通用版本框架。

## 4. 生命周期与所有权

| 消费者 | 订阅所有者 | 释放时机 |
| --- | --- | --- |
| Managed Document | Document ViewModel | ViewModel `Dispose`；`DocumentScopeManager` 先发关闭信号，再释放 Scope |
| BiliDownloader Coordinator | 插件根级生命周期 | Coordinator 关闭流程，且幂等 |
| MainWindow / Host Tool | 根级消费者自身 | 自身幂等 `Dispose`；根容器最终兜底 |
| 测试上下文 | 测试服务根或替身 | 测试容器结束时释放 |

构造期间订阅失败不再由空 `catch` 隐藏。契约配置错误会让对象构造明确失败，避免产生“对象看似可用、
实际未订阅”的半初始化状态。插件独立测试使用只实现 `IHostEventBus` 的小型内存替身，不再引用
Toolkit Messaging。

## 5. SOLID 与朴素设计取舍

- **SRP**：总线只负责登记和派发；订阅者负责自己的令牌与生命周期。
- **ISP**：公共接口只有发布和订阅，不暴露实现对象、批量注销或全局状态管理。
- **DIP**：Host、Document、Tool 和插件协调器依赖 SDK 抽象，不依赖 CommunityToolkit messenger。
- **OCP**：新增事件类型和消费者不需要修改总线实现。
- **LSP**：真实实现与测试替身都必须遵守同步、精确类型、异常传播和令牌释放契约。

实现选择“锁内快照、锁外调用”的最小模型。当前需求没有路由策略或跨进程传输，因此额外抽象会增加
生命周期和错误语义，而不会解决真实问题。

## 6. 依赖所有权

`MyAvaloniaManagementCommon.csproj` 已删除对 `CommunityToolkit.Mvvm` 的直接引用，基础 nupkg 的
nuspec 也不再声明该直接依赖。由于 SDK public Document 类型目前仍依赖 `Dock.Model.Mvvm`，而该包
自身传递依赖 Toolkit 8.4.0，独立还原图仍可能出现该包；这不是事件总线 public 签名依赖，也不允许
基础 SDK 重新直接拥有它。

BiliDownloader、DaTangAccountingHelpPlug、MyPlugTest 和 MySmallTools 因自身 ViewModel 使用 Toolkit，
分别显式声明直接依赖。Host 继续直接引用受支持版本，并由共享程序集策略让插件取得默认加载上下文
中的同一实例，避免依赖 Common 的偶然传递闭包。

## 7. 测试与门禁证据

G9 建立了以下保护：

- `HostEventBusTests` 覆盖同线程与顺序、精确类型、重复订阅、令牌独立/幂等、自释放、重入、异常
  原样传播与短路、并发发布/订阅/释放、两个服务根隔离、空参数和释放后行为；
- 事件感知 Document Scope 覆盖单文档关闭隔离、其他文档继续接收、构造失败回收已建订阅、
  重复释放和根容器兜底；
- BiliDownloader 完整回归覆盖登录、进度、任务提交和 Coordinator 关闭；
- SDK 包门禁编译 `IHostEventBus` 正例，并确认旧接口、实现、处理器与底层属性不能编译；
- public/依赖门禁验证 SDK 签名不含 Toolkit Messaging、基础包无直接 Toolkit 依赖、四插件显式
  依赖及 Host 默认上下文共享；
- `HostEventBus.cs` 进入关键文件覆盖率门禁，最低行覆盖率为 90%。

2026-08-18 的已执行证据：

| 门禁 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告、0 错误 |
| `HostEventBusTests` | 10/10 通过 |
| Document Scope 专项 | 5/5 通过 |
| Host 综合门禁 | Unit 162 + UI 37 + Plugin 146 = **345/345** 通过 |
| Host 覆盖率 | 行 **80.57%**、分支 **65.98%**；关键事件总线不低于 90% |
| BiliDownloader 完整测试 | 719/719 通过 |
| DaTangAccountingHelpPlug 完整测试 | 64/64 通过 |
| MySmallTools 完整测试 | 182/182 通过 |
| Plugin SDK 独立包消费 | 新事件 API 正例成功；旧消息器及既有旧契约反例按预期失败 |
| 锁定还原与 Release 构建 | 通过；0 警告、0 错误 |
| Windows Smoke | 通过 |

测试数量只作为 2026-08-18 的时间点证据，不是永久常量。Host 综合报告位于
`artifacts/test-results/MyAvaloniaManagement`。

## 8. 回滚边界

G9 应作为一个整体回滚：公共接口、Host 实现、消费者令牌所有权、依赖声明、测试和文档必须保持同一
基线。不能只恢复旧 SDK 接口或默认 messenger，否则会重新引入跨根全局状态和两套生命周期协议。
回滚不涉及磁盘 schema、Document 信封或业务 DTO，Host 内部广播删除仍留给 G10。

本任务没有使用 AIFLOW，也没有初始化或修改 `.aiflow`。
