using Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.ViewModels.Tools;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 G6 Adapter 的纯对象投影与所有权，不依赖 Avalonia 平台启动。</summary>
public sealed class HostDockAdapterTests
{
    [Fact]
    public void Host内建贡献模型全部是普通对象且只有Adapter继承Dock类型()
    {
        Assert.False(typeof(Document).IsAssignableFrom(typeof(WelcomeViewModel)));
        Assert.False(typeof(Tool).IsAssignableFrom(typeof(FileSystemTreeViewModel)));
        Assert.False(typeof(Tool).IsAssignableFrom(typeof(PlugGroupMenuViewModel)));
        Assert.False(typeof(Tool).IsAssignableFrom(typeof(ToolManagementViewModel)));
        Assert.False(typeof(Tool).IsAssignableFrom(typeof(PluginStatusViewModel)));
        Assert.True(typeof(Document).IsAssignableFrom(typeof(ManagedDocumentDockable)));
        Assert.True(typeof(Tool).IsAssignableFrom(typeof(ManagedToolDockable)));
    }

    [Fact]
    public void DocumentScope入口只接受普通插件模型()
    {
        var services = new ServiceCollection();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();

        Assert.Throws<InvalidOperationException>(() =>
            manager.CreatePluginDocument(typeof(object)));
    }

    [Fact]
    public void DocumentAdapter投影标题禁用浮动并按顺序释放Scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedDocument>();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var lease = manager.CreatePluginDocument(typeof(TrackedDocument));
        var model = Assert.IsType<TrackedDocument>(lease.Model);
        var registration = DocumentRegistration(typeof(TrackedDocument));
        var adapter = new ManagedDocumentDockable(
            new ActivatedPluginDocument(registration, lease),
            "请求标题");

        Assert.Equal("请求标题", adapter.Title);
        Assert.False(adapter.CanFloat);
        Assert.True(adapter.CanClose);
        Assert.False(adapter.CanPin);
        Assert.Same(model, adapter.Model);

        model.SetTitle("模型标题");
        Assert.Equal("模型标题", adapter.Title);
        adapter.Dispose();

        Assert.True(model.ClosingObservedDuringDispose);
        Assert.Equal(1, model.DisposeCount);
        adapter.Dispose();
        Assert.Equal(1, model.DisposeCount);
    }

    [Fact]
    public void Document事件退订抛出时仍取消ClosingToken并释放Scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ThrowingRemoveDocument>();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var lease = manager.CreatePluginDocument(typeof(ThrowingRemoveDocument));
        var model = Assert.IsType<ThrowingRemoveDocument>(lease.Model);
        var adapter = new ManagedDocumentDockable(
            new ActivatedPluginDocument(
                DocumentRegistration(typeof(ThrowingRemoveDocument)),
                lease),
            "请求标题");

        Assert.Throws<InvalidOperationException>(() => adapter.Dispose());

        Assert.True(model.ClosingObservedDuringDispose);
        Assert.Equal(1, model.DisposeCount);
        Assert.False(manager.Release(model));
    }

    [Theory]
    [InlineData(ToolCloseBehavior.Hide, true)]
    [InlineData(ToolCloseBehavior.Prevent, false)]
    public void ToolAdapter按Descriptor投影稳定状态(
        ToolCloseBehavior closeBehavior,
        bool canClose)
    {
        var descriptor = new ToolDescriptor(
            new ToolTypeId("myavalonia.plugin.g6.tool.sample"),
            "示例工具",
            "普通模型 Tool",
            ToolDockSide.Bottom,
            closeBehavior);
        var registration = new PluginToolRegistration(
            new PluginId("myavalonia.plugin.g6"),
            descriptor,
            typeof(object),
            typeof(UserControl),
            static () => new UserControl());
        var model = new object();
        using var adapter = new ManagedToolDockable(
            new ActivatedPluginTool(registration, model));

        Assert.Equal(descriptor.ToolTypeId.Value, adapter.Id);
        Assert.Equal(descriptor.DisplayName, adapter.Title);
        Assert.Equal(canClose, adapter.CanClose);
        Assert.True(adapter.CanPin);
        Assert.False(adapter.CanFloat);
        Assert.Same(model, adapter.Model);
    }

    [Fact]
    public void 单个Tool创建失败只隔离自身且诊断不泄露异常正文()
    {
        var healthyId = new ToolTypeId("myavalonia.plugin.g6.tool.healthy");
        var failedId = new ToolTypeId("myavalonia.plugin.g6.tool.failed");
        var registry = new PluginRegistry(
            [DocumentRegistration(typeof(TrackedDocument)) with
            {
                Descriptor = new DocumentDescriptor(
                    HostExtensionIds.V2WelcomeDocument,
                    "欢迎",
                    "Host Welcome",
                    "Host"),
            }],
            [ToolRegistration(healthyId), ToolRegistration(failedId)]);
        var dockableFactory = new SelectiveToolFactory(failedId);
        using var diagnostics = HostDiagnosticSession.Start(
            Path.Combine(Path.GetTempPath(), "g6-tool-isolation", Guid.NewGuid().ToString("N")));
        using var factory = new ManagementFactory(
            registry,
            dockableFactory,
            new DocumentScopeRegistry(),
            diagnostics: diagnostics);

        var root = factory.CreateLayout();

        Assert.NotNull(root);
        Assert.Contains(healthyId.Value, factory.CreatedTools.Keys);
        Assert.DoesNotContain(failedId.Value, factory.CreatedTools.Keys);
        var record = Assert.Single(
            diagnostics.Snapshot,
            item => item.Code == HostDiagnosticCodes.ToolAdapterActivationFailed);
        Assert.Equal(failedId.Value, record.StableId);
        Assert.Null(record.PluginDirectory);
        Assert.Null(record.TechnicalDetail);
        Assert.DoesNotContain("插件私有异常正文", record.UserMessage, StringComparison.Ordinal);
    }

    private static PluginDocumentRegistration DocumentRegistration(Type modelType) => new(
        HostExtensionIds.V2Owner,
        new DocumentDescriptor(
            new DocumentTypeId("myavalonia.host.document.g6-test"),
            "回退标题",
            "G6 测试",
            "测试"),
        modelType,
        typeof(UserControl),
        static () => new UserControl(),
        false);

    private static PluginToolRegistration ToolRegistration(ToolTypeId toolTypeId) => new(
        new PluginId("myavalonia.plugin.g6"),
        new ToolDescriptor(
            toolTypeId,
            toolTypeId.Value,
            "G6 隔离测试",
            ToolDockSide.Left,
            ToolCloseBehavior.Hide),
        typeof(object),
        typeof(UserControl),
        static () => new UserControl());

    /// <summary>
    /// 只模拟 Adapter 发布边界：一个 Tool 失败，另一个成功。它不创建 Avalonia View，
    /// 使本测试只验证 ManagementFactory 的隔离与原子发布职责。
    /// </summary>
    private sealed class SelectiveToolFactory(ToolTypeId failedId) : IHostDockableFactory
    {
        public ValueTask<Document> CreateDocumentAsync(
            DocumentTypeId documentTypeId,
            DocumentActivationContext context) =>
            ValueTask.FromResult<Document>(new Document
            {
                Id = documentTypeId.Value,
                Title = context.Title,
            });

        public Tool CreateTool(ToolTypeId toolTypeId)
        {
            if (toolTypeId == failedId)
            {
                throw new InvalidOperationException(
                    "插件私有异常正文 D:\\private\\plugin\\secret.dll");
            }

            return new Tool { Id = toolTypeId.Value, Title = toolTypeId.Value };
        }
    }

    private sealed class TrackedDocument(
        MyAvaloniaManagement.PluginSdk.IDocumentLifetime lifetime) :
        IPluginDocument,
        IDisposable
    {
        private string _title = string.Empty;
        public int DisposeCount { get; private set; }
        public bool ClosingObservedDuringDispose { get; private set; }
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

        public void Dispose()
        {
            ClosingObservedDuringDispose = lifetime.IsClosing;
            DisposeCount++;
        }
    }

    private sealed class ThrowingRemoveDocument(
        MyAvaloniaManagement.PluginSdk.IDocumentLifetime lifetime) :
        IPluginDocument,
        IDisposable
    {
        public int DisposeCount { get; private set; }
        public bool ClosingObservedDuringDispose { get; private set; }
        public DocumentPresentationState Presentation => new("事件异常测试");
        public event EventHandler? PresentationChanged
        {
            add { }
            remove { throw new InvalidOperationException("插件事件退订失败"); }
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
}
