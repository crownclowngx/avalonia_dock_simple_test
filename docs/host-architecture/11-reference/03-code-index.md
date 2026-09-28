# 源码索引：从类型反查架构机制

> 核对日期：2026-09-28。范围：本组正文已引用的 Host 与 SDK C# 源码；测试独立见测试索引。

[返回文档总览](../README.md) · [测试索引](04-test-index.md)

## 使用方式

每行左侧进入真实源码，右侧回到解释其职责或机制的文档。一个文件可能承载多个内部类型，文件名不等于完整类型清单。优先从模块正文理解上下文，再按方法名在编辑器内定位；无需依赖易漂移的固定行号。

## Host/MyAvaloniaManagement

| 源码 | 机制导读 |
| --- | --- |
| [Program.cs](../../../Host/MyAvaloniaManagement/Program.cs) | [自动重启：请求、许可和跨进程交接](../01-runtime/03-restart-handoff.md)、[运行时：组合、启动、退出与重启](../01-runtime/README.md)、[系统边界：Host、插件、SDK 与框架](../00-overview/01-system-boundary.md) |
| [ViewLocator.cs](../../../Host/MyAvaloniaManagement/ViewLocator.cs) | [Adapter 与 View：普通模型如何进入 Dock](../05-docking-layout/01-adapters-and-views.md) |
| [MainWindowViewModel.cs](../../../Host/MyAvaloniaManagement/ViewModels/MainWindowViewModel.cs) | [主窗口与绑定：谁协调 UI，谁拥有业务状态](../08-presentation/01-shell-and-bindings.md)、[场景：退出 Host 并安全释放对象图](../10-end-to-end/08-shutdown.md) |
| [CommandPaletteView.axaml.cs](../../../Host/MyAvaloniaManagement/Views/CommandPaletteView.axaml.cs) | [命令呈现：菜单、快捷键和四类搜索结果](../06-commands/04-presentation-and-palette.md) |
| [HostFloatingWindow.cs](../../../Host/MyAvaloniaManagement/Views/HostFloatingWindow.cs) | [布局保存与关闭：捕获时机和最终冻结](../05-docking-layout/04-save-queue-and-close.md) |

## Host/MyAvaloniaManagement.PluginSdk

| 源码 | 机制导读 |
| --- | --- |
| [DocumentContracts.cs](../../../Host/MyAvaloniaManagement.PluginSdk/DocumentContracts.cs) | [文档概念：激活、内容、修订和持久化状态](../04-documents/01-concepts.md) |
| [WorkbenchCommandContracts.cs](../../../Host/MyAvaloniaManagement.PluginSdk/WorkbenchCommandContracts.cs) | [命令声明与绑定：目录不持有业务执行对象](../06-commands/01-catalog-and-bindings.md) |
| [WorkflowActionContracts.cs](../../../Host/MyAvaloniaManagement.PluginSdk/WorkflowActionContracts.cs) | [Workflow 目录与契约：调用者身份和固定目录修订](../07-workflow-actions/01-contracts-and-catalog.md) |

## Host/MyAvaloniaManagement/Business/Commands

| 源码 | 机制导读 |
| --- | --- |
| [HostWorkbenchCommandCatalog.cs](../../../Host/MyAvaloniaManagement/Business/Commands/Catalog/HostWorkbenchCommandCatalog.cs) | [命令声明与绑定：目录不持有业务执行对象](../06-commands/01-catalog-and-bindings.md) |
| [WorkbenchCommandCatalog.cs](../../../Host/MyAvaloniaManagement/Business/Commands/Catalog/WorkbenchCommandCatalog.cs) | [命令声明与绑定：目录不持有业务执行对象](../06-commands/01-catalog-and-bindings.md) |
| [WorkbenchCommandTargetExpectation.cs](../../../Host/MyAvaloniaManagement/Business/Commands/Context/WorkbenchCommandTargetExpectation.cs) | [上下文与执行：防止命令落到另一张页面](../06-commands/02-context-and-execution.md)、[场景：从命令面板执行当前页面的命令](../10-end-to-end/05-command-palette.md) |
| [WorkbenchContextStore.cs](../../../Host/MyAvaloniaManagement/Business/Commands/Context/WorkbenchContextStore.cs) | [上下文与执行：防止命令落到另一张页面](../06-commands/02-context-and-execution.md) |
| [HostWorkbenchCommandBindings.cs](../../../Host/MyAvaloniaManagement/Business/Commands/Execution/HostWorkbenchCommandBindings.cs) | [命令声明与绑定：目录不持有业务执行对象](../06-commands/01-catalog-and-bindings.md) |
| [WorkbenchCommandExecutor.cs](../../../Host/MyAvaloniaManagement/Business/Commands/Execution/WorkbenchCommandExecutor.cs) | [上下文与执行：防止命令落到另一张页面](../06-commands/02-context-and-execution.md)、[命令租约与退出：执行完毕才能释放页面](../06-commands/03-leases-and-shutdown.md)、[场景：从命令面板执行当前页面的命令](../10-end-to-end/05-command-palette.md) |
| [WorkbenchDocumentCommandLeaseStore.cs](../../../Host/MyAvaloniaManagement/Business/Commands/Execution/WorkbenchDocumentCommandLeaseStore.cs) | [命令租约与退出：执行完毕才能释放页面](../06-commands/03-leases-and-shutdown.md) |
| [WorkbenchCommandStateQuery.cs](../../../Host/MyAvaloniaManagement/Business/Commands/State/WorkbenchCommandStateQuery.cs) | [上下文与执行：防止命令落到另一张页面](../06-commands/02-context-and-execution.md) |

## Host/MyAvaloniaManagement/Business/Compatibility

| 源码 | 机制导读 |
| --- | --- |
| [CompatibilityMatcher.cs](../../../Host/MyAvaloniaManagement/Business/Compatibility/CompatibilityMatcher.cs) | [兼容性与框架补丁：契约、产物身份和验证证据](../09-infrastructure/03-compatibility-and-patches.md) |
| [PluginDashboardEvidence.cs](../../../Host/MyAvaloniaManagement/Business/Compatibility/PluginDashboardEvidence.cs) | [功能中心、工具中心与插件看板：查询和操作分开](../08-presentation/02-centers-and-dashboard.md)、[兼容性与框架补丁：契约、产物身份和验证证据](../09-infrastructure/03-compatibility-and-patches.md) |

## Host/MyAvaloniaManagement/Business/Composition

| 源码 | 机制导读 |
| --- | --- |
| [HostResourceRetention.cs](../../../Host/MyAvaloniaManagement/Business/Composition/HostResourceRetention.cs) | [关闭与回滚：先证明可以释放](../01-runtime/02-shutdown-and-rollback.md) |
| [HostRuntime.cs](../../../Host/MyAvaloniaManagement/Business/Composition/HostRuntime.cs) | [启动与组合：Prepared 为什么还不是 Ready](../01-runtime/01-startup-and-composition.md)、[运行时：组合、启动、退出与重启](../01-runtime/README.md)、[系统边界：Host、插件、SDK 与框架](../00-overview/01-system-boundary.md) |
| [HostRuntimeShutdown.cs](../../../Host/MyAvaloniaManagement/Business/Composition/HostRuntimeShutdown.cs) | [关闭与回滚：先证明可以释放](../01-runtime/02-shutdown-and-rollback.md)、[从启动到退出：一次 Host 运行的故事](../00-overview/04-runtime-story.md)、[场景：退出 Host 并安全释放对象图](../10-end-to-end/08-shutdown.md) |
| [ServiceCollectionExtensions.cs](../../../Host/MyAvaloniaManagement/Business/Composition/ServiceCollectionExtensions.cs) | [启动与组合：Prepared 为什么还不是 Ready](../01-runtime/01-startup-and-composition.md) |

## Host/MyAvaloniaManagement/Business/Diagnostics

| 源码 | 机制导读 |
| --- | --- |
| [HostDiagnosticFailurePolicy.cs](../../../Host/MyAvaloniaManagement/Business/Diagnostics/HostDiagnosticFailurePolicy.cs) | [诊断与错误边界：草稿如何成为可保存记录](../09-infrastructure/02-diagnostics-and-errors.md) |
| [HostDiagnosticRedactionPolicy.cs](../../../Host/MyAvaloniaManagement/Business/Diagnostics/HostDiagnosticRedactionPolicy.cs) | [诊断与错误边界：草稿如何成为可保存记录](../09-infrastructure/02-diagnostics-and-errors.md) |
| [HostDiagnosticSession.cs](../../../Host/MyAvaloniaManagement/Business/Diagnostics/HostDiagnosticSession.cs) | [诊断与错误边界：草稿如何成为可保存记录](../09-infrastructure/02-diagnostics-and-errors.md) |

## Host/MyAvaloniaManagement/Business/Docking

| 源码 | 机制导读 |
| --- | --- |
| [DockDocumentLifetime.cs](../../../Host/MyAvaloniaManagement/Business/Docking/DockDocumentLifetime.cs) | [Adapter 与 View：普通模型如何进入 Dock](../05-docking-layout/01-adapters-and-views.md) |
| [DockWindowCloseCoordinator.cs](../../../Host/MyAvaloniaManagement/Business/Docking/DockWindowCloseCoordinator.cs) | [关闭与释放：许可、等待、复核和框架重入](../04-documents/06-close-and-release.md)、[布局保存与关闭：捕获时机和最终冻结](../05-docking-layout/04-save-queue-and-close.md) |
| [DocumentControlRecycling.cs](../../../Host/MyAvaloniaManagement/Business/Docking/DocumentControlRecycling.cs) | [Adapter 与 View：普通模型如何进入 Dock](../05-docking-layout/01-adapters-and-views.md) |
| [HostDockAdapterFactory.cs](../../../Host/MyAvaloniaManagement/Business/Docking/HostDockAdapterFactory.cs) | [目录、会话与激活：五种角色的分工](../03-workspace/01-catalog-session-activation.md)、[创建与激活：成功初始化以后才让页面可见](../04-documents/03-create-and-activate.md)、[文档源码导读与验证地图](../04-documents/08-code-and-tests.md) |
| [HostDockFactory.cs](../../../Host/MyAvaloniaManagement/Business/Docking/HostDockFactory.cs) | [布局树与窗口：稳定骨架、浮动内容与位置](../05-docking-layout/02-tree-windows-and-placement.md) |
| [ManagedDocumentDockable.cs](../../../Host/MyAvaloniaManagement/Business/Docking/ManagedDocumentDockable.cs) | [关闭与释放：许可、等待、复核和框架重入](../04-documents/06-close-and-release.md)、[文档源码导读与验证地图](../04-documents/08-code-and-tests.md)、[Adapter 与 View：普通模型如何进入 Dock](../05-docking-layout/01-adapters-and-views.md) |

## Host/MyAvaloniaManagement/Business/Documents

| 源码 | 机制导读 |
| --- | --- |
| [DocumentCloseCoordinator.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentCloseCoordinator.cs) | [文档内部结构：协调器、状态与资源所有者](../04-documents/02-internal-structure.md)、[关闭与释放：许可、等待、复核和框架重入](../04-documents/06-close-and-release.md)、[文档源码导读与验证地图](../04-documents/08-code-and-tests.md) |
| [DocumentEnvelopeSerializer.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentEnvelopeSerializer.cs) | [文件打开与恢复：候选准备完成后再询问](../04-documents/04-open-and-restore.md) |
| [DocumentOperationGate.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentOperationGate.cs) | [文档所有权与并发：一个 Scope 和两个等待边界](../04-documents/07-ownership-and-concurrency.md)、[文档源码导读与验证地图](../04-documents/08-code-and-tests.md) |
| [DocumentPathIdentity.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentPathIdentity.cs) | [存储与原子事务：单文件提交和上层业务提交](../09-infrastructure/01-storage-and-transactions.md) |
| [DocumentPersistenceCoordinator.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentPersistenceCoordinator.cs) | [文档内部结构：协调器、状态与资源所有者](../04-documents/02-internal-structure.md)、[文件打开与恢复：候选准备完成后再询问](../04-documents/04-open-and-restore.md)、[文档源码导读与验证地图](../04-documents/08-code-and-tests.md) |
| [DocumentPersistenceErrorMapper.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentPersistenceErrorMapper.cs) | [诊断与错误边界：草稿如何成为可保存记录](../09-infrastructure/02-diagnostics-and-errors.md) |
| [DocumentPersistenceStateStore.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentPersistenceStateStore.cs) | [文档概念：激活、内容、修订和持久化状态](../04-documents/01-concepts.md) |
| [DocumentSaveService.cs](../../../Host/MyAvaloniaManagement/Business/Documents/DocumentSaveService.cs) | [文档内部结构：协调器、状态与资源所有者](../04-documents/02-internal-structure.md)、[保存与修订：主文件提交以后不能倒写结果](../04-documents/05-save-and-revision.md)、[文档源码导读与验证地图](../04-documents/08-code-and-tests.md) |
| [DocumentScopeManager.cs](../../../Host/MyAvaloniaManagement/Business/Documents/Ownership/DocumentScopeManager.cs) | [文档所有权与并发：一个 Scope 和两个等待边界](../04-documents/07-ownership-and-concurrency.md) |

## Host/MyAvaloniaManagement/Business/Help

| 源码 | 机制导读 |
| --- | --- |
| [HelpContentCatalog.cs](../../../Host/MyAvaloniaManagement/Business/Help/HelpContentCatalog.cs) | [主题、图标与帮助：Host 统一呈现的支撑机制](../08-presentation/03-appearance-icons-help.md) |
| [HelpMarkdownRenderer.cs](../../../Host/MyAvaloniaManagement/Business/Help/HelpMarkdownRenderer.cs) | [主题、图标与帮助：Host 统一呈现的支撑机制](../08-presentation/03-appearance-icons-help.md) |
| [HelpWindowService.cs](../../../Host/MyAvaloniaManagement/Business/Help/HelpWindowService.cs) | [主题、图标与帮助：Host 统一呈现的支撑机制](../08-presentation/03-appearance-icons-help.md) |

## Host/MyAvaloniaManagement/Business/Layout

| 源码 | 机制导读 |
| --- | --- |
| [DockLayoutLifecycle.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutLifecycle.cs) | [布局保存与关闭：捕获时机和最终冻结](../05-docking-layout/04-save-queue-and-close.md) |
| [DockLayoutSaveQueue.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutSaveQueue.cs) | [布局保存与关闭：捕获时机和最终冻结](../05-docking-layout/04-save-queue-and-close.md) |
| [DockLayoutSnapshotV3.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutSnapshotV3.cs) | [布局快照与恢复：纯数据描述空间，实例继续复用](../05-docking-layout/03-snapshot-store-and-restore.md) |
| [DockLayoutV3Json.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutV3Json.cs) | [布局快照与恢复：纯数据描述空间，实例继续复用](../05-docking-layout/03-snapshot-store-and-restore.md) |
| [DockLayoutV3Store.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutV3Store.cs) | [布局快照与恢复：纯数据描述空间，实例继续复用](../05-docking-layout/03-snapshot-store-and-restore.md) |
| [DockLayoutWorkspaceState.Restore.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutWorkspaceState.Restore.cs) | [布局快照与恢复：纯数据描述空间，实例继续复用](../05-docking-layout/03-snapshot-store-and-restore.md) |
| [DockScreenPlacement.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DockScreenPlacement.cs) | [布局树与窗口：稳定骨架、浮动内容与位置](../05-docking-layout/02-tree-windows-and-placement.md) |
| [DocumentCreationTarget.cs](../../../Host/MyAvaloniaManagement/Business/Layout/DocumentCreationTarget.cs) | [页面身份、创建目标与即时查询](../03-workspace/02-pages-targets-and-queries.md) |
| [ToolDockCoordinator.cs](../../../Host/MyAvaloniaManagement/Business/Layout/ToolDockCoordinator.cs) | [工具显隐：复用实例与空间位置恢复](../03-workspace/03-tools-and-visibility.md) |

## Host/MyAvaloniaManagement/Business/Lifecycle

| 源码 | 机制导读 |
| --- | --- |
| [PluginLifecycleCoordinator.cs](../../../Host/MyAvaloniaManagement/Business/Lifecycle/PluginLifecycleCoordinator.cs) | [生命周期与可用性：注册成功之后仍有运行阶段](../02-plugins/03-lifecycle-and-availability.md) |
| [PluginLifecycleOperationRunner.cs](../../../Host/MyAvaloniaManagement/Business/Lifecycle/PluginLifecycleOperationRunner.cs) | [生命周期与可用性：注册成功之后仍有运行阶段](../02-plugins/03-lifecycle-and-availability.md) |
| [PluginLifecycleStateStore.cs](../../../Host/MyAvaloniaManagement/Business/Lifecycle/PluginLifecycleStateStore.cs) | [生命周期与可用性：注册成功之后仍有运行阶段](../02-plugins/03-lifecycle-and-availability.md) |

## Host/MyAvaloniaManagement/Business/PluginStatus

| 源码 | 机制导读 |
| --- | --- |
| [PluginStatusQuery.cs](../../../Host/MyAvaloniaManagement/Business/PluginStatus/PluginStatusQuery.cs) | [插件启用设置：当前事实与下次意图](../02-plugins/04-enablement.md)、[功能中心、工具中心与插件看板：查询和操作分开](../08-presentation/02-centers-and-dashboard.md) |

## Host/MyAvaloniaManagement/Business/Plugins

| 源码 | 机制导读 |
| --- | --- |
| [AssemblyLoaderHelper.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/AssemblyLoaderHelper.cs) | [发现与加载：先识别，再执行插件代码](../02-plugins/01-discovery-and-loading.md) |
| [PluginLoadContext.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginLoadContext.cs) | [发现与加载：先识别，再执行插件代码](../02-plugins/01-discovery-and-loading.md) |
| [PluginManifest.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginManifest.cs) | [发现与加载：先识别，再执行插件代码](../02-plugins/01-discovery-and-loading.md) |
| [PluginModuleCatalog.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginModuleCatalog.cs) | [发现与加载：先识别，再执行插件代码](../02-plugins/01-discovery-and-loading.md) |
| [PluginModulePreflight.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginModulePreflight.cs) | [发现与加载：先识别，再执行插件代码](../02-plugins/01-discovery-and-loading.md) |
| [PluginSharedAssemblyPolicy.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginSharedAssemblyPolicy.cs) | [发现与加载：先识别，再执行插件代码](../02-plugins/01-discovery-and-loading.md) |
| [PluginEnablementService.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Enablement/PluginEnablementService.cs) | [插件启用设置：当前事实与下次意图](../02-plugins/04-enablement.md) |
| [PluginEnablementSettingsStore.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Enablement/PluginEnablementSettingsStore.cs) | [插件启用设置：当前事实与下次意图](../02-plugins/04-enablement.md) |
| [PluginInstallApplier.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Installation/PluginInstallApplier.cs) | [本地 ZIP 安装：预览、事务日志与下次启动恢复](../02-plugins/05-installation-and-recovery.md) |
| [PluginInstallationService.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Installation/PluginInstallationService.cs) | [本地 ZIP 安装：预览、事务日志与下次启动恢复](../02-plugins/05-installation-and-recovery.md)、[场景：检查本地插件包并重启应用更新](../10-end-to-end/07-install-and-restart.md) |
| [PluginInstallationSession.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Installation/PluginInstallationSession.cs) | [本地 ZIP 安装：预览、事务日志与下次启动恢复](../02-plugins/05-installation-and-recovery.md)、[场景：检查本地插件包并重启应用更新](../10-end-to-end/07-install-and-restart.md) |
| [PluginConflictAnalyzer.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginConflictAnalyzer.cs) | [注册与容器：声明提交和实例激活分开](../02-plugins/02-registration-and-containers.md) |
| [PluginContributionActivator.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginContributionActivator.cs) | [注册与容器：声明提交和实例激活分开](../02-plugins/02-registration-and-containers.md) |
| [PluginContributionValidator.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginContributionValidator.cs) | [注册与容器：声明提交和实例激活分开](../02-plugins/02-registration-and-containers.md) |
| [PluginProviderOwner.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginProviderOwner.cs) | [注册与容器：声明提交和实例激活分开](../02-plugins/02-registration-and-containers.md)、[Workflow 调用：先治理，后进入 Provider Scope](../07-workflow-actions/02-invocation-and-authorization.md)、[场景：插件调用另一个插件的 Workflow Action](../10-end-to-end/06-workflow-call.md) |
| [PluginRegistryBuilder.cs](../../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginRegistryBuilder.cs) | [注册与容器：声明提交和实例激活分开](../02-plugins/02-registration-and-containers.md) |

## Host/MyAvaloniaManagement/Business/Presentation

| 源码 | 机制导读 |
| --- | --- |
| [UiRefreshScheduler.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/Commands/UiRefreshScheduler.cs) | [命令呈现：菜单、快捷键和四类搜索结果](../06-commands/04-presentation-and-palette.md) |
| [WorkbenchCommandPaletteProjection.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchCommandPaletteProjection.cs) | [命令呈现：菜单、快捷键和四类搜索结果](../06-commands/04-presentation-and-palette.md)、[场景：从命令面板执行当前页面的命令](../10-end-to-end/05-command-palette.md) |
| [WorkbenchCommandPresentation.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchCommandPresentation.cs) | [命令呈现：菜单、快捷键和四类搜索结果](../06-commands/04-presentation-and-palette.md) |
| [WorkspacePaletteActions.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkspacePaletteActions.cs) | [命令呈现：菜单、快捷键和四类搜索结果](../06-commands/04-presentation-and-palette.md) |
| [FunctionCenterWindowService.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/FunctionCenterWindowService.cs) | [功能中心、工具中心与插件看板：查询和操作分开](../08-presentation/02-centers-and-dashboard.md) |
| [HostDesktopShell.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/HostDesktopShell.cs) | [主窗口与绑定：谁协调 UI，谁拥有业务状态](../08-presentation/01-shell-and-bindings.md) |
| [HostIconCatalog.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/Icons/HostIconCatalog.cs) | [主题、图标与帮助：Host 统一呈现的支撑机制](../08-presentation/03-appearance-icons-help.md) |
| [StartupDesktopShell.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/StartupDesktopShell.cs) | [启动与组合：Prepared 为什么还不是 Ready](../01-runtime/01-startup-and-composition.md)、[场景：启动 Host 并显示可用工作台](../10-end-to-end/01-startup.md) |
| [WindowContentFullscreenSession.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/WindowContentFullscreenSession.cs) | [主窗口与绑定：谁协调 UI，谁拥有业务状态](../08-presentation/01-shell-and-bindings.md) |
| [WorkbenchWindowContext.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/WorkbenchWindowContext.cs) | [布局树与窗口：稳定骨架、浮动内容与位置](../05-docking-layout/02-tree-windows-and-placement.md) |
| [WorkbenchWindowInteraction.cs](../../../Host/MyAvaloniaManagement/Business/Presentation/WorkbenchWindowInteraction.cs) | [主窗口与绑定：谁协调 UI，谁拥有业务状态](../08-presentation/01-shell-and-bindings.md) |

## Host/MyAvaloniaManagement/Business/Restart

| 源码 | 机制导读 |
| --- | --- |
| [HostLaunchSpecification.cs](../../../Host/MyAvaloniaManagement/Business/Restart/HostLaunchSpecification.cs) | [自动重启：请求、许可和跨进程交接](../01-runtime/03-restart-handoff.md) |
| [HostRestartCoordinator.cs](../../../Host/MyAvaloniaManagement/Business/Restart/HostRestartCoordinator.cs) | [自动重启：请求、许可和跨进程交接](../01-runtime/03-restart-handoff.md) |
| [RestartHandoffSession.cs](../../../Host/MyAvaloniaManagement/Business/Restart/RestartHandoffSession.cs) | [自动重启：请求、许可和跨进程交接](../01-runtime/03-restart-handoff.md) |
| [RestartHelperRunner.cs](../../../Host/MyAvaloniaManagement/Business/Restart/RestartHelperRunner.cs) | [自动重启：请求、许可和跨进程交接](../01-runtime/03-restart-handoff.md) |

## Host/MyAvaloniaManagement/Business/Startup

| 源码 | 机制导读 |
| --- | --- |
| [StartupCoordinator.cs](../../../Host/MyAvaloniaManagement/Business/Startup/StartupCoordinator.cs) | [启动与组合：Prepared 为什么还不是 Ready](../01-runtime/01-startup-and-composition.md) |
| [StartupWorker.cs](../../../Host/MyAvaloniaManagement/Business/Startup/StartupWorker.cs) | [启动与组合：Prepared 为什么还不是 Ready](../01-runtime/01-startup-and-composition.md) |

## Host/MyAvaloniaManagement/Business/Storage

| 源码 | 机制导读 |
| --- | --- |
| [AtomicFileTransaction.cs](../../../Host/MyAvaloniaManagement/Business/Storage/AtomicFileTransaction.cs) | [存储与原子事务：单文件提交和上层业务提交](../09-infrastructure/01-storage-and-transactions.md) |
| [HostDataRootPolicy.cs](../../../Host/MyAvaloniaManagement/Business/Storage/HostDataRootPolicy.cs) | [存储与原子事务：单文件提交和上层业务提交](../09-infrastructure/01-storage-and-transactions.md) |
| [IHostStorageService.cs](../../../Host/MyAvaloniaManagement/Business/Storage/IHostStorageService.cs) | [存储与原子事务：单文件提交和上层业务提交](../09-infrastructure/01-storage-and-transactions.md) |

## Host/MyAvaloniaManagement/Business/ToolCenter

| 源码 | 机制导读 |
| --- | --- |
| [ToolCenterActions.cs](../../../Host/MyAvaloniaManagement/Business/ToolCenter/ToolCenterActions.cs) | [工具显隐：复用实例与空间位置恢复](../03-workspace/03-tools-and-visibility.md) |

## Host/MyAvaloniaManagement/Business/WorkflowActions

| 源码 | 机制导读 |
| --- | --- |
| [WorkflowActionCatalogStore.cs](../../../Host/MyAvaloniaManagement/Business/WorkflowActions/WorkflowActionCatalogStore.cs) | [Workflow 目录与契约：调用者身份和固定目录修订](../07-workflow-actions/01-contracts-and-catalog.md) |
| [WorkflowActionRuntime.cs](../../../Host/MyAvaloniaManagement/Business/WorkflowActions/WorkflowActionRuntime.cs) | [Workflow 调用：先治理，后进入 Provider Scope](../07-workflow-actions/02-invocation-and-authorization.md)、[Workflow 预算与退出：超时不会强杀插件](../07-workflow-actions/03-budgets-and-shutdown.md)、[场景：插件调用另一个插件的 Workflow Action](../10-end-to-end/06-workflow-call.md) |
| [WorkflowActionSchemaValidator.cs](../../../Host/MyAvaloniaManagement/Business/WorkflowActions/WorkflowActionSchemaValidator.cs) | [Workflow 目录与契约：调用者身份和固定目录修订](../07-workflow-actions/01-contracts-and-catalog.md) |

## Host/MyAvaloniaManagement/Business/Workspace

| 源码 | 机制导读 |
| --- | --- |
| [PendingWorkspaceDocument.cs](../../../Host/MyAvaloniaManagement/Business/Workspace/PendingWorkspaceDocument.cs) | [目录、会话与激活：五种角色的分工](../03-workspace/01-catalog-session-activation.md)、[创建与激活：成功初始化以后才让页面可见](../04-documents/03-create-and-activate.md) |
| [ToolWorkspaceReadModel.cs](../../../Host/MyAvaloniaManagement/Business/Workspace/ToolWorkspaceReadModel.cs) | [工具显隐：复用实例与空间位置恢复](../03-workspace/03-tools-and-visibility.md) |
| [WorkspaceCatalog.cs](../../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceCatalog.cs) | [目录、会话与激活：五种角色的分工](../03-workspace/01-catalog-session-activation.md) |
| [WorkspaceLayoutQuerySnapshot.cs](../../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceLayoutQuerySnapshot.cs) | [页面身份、创建目标与即时查询](../03-workspace/02-pages-targets-and-queries.md) |
| [WorkspaceSession.cs](../../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceSession.cs) | [目录、会话与激活：五种角色的分工](../03-workspace/01-catalog-session-activation.md)、[工具显隐：复用实例与空间位置恢复](../03-workspace/03-tools-and-visibility.md)、[创建与激活：成功初始化以后才让页面可见](../04-documents/03-create-and-activate.md) |
| [WorkspaceSession.Pages.cs](../../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceSession.Pages.cs) | [页面身份、创建目标与即时查询](../03-workspace/02-pages-targets-and-queries.md) |

## 新增类型时如何维护

本索引按当前正文引用建立，不自动声称覆盖 Host 每一个源码文件。新增关键类型后，在相应模块写清它拥有的事实、提交或释放职责，再将源码与机制的双向入口加入本页。普通 DTO、直接转发属性和第三方实现按需要在机制页说明。
