# 页面身份、创建目标与即时查询

> 核对日期：2026-09-28。依据：当前实现与文中源码、契约、测试链接。

[上层：工作区](README.md) · [文档总览](../README.md)

## 类型、实例和位置

DocumentTypeId 标识类型；WorkspacePageId 标识本次运行的页面；DocumentCreationTarget 描述一次创建请求借用的来源根与优先文档组。三个身份都不能互相替代：同类型页面可能同时存在于多个浮窗。

```mermaid
flowchart LR
  W["来源窗口"] -->|打开面板前捕获| T["DocumentCreationTarget"]
  T -->|随本次请求传递| C["异步初始化"]
  C -->|发布时重查归属与关闭状态| R["DocumentCreationTargetResolver"]
  R --> D["最终 DocumentDock"]
```

## 为什么不能只读全局活动页

打开功能中心或命令面板会改变焦点；插件初始化期间用户还可能切换窗口、移动或关闭组。因此目标必须来自原始操作窗口，并在发布时重验。没有目标的旧入口保留主默认组语义；有来源的请求按解析器规则在来源窗口与主窗口内回退，不能随意跳到另一个浮窗。

[DocumentCreationTargetResolver](../../../Host/MyAvaloniaManagement/Business/Layout/DocumentCreationTarget.cs) 用弱引用记录各根最近文档组，不持有窗口或 Scope。Session 提供根是否属于当前会话、是否正在关闭的事实。Resolver 不遍历 Avalonia 焦点树。

## 页面查询是只读投影

[WorkspaceSession.Pages](../../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceSession.Pages.cs) 为已发布页面维护运行身份及显示序号。关闭时移除引用和订阅；同名新页面不会继承旧页面身份。

一次查询先建立 [WorkspaceLayoutQuerySnapshot](../../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceLayoutQuerySnapshot.cs)，统一得到组、窗口和显隐关系，再结合实时标题、Dirty、关闭与可用性生成结果。快照不跨查询长期缓存，也不作为执行许可。

## 活动文档与 Tool 焦点

Session 按 Adapter 引用发布 ActiveDocumentChanged，WorkbenchContextStore 转换为命令上下文。切到 Tool 不应无条件清空仍适用的文档目标；文档真正移除时则必须在释放 Scope 前撤回旧 Target，让命令状态层先退订。

快速可用性查询，如 CanActivatePage 和 HasCreationEntry，不需要构造完整分类树或所有页面展示项。完整查询供列表显示，窄查询供执行前判断。

验证入口：[DocumentCreationTargetTests](../../../Host/MyAvaloniaManagement.Tests/DocumentCreationTargetTests.cs)、[WorkspaceLayoutQuerySnapshotTests](../../../Host/MyAvaloniaManagement.Tests/WorkspaceLayoutQuerySnapshotTests.cs)、[DocumentWindowV16UiTests](../../../Host/MyAvaloniaManagement.UiTests/DocumentWindowV16UiTests.cs)。下一层：[命令目标复核](../06-commands/02-context-and-execution.md)。
