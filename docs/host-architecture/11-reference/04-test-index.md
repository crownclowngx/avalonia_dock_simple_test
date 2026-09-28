# 测试索引：按架构风险查找验证

> 核对日期：2026-09-28。用途：当前 Host 架构文档的查找与维护入口。

[返回文档总览](../README.md)

## 使用方法

本页提供测试导航。引用测试表示它是相应行为的核对入口，不自动代表本次全部运行通过。实际命令、结果和边界应留在带日期记录中；人工交互、多屏及真实进程验证不能被纯 Headless 测试代替。

| 风险 | 主要测试 |
| --- | --- |
| 启动任务、取消及 Prepared/Ready | [StartupCoordinatorTests](../../../Host/MyAvaloniaManagement.Tests/StartupCoordinatorTests.cs)、[StartupSplashUiTests](../../../Host/MyAvaloniaManagement.UiTests/StartupSplashUiTests.cs) |
| Runtime 失败回滚与保留 | [HostLifecycleOwnershipTests](../../../Host/MyAvaloniaManagement.PluginTests/HostLifecycleOwnershipTests.cs) |
| 清单和私有依赖解析 | [PluginManifestCompatibilityTests](../../../Host/MyAvaloniaManagement.PluginTests/PluginManifestCompatibilityTests.cs)、[PluginDependencyIsolationTests](../../../Host/MyAvaloniaManagement.PluginTests/PluginDependencyIsolationTests.cs) |
| 注册所有权和冲突 | [PluginRegistrationOwnershipTests](../../../Host/MyAvaloniaManagement.Tests/PluginRegistrationOwnershipTests.cs)、[PluginConflictAnalyzerTests](../../../Host/MyAvaloniaManagement.Tests/PluginConflictAnalyzerTests.cs) |
| 启用设置与过滤时机 | [PluginEnablementServiceTests](../../../Host/MyAvaloniaManagement.Tests/PluginEnablementServiceTests.cs)、[PluginEnablementLoadingTests](../../../Host/MyAvaloniaManagement.PluginTests/PluginEnablementLoadingTests.cs) |
| 安装决策、文件中断及租约 | [PluginInstallPlannerTests](../../../Host/MyAvaloniaManagement.Tests/PluginInstallPlannerTests.cs)、[PluginInstallationTransactionTests](../../../Host/MyAvaloniaManagement.PluginTests/PluginInstallationTransactionTests.cs)、[PluginInstallationProcessTests](../../../Host/MyAvaloniaManagement.PluginTests/PluginInstallationProcessTests.cs) |
| Session 与 Factory 分工 | [WorkspaceSessionAndDockFactoryTests](../../../Host/MyAvaloniaManagement.Tests/WorkspaceSessionAndDockFactoryTests.cs) |
| 文档创建、保存与恢复 | [DocumentPersistenceTests](../../../Host/MyAvaloniaManagement.Tests/DocumentPersistenceTests.cs)、[DocumentEnvelopeV2Tests](../../../Host/MyAvaloniaManagement.Tests/DocumentEnvelopeV2Tests.cs) |
| 关闭后脏状态和一次性许可 | [DocumentCloseTests](../../../Host/MyAvaloniaManagement.Tests/DocumentCloseTests.cs) |
| Scope 与文档门 | [DocumentScopeManagerTests](../../../Host/MyAvaloniaManagement.PluginTests/DocumentScopeManagerTests.cs)、[DocumentOperationShutdownTests](../../../Host/MyAvaloniaManagement.Tests/DocumentOperationShutdownTests.cs) |
| 创建位置及浮窗最后一页 | [DocumentCreationTargetTests](../../../Host/MyAvaloniaManagement.Tests/DocumentCreationTargetTests.cs)、[DocumentWindowV16UiTests](../../../Host/MyAvaloniaManagement.UiTests/DocumentWindowV16UiTests.cs) |
| Layout 格式、写入与恢复 | [DockLayoutV3Tests](../../../Host/MyAvaloniaManagement.Tests/DockLayoutV3Tests.cs)、[DockLayoutV3StoreTests](../../../Host/MyAvaloniaManagement.Tests/DockLayoutV3StoreTests.cs)、[DockLayoutSaveQueueTests](../../../Host/MyAvaloniaManagement.Tests/DockLayoutSaveQueueTests.cs) |
| Tool 显隐、分割和浮窗关闭 | [ToolCenterTests](../../../Host/MyAvaloniaManagement.Tests/ToolCenterTests.cs)、[DockToolSplitUiTests](../../../Host/MyAvaloniaManagement.UiTests/DockToolSplitUiTests.cs)、[DockToolWindowCloseUiTests](../../../Host/MyAvaloniaManagement.UiTests/DockToolWindowCloseUiTests.cs) |
| 命令目标、状态与关闭竞态 | [WorkbenchCommandContextStateTests](../../../Host/MyAvaloniaManagement.Tests/WorkbenchCommandContextStateTests.cs)、[WorkbenchCommandDocumentTargetTests](../../../Host/MyAvaloniaManagement.Tests/WorkbenchCommandDocumentTargetTests.cs) |
| 命令面板分组及输入 | [CommandPaletteV18Tests](../../../Host/MyAvaloniaManagement.Tests/CommandPaletteV18Tests.cs)、[CommandPaletteV18UiTests](../../../Host/MyAvaloniaManagement.UiTests/CommandPaletteV18UiTests.cs) |
| Workflow 调用与退出 | [WorkflowActionKernelTests](../../../Host/MyAvaloniaManagement.Tests/WorkflowActionKernelTests.cs)、[WorkflowActionG1IntegrationTests](../../../Host/MyAvaloniaManagement.PluginTests/WorkflowActionG1IntegrationTests.cs)、[WorkflowActionShutdownGateTests](../../../Host/MyAvaloniaManagement.Tests/WorkflowActionShutdownGateTests.cs) |
| 诊断和兼容报告 | [HostDiagnosticsTests](../../../Host/MyAvaloniaManagement.Tests/HostDiagnosticsTests.cs)、[PluginCompatibilityEvidenceTests](../../../Host/MyAvaloniaManagement.Tests/PluginCompatibilityEvidenceTests.cs) |
| 帮助嵌入、渲染和窗口寿命 | [HelpContentTests](../../../Host/MyAvaloniaManagement.Tests/HelpContentTests.cs)、[HelpWindowTests](../../../Host/MyAvaloniaManagement.UiTests/HelpWindowTests.cs) |

## 典型验证命令

在仓库根目录运行。日常完整开发入口与附加政策以[主仓验证说明](../../maintenance/verification.md)为准：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

针对帮助文档的局部回归示例：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj -c Debug --filter FullyQualifiedName~HelpContentTests
dotnet test Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj -c Debug --filter FullyQualifiedName~HelpWindowTests
```

项目依赖 Host 本地框架补丁，首次环境准备依照根 README。不要用 --no-build 读取旧嵌入资源来验证新增 Markdown。架构行为改动则按风险补跑对应单元、插件或 UI 测试，必要时运行 Gate；单纯链接调整不需要重做所有业务实机验收。

## 测试能证明到哪里

断言方法名只是入口，最终应阅读安排、动作和断言。故障注入测试适合验证事务中断与释放；真实文件/进程测试适合验证锁和交接；UI 测试适合验证事件顺序和绑定。相应层面的失败应先定位原因，而不是不断重复所有测试。
