using System;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagement.Views.Hello;
using MyAvaloniaManagement.Views.Tools;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

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
        services.AddSingleton<DocumentEnvelopeSerializer>();
        services.AddSingleton<DocumentOperationGate>();
        services.AddSingleton<DocumentPersistenceStateStore>();
        services.AddSingleton<DocumentRecoveryRegistry>();
        services.AddSingleton<DocumentSaveService>();
        services.AddSingleton<DocumentOperationState>();
        services.AddSingleton<DocumentPersistenceCoordinator>();
        services.AddSingleton<IHostDocumentOpenService>(provider =>
            provider.GetRequiredService<DocumentPersistenceCoordinator>());
        services.AddSingleton<IDocumentInteractionService, AvaloniaDocumentInteractionService>();
        services.AddSingleton<DocumentCloseCoordinator>();
        RegisterHostContributions(services, registryBuilder);
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
        services.AddSingleton(provider => new PluginLifecycleCoordinator(
            provider.GetRequiredService<PluginRegistry>(),
            provider.GetRequiredService<IPluginLifecycleResolver>(),
            provider.GetRequiredService<PluginLifecycleStateStore>(),
            provider.GetService<IHostDiagnosticSink>()));
        services.AddSingleton<ViewLocator>();
        services.AddSingleton(provider => new PluginContributionActivator(
            provider,
            provider.GetRequiredService<PluginRegistry>(),
            pluginProviders,
            provider.GetRequiredService<PluginAvailabilityReadModel>()));
        services.AddSingleton<IHostDockableFactory, HostDockAdapterFactory>();

        // 注册ManagementFactory为单例
        services.AddSingleton(provider => new ManagementFactory(
            provider.GetRequiredService<PluginRegistry>(),
            provider.GetRequiredService<IHostDockableFactory>(),
            documentScopes,
            provider.GetRequiredService<DocumentPersistenceStateStore>(),
            provider.GetRequiredService<DocumentCloseCoordinator>(),
            provider.GetRequiredService<DocumentRecoveryRegistry>(),
            provider.GetService<IHostDiagnosticSink>(),
            provider.GetRequiredService<PluginAvailabilityReadModel>()));

        // 注册PluginMenuService为单例，依赖ManagementFactory
        services.AddSingleton<PluginMenuService>(provider =>
        {
            var factory = provider.GetRequiredService<ManagementFactory>();
            return new PluginMenuService(factory);
        });

        return services;
    }

    /// <summary>
    /// 将宿主内置扩展写入与插件完全相同的 Builder，而不是依赖宿主程序集扫描。
    /// </summary>
    /// <remarks>
    /// 这些声明集中在组合根，新增宿主 Tool 或根级 DataTemplate 时必须显式修改此处；这是一项
    /// 有意的可审阅成本，可防止仅因类型名称碰巧匹配就改变最终用户界面。
    /// </remarks>
    private static void RegisterHostContributions(
        IServiceCollection services,
        PluginRegistryBuilder builder)
    {
        var registration = new PluginRegistration(
            HostExtensionIds.V2Owner,
            services,
            builder);
        registration.AddDocument<WelcomeViewModel, WelcomeView>(
            new DocumentDescriptor(
                HostExtensionIds.V2WelcomeDocument,
                "欢迎主程序",
                "显示欢迎信息",
                "帮助"));
        registration.AddTool<FileSystemTreeViewModel, FileSystemTreeView>(
            new ToolDescriptor(
                HostExtensionIds.V2FileSystemTree,
                "文件系统浏览器",
                "浏览和管理文件系统",
                ToolDockSide.Left,
                ToolCloseBehavior.Prevent));
        registration.AddTool<PlugGroupMenuViewModel, PlugGroupMenuView>(
            new ToolDescriptor(
                HostExtensionIds.V2PluginMenu,
                "插件分组菜单",
                "显示按分类组织的插件文档菜单",
                ToolDockSide.Right,
                ToolCloseBehavior.Prevent));
        registration.AddTool<ToolManagementViewModel, ToolManagementView>(
            new ToolDescriptor(
                HostExtensionIds.V2ToolManagement,
                "工具管理",
                "管理所有工具的显示和隐藏",
                ToolDockSide.Right,
                ToolCloseBehavior.Prevent));
        registration.AddTool<PluginStatusViewModel, PluginStatusView>(
            new ToolDescriptor(
                HostExtensionIds.V2PluginStatus,
                "插件状态",
                "查看插件加载、依赖和生命周期诊断",
                ToolDockSide.Right,
                ToolCloseBehavior.Hide));
        registration.Seal();
        PluginServiceCommitGuard.AppendHostContributions(services, registration);
    }

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
            provider.GetRequiredService<ManagementFactory>(),
            provider.GetRequiredService<PluginMenuService>(),
            provider.GetRequiredService<DockLayoutLifecycle>(),
            provider.GetRequiredService<ApplicationThemeService>(),
            provider.GetRequiredService<DocumentPersistenceCoordinator>(),
            provider.GetRequiredService<DocumentOperationState>(),
            provider.GetRequiredService<DocumentCloseCoordinator>()));

        // 内置策略只依赖“创建某类对象”的窄工厂，不依赖整个 IServiceProvider。
        // 工厂闭包只存在于组合根，既保持每次创建的新实例语义，也避免策略成为服务定位器。
        // Welcome 策略在 Registry 构建期间已经被创建，而 ManagementFactory 依赖该 Registry。
        // 延迟工厂只在用户点击“显示工具”时解析 ManagementFactory，显式打破构造期循环。
        services.AddSingleton<Func<ManagementFactory>>(provider =>
            () => provider.GetRequiredService<ManagementFactory>());

        services.AddTransient<IHostDesktopShell, HostDesktopShell>();
        services.AddTransient(provider => new App(
            provider.GetRequiredService<IHostDesktopShell>(),
            provider.GetRequiredService<ViewLocator>()));

        return services;
    }
}
