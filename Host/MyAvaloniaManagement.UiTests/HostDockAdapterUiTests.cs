using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Workspace;
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
        var lease = manager.CreateDocument(typeof(MutableDocument));
        var model = Assert.IsType<MutableDocument>(lease.Model);
        using var adapter = new ManagedDocumentDockable(
            new ActivatedWorkspaceDocument(registration, lease),
            "请求标题");
        var locator = new ViewLocator(UiWorkspaceCatalogFactory.Create(registry));

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
        var lease = manager.CreateDocument(typeof(MutableDocument));
        var model = Assert.IsType<MutableDocument>(lease.Model);
        var adapter = new ManagedDocumentDockable(
            new ActivatedWorkspaceDocument(registration, lease),
            "请求标题");
        var locator = new ViewLocator(UiWorkspaceCatalogFactory.Create(registry));
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
    public async Task 可持久化Document后台脏状态投影到Dock且Host标题提交后保持权威()
    {
        var services = new ServiceCollection();
        services.AddScoped<MutablePersistableDocument>();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var lease = manager.CreateDocument(typeof(MutablePersistableDocument));
        var model = Assert.IsType<MutablePersistableDocument>(lease.Model);
        var registration = PersistableRegistration();
        var adapter = new ManagedDocumentDockable(
            new ActivatedWorkspaceDocument(registration, lease),
            "请求标题");

        Assert.False(adapter.IsModified);
        await Task.Run(() => model.SetDirty(true));
        Dispatcher.UIThread.RunJobs();
        Assert.True(adapter.IsModified);

        adapter.CommitHostTitle("saved-file");
        model.SetTitle("插件覆盖标题");
        Assert.Equal("saved-file", adapter.Title);

        await Task.Run(() => model.SetDirty(false));
        Dispatcher.UIThread.RunJobs();
        Assert.False(adapter.IsModified);

        adapter.Dispose();
        model.SetDirty(true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(adapter.IsModified);
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
        var registration = HostDocumentRegistration(
            provider,
            typeof(ViewFailureDocument),
            static () => throw new InvalidOperationException("View 创建失败"));
        var factory = CreateHostFactory(new HostWorkspaceCatalog([registration], []));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await factory.CreateDocumentAsync(
                registration.Descriptor.DocumentTypeId,
                new NewDocumentActivation("View 失败")));

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
        var registration = HostDocumentRegistration(
            provider,
            typeof(ThrowingPresentationDocument),
            static () => new UserControl());
        var factory = CreateHostFactory(new HostWorkspaceCatalog([registration], []));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await factory.CreateDocumentAsync(
                registration.Descriptor.DocumentTypeId,
                new NewDocumentActivation("Adapter 失败")));

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
        var toolTypeId = new ToolTypeId("myavalonia.host.tool.g7-view-failure");
        var registration = new HostWorkspaceToolRegistration(
            new ToolDescriptor(
                toolTypeId,
                "失败 Tool",
                "验证模型所有权",
                ToolDockSide.Right,
                ToolCloseBehavior.Hide),
            typeof(DisposableToolModel),
            typeof(UserControl),
            static () => throw new InvalidOperationException("View 创建失败"),
            () => provider.GetRequiredService<DisposableToolModel>());
        var factory = CreateHostFactory(new HostWorkspaceCatalog([], [registration]));

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

    private static HostWorkspaceDocumentRegistration HostDocumentRegistration(
        IServiceProvider provider,
        Type modelType,
        Func<Control> viewFactory) => new(
        new DocumentDescriptor(
            new DocumentTypeId($"myavalonia.host.document.{modelType.Name.ToLowerInvariant()}"),
            "Factory 回滚测试",
            "G6 UI 测试",
            "测试"),
        modelType,
        typeof(UserControl),
        viewFactory,
        () => provider.GetRequiredService<DocumentScopeManager>().CreateDocument(modelType),
        static (model, activation, token) =>
            model.InitializeAsync(activation, token).GetAwaiter().GetResult());

    private static PluginDocumentRegistration PersistableRegistration() => new(
        UiWorkspaceCatalogFactory.PluginOwner,
        new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.g7-ui-tests.document.mutable-persistable"),
            "可持久化测试",
            "脏状态投影测试",
            "测试"),
        typeof(MutablePersistableDocument),
        typeof(UserControl),
        static () => new UserControl(),
        true);

    private static HostDockAdapterFactory CreateHostFactory(HostWorkspaceCatalog hostCatalog)
    {
        var registry = new PluginRegistry([], []);
        var catalog = UiWorkspaceCatalogFactory.Create(registry, hostCatalog);
        var pluginProviders = new PluginProviderOwner();
        return new HostDockAdapterFactory(
            catalog,
            new HostWorkspaceActivator(hostCatalog),
            new PluginContributionActivator(registry, pluginProviders),
            new ViewLocator(catalog));
    }

    private sealed class MutableDocument : IPluginDocument
    {
        private string _title = string.Empty;
        public DocumentPresentationState Presentation => new(_title);
        public event EventHandler? PresentationChanged;
        public ValueTask InitializeAsync(
            DocumentActivation context,
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
            DocumentActivation context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose()
        {
            ClosingObservedDuringDispose = lifetime.IsClosing;
            DisposeCount++;
        }
    }

    private sealed class MutablePersistableDocument : IPersistablePluginDocument
    {
        private bool _isDirty;
        private string _title = "未保存标题";

        public bool IsDirty => _isDirty;
        public event EventHandler? IsDirtyChanged;
        public DocumentPresentationState Presentation => new(_title);
        public event EventHandler? PresentationChanged;

        public ValueTask InitializeAsync(
            DocumentActivation context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AcceptChanges(DocumentRevision savedRevision) => SetDirty(false);

        internal void SetDirty(bool value)
        {
            if (_isDirty == value)
            {
                return;
            }

            _isDirty = value;
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void SetTitle(string title)
        {
            _title = title;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
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
            DocumentActivation context,
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
