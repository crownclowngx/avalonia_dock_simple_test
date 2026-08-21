using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Create;
using DaTangAccountingHelpPlug.Create.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Plugin;
using DaTangAccountingHelpPlug.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.ToolCreation;
using MyPlugTest.Plugin;
using MySmallTools.InitPlug.SecretVideoPlayer;
using MySmallTools.Plugin;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MyAvaloniaManagement.PluginTests;

public sealed class PluginCompatibilityTests
{
    [Fact]
    public void LayoutV2不再公开历史ToolId归一化入口()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        _ = provider.GetRequiredService<MyAvaloniaManagement.ViewModels.ManagementFactory>();

        Assert.Null(typeof(MyAvaloniaManagement.ViewModels.ManagementFactory).GetMethod(
            "NormalizePersistedToolId",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public void 三个未迁移插件继续显式接入Legacy模块且不改变公共策略接口()
    {
        Assert.DoesNotContain(
            typeof(IPluginModule).GetProperties(),
            property => property.Name == "PluginId");
        Assert.DoesNotContain(
            typeof(IPluginLifecycle).GetProperties(),
            property => property.Name == "PluginId");
        Assert.Equal(
            ["Configure"],
            typeof(IPluginModule).GetMethods().Select(method => method.Name));
        Assert.Equal(2, typeof(IDocumentCreationStrategy).GetMethods().Length);
        Assert.Equal(2, typeof(IToolCreationStrategy).GetMethods().Length);

        var services = new ServiceCollection();
        var contexts = new[]
        {
            ConfigureForInspection(new BiliDownloaderPluginModule(),
                "myavalonia.plugin.bili-downloader", services),
            ConfigureForInspection(new DaTangAccountingHelpPluginModule(),
                "myavalonia.plugin.datang-accounting-help", services),
            ConfigureForInspection(new MySmallToolsPluginModule(),
                "myavalonia.plugin.my-small-tools", services),
        };
        Assert.Equal(
            [
                "Document:BiliDownloaderDocumentStrategy",
                "Tool:BiliSchedulerToolStrategy",
                "View:BiliDownloaderViewModel->BiliDownloaderView",
                "View:BiliSchedulerToolViewModel->BiliSchedulerToolView",
                "Lifecycle:BiliDownloaderPluginLifecycle",
            ],
            Describe(contexts[0]));
        Assert.Equal(
            [
                "Document:InvoiceInfoImportDocumentStrategy",
                "Document:BankBalanceReconciliationDocumentStrategy",
                "View:InvoiceInfoImportViewModel->InvoiceInfoImportView",
                "View:BankBalanceReconciliationViewModel->BankBalanceReconciliationView",
            ],
            Describe(contexts[1]));
        Assert.Equal(
            [
                "Document:SecretVideoDocumentStrategy",
                "Document:SecretVideoLibraryDocumentStrategy",
                "Document:VideoEncryptorDocumentStrategy",
                "Document:VideoDecryptorDocumentStrategy",
                "View:SecretVideoPlayerViewModel->SecretVideoPlayerView",
                "View:SecretVideoLibraryViewModel->SecretVideoLibraryView",
                "View:VideoEncryptorViewModel->VideoEncryptorView",
                "View:VideoDecryptorViewModel->VideoDecryptorView",
            ],
            Describe(contexts[2]));

        Assert.Equal(
            [
                "myavalonia.plugin.bili-downloader",
                "myavalonia.plugin.datang-accounting-help",
                "myavalonia.plugin.my-small-tools",
            ],
            contexts.Select(context => context.PluginId.Value));
    }

    [Fact]
    public void 插件状态模型投影四个托管模块与G5生命周期声明()
    {
        var registry = new PluginRegistry(
            CreatePluginSnapshots(), [], [],
            [new PluginLifecycleDeclaration(
                new MyAvaloniaManagement.PluginSdk.PluginId(
                    "myavalonia.plugin.bili-downloader"),
                typeof(ReadyBiliLifecycle))]);
        var states = new PluginLifecycleStateStore(registry);
        states.SetState(new PluginLifecycleState(
            new MyAvaloniaManagement.PluginSdk.PluginId(
                "myavalonia.plugin.bili-downloader"),
            PluginLifecycleStatus.Ready));
        var viewModel = new PluginStatusViewModel(
            registry,
            availability: new PluginAvailabilityReadModel(states));

        Assert.Equal(4, viewModel.Items.Count);
        Assert.Equal(
            [
                "myavalonia.plugin.bili-downloader",
                "myavalonia.plugin.datang-accounting-help",
                "myavalonia.plugin.my-plug-test",
                "myavalonia.plugin.my-small-tools"
            ],
            viewModel.Items.Select(item => item.PluginId));
        Assert.Equal(
            "生命周期初始化成功",
            viewModel.Items.Single(item =>
                item.PluginId == "myavalonia.plugin.bili-downloader").StatusText);
        Assert.All(
            viewModel.Items.Where(item =>
                item.PluginId != "myavalonia.plugin.bili-downloader"),
            item => Assert.Contains("无需后台生命周期", item.StatusText));
    }

    [Theory]
    [InlineData("NotStarted", "等待生命周期初始化", "尚不可用")]
    [InlineData("Initializing", "正在初始化", "尚不可用")]
    [InlineData("InitializationFailed", "生命周期初始化失败", "已隔离")]
    [InlineData("InitializationTimedOut", "生命周期初始化超时", "已隔离")]
    [InlineData("HostCancelled", "生命周期被宿主取消", "已隔离")]
    [InlineData("Stopping", "正在停止", "正在退出")]
    [InlineData("Stopped", "生命周期已停止", "已停止")]
    [InlineData("ShutdownFailed", "生命周期停止失败", "正在退出")]
    [InlineData("ShutdownTimedOut", "生命周期停止超时", "正在退出")]
    public void 插件状态Tool投影初始化隔离与停止状态(
        string statusName,
        string expectedStatus,
        string expectedAvailability)
    {
        var status = Enum.Parse<PluginLifecycleStatus>(statusName);
        var owner = new MyAvaloniaManagement.PluginSdk.PluginId(
            "myavalonia.plugin.bili-downloader");
        var registry = new PluginRegistry(
            CreatePluginSnapshots(), [], [],
            [new PluginLifecycleDeclaration(owner, typeof(ReadyBiliLifecycle))]);
        var states = new PluginLifecycleStateStore(registry);
        states.SetState(new PluginLifecycleState(owner, status)
        {
            Stage = status is PluginLifecycleStatus.Stopping
                or PluginLifecycleStatus.Stopped
                or PluginLifecycleStatus.ShutdownFailed
                or PluginLifecycleStatus.ShutdownTimedOut
                ? PluginLifecycleStage.Shutdown
                : PluginLifecycleStage.Initialization,
            ErrorCode = status switch
            {
                PluginLifecycleStatus.InitializationFailed =>
                    HostDiagnosticCodes.LifecycleInitializeFailed,
                PluginLifecycleStatus.InitializationTimedOut =>
                    HostDiagnosticCodes.LifecycleInitializeTimeout,
                PluginLifecycleStatus.HostCancelled =>
                    HostDiagnosticCodes.LifecycleHostCancelled,
                PluginLifecycleStatus.ShutdownFailed =>
                    HostDiagnosticCodes.LifecycleShutdownFailed,
                PluginLifecycleStatus.ShutdownTimedOut =>
                    HostDiagnosticCodes.LifecycleShutdownTimeout,
                _ => null,
            },
        });

        var item = new PluginStatusViewModel(
            registry,
            availability: new PluginAvailabilityReadModel(states)).Items
            .Single(value => value.PluginId == owner.Value);

        Assert.Equal(expectedStatus, item.StatusText);
        Assert.Equal(expectedAvailability, item.AvailabilityText);
    }

    [Fact]
    public void DaTang模块注册Scoped文档且禁止从根容器解析()
    {
        var services = new ServiceCollection();
        services.AddDocumentScopeManagement();
        services.AddLegacyPluginDocumentScopesForTests();
        var module = new DaTangAccountingHelpPluginModule();

        module.Configure(new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.datang-accounting-help"), services));

        var descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(InvoiceInfoImportViewModel));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.DoesNotContain(
            services,
            item => item.ServiceType == typeof(IPluginLifecycle));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<InvoiceInfoImportViewModel>);
    }

    [Fact]
    public void DaTang托管策略无需无参构造也能按当前规则发现()
    {
        var assembly = typeof(DaTangAccountingHelpPluginModule).Assembly;
        var documentStrategies = assembly
            .GetTypes()
            .Where(type => typeof(IDocumentCreationStrategy).IsAssignableFrom(type)
                           && !type.IsAbstract
                           && !type.IsInterface)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                typeof(BankBalanceReconciliationDocumentStrategy).FullName!,
                typeof(InvoiceInfoImportDocumentStrategy).FullName!
            ],
            documentStrategies);
        Assert.Null(typeof(BankBalanceReconciliationDocumentStrategy).GetConstructor(Type.EmptyTypes));
        Assert.Null(typeof(InvoiceInfoImportDocumentStrategy).GetConstructor(Type.EmptyTypes));
        Assert.Null(typeof(InvoiceInfoImportViewModel).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void 未注册生命周期的托管插件无需运行状态即可用()
    {
        var registry = new PluginRegistry(CreatePluginSnapshots(), [], [], []);
        var states = new PluginLifecycleStateStore(registry);
        var availability = new PluginAvailabilityReadModel(states);

        Assert.Empty(availability.LifecycleStates);
        Assert.All(
            CreatePluginSnapshots(),
            plugin => Assert.True(availability.IsAvailable(
                new MyAvaloniaManagement.PluginSdk.PluginId(
                    plugin.Manifest.PluginId.Value))));
    }

    [Fact]
    public void DaTang策略通过统一DI激活且每次返回独立文档()
    {
        var services = new ServiceCollection();
        services.AddDocumentScopeManagement();
        services.AddLegacyPluginDocumentScopesForTests();
        new DaTangAccountingHelpPluginModule().Configure(new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.datang-accounting-help"), services));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var strategy = ActivatorUtilities.CreateInstance<InvoiceInfoImportDocumentStrategy>(provider);
        var firstParams = new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId)
        {
            Title = "第一份发票计算",
        };
        var secondParams = new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId);
        var first = Assert.IsType<InvoiceInfoImportViewModel>(
            strategy.CreateDocument(firstParams));
        var second = Assert.IsType<InvoiceInfoImportViewModel>(
            strategy.CreateDocument(secondParams));

        Assert.NotSame(first, second);
        Assert.Equal("第一份发票计算", first.Title);
        Assert.Equal("发票信息导入和计算", second.Title);
        Assert.Equal("大唐-会计", strategy.GetMetadata().MenuCategory);
        var manager = provider.GetRequiredService<LegacyPluginDocumentScopeFactory>();
        Assert.True(manager.Release(first));
        Assert.False(manager.Release(first));
        Assert.True(manager.Release(second));
    }

    [Fact]
    public void MySmallTools模块注册可通过作用域验证且加密Document彼此独立()
    {
        var services = new ServiceCollection();
        services.AddLegacyPluginDocumentScopesForTests();
        new MySmallToolsPluginModule().Configure(new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.my-small-tools"), services));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var strategy = ActivatorUtilities.CreateInstance<VideoEncryptorDocumentStrategy>(provider);
        var first = Assert.IsType<VideoEncryptorViewModel>(strategy.CreateDocument(
            new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId)));
        var second = Assert.IsType<VideoEncryptorViewModel>(strategy.CreateDocument(
            new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId)));

        Assert.NotSame(first, second);
        var manager = provider.GetRequiredService<LegacyPluginDocumentScopeFactory>();
        Assert.True(manager.Release(first));
        Assert.True(manager.Release(second));
    }

    private sealed class ReadyBiliLifecycle : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShutdownAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static TestPluginRegistrationContext ConfigureForInspection(
        IPluginModule module,
        string pluginId,
        IServiceCollection services)
    {
        var context = new TestPluginRegistrationContext(new PluginId(pluginId), services);
        module.Configure(context);
        return context;
    }

    private static string[] Describe(TestPluginRegistrationContext context) =>
        context.Contributions.Select(item => item.Second is null
            ? $"{item.Kind}:{item.First.Name}"
            : $"{item.Kind}:{item.First.Name}->{item.Second.Name}").ToArray();

    private static IReadOnlyList<PluginRegistryPlugin> CreatePluginSnapshots() =>
    [
        Snapshot<BiliDownloaderPluginModule>("myavalonia.plugin.bili-downloader"),
        Snapshot<DaTangAccountingHelpPluginModule>("myavalonia.plugin.datang-accounting-help"),
        Snapshot<MyPlugTestPluginModule>("myavalonia.plugin.my-plug-test"),
        Snapshot<MySmallToolsPluginModule>("myavalonia.plugin.my-small-tools"),
    ];

    private static PluginRegistryPlugin Snapshot<TModule>(string pluginId)
    {
        var assembly = typeof(TModule).Assembly;
        var manifest = new PluginManifest(
            PluginManifestReader.CurrentSchemaVersion,
            new PluginId(pluginId),
            new Version(1, 0, 0, 0),
            new PluginEntryPoint(
                $"{assembly.GetName().Name}.dll",
                typeof(TModule).FullName!),
            new PluginVersionRange(new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)));
        return new PluginRegistryPlugin(
            manifest, assembly, typeof(TModule), [], [], [], []);
    }

    private static IDocumentCreationStrategy Activate<TStrategy>(
        IServiceProvider provider) where TStrategy : IDocumentCreationStrategy =>
        ActivatorUtilities.CreateInstance<TStrategy>(provider);
}
