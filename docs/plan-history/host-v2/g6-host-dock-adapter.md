# Managed Plugin V2 G6：Host Dock Adapter

> 状态：已完成。
>
> 实施日期：2026-08-21。
>
> 基线：`dev-重构-2026年8月18日` 分支 G5 提交 `ad82020`。
>
> 性质：开发阶段非发布整改；没有运行 Windows CI、Windows Smoke、ReleaseAcceptance、发布包门禁、上传、标签或发布。

## 1. 结果

G6 把 Dock 框架依赖收回 Host internal 边界。生产代码中只有
`ManagedDocumentDockable` 与 `ManagedToolDockable` 继承 Dock `Document`/`Tool`；Welcome 和四个
Host Tool ViewModel 都是普通对象。最终 SDK 测试插件也以普通模型参与激活，插件无需引用 Dock。

本阶段没有改变 Core/UI SDK public API、版本、API v2 文本基线、manifest、Document v1 信封或
layout-v1 schema。四个真实业务插件仍保留 Legacy 源码并由当前 Host 拒绝加载；迁移仍属于 G9–G12。

## 2. SOLID 与朴素模式

| 协作者 | 单一职责 | 所有权 |
| --- | --- | --- |
| `PluginContributionActivator` | 按 Registry 所有者选择 Host/插件 Provider，激活普通模型 | Document 返回 Scope Lease；Tool 返回 Provider singleton 引用 |
| `HostDockAdapterFactory` | 组合激活结果、Adapter 与预构建 View | 失败时回滚未发布 Adapter/Scope |
| `ManagedDocumentDockable` | 把 Document 展示和关闭状态投影到 Dock | 模型、View、Scope Lease |
| `ManagedToolDockable` | 把 Tool Descriptor 与 View 投影到 Dock | 只拥有 View；不释放 Tool singleton |
| `ViewLocator` | 使用 Registry 冻结的精确工厂构造一次 View | 不拥有模型或 Scope |
| `ManagementFactory` | 发布 Dock 项、协调布局并汇合最终释放 | 跟踪已发布 Adapter，不创建业务模型 |
| `DocumentScopeManager` | 创建和释放普通 Document Scope | ClosingToken、模型与 scoped 依赖 |

实现只使用三种直接模式：内部 Factory 隔离创建，Adapter 隔离 Dock，Scope Lease 表达 Document
释放权。没有引入策略注册双轨、反射 View Locator、规则引擎、动态代理或通用生命周期框架。

依赖方向为 `ManagementFactory → IHostDockableFactory → Activator/ViewLocator`。接口仅有创建
Document/Tool 两个方法，生产只注册 `HostDockAdapterFactory`；G7 前旧持久化测试通过测试专用 seam
注入，不把 `IDocumentScopeFactory` 恢复到生产容器。这样满足依赖倒置，同时没有为了形式上的抽象扩大 API。

## 3. Document Adapter 与释放顺序

每次 Document 激活都会建立独立 DI Scope，并返回包含注册事实、`IPluginDocument` 和幂等释放入口的
`ActivatedPluginDocument`。Adapter 标题按“非空 `Presentation.Title` → 请求标题 → Descriptor 名称”
回退；`PresentationChanged` 从后台线程到达时统一投递 Avalonia UI Dispatcher。释放后事件已解除，
排队或迟到通知不再修改 Dock 状态。

最终释放顺序固定为：

1. 原子标记 Adapter 已释放并解除 `PresentationChanged`；
2. 释放 View 租约，先断开 `DataContext`，再按需 `Dispose` View；
3. 释放 Scope Lease，由 `DocumentScopeManager` 先发出 `ClosingToken`；
4. 释放 Document 模型和 scoped 依赖；任一步异常都不能跳过 Scope 的兜底释放。

Dock 关闭确认仍由现有关闭协调器负责。只有最终确认关闭后才调用 Adapter 释放；关闭取消不会提前终止
Scope。Runtime 退出则由 `HostRuntime` 先释放 `ManagementFactory` 中残余 Adapter/View，再关闭所有
Document Scope 和插件 Provider。

## 4. Tool Adapter 与布局状态

Tool 模型由所属插件 Provider 以 singleton 激活。Adapter 的稳定 `Id` 取 `ToolTypeId.Value`，标题、
默认方向和关闭行为取冻结 Descriptor：`Hide` 可关闭并进入隐藏集合，`Prevent` 不可关闭；所有 Tool
均可 Pinned、不可浮动。隐藏、恢复、重新停靠和 Pinned 状态继续由现有 `ToolDockCoordinator` 与布局
生命周期实现，Adapter 不复制状态机。

Adapter 只拥有预构建 View。关闭、隐藏或 Runtime 退出均不会越权 Dispose Tool 模型；插件 Provider
在自身生命周期末尾统一释放 singleton。

## 5. View 原子发布与失败隔离

`ViewLocator.Prepare` 只接受 Host Adapter，并核对 Adapter 携带的所有者、模型类型、View 类型与
Registry 精确注册。它调用同一个冻结工厂一次，把普通模型设置为 `DataContext`，并把实例交给幂等
View Lease；Dock DataTemplate 后续只返回该实例。不存在程序集扫描、类型名猜测、反射构造或把普通模型
直接当作 Dock 内容的回退。

Document View 失败发生在加入 DocumentDock 之前：记录脱敏 `VIEW_CREATION_FAILED`，释放暂存 Adapter、
ClosingToken、模型和 Scope，界面不出现半发布标签。单个 Tool 模型或 View 失败记录
`TOOL_ADAPTER_ACTIVATION_FAILED` 并只隔离自身，其他 Tool 与布局继续。Host Welcome 失败则直接中止
布局初始化，因为中央工作区没有可接受的降级形状。诊断只持久化白名单字段和异常类型，不写异常正文、
绝对路径或插件内容。

## 6. 测试与实际证据

新增 `scripts/Test-HostDockAdapter.ps1`，串行执行 Unit、Plugin、Headless UI 过滤集和生产结构扫描，
摘要明确写入 `windowsCi=false`、`windowsSmoke=false`、`releaseGate=false`。覆盖模型/Dock 类型边界、
Document 独立 Scope、Tool singleton、Descriptor 投影、四向布局、隐藏/恢复/Pinned/禁浮动、后台标题、
空标题回退、迟到事件、精确单实例 View、DataContext、关闭取消、失败原子性、Runtime 兜底和诊断脱敏。

本轮已实际执行：

- G6 专项：Unit 16、Plugin 35、Headless UI 23，共 **74/74**；
- Host 全量：Unit 182、Headless UI 44、Plugin 160，共 **386/386**；
- Host 覆盖率：行 **82.41%**、分支 **66.85%**；既有整体阈值未降低；
- G6 关键文件行覆盖率：Factory **100%**、Document Adapter **95.83%**、Tool Adapter **95.83%**、
  `ViewLocator` **93.18%**、`DocumentScopeManager` **91.57%**；
- Plugin SDK 单元测试 **32/32**，Core/UI API v2 兼容变异门禁通过；
- BiliDownloader **720/720**、DaTangAccountingHelpPlug **64/64**、MySmallTools **183/183**，共 **967/967**；
- Release `-warnaserror` 全解决方案构建为 0 警告、0 错误。

专项脚本的最终测试数和机器可读结果以
`artifacts/test-results/HostDockAdapter/summary.json` 为准；覆盖率结果位于
`artifacts/test-results/MyAvaloniaManagement/`。这些是本轮开发证据，不是发布签署。

## 7. 阶段边界、G7 入口与回滚

G6 不调用 `IPluginDocument.InitializeAsync`，不实现 Creation Intent、异步发布链、Document v2 恢复、
保存、`JsonElement` 内容信封或插件可用性门控。layout-v1 读取和现有生命周期编排保持不变；layout-v2
仍属于 G8。快速开始必须继续标记为历史示例，直到 G9 迁移第一个真实插件后才能成为完整可运行教程。

完整回滚单位是 G6 的 Adapter、View Lease、Activator 普通模型返回值、ManagementFactory 接入、
Host 内建普通模型、专项测试/门禁和本文档。回滚必须整体回到 G5，不允许同时保留插件 Dock 对象与
Host Adapter，也不得恢复生产 `IDocumentScopeFactory` 回退或修改用户 Document/layout 文件。
