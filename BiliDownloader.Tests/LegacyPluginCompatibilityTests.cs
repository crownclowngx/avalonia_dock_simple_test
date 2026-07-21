using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Create;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;
using MyPlugTest.Models;
using MyPlugTest.Plugin;
using MySmallTools.InitPlug.SecretVideoPlayer;
using Microsoft.Extensions.DependencyInjection;
using Dock.Model.Mvvm.Controls;

namespace BiliDownloader.Tests;

public sealed class LegacyPluginCompatibilityTests
{
    [Fact]
    public void 历史插件程序集不会被标记为宿主管理模块()
    {
        var legacyAssemblies = new[]
        {
            typeof(InvoiceInfoImportDocumentStrategy).Assembly,
            typeof(SecretVideoDocumentStrategy).Assembly,
        };

        var catalog = PluginModuleCatalog.Discover(legacyAssemblies);

        Assert.Empty(catalog.Modules);
        Assert.All(legacyAssemblies, assembly => Assert.False(catalog.IsManaged(assembly)));
    }

    [Fact]
    public void BiliDownloader与MyPlugTest程序集显式接入模块_且不改变公共策略接口()
    {
        var biliAssembly = typeof(BiliDownloaderPluginModule).Assembly;
        var myPlugTestAssembly = typeof(MyPlugTestPluginModule).Assembly;
        var catalog = PluginModuleCatalog.Discover([biliAssembly, myPlugTestAssembly]);

        Assert.Equal(["BiliDownloader", "MyPlugTest"], catalog.Modules.Select(x => x.PluginId));
        Assert.True(catalog.IsManaged(biliAssembly));
        Assert.True(catalog.IsManaged(myPlugTestAssembly));
        Assert.Equal(2, typeof(IDocumentCreationStrategy).GetMethods().Length);
        Assert.Equal(2, typeof(IToolCreationStrategy).GetMethods().Length);
    }

    [Fact]
    public void 当前历史策略仍保留公共无参构造函数()
    {
        var strategyTypes = new[]
        {
            typeof(InvoiceInfoImportDocumentStrategy),
            typeof(SecretVideoDocumentStrategy),
            typeof(VideoEncryptorDocumentStrategy),
        };

        Assert.All(strategyTypes, type =>
        {
            var constructor = type.GetConstructor(Type.EmptyTypes);
            Assert.NotNull(constructor);
            Assert.NotNull(Activator.CreateInstance(type));
        });
    }

    [Fact]
    public void 当前所有历史Document与Tool策略仍可按原规则发现()
    {
        var legacyAssemblies = new[]
        {
            typeof(InvoiceInfoImportDocumentStrategy).Assembly,
            typeof(SecretVideoDocumentStrategy).Assembly,
        };

        // 这里刻意复刻改造前的发现条件：实现原策略接口、不是抽象类型，
        // 并且具有公共无参构造函数。若后续宿主误把历史插件切换到依赖注入路径，
        // 这份固定清单会立即暴露发现数量或策略类型的兼容性回归。
        var documentStrategies = legacyAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IDocumentCreationStrategy).IsAssignableFrom(type)
                           && !type.IsAbstract
                           && !type.IsInterface
                           && type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var toolStrategies = legacyAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IToolCreationStrategy).IsAssignableFrom(type)
                           && !type.IsAbstract
                           && !type.IsInterface
                           && type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            typeof(InvoiceInfoImportDocumentStrategy).FullName!,
            typeof(SecretVideoDocumentStrategy).FullName!,
            typeof(VideoEncryptorDocumentStrategy).FullName!,
        }.OrderBy(name => name, StringComparer.Ordinal), documentStrategies);
        Assert.Empty(toolStrategies);
    }

    [Fact]
    public void 未注册生命周期的托管示例与历史插件都不会进入生命周期管理器()
    {
        var manager = new PluginLifecycleManager([]);

        Assert.Empty(manager.States);
        Assert.Null(manager.GetState("MyPlugTest"));
        Assert.Null(manager.GetState("DaTangAccountingHelpPlug"));
        Assert.Null(manager.GetState("MySmallTools"));
    }

    [Fact]
    public void 策略激活器对历史插件使用无参路径_对托管插件使用依赖注入路径()
    {
        var legacyCatalog = PluginModuleCatalog.Discover([typeof(SecretVideoDocumentStrategy).Assembly]);
        using var emptyProvider = new ServiceCollection().BuildServiceProvider();

        var legacyStrategy = PluginStrategyActivator.Create<IDocumentCreationStrategy>(
            typeof(SecretVideoDocumentStrategy),
            typeof(SecretVideoDocumentStrategy).Assembly,
            emptyProvider,
            legacyCatalog);

        Assert.IsType<SecretVideoDocumentStrategy>(legacyStrategy);

        var managedAssembly = typeof(TestManagedPluginModule).Assembly;
        var managedCatalog = PluginModuleCatalog.Discover([managedAssembly]);
        var services = new ServiceCollection();
        services.AddSingleton<TestManagedDependency>();
        using var managedProvider = services.BuildServiceProvider();

        var managedStrategy = PluginStrategyActivator.Create<IToolCreationStrategy>(
            typeof(TestManagedToolStrategy),
            managedAssembly,
            managedProvider,
            managedCatalog);

        Assert.IsType<TestManagedToolStrategy>(managedStrategy);
    }
}

/// <summary>
/// 测试程序集中的最小托管模块，仅用于证明声明模块后策略会切换到 DI 激活路径。
/// </summary>
public sealed class TestManagedPluginModule : IPluginModule
{
    public string PluginId => "TestManagedPlugin";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}

public sealed class TestManagedDependency;

public sealed class TestManagedToolStrategy : IToolCreationStrategy
{
    public TestManagedToolStrategy(TestManagedDependency dependency)
    {
        Dependency = dependency;
    }

    public TestManagedDependency Dependency { get; }

    public Tool CreateTool() => new();

    public ToolMetadata GetMetadata() => new()
    {
        ToolTypeId = "TestManagedTool",
        DisplayName = "测试托管工具",
        Description = "仅用于验证托管插件策略的依赖注入激活路径",
        IconPath = string.Empty,
        Alignment = "Right",
    };
}
