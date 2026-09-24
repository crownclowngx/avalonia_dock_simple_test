using System;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Navigation;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.Help;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Enablement;
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
using MyAvaloniaManagement.Business.Restart;

namespace MyAvaloniaManagement.Business.Composition;

/// <summary>
/// 依赖注入服务注册扩展方法
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// 按既有顺序登记应用组合根，以共享所有者和明确的服务分组构成唯一 Host 容器。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="registryBuilder">本次插件声明的收集者；缺省时仅在入口创建一次。</param>
    /// <param name="pluginProviders">插件私有 Provider 的所有者，所有注册段借用同一实例。</param>
    /// <param name="documentScopes">记录 Host 与插件 Document Scope 管理器的共享目录。</param>
    /// <param name="shutdownParticipants">记录工厂实际交付对象的退出目录，不能由注册辅助方法另行创建。</param>
    /// <returns>服务集合</returns>
    /// <remarks>
    /// 状态协调器采用单例，保证全应用共享同一 Dock 和布局状态；
    /// 存储服务也作为无状态单例注册，便于 ViewModel 通过接口替换测试实现。
    /// 各分组只追加服务描述符，保留登记顺序与工厂内容；服务何时解析、在哪个线程创建，
    /// 继续由启动与工作台调用链决定。拆分方法不会把依赖解析提前到注册阶段。
    /// </remarks>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        PluginRegistryBuilder? registryBuilder = null,
        PluginProviderOwner? pluginProviders = null,
        DocumentScopeRegistry? documentScopes = null,
        HostShutdownParticipants? shutdownParticipants = null)
    {
        shutdownParticipants ??= new HostShutdownParticipants();
        services.AddSingleton(shutdownParticipants);
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
        RegisterLayoutServices(services);
        RegisterNavigationAndTools(services);
        RegisterPluginStatus(services);
        RegisterHostInteraction(services);
        RegisterWorkflowActions(services, shutdownParticipants);
        RegisterDocumentOperations(services, shutdownParticipants);
        RegisterHostWorkspace(services);
        RegisterPluginRegistry(services, registryBuilder, pluginProviders, shutdownParticipants);
        RegisterWorkbenchCommands(services, shutdownParticipants);
        services.AddSingleton(provider => new WorkspaceCatalog(
            provider.GetRequiredService<HostWorkspaceCatalog>(),
            provider.GetRequiredService<PluginRegistry>(),
            provider.GetRequiredService<PluginAvailabilityReadModel>()));
        RegisterPluginLifecycle(services, shutdownParticipants);
        RegisterWorkspaceActivation(services, pluginProviders);
        RegisterWorkspaceSession(services, shutdownParticipants);

        return services;
    }

    /// <summary>登记布局存储与布局生命周期，实际创建仍由首次解析触发。</summary>
    /// <remarks>
    /// 布局生命周期创建成功后才交给退出参与者记录；存储诊断继续通过可选端口报告。
    /// 本方法不读取布局、不创建生命周期实例，也不改变 Runtime 的最终保存与释放顺序。
    /// </remarks>
    private static void RegisterLayoutServices(
        IServiceCollection services)
    {
        services.AddSingleton(provider => new DockLayoutV3Store(HostDataRootPolicy.ResolveDefault(),
            (code, exception) => provider.GetService<IHostDiagnosticSink>()?.Report(
                new HostDiagnosticDraft(code, HostDiagnosticPhase.Layout) { Exception = exception })));
        services.AddSingleton(provider =>
        {
            var layout = new DockLayoutLifecycle(provider.GetRequiredService<DockLayoutV3Store>());
            provider.GetRequiredService<HostShutdownParticipants>().Record(layout);
            return layout;
        });
    }

    /// <summary>登记外观设置、功能导航和工具中心的查询、动作及窗口入口。</summary>
    /// <remarks>
    /// 这些 Runtime 级服务保留单例寿命；窗口服务仍按需创建窗口模型。
    /// Document 创建入口与工具显隐动作继续使用既有用例，不在注册时创建业务 View。
    /// </remarks>
    private static void RegisterNavigationAndTools(
        IServiceCollection services)
    {
        services.AddSingleton<AppearanceSettingsStore>();
        services.AddSingleton<PluginNavigationSettingsStore>();
        services.AddSingleton<FunctionCenterWindowService>();
        services.AddSingleton<WorkspacePaletteActions>();
        services.AddSingleton<HostNewDocumentCommandHandler>();
        services.AddSingleton<ToolCenterPreferencesStore>();
        services.AddSingleton<ToolCenterPreferences>();
        services.AddSingleton<ToolCenterQuery>();
        services.AddSingleton<ToolCenterActions>();
        services.AddSingleton<ToolCenterWindowService>();
        services.AddSingleton<HostOpenToolCenterCommandHandler>();
    }

    /// <summary>登记插件看板的只读查询、兼容证据与窗口命令。</summary>
    /// <remarks>
    /// 查询优先使用具体诊断会话，再回退到诊断端口中的会话；两个来源都不存在时仍允许解析。
    /// 兼容报告目录与证据读取方式保持原样，组合根不在登记描述符时扫描插件或打开窗口。
    /// </remarks>
    private static void RegisterPluginStatus(
        IServiceCollection services)
    {
        // 查询为 Runtime 级只读服务，窗口模型由窗口服务按需创建，不再登记为 Dock Tool。
        services.AddSingleton<IPluginStatusQuery>(provider => new PluginStatusQuery(
            provider.GetRequiredService<PluginRegistry>(),
            provider.GetRequiredService<PluginAvailabilityReadModel>(),
            provider.GetService<HostDiagnosticSession>() ?? provider.GetService<IHostDiagnosticSink>() as HostDiagnosticSession,
            provider.GetService<PluginDiscoverySnapshot>(), provider.GetService<IPluginEnablementState>()));
        services.AddSingleton<PluginStatusWindowService>();
        services.AddSingleton(_ => new CompatibilityReportStore(System.IO.Path.Combine(HostDataRootPolicy.ResolveDefault(), "compatibility", "reports")));
        services.AddSingleton<IPluginDashboardEvidence>(provider => new PluginDashboardEvidence(
            provider.GetRequiredService<PluginRegistry>(), provider.GetRequiredService<CompatibilityReportStore>(),
            provider.GetRequiredService<TimeProvider>(), System.IO.Path.Combine(AppContext.BaseDirectory, "CompatibilityReports"),
            provider.GetService<PluginDiscoverySnapshot>()));
        services.AddSingleton<HostOpenPluginStatusCommandHandler>();
    }

    /// <summary>登记主题、帮助以及文件和窗口交互端口。</summary>
    /// <remarks>
    /// 主题与窗口服务维持 Host 单例，帮助阅读器仍通过工厂按需新建。
    /// 插件只能解析既有窄交互接口，具体 Window、存储选择器和剪贴板仍由 Host 适配器拥有。
    /// </remarks>
    private static void RegisterHostInteraction(
        IServiceCollection services)
    {
        services.AddSingleton<ApplicationThemeService>();
        services.AddSingleton<HelpContentCatalog>();
        services.AddSingleton<HelpReadingStateStore>();
        services.AddSingleton<Func<IHelpReader>>(_ => static () => new HelpWebReader());
        services.AddSingleton<HelpWindowService>();
        services.AddSingleton<HostOpenHelpCommandHandler>();
        services.AddSingleton<IHostStorageService, AvaloniaHostStorageService>();
        // 插件只能取得窄窗口交互端口；具体 Window、StorageProvider 与 Clipboard 始终留在 Host。
        services.AddSingleton<IPluginWindowInteraction, AvaloniaPluginWindowInteraction>();
    }

    /// <summary>登记 Workflow 目录、授权、运行管理器与关闭端口。</summary>
    /// <remarks>
    /// 运行管理器可能在插件组合期间间接创建，因此工厂成功交付前必须登记真实实例。
    /// 关闭接口通过解析同一管理器建立别名，不能创建第二个运行计数或取消所有者。
    /// </remarks>
    private static void RegisterWorkflowActions(
        IServiceCollection services,
        HostShutdownParticipants shutdownParticipants)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkflowActionCatalogStore>();
        services.AddSingleton(WorkflowActionExecutionLimits.Default);
        services.AddSingleton<IWorkflowActionAuthorizer, AvaloniaWorkflowActionAuthorizer>();
        services.AddSingleton(provider =>
        {
            var instance = new WorkflowActionRunManager(
                provider.GetRequiredService<WorkflowActionCatalogStore>(),
                provider.GetRequiredService<IWorkflowActionScopeFactory>(),
                provider.GetRequiredService<IWorkflowActionAuthorizer>(),
                provider.GetRequiredService<WorkflowActionExecutionLimits>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetService<IHostDiagnosticSink>());
            // 工厂成功交付前登记真实实例，回滚不再通过解析 DI 猜测创建阶段。
            shutdownParticipants.Record(instance);
            return instance;
        });
        services.AddSingleton<IWorkflowActionShutdownParticipant>(provider =>
            provider.GetRequiredService<WorkflowActionRunManager>());
        services.AddSingleton<WorkflowActionShutdownGate>();
    }

    /// <summary>登记文档持久化、关闭、反馈以及依赖这些用例的重启和 Host 命令。</summary>
    /// <remarks>
    /// 保持这段原有登记顺序；命令目录所需的服务在实际解析时才取得，不在此处初始化工作台。
    /// Document 操作门创建成功后才登记，关闭协调器、回收器和生命周期仍共享原单例。
    /// </remarks>
    private static void RegisterDocumentOperations(
        IServiceCollection services,
        HostShutdownParticipants shutdownParticipants)
    {
        services.AddSingleton<DocumentEnvelopeSerializer>();
        services.AddSingleton(_ =>
        {
            var gate = new DocumentOperationGate();
            shutdownParticipants.Record(gate);
            return gate;
        });
        services.AddSingleton<DocumentPersistenceStateStore>();
        services.AddSingleton<DocumentRecoveryRegistry>();
        services.AddSingleton<DocumentSaveService>();
        services.AddSingleton<DocumentOperationState>();
        services.AddSingleton(provider =>
        {
            var feedback = provider.GetRequiredService<DocumentOperationState>();
            return new HostRestartCoordinator(provider.GetService<IHostRestartHandoff>(),
                provider.GetService<IPluginEnablementRestartBarrier>(),
                message => feedback.Apply(DocumentOperationResult.Failure(message)),
                provider.GetService<MyAvaloniaManagement.Business.Plugins.Installation.IPluginInstallationRestartBarrier>());
        });
        services.AddSingleton<IHostRestartActions>(provider => provider.GetRequiredService<HostRestartCoordinator>());
        services.AddSingleton<HostRestartCommandHandler>();
        services.AddSingleton<DocumentPersistenceCoordinator>();
        services.AddSingleton<HostOpenDocumentCommandHandler>();
        services.AddSingleton<HostSaveDocumentCommandHandler>();
        services.AddSingleton<IHostDocumentOpenService>(provider =>
            provider.GetRequiredService<DocumentPersistenceCoordinator>());
        services.AddSingleton<IDocumentInteractionService, AvaloniaDocumentInteractionService>();
        services.AddSingleton(provider => new WorkbenchDocumentCommandLeaseStore(
            provider.GetService<IHostDiagnosticSink>()));
        services.AddSingleton<DocumentCloseCoordinator>();
        // 每个 Host 容器只有一个回收器。App 资源、Dock Style 与关闭链均使用此实例，
        // Lifetime 不通过 Application.Current 或 IServiceProvider 反向定位依赖。
        services.AddSingleton<DocumentControlRecycling>();
        services.AddSingleton<DockDocumentLifetime>();
    }

    /// <summary>登记冻结贡献目录、生命周期状态及只读可用性投影。</summary>
    /// <remarks>
    /// 复用入口选定的 Builder 和插件 Provider 所有者，辅助方法不创建替代实例。
    /// Build 仍在 Registry 首次解析时执行；状态实例创建成功后登记，注册过程不提交插件目录。
    /// </remarks>
    private static void RegisterPluginRegistry(
        IServiceCollection services,
        PluginRegistryBuilder registryBuilder,
        PluginProviderOwner pluginProviders,
        HostShutdownParticipants shutdownParticipants)
    {
        services.AddSingleton(provider => registryBuilder.Build(
            provider.GetService<PluginModuleCatalog>(),
            provider.GetService<IHostDiagnosticSink>(),
            pluginProviders));
        // 这些实现是刻意保持 internal 的 Host 编排细节。使用显式工厂既避免为了 DI
        // 把构造函数扩大为 public，也把组合根需要的依赖完整列出，防止容器约定成为隐式 API。
        services.AddSingleton(provider =>
        {
            var instance = new PluginLifecycleStateStore(
                provider.GetRequiredService<PluginRegistry>());
            // 工厂成功交付前登记真实实例，回滚不再通过解析 DI 猜测创建阶段。
            shutdownParticipants.Record(instance);
            return instance;
        });
        services.AddSingleton(provider => new PluginAvailabilityReadModel(
            provider.GetRequiredService<PluginLifecycleStateStore>()));
    }

    /// <summary>登记命令目录、上下文、状态、执行与根级展示对象。</summary>
    /// <remarks>
    /// 这些工厂会间接解析 Workspace，调用线程与解析时机继续由启动协调器控制。
    /// 执行器在交付前登记且与关闭接口共用实例；展示接口也引用唯一组合对象，不另建缓存。
    /// </remarks>
    private static void RegisterWorkbenchCommands(
        IServiceCollection services,
        HostShutdownParticipants shutdownParticipants)
    {
        // 元数据可在后台校验；实际 Handler 仅在 UI 组合边界解析。固定身份与实例显式绑定，
        // 不把 Provider 闭包保存在目录里，也不提供按字符串查服务的后门。
        services.AddSingleton(_ => new HostWorkbenchCommandCatalog());
        services.AddSingleton(provider => new HostWorkbenchCommandBindings(
            provider.GetRequiredService<HostWorkbenchCommandCatalog>(),
            [
                new(HostWorkbenchCommandIds.OpenDocument, provider.GetRequiredService<HostOpenDocumentCommandHandler>()),
                new(HostWorkbenchCommandIds.SaveDocument, provider.GetRequiredService<HostSaveDocumentCommandHandler>()),
                new(HostWorkbenchCommandIds.OpenHelp, provider.GetRequiredService<HostOpenHelpCommandHandler>()),
                new(HostWorkbenchCommandIds.NewDocument, provider.GetRequiredService<HostNewDocumentCommandHandler>()),
                new(HostWorkbenchCommandIds.OpenToolCenter, provider.GetRequiredService<HostOpenToolCenterCommandHandler>()),
                new(HostWorkbenchCommandIds.OpenPluginStatus, provider.GetRequiredService<HostOpenPluginStatusCommandHandler>()),
                new(HostWorkbenchCommandIds.Restart, provider.GetRequiredService<HostRestartCommandHandler>()),
            ]));
        services.AddSingleton(provider => new WorkbenchCommandCatalog(
            provider.GetRequiredService<HostWorkbenchCommandCatalog>(),
            provider.GetRequiredService<PluginRegistry>()));
        services.AddSingleton(_ => new HostWorkbenchCommandProjectionCatalog());
        services.AddSingleton(provider => new WorkbenchContextStore(
            provider.GetRequiredService<WorkspaceSession>()));
        services.AddSingleton(provider => new WorkbenchCommandStateQuery(
            provider.GetRequiredService<WorkbenchCommandCatalog>(),
            provider.GetRequiredService<HostWorkbenchCommandBindings>(),
            provider.GetRequiredService<PluginAvailabilityReadModel>(),
            provider.GetRequiredService<WorkbenchContextStore>(),
            provider.GetService<IHostDiagnosticSink>(),
            provider.GetRequiredService<IHostRestartActions>()));
        services.AddSingleton(provider =>
        {
            var instance = new WorkbenchCommandExecutor(
                provider.GetRequiredService<WorkbenchCommandStateQuery>(),
                provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>(),
                provider.GetService<IHostDiagnosticSink>());
            // 工厂成功交付前登记真实实例，回滚不再通过解析 DI 猜测创建阶段。
            shutdownParticipants.Record(instance);
            return instance;
        });
        services.AddSingleton<IWorkbenchCommandShutdownParticipant>(provider =>
            provider.GetRequiredService<WorkbenchCommandExecutor>());
        services.AddSingleton<WorkbenchCommandShutdownGate>();
        services.AddSingleton(provider => new WorkbenchCommandPresentation(
            provider.GetRequiredService<HostWorkbenchCommandProjectionCatalog>(),
            provider.GetRequiredService<PluginRegistry>(),
            provider.GetRequiredService<WorkbenchCommandCatalog>(),
            provider.GetRequiredService<WorkbenchCommandStateQuery>(),
            provider.GetRequiredService<WorkbenchCommandExecutor>(),
            provider.GetRequiredService<PluginAvailabilityReadModel>(),
            Dispatcher.UIThread,
            provider.GetService<IHostDiagnosticSink>(),
            provider.GetRequiredService<ToolWorkspaceReadModel>(),
            provider.GetRequiredService<WorkspaceSession>(),
            provider.GetRequiredService<DocumentCreationMenuQuery>(),
            provider.GetRequiredService<WorkspacePaletteActions>(),
            provider.GetRequiredService<HostIconRenderer>()));
        services.AddSingleton<IWorkbenchCommandPresentationBindings>(provider =>
            provider.GetRequiredService<WorkbenchCommandPresentation>());
    }

    /// <summary>登记插件生命周期协调器，并在首次创建成功时记录退出参与者。</summary>
    /// <remarks>
    /// 协调器消费已经冻结的 Registry 和既有解析端口；登记本身不会调用插件初始化。
    /// 实际实例登记仍位于工厂返回前，启动失败回滚据此判断已创建对象，不额外解析服务。
    /// </remarks>
    private static void RegisterPluginLifecycle(
        IServiceCollection services,
        HostShutdownParticipants shutdownParticipants)
    {
        services.AddSingleton(provider =>
        {
            var instance = new PluginLifecycleCoordinator(
                provider.GetRequiredService<PluginRegistry>(),
                provider.GetRequiredService<IPluginLifecycleResolver>(),
                provider.GetRequiredService<PluginLifecycleStateStore>(),
                provider.GetService<IHostDiagnosticSink>());
            // 工厂成功交付前登记真实实例，回滚不再通过解析 DI 猜测创建阶段。
            shutdownParticipants.Record(instance);
            return instance;
        });
    }

    /// <summary>登记图标、精确模型与 View 激活器，以及工作台窗口上下文。</summary>
    /// <remarks>
    /// 插件激活器借用入口持有的 Provider 所有者，可用性仍来自既有只读模型。
    /// 保持默认 Dock 适配器与窗口上下文单例，不增加第二个实例目录或服务定位入口。
    /// </remarks>
    private static void RegisterWorkspaceActivation(
        IServiceCollection services,
        PluginProviderOwner pluginProviders)
    {
        services.AddSingleton<HostIconCatalog>();
        services.AddSingleton<HostIconRenderer>();
        services.AddSingleton<ViewLocator>();
        services.AddSingleton<HostWorkspaceActivator>();
        services.AddSingleton(provider => new PluginContributionActivator(
            provider.GetRequiredService<PluginRegistry>(),
            pluginProviders,
            provider.GetRequiredService<PluginAvailabilityReadModel>()));
        services.AddSingleton<IHostDockableFactory, HostDockAdapterFactory>();
        services.AddSingleton<WorkbenchWindowContext>();
    }

    /// <summary>登记唯一工作区 Session、其 Dock 工厂别名及只读查询。</summary>
    /// <remarks>
    /// 工厂先创建，随后创建 Session、挂接回调、登记 Session，成功后才向容器交付。
    /// Dock 工厂别名必须返回 Session 已拥有的对象；查询服务不拥有模型、View 或 Document Scope。
    /// </remarks>
    private static void RegisterWorkspaceSession(
        IServiceCollection services,
        HostShutdownParticipants shutdownParticipants)
    {
        // Session 是工作区状态的唯一所有者；Factory 只作为 Session 内部创建并一次性绑定的
        // Dock Framework Adapter 注册。显式工厂避免构造期循环，也没有使用 IServiceProvider 定位器。
        services.AddSingleton(provider =>
        {
            var dockFactory = new HostDockFactory(provider.GetRequiredService<WorkbenchWindowContext>());
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
            shutdownParticipants.Record(session);
            return session;
        });
        services.AddSingleton(provider =>
            provider.GetRequiredService<WorkspaceSession>().DockFactory);
        services.AddSingleton<ToolWorkspaceReadModel>();
        services.AddSingleton<DocumentCreationMenuQuery>();
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
        services.AddScoped(provider => new WelcomeViewModel(
            () => provider.GetRequiredService<FunctionCenterWindowService>().ShowOrActivate(),
            () => provider.GetRequiredService<ToolCenterWindowService>().ShowOrActivate()));
        services.AddSingleton<FileSystemTreeViewModel>();
        services.AddSingleton<PlugGroupMenuViewModel>();
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
                        ToolCloseBehavior.Hide)),
                HostTool<PlugGroupMenuViewModel, PlugGroupMenuView>(
                    provider,
                    new ToolDescriptor(
                        HostExtensionIds.PluginMenu,
                        "插件分组菜单",
                        "显示按分类组织的插件文档菜单",
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
        DocumentScopeRegistry? documentScopes = null,
        HostShutdownParticipants? shutdownParticipants = null)
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
    /// 注册主窗口、宿主导航工具 ViewModel 及其窄创建工厂。
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
            provider.GetRequiredService<IWorkbenchCommandPresentationBindings>(),
            provider.GetRequiredService<DocumentOperationState>(),
            provider.GetRequiredService<IHostRestartActions>()));

        services.AddTransient<IHostDesktopShell, HostDesktopShell>();
        services.AddTransient(provider => new App(
            provider.GetRequiredService<IHostDesktopShell>(),
            provider.GetRequiredService<ViewLocator>(),
            provider.GetRequiredService<DocumentControlRecycling>()));

        return services;
    }
}
