using System;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Welcome;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagement.Views.Welcome;
using MyAvaloniaManagement.Views.Tools;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Business.WorkflowActions;

namespace MyAvaloniaManagement.Business.Composition;

/// <summary>
/// 依赖注入服务注册扩展方法
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册应用程序核心服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    /// <remarks>
    /// 状态协调器采用单例，保证全应用共享同一 Dock 和布局状态；
    /// 存储服务也作为无状态单例注册，便于 ViewModel 通过接口替换测试实现。
    /// </remarks>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        PluginRegistryBuilder? registryBuilder = null,
        PluginProviderOwner? pluginProviders = null,
        DocumentScopeRegistry? documentScopes = null)
    {
        registryBuilder ??= new PluginRegistryBuilder();
        pluginProviders ??= new PluginProviderOwner();
        documentScopes ??= new DocumentScopeRegistry();
        services.AddSingleton(registryBuilder);
        services.AddSingleton(pluginProviders);
        services.AddSingleton<IPluginLifecycleResolver>(pluginProviders);
        services.AddSingleton<IWorkflowActionScopeFactory>(pluginProviders);

        // 每个由托管插件创建的 Document 都拥有独立 Scope。插件只依赖公共创建接口，
        // Dock 关闭时则由宿主使用具体管理器释放对应 Scope。
        services.AddDocumentScopeManagement(documentScopes);

        services.AddSingleton(provider =>
        {
            var diagnostics = provider.GetService<IHostDiagnosticSink>();
            return diagnostics is null
                ? new DockLayoutStore()
                : new DockLayoutStore(diagnostics);
        });
        services.AddSingleton<DockLayoutLifecycle>();
        services.AddSingleton<AppearanceSettingsStore>();
        services.AddSingleton<ApplicationThemeService>();
        services.AddSingleton<IHostStorageService, AvaloniaHostStorageService>();
        // 插件只能取得窄窗口交互端口；具体 Window、StorageProvider 与 Clipboard 始终留在 Host。
        services.AddSingleton<IPluginWindowInteraction, AvaloniaPluginWindowInteraction>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkflowActionCatalogStore>();
        services.AddSingleton(WorkflowActionExecutionLimits.Default);
        services.AddSingleton<IWorkflowActionAuthorizer, AvaloniaWorkflowActionAuthorizer>();
        services.AddSingleton(provider => new WorkflowActionRunManager(
            provider.GetRequiredService<WorkflowActionCatalogStore>(),
            provider.GetRequiredService<IWorkflowActionScopeFactory>(),
            provider.GetRequiredService<IWorkflowActionAuthorizer>(),
            provider.GetRequiredService<WorkflowActionExecutionLimits>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IHostDiagnosticSink>()));
        services.AddSingleton<IWorkflowActionShutdownParticipant>(provider =>
            provider.GetRequiredService<WorkflowActionRunManager>());
        services.AddSingleton<WorkflowActionShutdownGate>();
        services.AddSingleton<DocumentEnvelopeSerializer>();
        services.AddSingleton<DocumentOperationGate>();
        services.AddSingleton<DocumentPersistenceStateStore>();
        services.AddSingleton<DocumentRecoveryRegistry>();
        services.AddSingleton<DocumentSaveService>();
        services.AddSingleton<DocumentOperationState>();
        services.AddSingleton<DocumentPersistenceCoordinator>();
        services.AddSingleton<HostOpenDocumentCommandHandler>();
        services.AddSingleton<HostSaveDocumentCommandHandler>();
        services.AddSingleton(provider => new HostWorkbenchCommandCatalog(
            provider.GetRequiredService<HostOpenDocumentCommandHandler>(),
            provider.GetRequiredService<HostSaveDocumentCommandHandler>()));
        services.AddSingleton<IHostDocumentOpenService>(provider =>
            provider.GetRequiredService<DocumentPersistenceCoordinator>());
        services.AddSingleton<IDocumentInteractionService, AvaloniaDocumentInteractionService>();
        services.AddSingleton<DocumentCloseCoordinator>();
        // 每个 Host 容器只有一个回收器。App 资源、Dock Style 与关闭链均使用此实例，
        // Lifetime 不通过 Application.Current 或 IServiceProvider 反向定位依赖。
        services.AddSingleton<DocumentControlRecycling>();
        services.AddSingleton<DockDocumentLifetime>();
        RegisterHostWorkspace(services);
        services.AddSingleton(provider => registryBuilder.Build(
            provider.GetService<PluginModuleCatalog>(),
            provider.GetService<IHostDiagnosticSink>(),
            pluginProviders));
        // 这些实现是刻意保持 internal 的 Host 编排细节。使用显式工厂既避免为了 DI
        // 把构造函数扩大为 public，也把组合根需要的依赖完整列出，防止容器约定成为隐式 API。
        services.AddSingleton(provider => new PluginLifecycleStateStore(
            provider.GetRequiredService<PluginRegistry>()));
        services.AddSingleton(provider => new PluginAvailabilityReadModel(
            provider.GetRequiredService<PluginLifecycleStateStore>()));
        services.AddSingleton(provider => new WorkbenchCommandCatalog(
            provider.GetRequiredService<HostWorkbenchCommandCatalog>(),
            provider.GetRequiredService<PluginRegistry>()));
        services.AddSingleton(provider => new WorkbenchCommandExecutor(
            provider.GetRequiredService<WorkbenchCommandCatalog>(),
            provider.GetRequiredService<PluginAvailabilityReadModel>(),
            provider.GetService<IHostDiagnosticSink>()));
        services.AddSingleton<IWorkbenchCommandShutdownParticipant>(provider =>
            provider.GetRequiredService<WorkbenchCommandExecutor>());
        services.AddSingleton<WorkbenchCommandShutdownGate>();
        services.AddSingleton(provider => new WorkspaceCatalog(
            provider.GetRequiredService<HostWorkspaceCatalog>(),
            provider.GetRequiredService<PluginRegistry>(),
            provider.GetRequiredService<PluginAvailabilityReadModel>()));
        services.AddSingleton(provider => new PluginLifecycleCoordinator(
            provider.GetRequiredService<PluginRegistry>(),
            provider.GetRequiredService<IPluginLifecycleResolver>(),
            provider.GetRequiredService<PluginLifecycleStateStore>(),
            provider.GetService<IHostDiagnosticSink>()));
        services.AddSingleton<ViewLocator>();
        services.AddSingleton<HostWorkspaceActivator>();
        services.AddSingleton(provider => new PluginContributionActivator(
            provider.GetRequiredService<PluginRegistry>(),
            pluginProviders,
            provider.GetRequiredService<PluginAvailabilityReadModel>()));
        services.AddSingleton<IHostDockableFactory, HostDockAdapterFactory>();

        // Session 是工作区状态的唯一所有者；Factory 只作为 Session 内部创建并一次性绑定的
        // Dock Framework Adapter 注册。显式工厂避免构造期循环，也没有使用 IServiceProvider 定位器。
        services.AddSingleton(provider =>
        {
            var dockFactory = new HostDockFactory();
            var session = new WorkspaceSession(
                dockFactory,
                provider.GetRequiredService<WorkspaceCatalog>(),
                provider.GetRequiredService<IHostDockableFactory>(),
                provider.GetRequiredService<DocumentPersistenceStateStore>(),
                provider.GetRequiredService<DocumentCloseCoordinator>(),
                provider.GetRequiredService<DocumentRecoveryRegistry>(),
                provider.GetRequiredService<DockDocumentLifetime>(),
                provider.GetService<IHostDiagnosticSink>());
            dockFactory.AttachCallbacks(session);
            return session;
        });
        services.AddSingleton(provider =>
            provider.GetRequiredService<WorkspaceSession>().DockFactory);
        services.AddSingleton<ToolWorkspaceReadModel>();
        services.AddSingleton<DocumentCreationMenuQuery>();

        return services;
    }

    /// <summary>
    /// 注册 Host 内建模型，并建立与 Plugin Registry 完全分离的不可变工作区目录。
    /// </summary>
    /// <remarks>
    /// 这些声明集中在组合根，新增 Host Tool 或根级 DataTemplate 时必须显式修改此处。目录中的
    /// 模型工厂均绑定精确类型，不接受任意 Type 或服务名；Catalog 因此无需接触 IServiceProvider。
    /// </remarks>
    private static void RegisterHostWorkspace(IServiceCollection services)
    {
        services.AddScoped<WelcomeViewModel>();
        services.AddSingleton<FileSystemTreeViewModel>();
        services.AddSingleton<PlugGroupMenuViewModel>();
        services.AddSingleton<ToolManagementViewModel>();
        services.AddSingleton<PluginStatusViewModel>();
        services.AddSingleton(provider => new HostWorkspaceCatalog(
            [
                new HostWorkspaceDocumentRegistration(
                    new DocumentDescriptor(
                        HostExtensionIds.WelcomeDocument,
                        "欢迎主程序",
                        "显示欢迎信息",
                        "帮助"),
                    typeof(WelcomeViewModel),
                    typeof(WelcomeView),
                    static () => new WelcomeView(),
                    () => provider.GetRequiredService<DocumentScopeManager>()
                        .CreateDocument(typeof(WelcomeViewModel)),
                    static (model, activation, cancellationToken) =>
                        ((WelcomeViewModel)model).InitializeHost(
                            activation,
                            cancellationToken))
            ],
            [
                HostTool<FileSystemTreeViewModel, FileSystemTreeView>(
                    provider,
                    new ToolDescriptor(
                        HostExtensionIds.FileSystemTree,
                        "文件系统浏览器",
                        "浏览和管理文件系统",
                        ToolDockSide.Left,
                        ToolCloseBehavior.Prevent)),
                HostTool<PlugGroupMenuViewModel, PlugGroupMenuView>(
                    provider,
                    new ToolDescriptor(
                        HostExtensionIds.PluginMenu,
                        "插件分组菜单",
                        "显示按分类组织的插件文档菜单",
                        ToolDockSide.Right,
                        ToolCloseBehavior.Prevent)),
                HostTool<ToolManagementViewModel, ToolManagementView>(
                    provider,
                    new ToolDescriptor(
                        HostExtensionIds.ToolManagement,
                        "工具管理",
                        "管理所有工具的显示和隐藏",
                        ToolDockSide.Right,
                        ToolCloseBehavior.Prevent)),
                HostTool<PluginStatusViewModel, PluginStatusView>(
                    provider,
                    new ToolDescriptor(
                        HostExtensionIds.PluginStatus,
                        "插件状态",
                        "查看插件加载、依赖和生命周期诊断",
                        ToolDockSide.Right,
                        ToolCloseBehavior.Hide))
            ]));
    }

    private static HostWorkspaceToolRegistration HostTool<TModel, TView>(
        IServiceProvider provider,
        ToolDescriptor descriptor)
        where TModel : class
        where TView : Avalonia.Controls.Control, new() => new(
            descriptor,
            typeof(TModel),
            typeof(TView),
            static () => new TView(),
            () => provider.GetRequiredService<TModel>());

    /// <summary>
    /// 注册由宿主统一持有的每 Document Scope 与关闭取消信号。
    /// </summary>
    /// <remarks>
    /// 将这组注册集中在同一个方法中，是为了确保生产组合根和生命周期测试采用完全相同的
    /// scoped 语义，避免测试只注册 ScopeManager 却遗漏 IDocumentLifetime，导致测试通过、
    /// 正式运行时才暴露取消链不完整的问题。
    /// </remarks>
    public static IServiceCollection AddDocumentScopeManagement(
        this IServiceCollection services,
        DocumentScopeRegistry? documentScopes = null)
    {
        documentScopes ??= new DocumentScopeRegistry();
        services.AddSingleton(documentScopes);
        services.AddScoped<DocumentLifetime>();
        services.AddScoped<MyAvaloniaManagement.PluginSdk.IDocumentLifetime>(provider =>
            provider.GetRequiredService<DocumentLifetime>());
        services.AddSingleton(provider =>
        {
            var manager = new DocumentScopeManager(
                provider.GetRequiredService<IServiceScopeFactory>());
            documentScopes.Register(manager);
            return manager;
        });
        return services;
    }

    /// <summary>
    /// 注册主窗口、四个宿主工具 ViewModel 及其窄创建工厂。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    /// <remarks>
    /// 生产 ViewModel 采用瞬态生命周期，避免 Headless 测试和窗口实例之间共享可变绑定状态。
    /// 显式注册调用 internal 注入构造函数；设计器使用独立样例，不进入此生产对象图。
    /// </remarks>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // 注册MainWindowViewModel为瞬态，每次请求都创建新实例
        services.AddTransient(provider => new MainWindowViewModel(
            provider.GetRequiredService<WorkspaceSession>(),
            provider.GetRequiredService<DockLayoutLifecycle>(),
            provider.GetRequiredService<ApplicationThemeService>(),
            provider.GetRequiredService<DocumentPersistenceCoordinator>(),
            provider.GetRequiredService<DocumentOperationState>()));

        // 内置策略只依赖“创建某类对象”的窄工厂，不依赖整个 IServiceProvider。
        // 工厂闭包只存在于组合根，既保持每次创建的新实例语义，也避免策略成为服务定位器。
        // Welcome 只获得“显示某个 Tool”这一窄动作，不接收 Session、Dock Factory 或服务容器。
        // 委托在命令执行时解析已构造的唯一 Session，不参与 Session 创建阶段。
        services.AddSingleton<Action<ToolTypeId>>(provider => toolTypeId =>
            provider.GetRequiredService<WorkspaceSession>().ShowTool(toolTypeId));

        services.AddTransient<IHostDesktopShell, HostDesktopShell>();
        services.AddTransient(provider => new App(
            provider.GetRequiredService<IHostDesktopShell>(),
            provider.GetRequiredService<ViewLocator>(),
            provider.GetRequiredService<DocumentControlRecycling>()));

        return services;
    }
}
