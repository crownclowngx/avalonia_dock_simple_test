using Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Tools;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 V3 G6 的 Factory Adapter、Session 所有权和无 Dock 只读投影边界。</summary>
public sealed class WorkspaceSessionAndDockFactoryTests
{
    [Fact]
    public void DockFactory未绑定和重复绑定均快速失败()
    {
        var unbound = new HostDockFactory();
        Assert.Throws<InvalidOperationException>(unbound.CreateLayout);

        using var context = new TestHostContext();
        var factory = context.Workspace.DockFactory;
        Assert.Throws<InvalidOperationException>(() =>
            factory.AttachCallbacks(context.Workspace));
    }

    [Fact]
    public void DockFactory仅转发框架协议并建立规范Locator()
    {
        var factory = new HostDockFactory();
        var callbacks = new RecordingWorkspaceCallbacks(factory);
        factory.AttachCallbacks(callbacks);

        Assert.Same(callbacks.Root, factory.CreateLayout());
        factory.InitLayout(callbacks.Root);
        Assert.Same(callbacks.Root, factory.GetDockable<IRootDock>(DockLayoutIds.Root));
        Assert.Same(callbacks.Workspace, factory.GetDockable<IDock>(DockLayoutIds.Workspace));
        Assert.Same(callbacks.Documents, factory.GetDockable<IDocumentDock>(DockLayoutIds.Documents));
        Assert.Null(factory.GetDockable<ITool>("Plug"));
        Assert.Same(callbacks.Root, factory.GetContext(callbacks.Tool.Id!));
        Assert.NotNull(factory.HostWindowLocator);
        Assert.Contains("IDockWindow", factory.HostWindowLocator.Keys);

        factory.OnDockableDocked(callbacks.Tool, DockOperation.Top);
        factory.OnDockableHidden(callbacks.Tool);
        callbacks.AllowClose = false;
        Assert.False(factory.OnDockableClosing(callbacks.Tool));
        callbacks.AllowClose = true;
        Assert.True(factory.OnDockableClosing(callbacks.Tool));
        factory.OnDockableClosed(callbacks.Tool);

        Assert.Equal(1, callbacks.DockedCount);
        Assert.Equal(1, callbacks.HiddenCount);
        Assert.Equal(2, callbacks.ClosingCount);
        Assert.Equal(1, callbacks.ClosedCount);
    }

    [Fact]
    public void Factory与Session在类型和状态所有权上明确分离()
    {
        Assert.True(typeof(Factory).IsAssignableFrom(typeof(HostDockFactory)));
        Assert.False(typeof(Factory).IsAssignableFrom(typeof(WorkspaceSession)));
        Assert.True(typeof(HostDockFactory).IsSealed);
        Assert.True(typeof(WorkspaceSession).IsSealed);
        Assert.Null(typeof(WorkspaceSession).Assembly.GetType(
            "MyAvaloniaManagement.ViewModels.ManagementFactory"));
    }

    [Fact]
    public void 多个主窗口ViewModel共享唯一Session布局且不重复创建Tool()
    {
        using var context = new TestHostContext();
        using var first = context.CreateMainWindowViewModel();
        var toolSnapshot = context.Workspace.CreatedTools.ToArray();
        using var second = context.CreateMainWindowViewModel();

        Assert.Same(first.Layout, second.Layout);
        Assert.Same(first.Layout, context.Workspace.RootDock);
        Assert.Equal(toolSnapshot.Length, context.Workspace.CreatedTools.Count);
        Assert.All(toolSnapshot, pair =>
            Assert.Same(pair.Value, context.Workspace.CreatedTools[pair.Key]));
    }

    [Fact]
    public void MainWindow订阅可独立释放而不影响其他窗口投影()
    {
        using var context = new TestHostContext();
        var first = context.CreateMainWindowViewModel();
        using var second = context.CreateMainWindowViewModel();
        var firstChanges = 0;
        var secondChanges = 0;
        first.PropertyChanged += (_, args) =>
            firstChanges += args.PropertyName == nameof(MainWindowViewModel.Layout) ? 1 : 0;
        second.PropertyChanged += (_, args) =>
            secondChanges += args.PropertyName == nameof(MainWindowViewModel.Layout) ? 1 : 0;
        first.Dispose();

        Assert.True(context.Workspace.ShowTool(HostExtensionIds.PluginMenu));
        Assert.Equal(0, firstChanges);
        Assert.Equal(1, secondChanges);
    }

    [Fact]
    public void DockLocator只提供规范Documents与ToolId且不再提供Files和Plug()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var pluginMenu = context.Workspace.CreatedTools[HostExtensionIds.PluginMenu.Value];

        Assert.IsType<DocumentDock>(context.Workspace.DockFactory.GetDockable<
            Dock.Model.Controls.IDocumentDock>(DockLayoutIds.Documents));
        Assert.Same(
            pluginMenu,
            context.Workspace.DockFactory.GetDockable<ITool>(HostExtensionIds.PluginMenu.Value));
        Assert.Null(context.Workspace.DockFactory.GetDockable<ITool>("Plug"));
        Assert.Null(context.Workspace.DockFactory.GetDockable<
            Dock.Model.Controls.IDocumentDock>("Files"));
    }

    [Fact]
    public void Session工具查询无变化提交与幂等释放保持稳定()
    {
        using var context = new TestHostContext();
        _ = context.Workspace.CreateLayout();
        var pluginMenuId = HostExtensionIds.PluginMenu.Value;

        Assert.True(context.Workspace.IsRegisteredTool(pluginMenuId));
        Assert.True(context.Workspace.IsToolAvailable(pluginMenuId));
        Assert.False(context.Workspace.IsRegisteredTool("not-a-tool-id"));
        Assert.False(context.Workspace.IsToolAvailable("not-a-tool-id"));
        var hideableTool = context.Workspace.CreatedTools.Values.First(tool => tool.CanClose);
        Assert.False(context.Workspace.TrySetToolVisibility(hideableTool.Id, isVisible: true));

        var tool = context.Workspace.CreatedTools[pluginMenuId];
        context.Workspace.DockFactory.OnDockableDocked(tool, DockOperation.Top);
        context.Workspace.Dispose();
        context.Workspace.Dispose();
    }

    [Fact]
    public async Task Session统一处理Document发布回滚关闭与退出拒绝()
    {
        using var unpublishedContext = DocumentTestContext.Create();
        var unpublished = unpublishedContext.Workspace;
        Assert.False(unpublished.TryActivateDocument("missing.mamdoc"));
        Assert.False(unpublished.TryGetPersistablePluginDocumentRegistration(
            new DocumentTypeId("myavalonia.unknown.document"),
            out _));
        await Assert.ThrowsAsync<NotSupportedException>(() => unpublished
            .CreateDocumentAsync(
                new DocumentTypeId("myavalonia.unknown.document"),
                new NewDocumentActivation("未知"))
            .AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => unpublished
            .CreateAndPublishDocumentAsync(
                TestDocumentIds.TypeId,
                new NewDocumentActivation("尚未建立布局"))
            .AsTask());
        Assert.Equal(
            1,
            unpublishedContext.Provider
                .GetRequiredService<DocumentTestProbe>()
                .DisposeCount);

        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var session = context.Workspace;
        var root = session.CreateLayout();
        Assert.Same(root, session.CreateLayout());
        var document = await session.CreateDocumentAsync(
            TestDocumentIds.TypeId,
            new NewDocumentActivation("发布测试"));
        session.PublishDocument(document);
        Assert.Throws<InvalidOperationException>(() => session.PublishDocument(document));
        Assert.True(session.DockFactory.OnDockableClosing(document));
        session.DockFactory.OnDockableClosed(document);
        Assert.DoesNotContain(document, session.GetDocuments());

        session.BeginShutdown();
        Assert.Throws<ObjectDisposedException>(session.CreateLayout);
    }

    [Fact]
    public void 布局恢复回退重建Root但复用同一Tool所有权集合()
    {
        using var context = new TestHostContext();
        var originalRoot = context.Workspace.CreateLayout();
        var originalTools = context.Workspace.CreatedTools.ToArray();

        var rebuiltRoot = context.Workspace.RecreateLayoutAfterFailedRestore();

        Assert.NotSame(originalRoot, rebuiltRoot);
        Assert.Single(context.Workspace.GetDocuments());
        Assert.Equal(originalTools.Length, context.Workspace.CreatedTools.Count);
        Assert.All(originalTools, pair =>
            Assert.Same(pair.Value, context.Workspace.CreatedTools[pair.Key]));
    }

    [Fact]
    public async Task 退出按Document先于Tool释放且单项异常最终聚合()
    {
        var probe = new SessionReleaseProbe();
        var toolId = new ToolTypeId("myavalonia.host.tool.g6-release-order");
        var tool = new OrderedDisposableTool(probe);
        var documentId = new DocumentTypeId("myavalonia.host.document.g6-release-order");
        using var context = new TestHostContext(
            [new StubToolContribution(
                tool,
                new ToolDescriptor(
                    toolId,
                    "释放顺序 Tool",
                    "验证 Session 退出顺序",
                    ToolDockSide.Left,
                    ToolCloseBehavior.Hide))],
            configureServices: services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<ThrowingSessionDocument>();
            },
            configureContributions: (_, builder) => builder.AddDocument(
                TestPluginIds.Owner,
                new DocumentDescriptor(
                    documentId,
                    "释放顺序 Document",
                    "验证 Session 退出聚合",
                    "测试"),
                typeof(ThrowingSessionDocument),
                typeof(UserControl),
                static () => new UserControl(),
                isPersistable: false));
        _ = context.Workspace.CreateLayout();
        _ = await context.Workspace.CreateDocumentAsync(
            documentId,
            new NewDocumentActivation("退出测试"));

        var exception = Assert.Throws<AggregateException>(context.Workspace.Dispose);

        Assert.Single(exception.InnerExceptions);
        Assert.Equal(["document", "tool"], probe.Events);
        Assert.Empty(context.Workspace.CreatedTools);
        context.Workspace.Dispose();
    }

    [Fact]
    public void Tool只读状态和ViewModel依赖均不泄漏Dock类型()
    {
        var stateProperties = typeof(ToolWorkspaceState).GetProperties();
        Assert.All(stateProperties, property => Assert.False(
            property.PropertyType.Namespace?.StartsWith("Dock.", StringComparison.Ordinal) == true));

        var constructorTypes = typeof(ToolManagementViewModel)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.Equal(
            [typeof(ToolWorkspaceReadModel), typeof(WorkspaceSession)],
            constructorTypes);
        Assert.DoesNotContain(
            typeof(ToolManagementViewModel).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType.Namespace?.StartsWith("Dock.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Welcome生产模型通过窄动作显示对应Tool()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var welcome = Assert.IsType<MyAvaloniaManagement.ViewModels.Hello.WelcomeViewModel>(
            context.Workspace.GetDocuments().Single().Model);
        var pluginMenu = context.Workspace.CreatedTools[HostExtensionIds.PluginMenu.Value];
        context.Workspace.DockFactory.HideDockable(pluginMenu);

        welcome.OpenPluginMenuCommand.Execute(null);

        Assert.NotNull(DockTreeNavigator.FindToolDock(
            context.Workspace.RootDock!,
            Assert.IsAssignableFrom<Tool>(pluginMenu)));
    }

    /// <summary>
    /// 记录 Factory 的窄回调，用纯对象断言 override 转发与 Locator；它刻意不包含任何
    /// 工作区业务，从而让测试本身也保持 Factory Adapter 与 Session 所有权分离。
    /// </summary>
    private sealed class RecordingWorkspaceCallbacks : IWorkspaceDockCallbacks
    {
        internal RecordingWorkspaceCallbacks(HostDockFactory factory)
        {
            Tool = new Tool { Id = "myavalonia.test.tool", Title = "测试 Tool" };
            Documents = new DocumentDock { Id = DockLayoutIds.Documents };
            Workspace = new ProportionalDock
            {
                Id = DockLayoutIds.Workspace,
                VisibleDockables = factory.CreateList<IDockable>(Documents, Tool),
                ActiveDockable = Documents,
            };
            Root = new RootDock
            {
                Id = DockLayoutIds.Root,
                VisibleDockables = factory.CreateList<IDockable>(Workspace),
                ActiveDockable = Workspace,
            };
        }

        internal RootDock Root { get; }
        internal ProportionalDock Workspace { get; }
        internal DocumentDock Documents { get; }
        internal Tool Tool { get; }
        internal bool AllowClose { get; set; } = true;
        internal int DockedCount { get; private set; }
        internal int HiddenCount { get; private set; }
        internal int ClosingCount { get; private set; }
        internal int ClosedCount { get; private set; }

        IRootDock? IWorkspaceDockCallbacks.RootDock => Root;
        IReadOnlyCollection<string> IWorkspaceDockCallbacks.CreatedToolIds => [Tool.Id!];
        IRootDock IWorkspaceDockCallbacks.CreateLayout() => Root;
        IDockable? IWorkspaceDockCallbacks.ResolveDockable(string dockableId) => dockableId switch
        {
            DockLayoutIds.Documents => Documents,
            _ when dockableId == Tool.Id => Tool,
            _ => null,
        };
        void IWorkspaceDockCallbacks.OnDockableDocked(
            IDockable? dockable,
            DockOperation operation) => DockedCount++;
        void IWorkspaceDockCallbacks.OnDockableHidden(IDockable? dockable) => HiddenCount++;
        bool IWorkspaceDockCallbacks.OnDockableClosing(IDockable? dockable)
        {
            ClosingCount++;
            return AllowClose;
        }
        void IWorkspaceDockCallbacks.OnDockableClosed(IDockable? dockable) => ClosedCount++;
    }

    private sealed class SessionReleaseProbe
    {
        internal List<string> Events { get; } = [];
    }

    /// <summary>在模型释放时抛出事件退订异常，验证 Session 仍继续释放后续资源。</summary>
    private sealed class ThrowingSessionDocument(
        SessionReleaseProbe probe,
        IDocumentLifetime lifetime) : IPluginDocument, IDisposable
    {
        public DocumentPresentationState Presentation => new("释放顺序 Document");
        public event EventHandler? PresentationChanged
        {
            add { }
            remove { throw new InvalidOperationException("测试事件退订失败"); }
        }

        public ValueTask InitializeAsync(
            DocumentActivation activation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            Assert.True(lifetime.IsClosing);
            probe.Events.Add("document");
        }
    }

    /// <summary>记录 Tool 释放时点；幂等实现避免 Host Provider 兜底释放重复写入探针。</summary>
    private sealed class OrderedDisposableTool(SessionReleaseProbe probe) : Tool, IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            probe.Events.Add("tool");
        }
    }
}
