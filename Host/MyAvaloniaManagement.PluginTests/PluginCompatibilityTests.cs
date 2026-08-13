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
using MyAvaloniaManagementCommon.Save;
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
    public void 全部历史Document与ToolId经真实四插件注册表迁移到规范Id()
    {
        var assemblies = new[]
        {
            typeof(BiliDownloaderPluginModule).Assembly,
            typeof(DaTangAccountingHelpPluginModule).Assembly,
            typeof(MyPlugTestPluginModule).Assembly,
            typeof(MySmallToolsPluginModule).Assembly
        };
        var catalog = PluginModuleCatalog.Discover(assemblies);
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();
        catalog.ConfigureServices(services);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        var factory = provider.GetRequiredService<MyAvaloniaManagement.ViewModels.ManagementFactory>();

        var documentMappings = new (string Legacy, string Canonical)[]
        {
            ("DD7A1E38-07C5-B38C-FB02-1B991896EF49", "myavalonia.host.document.welcome"),
            ("A3F7E1B2-9C4D-4E8A-B6F1-2D5E8A7C3B10", "myavalonia.plugin.bili-downloader.document.download"),
            ("D8525F12-F58B-F95D-1B4B-62EE33CF128D", "myavalonia.plugin.datang-accounting-help.document.invoice-info-import"),
            ("9D0ACD63-6C35-4CC8-87B1-E9B3C91E1C18", "myavalonia.plugin.datang-accounting-help.document.bank-balance-reconciliation"),
            ("7DEE4212-DFF1-9923-B527-1B047D1B2918", "myavalonia.plugin.my-plug-test.document.welcome"),
            ("384D28C4-F6E8-4D49-B0BD-2CE484D4D177", "myavalonia.plugin.my-plug-test.document.message-receiver"),
            ("C1B13C72-C21A-4C39-9612-77C341DA85B6", "myavalonia.plugin.my-plug-test.document.batch-http-get"),
            ("A1B2C3D4-E5F6-7890-ABCD-EF1234567890", "myavalonia.plugin.my-small-tools.document.secret-video-player"),
            ("B2C3D4E5-F6G7-8901-BCDE-F23456789012", "myavalonia.plugin.my-small-tools.document.video-encryptor"),
            ("C3D4E5F6-A7B8-4901-CDEF-345678901234", "myavalonia.plugin.my-small-tools.document.secret-video-library"),
            ("D4E5F6A7-B8C9-4A12-DEF0-456789012345", "myavalonia.plugin.my-small-tools.document.video-decryptor")
        };
        foreach (var (legacy, canonical) in documentMappings)
        {
            var document = factory.CreateManagementNewDocument(
                new DocumentCreationParams(new DocumentTypeId(legacy)));
            Assert.Equal(
                canonical,
                factory.NormalizePersistedDocumentTypeId(new DocumentTypeId(legacy)).Value);
            if (document is ISavableDocument savable)
            {
                Assert.Equal(canonical, savable.SaveDocumentTypeId.Value);
            }

            factory.OnDockableClosed(document);
        }

        var toolMappings = new (string Legacy, string Canonical)[]
        {
            ("fileSystemTree", "myavalonia.host.tool.file-system-tree"),
            ("plugGroupMenu", "myavalonia.host.tool.plugin-menu"),
            ("pluginStatus", "myavalonia.host.tool.plugin-status"),
            ("toolManagement", "myavalonia.host.tool.management"),
            ("BiliSchedulerTool", "myavalonia.plugin.bili-downloader.tool.scheduler"),
            ("MyCustomTool", "myavalonia.plugin.my-plug-test.tool.custom")
        };
        Assert.All(toolMappings, mapping =>
            Assert.Equal(mapping.Canonical, factory.NormalizePersistedToolId(mapping.Legacy)));
    }

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
            [
                "myavalonia.plugin.bili-downloader",
                "myavalonia.plugin.datang-accounting-help",
                "myavalonia.plugin.my-plug-test",
                "myavalonia.plugin.my-small-tools"
            ],
            catalog.Modules.Select(x => x.PluginId.Value));
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
            [
                "myavalonia.plugin.bili-downloader",
                "myavalonia.plugin.datang-accounting-help",
                "myavalonia.plugin.my-plug-test",
                "myavalonia.plugin.my-small-tools"
            ],
            viewModel.Items.Select(item => item.PluginId));
        Assert.Equal(
            "运行正常",
            viewModel.Items.Single(item =>
                item.PluginId == "myavalonia.plugin.bili-downloader").StatusText);
        Assert.All(
            viewModel.Items.Where(item =>
                item.PluginId != "myavalonia.plugin.bili-downloader"),
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
        Assert.Null(manager.GetState(MyPlugTest.Constants.SaveDocumentTypeIdConstant.PluginId));
        Assert.Null(manager.GetState(DaTangAccountingHelpPlug.Constants.SaveDocumentTypeIdConstant.PluginId));
        Assert.Null(manager.GetState(MySmallTools.Constants.DocumentTypeIdConstant.PluginId));
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
            new DocumentCreationParams(welcomeStrategy.GetMetadata().DocumentTypeId) { Title = "欢迎 A" }));
        var secondWelcome = Assert.IsType<TestWelcomeViewModel>(welcomeStrategy.CreateDocument(
            new DocumentCreationParams(welcomeStrategy.GetMetadata().DocumentTypeId)));
        var receiver = Assert.IsType<TestMessageReceiveViewModel>(receiveStrategy.CreateDocument(
            new DocumentCreationParams(receiveStrategy.GetMetadata().DocumentTypeId)));
        var batch = Assert.IsType<BatchHttpGetViewModel>(batchStrategy.CreateDocument(
            new DocumentCreationParams(batchStrategy.GetMetadata().DocumentTypeId)));

        Assert.NotSame(firstWelcome, secondWelcome);
        Assert.NotSame(firstWelcome.UrlHistory, secondWelcome.UrlHistory);
        firstWelcome.UrlHistory.AddUrl("https://first.test");
        Assert.Single(firstWelcome.UrlHistory.HistoryItems);
        Assert.Empty(secondWelcome.UrlHistory.HistoryItems);
        Assert.Equal("欢迎 A", firstWelcome.Title);
        Assert.True(firstWelcome.IsDirty);
        _ = firstWelcome.CreateSaveDocumentMetaData("unused.mamdoc");
        Assert.True(firstWelcome.IsDirty);
        firstWelcome.AcceptChanges();
        Assert.False(firstWelcome.IsDirty);
        Assert.Throws<DocumentLoadException>(() =>
            firstWelcome.LoadDocumentByMetaData(new DocumentSaveData
            {
                DocumentTypeId = firstWelcome.SaveDocumentTypeId,
                Title = "损坏",
                SaveTime = DateTime.UtcNow,
                Content = "{broken",
                PluginMetadata = "{}",
            }));

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
            new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId)));
        var second = Assert.IsType<VideoEncryptorViewModel>(strategy.CreateDocument(
            new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId)));

        Assert.NotSame(first, second);
        Assert.True(provider.GetRequiredService<DocumentScopeManager>().Release(first));
        Assert.True(provider.GetRequiredService<DocumentScopeManager>().Release(second));
    }

    private sealed class ReadyBiliLifecycle : IPluginLifecycle
    {
        public PluginId PluginId => BiliDownloader.Constants.SaveDocumentTypeIdConstant.PluginId;

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
