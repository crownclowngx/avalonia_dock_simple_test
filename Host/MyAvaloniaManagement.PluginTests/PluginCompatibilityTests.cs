using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Create;
using DaTangAccountingHelpPlug.Create.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Plugin;
using DaTangAccountingHelpPlug.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;
using MyPlugTest.Create;
using MyPlugTest.Models;
using MyPlugTest.Plugin;
using MyPlugTest.ViewModels;
using MySmallTools.InitPlug.SecretVideoPlayer;
using MySmallTools.Plugin;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MyAvaloniaManagement.PluginTests;

public sealed class PluginCompatibilityTests
{
    [Fact]
    public void 未声明插件模块的共享程序集不会被标记为宿主管理模块()
    {
        var sharedAssembly = typeof(IPluginModule).Assembly;

        var catalog = PluginModuleCatalog.Discover([sharedAssembly]);

        Assert.Empty(catalog.Modules);
        Assert.False(catalog.IsManaged(sharedAssembly));
    }

    [Fact]
    public void 四个插件程序集显式接入模块且不改变公共策略接口()
    {
        var biliAssembly = typeof(BiliDownloaderPluginModule).Assembly;
        var daTangAssembly = typeof(DaTangAccountingHelpPluginModule).Assembly;
        var myPlugTestAssembly = typeof(MyPlugTestPluginModule).Assembly;
        var mySmallToolsAssembly = typeof(MySmallToolsPluginModule).Assembly;
        var catalog = PluginModuleCatalog.Discover(
            [biliAssembly, daTangAssembly, myPlugTestAssembly, mySmallToolsAssembly]);

        Assert.Equal(
            ["BiliDownloader", "DaTangAccountingHelpPlug", "MyPlugTest", "MySmallTools"],
            catalog.Modules.Select(x => x.PluginId));
        Assert.True(catalog.IsManaged(biliAssembly));
        Assert.True(catalog.IsManaged(daTangAssembly));
        Assert.True(catalog.IsManaged(myPlugTestAssembly));
        Assert.True(catalog.IsManaged(mySmallToolsAssembly));
        Assert.Equal(2, typeof(IDocumentCreationStrategy).GetMethods().Length);
        Assert.Equal(2, typeof(IToolCreationStrategy).GetMethods().Length);
    }

    [Fact]
    public async Task 插件状态模型合并四个托管模块与生命周期结果()
    {
        var catalog = PluginModuleCatalog.Discover([
            typeof(BiliDownloaderPluginModule).Assembly,
            typeof(DaTangAccountingHelpPluginModule).Assembly,
            typeof(MyPlugTestPluginModule).Assembly,
            typeof(MySmallToolsPluginModule).Assembly,
        ]);
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();
        services.AddSingleton(catalog);
        services.AddSingleton<IPluginLifecycle, ReadyBiliLifecycle>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var manager = provider.GetRequiredService<PluginLifecycleManager>();
        await manager.InitializeAllAsync();
        var viewModel = provider.GetRequiredService<PluginStatusViewModel>();

        Assert.Equal(4, viewModel.Items.Count);
        Assert.Equal(
            ["BiliDownloader", "DaTangAccountingHelpPlug", "MyPlugTest", "MySmallTools"],
            viewModel.Items.Select(item => item.PluginId));
        Assert.Equal(
            "运行正常",
            viewModel.Items.Single(item => item.PluginId == "BiliDownloader").StatusText);
        Assert.All(
            viewModel.Items.Where(item => item.PluginId != "BiliDownloader"),
            item => Assert.Contains("无需后台生命周期", item.StatusText));
    }

    [Fact]
    public void DaTang模块注册Scoped文档且禁止从根容器解析()
    {
        var services = new ServiceCollection();
        var module = new DaTangAccountingHelpPluginModule();

        module.ConfigureServices(services);

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
        var catalog = PluginModuleCatalog.Discover([assembly]);

        var documentStrategies = assembly
            .GetTypes()
            .Where(type => typeof(IDocumentCreationStrategy).IsAssignableFrom(type)
                           && !type.IsAbstract
                           && !type.IsInterface
                           && (catalog.IsManaged(assembly)
                               || type.GetConstructor(Type.EmptyTypes) != null))
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
    }

    [Fact]
    public void 未注册生命周期的托管插件不会进入生命周期管理器()
    {
        var manager = new PluginLifecycleManager([]);

        Assert.Empty(manager.States);
        Assert.Null(manager.GetState("MyPlugTest"));
        Assert.Null(manager.GetState("DaTangAccountingHelpPlug"));
        Assert.Null(manager.GetState("MySmallTools"));
    }

    [Fact]
    public void DaTang策略通过托管激活器创建且每次返回独立文档()
    {
        var assembly = typeof(DaTangAccountingHelpPluginModule).Assembly;
        var catalog = PluginModuleCatalog.Discover([assembly]);
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        new DaTangAccountingHelpPluginModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var strategy = PluginStrategyActivator.Create<IDocumentCreationStrategy>(
            typeof(InvoiceInfoImportDocumentStrategy),
            assembly,
            provider,
            catalog);
        var firstParams = new DocumentCreationParams("invoice-import")
        {
            Title = "第一份发票计算",
        };
        var secondParams = new DocumentCreationParams("invoice-import");
        var first = Assert.IsType<InvoiceInfoImportViewModel>(
            strategy.CreateDocument(firstParams));
        var second = Assert.IsType<InvoiceInfoImportViewModel>(
            strategy.CreateDocument(secondParams));

        Assert.NotSame(first, second);
        Assert.Equal("第一份发票计算", first.Title);
        Assert.Equal("发票信息导入和计算", second.Title);
        Assert.Equal("大唐-会计", strategy.GetMetadata().MenuCategory);
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        Assert.True(manager.Release(first));
        Assert.False(manager.Release(first));
        Assert.True(manager.Release(second));
    }

    [Fact]
    public void MyPlugTest三个Document策略均由独立Scope托管()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        new MyPlugTestPluginModule().ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        Assert.All(
            new[]
            {
                typeof(TestWelcomeViewModel),
                typeof(TestMessageReceiveViewModel),
                typeof(BatchHttpGetViewModel),
                typeof(UrlHistoryViewModel),
            },
            serviceType => Assert.Equal(
                ServiceLifetime.Scoped,
                Assert.Single(services, item => item.ServiceType == serviceType).Lifetime));
        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<TestWelcomeViewModel>);
        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<TestMessageReceiveViewModel>);
        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<BatchHttpGetViewModel>);

        var assembly = typeof(MyPlugTestPluginModule).Assembly;
        var catalog = PluginModuleCatalog.Discover([assembly]);
        var welcomeStrategy = Activate<TestWelcomeDocumentStrategy>(assembly, provider, catalog);
        var receiveStrategy = Activate<TestMessageReceiveDocumentStrategy>(assembly, provider, catalog);
        var batchStrategy = Activate<BatchHttpGetDocumentStrategy>(assembly, provider, catalog);

        var firstWelcome = Assert.IsType<TestWelcomeViewModel>(welcomeStrategy.CreateDocument(
            new DocumentCreationParams("welcome") { Title = "欢迎 A" }));
        var secondWelcome = Assert.IsType<TestWelcomeViewModel>(welcomeStrategy.CreateDocument(
            new DocumentCreationParams("welcome")));
        var receiver = Assert.IsType<TestMessageReceiveViewModel>(receiveStrategy.CreateDocument(
            new DocumentCreationParams("receiver")));
        var batch = Assert.IsType<BatchHttpGetViewModel>(batchStrategy.CreateDocument(
            new DocumentCreationParams("batch")));

        Assert.NotSame(firstWelcome, secondWelcome);
        Assert.NotSame(firstWelcome.UrlHistory, secondWelcome.UrlHistory);
        firstWelcome.UrlHistory.AddUrl("https://first.test");
        Assert.Single(firstWelcome.UrlHistory.HistoryItems);
        Assert.Empty(secondWelcome.UrlHistory.HistoryItems);
        Assert.Equal("欢迎 A", firstWelcome.Title);

        var manager = provider.GetRequiredService<DocumentScopeManager>();
        Assert.True(manager.Release(firstWelcome));
        Assert.False(manager.Release(firstWelcome));
        Assert.True(manager.Release(secondWelcome));
        Assert.True(manager.Release(receiver));
        Assert.True(manager.Release(batch));
    }

    [Fact]
    public void MySmallTools模块注册可通过作用域验证且加密Document彼此独立()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        new MySmallToolsPluginModule().ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var assembly = typeof(MySmallToolsPluginModule).Assembly;
        var catalog = PluginModuleCatalog.Discover([assembly]);
        var strategy = PluginStrategyActivator.Create<IDocumentCreationStrategy>(
            typeof(VideoEncryptorDocumentStrategy),
            assembly,
            provider,
            catalog);
        var first = Assert.IsType<VideoEncryptorViewModel>(strategy.CreateDocument(
            new DocumentCreationParams("video-encryptor")));
        var second = Assert.IsType<VideoEncryptorViewModel>(strategy.CreateDocument(
            new DocumentCreationParams("video-encryptor")));

        Assert.NotSame(first, second);
        Assert.True(provider.GetRequiredService<DocumentScopeManager>().Release(first));
        Assert.True(provider.GetRequiredService<DocumentScopeManager>().Release(second));
    }

    private sealed class ReadyBiliLifecycle : IPluginLifecycle
    {
        public string PluginId => "BiliDownloader";

        public int Order => 0;

        public Task InitializeAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShutdownAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static IDocumentCreationStrategy Activate<TStrategy>(
        System.Reflection.Assembly assembly,
        IServiceProvider provider,
        PluginModuleCatalog catalog) where TStrategy : IDocumentCreationStrategy =>
        PluginStrategyActivator.Create<IDocumentCreationStrategy>(
            typeof(TStrategy),
            assembly,
            provider,
            catalog);
}
