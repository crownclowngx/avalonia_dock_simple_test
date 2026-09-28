# 文档源码导读与验证地图

> 核对日期：2026-09-28。依据：当前实现与文中源码、契约、测试链接。

[上层：文档模块](README.md) · [文档总览](../README.md)

## 按问题选入口

| 问题 | 第一入口 | 接着追踪 |
| --- | --- | --- |
| 点击新建后发生什么 | [Coordinator.CreateDocumentAsync](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentPersistenceCoordinator.cs) | Session → AdapterFactory → Activator |
| 模型初始化失败会留下页面吗 | [AdapterFactory.CreateDocumentAsync](../../../Host/MyAvaloniaManagement/Business/Docking/HostDockAdapterFactory.cs) | catch 中 Adapter / activation 的互斥释放 |
| 为什么同路径不重复打开 | [Coordinator.OpenAllAsync](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentPersistenceCoordinator.cs) | OperationGate → Session.TryActivateDocument |
| 恢复备份何时弹窗 | 同文件 LoadPrimaryOrRecoveryAsync | 候选完整准备、using、恢复登记 |
| 文件什么时候算保存成功 | [SaveService.SaveCoreAsync](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentSaveService.cs) | 主写入后 CommitFile 与后续警告 |
| 用户取消后为何仍可执行命令 | [CloseCoordinator](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentCloseCoordinator.cs) | ReleaseRange / TryReopenCommands |
| 控件和模型谁先释放 | [ManagedDocumentDockable.Dispose](../../../Host/MyAvaloniaManagement/Business/Docking/ManagedDocumentDockable.cs) | View Lease → activation → ScopeManager |
| 退出为什么要等创建操作 | [DocumentOperationGate](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentOperationGate.cs) | pending 计数 → RuntimeShutdown |

链接定位文件，表内方法名用于编辑器搜索；不把易漂移的行号当作永久导航。

## 验证地图

| 行为或风险 | 测试入口 |
| --- | --- |
| 信封字段、版本、深度、内容和身份 | [DocumentEnvelopeV2Tests](../../../Host/MyAvaloniaManagement.Tests/DocumentEnvelopeV2Tests.cs) |
| 创建、打开、保存、恢复和失败发布 | [DocumentPersistenceTests](../../../Host/MyAvaloniaManagement.Tests/DocumentPersistenceTests.cs) |
| 保存确认、重入关闭、取消和最终脏状态 | [DocumentCloseTests](../../../Host/MyAvaloniaManagement.Tests/DocumentCloseTests.cs) |
| 创建来源与分组失效 | [DocumentCreationTargetTests](../../../Host/MyAvaloniaManagement.Tests/DocumentCreationTargetTests.cs) |
| 排队和在途操作关闭 | [DocumentOperationShutdownTests](../../../Host/MyAvaloniaManagement.Tests/DocumentOperationShutdownTests.cs) |
| 容器 Scope 与模型释放 | [DocumentScopeManagerTests](../../../Host/MyAvaloniaManagement.PluginTests/DocumentScopeManagerTests.cs) |
| View 缓存、跨父级和释放 | [DocumentControlRecyclingTests](../../../Host/MyAvaloniaManagement.UiTests/DocumentControlRecyclingTests.cs) |
| 真实窗口范围及最后一页 | [DocumentWindowV16UiTests](../../../Host/MyAvaloniaManagement.UiTests/DocumentWindowV16UiTests.cs) |

## 一个源码走读练习

设页面捕获 R1 内容后又变成 R2，用户同时关闭窗口。先画保存主文件成功点，再跟踪 AcceptChanges 是否清除 R2；随后看 CloseCoordinator 的 HasPendingChanges 和最终复核；最后证明 Scope 在任何失败分支都未提前释放。

这个练习同时连接持久化状态、命令等待、窗口许可和所有权，比孤立记忆类名更能解释设计原因。

## 如何修改文档

算法顺序变化时更新相应机制页的时序图和代码解析；SDK 或文件格式变化时先更新已有权威契约，再调整本文链接和解释。测试变化应维护风险对应关系，不把某天测试数量写成长期架构事实。验证命令见[统一测试索引](../11-reference/04-test-index.md)。
