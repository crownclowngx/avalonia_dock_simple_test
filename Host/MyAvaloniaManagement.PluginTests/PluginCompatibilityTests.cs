using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Create;
using DaTangAccountingHelpPlug.Create.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Plugin;
using DaTangAccountingHelpPlug.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;
using MyPlugTest.Models;
using MyPlugTest.Plugin;
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
    public void DaTang模块注册Transient文档且不注册生命周期()
    {
        var services = new ServiceCollection();
        var module = new DaTangAccountingHelpPluginModule();

        module.ConfigureServices(services);

        var descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(InvoiceInfoImportViewModel));
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
        Assert.DoesNotContain(
            services,
            item => item.ServiceType == typeof(IPluginLifecycle));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        Assert.NotSame(
            provider.GetRequiredService<InvoiceInfoImportViewModel>(),
            provider.GetRequiredService<InvoiceInfoImportViewModel>());
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
}
