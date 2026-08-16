using Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证 G5 的核心不变量：只有显式声明能够进入不可变 Registry，所有权来自注册上下文，
/// View 创建失败受控降级，普通 DI 注册不能绕过贡献边界。
/// </summary>
public sealed class ExplicitContributionAndPluginRegistryTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.explicit-test");

    [Fact]
    public void 未登记类型不会因存在于程序集而进入Registry()
    {
        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var context = new PluginRegistrationContext(Owner, services, builder);
        new ExplicitModule().Configure(context);
        Assert.Empty(context.SealAndGetBypassedContributionTypes());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var registry = builder.Build(provider, catalog: null);

        Assert.Equal(
            [new DocumentTypeId("myavalonia.plugin.explicit-test.document.registered")],
            registry.DocumentMetadata.Keys);
        Assert.DoesNotContain(
            new DocumentTypeId("myavalonia.plugin.explicit-test.document.unregistered"),
            registry.DocumentMetadata.Keys);
        Assert.True(registry.TryGetView(typeof(RegisteredViewModel), out var view));
        Assert.Equal(typeof(RegisteredView), view.ViewType);
        Assert.False(registry.TryGetView(typeof(UnregisteredViewModel), out _));
        Assert.Equal(Owner, Assert.Single(registry.Lifecycles).PluginId);
    }

    [Fact]
    public void 重复ViewModel映射在Registry发布前给出结构诊断()
    {
        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var context = new PluginRegistrationContext(Owner, services, builder);
        context.AddView<RegisteredViewModel, RegisteredView>();
        context.AddView<RegisteredViewModel, AlternateRegisteredView>();
        context.SealAndGetBypassedContributionTypes();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<HostCompositionException>(() =>
            builder.Build(provider, catalog: null));

        Assert.Contains(exception.Diagnostics,
            item => item.Code == "VIEW_MODEL_REGISTRATION_DUPLICATE");
    }

    [Fact]
    public void 直接DI注册贡献接口会被上下文识别为绕行()
    {
        var services = new ServiceCollection();
        var context = new PluginRegistrationContext(
            Owner, services, new PluginRegistryBuilder());
        context.Services.AddSingleton<IDocumentCreationStrategy, RegisteredStrategy>();

        var bypasses = context.SealAndGetBypassedContributionTypes();

        Assert.Equal([typeof(IDocumentCreationStrategy)], bypasses);
    }

    [Fact]
    public void 抽象贡献在容器构建前给出稳定结构诊断()
    {
        var context = new PluginRegistrationContext(
            Owner, new ServiceCollection(), new PluginRegistryBuilder());

        var exception = Assert.Throws<HostCompositionException>(
            context.AddDocument<AbstractStrategy>);

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("CONTRIBUTION_TYPE_INVALID", diagnostic.Code);
        Assert.Equal(Owner.Value, diagnostic.StableId);
        Assert.Equal(typeof(AbstractStrategy).FullName,
            Assert.Single(diagnostic.Contributors).TypeName);
    }

    [Fact]
    public void View构造失败记录稳定诊断并返回占位控件()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "g5-view-diagnostics", Guid.NewGuid().ToString("N"));
        using var diagnostics = HostDiagnosticSession.Start(directory);
        var registry = new PluginRegistry(
            [], [], [],
            [new PluginViewRegistration(
                Owner,
                typeof(RegisteredViewModel),
                typeof(RegisteredView),
                () => throw new InvalidOperationException("插件私有异常正文"))],
            []);
        var locator = new ViewLocator(registry, diagnostics);

        var result = locator.Build(new RegisteredViewModel());

        Assert.IsType<TextBlock>(result);
        var record = Assert.Single(diagnostics.Snapshot,
            item => item.Code == "VIEW_CREATION_FAILED");
        Assert.Equal(Owner.Value, record.PluginId);
        Assert.Equal(typeof(RegisteredView).FullName, record.StableId);
        Assert.Contains("InvalidOperationException", record.TechnicalDetail);
        var persisted = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.jsonl").Select(File.ReadAllText));
        Assert.DoesNotContain("插件私有异常正文", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void 两个组合根拥有独立且发布后不可写的Registry()
    {
        var firstServices = new ServiceCollection();
        var firstBuilder = new PluginRegistryBuilder();
        firstServices.AddApplicationServices(firstBuilder).AddViewModels();
        using var firstProvider = firstServices.BuildServiceProvider();

        var secondServices = new ServiceCollection();
        var secondBuilder = new PluginRegistryBuilder();
        secondServices.AddApplicationServices(secondBuilder).AddViewModels();
        using var secondProvider = secondServices.BuildServiceProvider();

        var first = firstProvider.GetRequiredService<PluginRegistry>();
        var second = secondProvider.GetRequiredService<PluginRegistry>();

        Assert.NotSame(first, second);
        Assert.Equal(first.DocumentMetadata.Keys, second.DocumentMetadata.Keys);
        Assert.Throws<InvalidOperationException>(() =>
            firstBuilder.AddDocument(Owner, typeof(RegisteredStrategy)));
        Assert.Single(second.DocumentMetadata);
    }

    private sealed class ExplicitModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.AddDocument<RegisteredStrategy>();
            context.AddView<RegisteredViewModel, RegisteredView>();
            context.AddLifecycle<RegisteredLifecycle>();
        }
    }

    private sealed class RegisteredStrategy : IDocumentCreationStrategy
    {
        public Document CreateDocument(DocumentCreationParams @params) => new();

        public DocumentMetadata GetMetadata() => new(
            new DocumentTypeId("myavalonia.plugin.explicit-test.document.registered"),
            "已登记 Document");
    }

    // 本类型与已登记策略位于同一程序集，但没有出现在 Context 中；测试用它证明 G5 不扫描类型。
    private sealed class UnregisteredStrategy : IDocumentCreationStrategy
    {
        public Document CreateDocument(DocumentCreationParams @params) => new();

        public DocumentMetadata GetMetadata() => new(
            new DocumentTypeId("myavalonia.plugin.explicit-test.document.unregistered"),
            "未登记 Document");
    }

    private abstract class AbstractStrategy : IDocumentCreationStrategy
    {
        public Document CreateDocument(DocumentCreationParams @params) => new();

        public DocumentMetadata GetMetadata() => new(
            new DocumentTypeId("myavalonia.plugin.explicit-test.document.abstract"),
            "抽象贡献");
    }

    private sealed class RegisteredLifecycle : IPluginLifecycle
    {
        public int Order => 0;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RegisteredViewModel;

    private sealed class UnregisteredViewModel;

    private sealed class RegisteredView : UserControl
    {
        public RegisteredView() { }
    }

    private sealed class AlternateRegisteredView : UserControl
    {
        public AlternateRegisteredView() { }
    }
}
