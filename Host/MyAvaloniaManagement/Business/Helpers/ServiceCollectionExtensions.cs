using System;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

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
    /// 状态协调器采用单例，保证全应用共享同一 Dock、消息和布局状态；
    /// 存储服务也作为无状态单例注册，便于 ViewModel 通过接口替换测试实现。
    /// </remarks>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(new PluginLifecycleOptions());
        services.AddSingleton<PluginLifecycleManager>();

        // 注册消息服务为单例
        services.AddSingleton<IMessengerService, MessengerService>();

        // 每个由托管插件创建的 Document 都拥有独立 Scope。插件只依赖公共创建接口，
        // Dock 关闭时则由宿主使用具体管理器释放对应 Scope。
        services.AddDocumentScopeManagement();

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
        services.AddSingleton<DocumentEnvelopeSerializer>();
        services.AddSingleton<DocumentOperationGate>();
        services.AddSingleton<DocumentRecoveryRegistry>();
        services.AddSingleton<DocumentSaveService>();
        services.AddSingleton<IDocumentInteractionService, AvaloniaDocumentInteractionService>();
        services.AddSingleton<DocumentCloseCoordinator>();
        services.AddSingleton(provider => new HostExtensionRegistry(
            provider,
            provider.GetRequiredService<PluginModuleCatalog>(),
            provider.GetServices<IDocumentCreationStrategy>(),
            provider.GetServices<IToolCreationStrategy>(),
            provider.GetService<IHostDiagnosticSink>()));

        // 注册ManagementFactory为单例
        services.AddSingleton(provider => new ManagementFactory(
            provider.GetRequiredService<HostExtensionRegistry>(),
            provider.GetRequiredService<DocumentScopeManager>(),
            provider.GetRequiredService<IMessengerService>(),
            provider.GetRequiredService<DocumentCloseCoordinator>(),
            provider.GetRequiredService<DocumentRecoveryRegistry>()));

        // 注册PluginMenuService为单例，依赖ManagementFactory
        services.AddSingleton<PluginMenuService>(provider =>
        {
            var factory = provider.GetRequiredService<ManagementFactory>();
            return new PluginMenuService(factory);
        });

        return services;
    }

    /// <summary>
    /// 注册由宿主统一持有的每 Document Scope 与关闭取消信号。
    /// </summary>
    /// <remarks>
    /// 将这组注册集中在同一个方法中，是为了确保生产组合根和生命周期测试采用完全相同的
    /// scoped 语义，避免测试只注册 ScopeManager 却遗漏 IDocumentLifetime，导致测试通过、
    /// 正式运行时才暴露取消链不完整的问题。
    /// </remarks>
    public static IServiceCollection AddDocumentScopeManagement(this IServiceCollection services)
    {
        services.AddScoped<DocumentLifetime>();
        services.AddScoped<IDocumentLifetime>(provider =>
            provider.GetRequiredService<DocumentLifetime>());
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
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
        services.AddTransient(provider => new FileSystemTreeViewModel(
            provider.GetRequiredService<IHostStorageService>(),
            provider.GetRequiredService<IMessengerService>()));
        services.AddTransient(provider => new PlugGroupMenuViewModel(
            provider.GetRequiredService<ManagementFactory>(),
            provider.GetRequiredService<PluginMenuService>()));
        services.AddTransient(provider => new ToolManagementViewModel(
            provider.GetRequiredService<ManagementFactory>(),
            provider.GetRequiredService<IMessengerService>()));
        services.AddTransient(provider => new PluginStatusViewModel(
            provider.GetRequiredService<PluginModuleCatalog>(),
            provider.GetRequiredService<PluginLifecycleManager>(),
            provider.GetService<HostDiagnosticSession>()));

        // 注册MainWindowViewModel为瞬态，每次请求都创建新实例
        services.AddTransient(provider => new MainWindowViewModel(
            provider.GetRequiredService<ManagementFactory>(),
            provider.GetRequiredService<PluginMenuService>(),
            provider.GetRequiredService<IMessengerService>(),
            provider.GetRequiredService<DockLayoutLifecycle>(),
            provider.GetRequiredService<IHostStorageService>(),
            provider.GetRequiredService<ApplicationThemeService>(),
            provider.GetRequiredService<DocumentSaveService>(),
            provider.GetRequiredService<DocumentOperationGate>(),
            provider.GetRequiredService<DocumentRecoveryRegistry>(),
            provider.GetRequiredService<IDocumentInteractionService>(),
            provider.GetRequiredService<DocumentEnvelopeSerializer>(),
            provider.GetRequiredService<DocumentCloseCoordinator>()));

        // 内置策略只依赖“创建某类对象”的窄工厂，不依赖整个 IServiceProvider。
        // 工厂闭包只存在于组合根，既保持每次创建的新实例语义，也避免策略成为服务定位器。
        services.AddSingleton<Func<FileSystemTreeViewModel>>(provider =>
            () => provider.GetRequiredService<FileSystemTreeViewModel>());
        services.AddSingleton<Func<PlugGroupMenuViewModel>>(provider =>
            () => provider.GetRequiredService<PlugGroupMenuViewModel>());
        services.AddSingleton<Func<ToolManagementViewModel>>(provider =>
            () => provider.GetRequiredService<ToolManagementViewModel>());
        services.AddSingleton<Func<PluginStatusViewModel>>(provider =>
            () => provider.GetRequiredService<PluginStatusViewModel>());

        // Welcome 策略在 Registry 构建期间已经被创建，而 ManagementFactory 依赖该 Registry。
        // 延迟工厂只在用户点击“显示工具”时解析 ManagementFactory，显式打破构造期循环。
        services.AddSingleton<Func<ManagementFactory>>(provider =>
            () => provider.GetRequiredService<ManagementFactory>());

        services.AddTransient<IHostDesktopShell, HostDesktopShell>();
        services.AddTransient(provider => new App(
            provider.GetRequiredService<IHostDesktopShell>()));

        return services;
    }
}
