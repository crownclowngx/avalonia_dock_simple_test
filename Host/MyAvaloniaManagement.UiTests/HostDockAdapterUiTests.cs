using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>在真实 Headless Avalonia Dispatcher 与控件绑定环境中验证 G6 View/标题投影。</summary>
public sealed class HostDockAdapterUiTests
{
    [AvaloniaFact]
    public void View预构建只执行一次并把普通模型设置为DataContext()
    {
        var created = 0;
        var registration = Registration(() =>
        {
            created++;
            return new DisposableProbeView();
        });
        var registry = new PluginRegistry([registration], []);
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var lease = manager.CreatePluginDocument(typeof(MutableDocument));
        var model = Assert.IsType<MutableDocument>(lease.Model);
        using var adapter = new ManagedDocumentDockable(
            new ActivatedPluginDocument(registration, lease),
            "请求标题");
        var locator = new ViewLocator(registry);

        var prepared = locator.Prepare(adapter);

        Assert.Same(prepared, locator.Build(adapter));
        Assert.Same(model, prepared.DataContext);
        Assert.Equal(1, created);
        Assert.False(locator.Match(model));
        Assert.True(locator.Match(adapter));
    }

    [AvaloniaFact]
    public async Task 后台标题变化切回UI线程且释放后迟到通知无效()
    {
        var registration = Registration(static () => new DisposableProbeView());
        var registry = new PluginRegistry([registration], []);
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var lease = manager.CreatePluginDocument(typeof(MutableDocument));
        var model = Assert.IsType<MutableDocument>(lease.Model);
        var adapter = new ManagedDocumentDockable(
            new ActivatedPluginDocument(registration, lease),
            "请求标题");
        var locator = new ViewLocator(registry);
        var view = Assert.IsType<DisposableProbeView>(locator.Prepare(adapter));

        await Task.Run(() => model.SetTitle("后台标题"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("后台标题", adapter.Title);

        adapter.Dispose();
        model.SetTitle("迟到标题");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("后台标题", adapter.Title);
        Assert.Null(view.DataContext);
        Assert.Equal(1, view.DisposeCount);
    }

    [AvaloniaFact]
    public async Task DocumentView创建失败时Factory释放已建立Adapter和Scope()
    {
        ViewFailureDocument? created = null;
        var services = new ServiceCollection();
        services.AddScoped<ViewFailureDocument>(provider =>
            created = new ViewFailureDocument(
                provider.GetRequiredService<IDocumentLifetime>()));
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var registration = DocumentRegistration(
            typeof(ViewFailureDocument),
            static () => throw new InvalidOperationException("View 创建失败"));
        var registry = new PluginRegistry([registration], []);
        using var pluginProviders = new PluginProviderOwner();
        var factory = new HostDockAdapterFactory(
            new PluginContributionActivator(provider, registry, pluginProviders),
            new ViewLocator(registry));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await factory.CreateDocumentAsync(
                registration.Descriptor.DocumentTypeId,
                new DocumentActivationContext("View 失败")));

        Assert.NotNull(created);
        Assert.True(created.ClosingObservedDuringDispose);
        Assert.Equal(1, created.DisposeCount);
    }

    [AvaloniaFact]
    public async Task DocumentAdapter构造失败时Factory仍直接释放暂存Scope()
    {
        ThrowingPresentationDocument? created = null;
        var services = new ServiceCollection();
        services.AddScoped<ThrowingPresentationDocument>(_ =>
            created = new ThrowingPresentationDocument());
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var registration = DocumentRegistration(
            typeof(ThrowingPresentationDocument),
            static () => new UserControl());
        var registry = new PluginRegistry([registration], []);
        using var pluginProviders = new PluginProviderOwner();
        var factory = new HostDockAdapterFactory(
            new PluginContributionActivator(provider, registry, pluginProviders),
            new ViewLocator(registry));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await factory.CreateDocumentAsync(
                registration.Descriptor.DocumentTypeId,
                new DocumentActivationContext("Adapter 失败")));

        Assert.NotNull(created);
        Assert.Equal(1, created.DisposeCount);
    }

    [AvaloniaFact]
    public void ToolView创建失败时Factory不释放Provider拥有的Singleton模型()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposableToolModel>();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var toolTypeId = new ToolTypeId("myavalonia.host.tool.g6-view-failure");
        var registration = new PluginToolRegistration(
            HostExtensionIds.V2Owner,
            new ToolDescriptor(
                toolTypeId,
                "失败 Tool",
                "验证模型所有权",
                ToolDockSide.Right,
                ToolCloseBehavior.Hide),
            typeof(DisposableToolModel),
            typeof(UserControl),
            static () => throw new InvalidOperationException("View 创建失败"));
        var registry = new PluginRegistry([], [registration]);
        using var pluginProviders = new PluginProviderOwner();
        var factory = new HostDockAdapterFactory(
            new PluginContributionActivator(provider, registry, pluginProviders),
            new ViewLocator(registry));

        Assert.Throws<InvalidOperationException>(() => factory.CreateTool(toolTypeId));

        Assert.Equal(0, provider.GetRequiredService<DisposableToolModel>().DisposeCount);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<MutableDocument>();
        services.AddDocumentScopeManagement();
        return services.BuildServiceProvider();
    }

    private static PluginDocumentRegistration Registration(Func<Control> viewFactory) => new(
        new PluginId("myavalonia.plugin.g6-ui"),
        new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.g6-ui.document.sample"),
            "示例",
            "G6 UI 测试",
            "测试"),
        typeof(MutableDocument),
        typeof(DisposableProbeView),
        viewFactory,
        false);

    private static PluginDocumentRegistration DocumentRegistration(
        Type modelType,
        Func<Control> viewFactory) => new(
        HostExtensionIds.V2Owner,
        new DocumentDescriptor(
            new DocumentTypeId($"myavalonia.host.document.{modelType.Name.ToLowerInvariant()}"),
            "Factory 回滚测试",
            "G6 UI 测试",
            "测试"),
        modelType,
        typeof(UserControl),
        viewFactory,
        false);

    private sealed class MutableDocument : IPluginDocument
    {
        private string _title = string.Empty;
        public DocumentPresentationState Presentation => new(_title);
        public event EventHandler? PresentationChanged;
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal void SetTitle(string title)
        {
            _title = title;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ViewFailureDocument(IDocumentLifetime lifetime) :
        IPluginDocument,
        IDisposable
    {
        public int DisposeCount { get; private set; }
        public bool ClosingObservedDuringDispose { get; private set; }
        public DocumentPresentationState Presentation => new("可构造");
        public event EventHandler? PresentationChanged
        {
            add { }
            remove { }
        }
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose()
        {
            ClosingObservedDuringDispose = lifetime.IsClosing;
            DisposeCount++;
        }
    }

    private sealed class ThrowingPresentationDocument : IPluginDocument, IDisposable
    {
        public int DisposeCount { get; private set; }
        public DocumentPresentationState Presentation =>
            throw new InvalidOperationException("Presentation 读取失败");
        public event EventHandler? PresentationChanged
        {
            add { }
            remove { }
        }
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Dispose() => DisposeCount++;
    }

    private sealed class DisposableToolModel : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class DisposableProbeView : UserControl, IDisposable
    {
        internal int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
