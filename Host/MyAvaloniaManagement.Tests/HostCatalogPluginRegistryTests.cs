using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Welcome;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagement.Views.Welcome;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证 V3 G7 的 Host Catalog、Plugin Registry 与 Workspace 只读合并边界。
/// </summary>
/// <remarks>
/// 测试刻意分别构造 Host 与插件事实：Host 注册记录没有 PluginId，插件记录必须带真实 owner。
/// 这样可以证明绿色结果不依赖已经删除的 Host 伪插件或 Availability 特判。
/// </remarks>
public sealed class HostCatalogPluginRegistryTests
{
    [Fact]
    public void 零插件时Registry为空而HostCatalog仍发布Welcome和四个Tool()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices().AddViewModels();
        services.AddSingleton(PluginModuleCatalog.Discover(PluginDiscoverySnapshot.Empty));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var registry = provider.GetRequiredService<PluginRegistry>();
        var host = provider.GetRequiredService<HostWorkspaceCatalog>();
        var workspace = provider.GetRequiredService<WorkspaceCatalog>();

        Assert.Empty(registry.Plugins);
        Assert.Empty(registry.Documents);
        Assert.Empty(registry.Tools);
        Assert.Empty(registry.DeclaredOwnerIds);
        Assert.Single(host.Documents);
        Assert.Equal(4, host.Tools.Count);
        Assert.True(workspace.TryGetDocument(HostExtensionIds.WelcomeDocument, out var welcome));
        Assert.IsType<HostWorkspaceDocumentRegistration>(welcome);
        Assert.Single(workspace.GetCreationEntries());
        Assert.Empty(provider.GetRequiredService<PluginStatusViewModel>().Items);

        using var context = new TestHostContext();
        Assert.NotNull(context.Workspace.CreateLayout());
        Assert.Single(context.Workspace.GetDocuments());
        Assert.Equal(4, context.Workspace.CreatedTools.Count);
    }

    [Fact]
    public void 插件生命周期失败只撤回插件项且Host项始终可查询()
    {
        var owner = new PluginId("myavalonia.plugin.g7-failed");
        var pluginDocument = new PluginDocumentRegistration(
            owner,
            new DocumentDescriptor(
                new DocumentTypeId("myavalonia.plugin.g7-failed.document.sample"),
                "失败插件文档",
                "失败插件文档",
                "插件"),
            typeof(FailedPluginDocument),
            typeof(UserControl),
            static () => new UserControl(),
            false);
        var registry = new PluginRegistry(
            [],
            [pluginDocument],
            [],
            [new PluginLifecycleDeclaration(owner, typeof(FailedLifecycle))]);
        var states = new PluginLifecycleStateStore(registry);
        states.SetState(new PluginLifecycleState(
            owner,
            PluginLifecycleStatus.InitializationFailed));
        var host = CreateQueryOnlyHostCatalog();
        var workspace = new WorkspaceCatalog(
            host,
            registry,
            new PluginAvailabilityReadModel(states));

        Assert.True(workspace.TryGetDocument(HostExtensionIds.WelcomeDocument, out _));
        Assert.False(workspace.TryGetDocument(pluginDocument.Descriptor.DocumentTypeId, out _));
        Assert.Equal(
            [HostExtensionIds.WelcomeDocument],
            workspace.GetCreationEntries().Select(item => item.DocumentTypeId));
    }

    [Fact]
    public void 全部插件不可用时默认布局仍只由Host项完整建立()
    {
        var owner = new PluginId("myavalonia.plugin.g7-unavailable");
        var pluginDocumentId = new DocumentTypeId(
            "myavalonia.plugin.g7-unavailable.document.sample");
        using var context = new TestHostContext(configureContributions: (_, builder) =>
        {
            builder.AddDocument(
                owner,
                new DocumentDescriptor(
                    pluginDocumentId,
                    "不可用插件文档",
                    "不可用插件文档",
                    "插件"),
                typeof(FailedPluginDocument),
                typeof(UserControl),
                static () => new UserControl(),
                false);
            builder.AddLifecycle(owner, typeof(FailedLifecycle));
        });

        var registry = context.Provider.GetRequiredService<PluginRegistry>();
        var availability = context.Provider.GetRequiredService<PluginAvailabilityReadModel>();
        Assert.True(registry.TryGetDocumentRegistration(pluginDocumentId, out _));
        Assert.False(availability.IsAvailable(owner));

        Assert.NotNull(context.Workspace.CreateLayout());
        Assert.Single(context.Workspace.GetDocuments());
        Assert.Equal(4, context.Workspace.CreatedTools.Count);
        Assert.Equal(
            [HostExtensionIds.WelcomeDocument],
            context.Workspace.GetAllDocumentCreationEntries()
                .Select(item => item.DocumentTypeId));
    }

    [Fact]
    public void WorkspaceCatalog合并Host与插件View但不合并Provider所有权()
    {
        var owner = new PluginId("myavalonia.plugin.g7-view");
        var pluginDocument = new PluginDocumentRegistration(
            owner,
            new DocumentDescriptor(
                new DocumentTypeId("myavalonia.plugin.g7-view.document.sample"),
                "插件视图",
                "插件视图",
                "插件"),
            typeof(PluginViewDocument),
            typeof(PluginView),
            static () => new PluginView(),
            false);
        var registry = new PluginRegistry([pluginDocument], []);
        var workspace = new WorkspaceCatalog(
            CreateQueryOnlyHostCatalog(),
            registry,
            new PluginAvailabilityReadModel(new PluginLifecycleStateStore(registry)));

        Assert.True(workspace.TryGetView(typeof(WelcomeViewModel), out var hostView));
        Assert.IsType<HostWorkspaceViewRegistration>(hostView);
        Assert.True(workspace.TryGetView(typeof(PluginViewDocument), out var pluginView));
        Assert.Equal(owner, Assert.IsType<PluginWorkspaceViewRegistration>(pluginView).OwnerId);
    }

    [Fact]
    public void PluginActivator不能激活HostId且构造函数不再接收HostProvider()
    {
        var registry = new PluginRegistry([], []);
        using var providers = new PluginProviderOwner();
        var activator = new PluginContributionActivator(registry, providers);

        Assert.Throws<NotSupportedException>(() =>
            activator.ActivateDocument(HostExtensionIds.WelcomeDocument));
        Assert.Throws<NotSupportedException>(() =>
            activator.ActivateTool(HostExtensionIds.PluginMenu));
        Assert.DoesNotContain(
            typeof(IServiceProvider),
            typeof(PluginContributionActivator).GetConstructors(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)
                .SelectMany(item => item.GetParameters())
                .Select(item => item.ParameterType));
    }

    [Fact]
    public void Host与插件目录发生Id碰撞时合并立即失败()
    {
        var owner = new PluginId("myavalonia.plugin.g7-collision");
        var registry = new PluginRegistry(
            [new PluginDocumentRegistration(
                owner,
                new DocumentDescriptor(
                    HostExtensionIds.WelcomeDocument,
                    "非法覆盖",
                    "非法覆盖",
                    "插件"),
                typeof(FailedPluginDocument),
                typeof(UserControl),
                static () => new UserControl(),
                false)],
            []);

        Assert.Throws<InvalidOperationException>(() => new WorkspaceCatalog(
            CreateQueryOnlyHostCatalog(),
            registry,
            new PluginAvailabilityReadModel(new PluginLifecycleStateStore(registry))));
    }

    [Fact]
    public void HostActivator只执行目录内精确工厂并在初始化失败时释放Scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<DocumentLifetime>();
        services.AddScoped<HostActivationDocument>();
        using var provider = services.BuildServiceProvider();
        using var scopes = new DocumentScopeManager(
            provider.GetRequiredService<IServiceScopeFactory>());
        var tool = new object();
        var initializeCount = 0;
        var documentRegistration = new HostWorkspaceDocumentRegistration(
            new DocumentDescriptor(
                HostExtensionIds.WelcomeDocument,
                "欢迎",
                "欢迎",
                "Host"),
            typeof(HostActivationDocument),
            typeof(WelcomeView),
            static () => new WelcomeView(),
            () => scopes.CreateDocument(typeof(HostActivationDocument)),
            (_, _, _) => initializeCount++);
        var catalog = new HostWorkspaceCatalog(
            [documentRegistration],
            [new HostWorkspaceToolRegistration(
                new ToolDescriptor(
                    HostExtensionIds.PluginMenu,
                    "插件",
                    "插件",
                    ToolDockSide.Right,
                    ToolCloseBehavior.Prevent),
                typeof(object),
                typeof(UserControl),
                static () => new UserControl(),
                () => tool)]);
        var activator = new HostWorkspaceActivator(catalog);

        using (var activated = activator.ActivateDocument(
                   HostExtensionIds.WelcomeDocument,
                   new NewDocumentActivation("G7 Host 激活")))
        {
            Assert.IsType<HostActivationDocument>(activated.Model);
            Assert.Same(documentRegistration, activated.Registration);
        }
        Assert.Equal(1, initializeCount);
        Assert.Same(tool, activator.ActivateTool(HostExtensionIds.PluginMenu).Model);
        Assert.Throws<NotSupportedException>(() => activator.ActivateDocument(
            new DocumentTypeId("myavalonia.host.document.unknown"),
            new NewDocumentActivation("未知 Host Document")));
        Assert.Throws<NotSupportedException>(() => activator.ActivateTool(
            new ToolTypeId("myavalonia.host.tool.unknown")));

        ManagedDocumentScopeLease? failedLease = null;
        var failingRegistration = documentRegistration with
        {
            ModelFactory = () => failedLease = scopes.CreateDocument(
                typeof(HostActivationDocument)),
            Initialize = static (_, _, _) => throw new InvalidOperationException("初始化失败")
        };
        var failing = new HostWorkspaceActivator(new HostWorkspaceCatalog(
            [failingRegistration],
            []));
        Assert.Throws<InvalidOperationException>(() => failing.ActivateDocument(
            HostExtensionIds.WelcomeDocument,
            new NewDocumentActivation("失败回滚")));
        Assert.NotNull(failedLease);
        Assert.False(scopes.Release(failedLease.Model));
    }

    private static HostWorkspaceCatalog CreateQueryOnlyHostCatalog() => new(
        [new HostWorkspaceDocumentRegistration(
            new DocumentDescriptor(
                HostExtensionIds.WelcomeDocument,
                "欢迎",
                "欢迎",
                "Host"),
            typeof(WelcomeViewModel),
            typeof(WelcomeView),
            static () => new WelcomeView(),
            static () => throw new NotSupportedException("只读目录测试不创建模型。"),
            static (_, _, _) => { })],
        []);

    private sealed class FailedPluginDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("失败");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivation activation,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class PluginViewDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("视图");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivation activation,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class PluginView : UserControl;

    private sealed class HostActivationDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("Host");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivation activation,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FailedLifecycle : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
