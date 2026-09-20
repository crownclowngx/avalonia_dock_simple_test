# Workbench Command 当前契约

> 用途：声明和维护菜单、快捷键、命令面板共用的用户操作。状态：当前；核对日期：2026-09-20。事实源：[Command 内核](../../Host/MyAvaloniaManagement/Business/Commands)、[Presentation](../../Host/MyAvaloniaManagement/Business/Presentation/Commands)及 UI SDK 声明。

## 语义与注册

Command 使用稳定 `CommandId` 表达操作；Avalonia ICommand 只是展示层适配。文档内普通表单、格子点击和局部按钮不必全部提升为工作台 Command。

插件通过可选 `IWorkbenchCommandRegistration` 声明 Descriptor、目标 Document 类型、菜单及快捷键贡献。注册结果不保存 Control、Provider、MenuItem、KeyBinding 或业务回调；Host 创建和拥有展示对象。

## 状态与执行

Host/Plugin 贡献合并成唯一 Catalog。Context 只表达 Host 确认的活动 Document 事实；State Query 组合目录、插件可用性、Context 和活动实例状态，区分 Visible 与 Enabled。

Document 模型可实现 `IWorkbenchDocumentCommandTarget`。执行路由到当前实例，不能为命令另开任意服务解析口；两个同类型页面可以具有不同状态，操作不能串到另一个实例。Host 打开/保存同样进入统一 Executor。

菜单、有效快捷键和命令面板复用状态与执行路径。执行时重新校验上下文和可用性，不能按显示名称选目标；取消、失败和关闭遵循统一门控，未排空时不能释放在用资源。

## 菜单与快捷键

Host 拥有保留位置与核心快捷键。插件只贡献允许的共享末端位置，不覆盖或删除 Host 菜单。快捷键由 Host 处理冲突，不能绕过投影直接争用主窗口绑定。

模板演示实例级 Command 和共享菜单，但不默认注册快捷键。命令状态变化由当前 Document Target 通知；View 不建立第二个 Catalog 或 Executor。

## 命令面板与 Workflow 的区别

当前面板还发现功能创建入口、已有页面和工具，见[工作区搜索](../quick-start/workbench-search.md)。新建成功才关闭相应搜索入口；失败保留查询及错误，普通 Command 沿用关闭面板后执行的时序。

V16 的功能新建通过 Host internal `DocumentCreationTarget` 显式携带面板来源根与文档组，串行排队和初始化不重取活动窗口；发布前验证来源归属及关闭状态。结果携带本次页面身份供焦点恢复重验，目标不存入共享展示命令。普通 Command 的 Context/Target 和 SDK 声明不变，保存对话框 Owner 仍由原窗口上下文负责。Dock 的标签选择与文档组焦点通知共同更新活动文档事实，因此点击另一组已选中的页也能正确路由。

Workflow Action 提供受控跨插件调用；Workbench Command 提供用户入口。需要编排时，Document 的 Command 可进入既有 Runner/Gateway，不复制另一套 Action Runtime。

原 G0–G10 的实施过程与当时测试记录位于[历史归档](../archive/README.md)。当前 SDK 版本、基线及发布边界见[API 维护](plugin-sdk-api-compatibility.md)。
