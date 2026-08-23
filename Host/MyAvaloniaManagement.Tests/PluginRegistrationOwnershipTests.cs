using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证 V3 G4 的服务提交所有权、贡献根固定生命周期和稳定 ID 命名空间。
/// </summary>
/// <remarks>
/// 测试直接使用 Host internal 组合边界，避免用测试替身重新实现一套校验规则。生产集成中的
/// “失败只隔离当前插件”由 PluginContainerIsolationTests 继续验证，本类专注单个候选在 Seal 与
/// Commit 之间的确定性事实。
/// </remarks>
public sealed class PluginRegistrationOwnershipTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.ownership-test");

    [Fact]
    public void Host在Seal后唯一提交端口Scope基础设施和贡献固定生命周期()
    {
        var windowInteraction = new TestWindowInteraction();
        var hostServices = new ServiceCollection();
        hostServices.AddSingleton<IPluginWindowInteraction>(windowInteraction);
        using var hostProvider = hostServices.BuildServiceProvider();

        var pluginServices = new ServiceCollection();
        var registration = new PluginRegistration(
            Owner,
            pluginServices,
            new PluginRegistryBuilder());
        Assert.Empty(registration.Services);
        registration.Services.AddSingleton<IPrivateService, FirstPrivateService>();
        registration.Services.AddSingleton<IPrivateService, SecondPrivateService>();
        registration.Services.AddKeyedSingleton<IPrivateService, FirstPrivateService>("first");
        registration.Services.AddSingleton(typeof(IPrivateBox<>), typeof(PrivateBox<>));
        registration.AddDocument<OwnedDocument, EmptyView>(Document(Owner, "sample"));
        registration.AddTool<OwnedTool, EmptyView>(Tool(Owner, "sample"));
        registration.UseLifecycle<OwnedLifecycle>();
        registration.Seal();

        Assert.DoesNotContain(pluginServices, item =>
            item.ServiceType == typeof(OwnedDocument) ||
            item.ServiceType == typeof(OwnedTool) ||
            item.ServiceType == typeof(OwnedLifecycle) ||
            item.ServiceType == typeof(IDocumentLifetime));

        PluginServiceCommitGuard.ValidateAndCommit(
            pluginServices,
            registration,
            hostProvider);

        AssertDescriptor(pluginServices, typeof(OwnedDocument), ServiceLifetime.Scoped);
        AssertDescriptor(pluginServices, typeof(OwnedTool), ServiceLifetime.Singleton);
        AssertDescriptor(pluginServices, typeof(OwnedLifecycle), ServiceLifetime.Singleton);
        AssertDescriptor(pluginServices, typeof(DocumentLifetime), ServiceLifetime.Scoped);
        AssertDescriptor(pluginServices, typeof(IDocumentLifetime), ServiceLifetime.Scoped);
        AssertDescriptor(pluginServices, typeof(DocumentScopeManager), ServiceLifetime.Singleton);
        Assert.Same(windowInteraction, Assert.Single(pluginServices, item =>
            item.ServiceType == typeof(IPluginWindowInteraction)).ImplementationInstance);
        Assert.Throws<InvalidOperationException>(() =>
            registration.Services.AddSingleton<ThirdPrivateService>());

        using var pluginProvider = pluginServices.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        Assert.Equal(2, pluginProvider.GetServices<IPrivateService>().Count());
        Assert.IsType<FirstPrivateService>(
            pluginProvider.GetRequiredKeyedService<IPrivateService>("first"));
        Assert.IsType<PrivateBox<string>>(
            pluginProvider.GetRequiredService<IPrivateBox<string>>());
        Assert.Same(windowInteraction, pluginProvider.GetRequiredService<IPluginWindowInteraction>());
        Assert.Same(
            pluginProvider.GetRequiredService<OwnedTool>(),
            pluginProvider.GetRequiredService<OwnedTool>());
        Assert.Same(
            pluginProvider.GetRequiredService<OwnedLifecycle>(),
            pluginProvider.GetRequiredService<OwnedLifecycle>());
        using var firstScope = pluginProvider.CreateScope();
        using var secondScope = pluginProvider.CreateScope();
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<OwnedDocument>(),
            secondScope.ServiceProvider.GetRequiredService<OwnedDocument>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<DocumentLifetime>(),
            firstScope.ServiceProvider.GetRequiredService<IDocumentLifetime>());
    }

    [Fact]
    public void 普通和Keyed注册Host端口均在Provider构建前拒绝()
    {
        using var hostProvider = CreateHostProvider();
        var pluginServices = new ServiceCollection();
        var registration = new PluginRegistration(
            Owner,
            pluginServices,
            new PluginRegistryBuilder());
        registration.Services.AddSingleton<IDocumentLifetime, TestDocumentLifetime>();
        registration.Services.AddKeyedSingleton<IPluginWindowInteraction, TestWindowInteraction>("shadow");
        registration.Services.AddSingleton<IPluginWindowInteraction>(new TestWindowInteraction());
        registration.Services.AddSingleton<IPluginWindowInteraction>(
            _ => new TestWindowInteraction());
        registration.Services.AddKeyedSingleton<IPluginWindowInteraction>(
            "factory",
            (_, _) => new TestWindowInteraction());
        registration.Services.AddKeyedSingleton<IPluginWindowInteraction>(
            "instance",
            new TestWindowInteraction());
        registration.Seal();

        var exception = Assert.Throws<HostCompositionException>(() =>
            PluginServiceCommitGuard.ValidateAndCommit(
                pluginServices,
                registration,
                hostProvider));

        Assert.Equal(6, exception.Diagnostics.Count(item =>
            item.Code == HostDiagnosticCodes.PluginHostServiceRegistrationForbidden));
        Assert.DoesNotContain(pluginServices, item =>
            item.ServiceType == typeof(DocumentScopeManager));
    }

    [Fact]
    public void 手工登记DocumentTool和Lifecycle根类型无论生命周期或Keyed均拒绝()
    {
        using var hostProvider = CreateHostProvider();
        var pluginServices = new ServiceCollection();
        var registration = new PluginRegistration(
            Owner,
            pluginServices,
            new PluginRegistryBuilder());
        registration.Services.AddTransient<OwnedDocument>();
        registration.Services.AddKeyedSingleton<OwnedTool>("shadow");
        registration.Services.AddScoped<OwnedLifecycle>();
        registration.AddDocument<OwnedDocument, EmptyView>(Document(Owner, "sample"));
        registration.AddTool<OwnedTool, EmptyView>(Tool(Owner, "sample"));
        registration.UseLifecycle<OwnedLifecycle>();
        registration.Seal();

        var exception = Assert.Throws<HostCompositionException>(() =>
            PluginServiceCommitGuard.ValidateAndCommit(
                pluginServices,
                registration,
                hostProvider));

        Assert.Equal(3, exception.Diagnostics.Count(item =>
            item.Code == HostDiagnosticCodes.PluginContributionServiceRegistrationForbidden));
        Assert.DoesNotContain(pluginServices, item =>
            item.ServiceType == typeof(IDocumentLifetime));
    }

    [Fact]
    public void Document和Tool必须属于精确PluginId及正确贡献种类命名空间()
    {
        var registration = new PluginRegistration(
            Owner,
            new ServiceCollection(),
            new PluginRegistryBuilder());
        registration.AddDocument<OwnedDocument, EmptyView>(new DocumentDescriptor(
            new DocumentTypeId("myavalonia.host.document.welcome"),
            "Host 越权", "测试", "测试"));
        registration.AddDocument<SecondOwnedDocument, EmptyView>(new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.other.document.sample"),
            "他插件越权", "测试", "测试"));
        registration.AddDocument<ThirdOwnedDocument, EmptyView>(new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.ownership-testing.document.sample"),
            "相似前缀", "测试", "测试"));
        registration.AddDocument<FourthOwnedDocument, EmptyView>(new DocumentDescriptor(
            new DocumentTypeId($"{Owner.Value}.tool.wrong-kind"),
            "错误种类", "测试", "测试"));
        registration.AddTool<OwnedTool, EmptyView>(new ToolDescriptor(
            new ToolTypeId($"{Owner.Value}.document.wrong-kind"),
            "错误 Tool 种类", "测试", ToolDockSide.Left, ToolCloseBehavior.Hide));
        registration.AddTool<SecondOwnedTool, EmptyView>(new ToolDescriptor(
            new ToolTypeId($"{Owner.Value}.tool"),
            "缺少后缀", "测试", ToolDockSide.Left, ToolCloseBehavior.Hide));

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Equal(4, exception.Diagnostics.Count(item =>
            item.Code == HostDiagnosticCodes.DocumentIdOwnerMismatch));
        Assert.Equal(2, exception.Diagnostics.Count(item =>
            item.Code == HostDiagnosticCodes.ToolIdOwnerMismatch));
        Assert.Equal(
            exception.Diagnostics.Select(item => (item.Code, item.StableId)),
            exception.Diagnostics
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.StableId, StringComparer.Ordinal)
                .Select(item => (item.Code, item.StableId)));
    }

    [Fact]
    public void 全局重复检查作为绕过局部Seal后的纵深防线继续存在()
    {
        var builder = new PluginRegistryBuilder();
        var sharedId = new DocumentTypeId("shared.document.collision");
        builder.AddDocument(
            new PluginId("myavalonia.plugin.first"),
            new DocumentDescriptor(sharedId, "第一项", "测试", "测试"),
            typeof(OwnedDocument),
            typeof(EmptyView),
            static () => new EmptyView(),
            false);
        builder.AddDocument(
            new PluginId("myavalonia.plugin.second"),
            new DocumentDescriptor(sharedId, "第二项", "测试", "测试"),
            typeof(SecondOwnedDocument),
            typeof(SecondEmptyView),
            static () => new SecondEmptyView(),
            false);

        var registry = builder.Build(catalog: null);

        Assert.Empty(registry.DocumentDescriptors);
    }

    private static ServiceProvider CreateHostProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPluginWindowInteraction, TestWindowInteraction>();
        return services.BuildServiceProvider();
    }

    private static void AssertDescriptor(
        IEnumerable<ServiceDescriptor> services,
        Type serviceType,
        ServiceLifetime expectedLifetime) =>
        Assert.Equal(expectedLifetime, Assert.Single(services, item =>
            item.ServiceType == serviceType).Lifetime);

    private static DocumentDescriptor Document(PluginId owner, string suffix) => new(
        new DocumentTypeId($"{owner.Value}.document.{suffix}"),
        "测试 Document",
        "验证 G4 所有权",
        "测试");

    private static ToolDescriptor Tool(PluginId owner, string suffix) => new(
        new ToolTypeId($"{owner.Value}.tool.{suffix}"),
        "测试 Tool",
        "验证 G4 所有权",
        ToolDockSide.Left,
        ToolCloseBehavior.Hide);

    private interface IPrivateService;
    private sealed class FirstPrivateService : IPrivateService;
    private sealed class SecondPrivateService : IPrivateService;
    private sealed class ThirdPrivateService;
    private interface IPrivateBox<T>;
    private sealed class PrivateBox<T> : IPrivateBox<T>;

    private class OwnedDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivation activation,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class SecondOwnedDocument : OwnedDocument;
    private sealed class ThirdOwnedDocument : OwnedDocument;
    private sealed class FourthOwnedDocument : OwnedDocument;
    private sealed class OwnedTool;
    private sealed class SecondOwnedTool;
    private sealed class OwnedLifecycle : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyView : UserControl;
    private sealed class SecondEmptyView : UserControl;

    private sealed class TestDocumentLifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }

    private sealed class TestWindowInteraction : IPluginWindowInteraction
    {
        public Task<IReadOnlyList<string>> PickOpenFilesAsync(
            FilePickerOpenOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(
            FilePickerSaveOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<bool> TrySetClipboardTextAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

}
