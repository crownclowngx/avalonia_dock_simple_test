using DaTangAccountingHelpPlug.Plugin;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Avalonia.Controls;
using MyPlugTest.Plugin;
using MyPlugTest.Messaging;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证 G4 的核心所有权边界：宿主、插件和 Document 分别由不同容器拥有，失败与释放不会越界。
/// </summary>
public sealed class PluginContainerIsolationTests
{
    private sealed class MessageProbeBus { }
    private sealed class MessageProbeModule : IPluginModule
    {
        public void Configure(IPluginRegistration registration) => registration.Services.AddSingleton<MessageProbeBus>();
    }

    [Fact]
    public void Host与插件Provider只解析各自拥有的消息器()
    {
        var myPlugId = new PluginId("myavalonia.plugin.my-plug-test");
        var probeId = new PluginId("myavalonia.plugin.message-probe");
        using var composition = Compose(
            (myPlugId.Value, new MyPlugTestPluginModule()),
            (probeId.Value, new MessageProbeModule()));

        var myPlugBus = composition.PluginProviders.GetRequiredService(
            myPlugId,
            typeof(IMyPlugTestEventBus));
        var probeBus = composition.PluginProviders.GetRequiredService(
            probeId,
            typeof(MessageProbeBus));

        Assert.IsAssignableFrom<IMyPlugTestEventBus>(myPlugBus);
        Assert.IsAssignableFrom<MessageProbeBus>(probeBus);
        Assert.Null(composition.HostProvider.GetService<IMyPlugTestEventBus>());
        Assert.Null(composition.HostProvider.GetService<MessageProbeBus>());
        Assert.Throws<InvalidOperationException>(() =>
            composition.PluginProviders.GetRequiredService(
                myPlugId,
                typeof(MessageProbeBus)));
        Assert.Throws<InvalidOperationException>(() =>
            composition.PluginProviders.GetRequiredService(
                probeId,
                typeof(IMyPlugTestEventBus)));
    }

    [Fact]
    public void 插件从空集合开始且清理私有描述符不影响宿主对象图()
    {
        ClearAndReplaceOwnServicesModule.InitialServiceCount = -1;
        using var composition = Compose(
            ("myavalonia.plugin.g4-mutation", new ClearAndReplaceOwnServicesModule()));

        Assert.Equal(0, ClearAndReplaceOwnServicesModule.InitialServiceCount);
        Assert.Equal(composition.HostBaseline.Length, composition.HostServices.Count);
        Assert.All(composition.HostBaseline, (descriptor, index) =>
            Assert.Same(descriptor, composition.HostServices[index]));
        Assert.Null(composition.HostProvider.GetService<PrivateSingleton>());
        Assert.NotNull(composition.PluginProviders.GetRequiredService(
            new PluginId("myavalonia.plugin.g4-mutation"),
            typeof(PrivateSingleton)));
    }

    [Fact]
    public void 插件私有服务不能由宿主或另一个插件解析()
    {
        using var composition = Compose(
            ("myavalonia.plugin.g4-first", new FirstPrivateModule()),
            ("myavalonia.plugin.g4-second", new SecondPrivateModule()));

        var firstId = new PluginId("myavalonia.plugin.g4-first");
        var secondId = new PluginId("myavalonia.plugin.g4-second");
        Assert.IsType<FirstPrivateService>(composition.PluginProviders.GetRequiredService(
            firstId, typeof(FirstPrivateService)));
        Assert.IsType<SecondPrivateService>(composition.PluginProviders.GetRequiredService(
            secondId, typeof(SecondPrivateService)));
        Assert.Null(composition.HostProvider.GetService<FirstPrivateService>());
        Assert.Throws<InvalidOperationException>(() =>
            composition.PluginProviders.GetRequiredService(secondId, typeof(FirstPrivateService)));
    }

    [Fact]
    public void 开放泛型Keyed服务和多实现保留MicrosoftDI原生语义()
    {
        using var composition = Compose(
            ("myavalonia.plugin.g4-di", new NativeDiFeaturesModule()));
        var pluginId = new PluginId("myavalonia.plugin.g4-di");
        var probe = Assert.IsType<NativeDiProbe>(composition.PluginProviders.GetRequiredService(
            pluginId, typeof(NativeDiProbe)));

        Assert.Equal(2, probe.Formatters.Count);
        Assert.IsType<FirstFormatter>(probe.KeyedFormatter);
        Assert.IsType<PrivateBox<string>>(probe.Box);
    }

    [Fact]
    public void 模块配置和Provider构建失败分别隔离且成功插件仍发布()
    {
        using var composition = Compose(
            ("myavalonia.plugin.g4-config-failure", new ThrowingConfigureModule()),
            ("myavalonia.plugin.g4-provider-failure", new BrokenProviderModule()),
            ("myavalonia.plugin.g4-valid", new ValidLifecycleModule()));

        Assert.Equal(
            [new PluginId("myavalonia.plugin.g4-valid")],
            composition.PluginProviders.AvailablePluginIds);
        Assert.Equal(1, composition.DocumentScopes.ManagerCount);
        Assert.Single(composition.Registry.Lifecycles);
        Assert.Contains(composition.Diagnostics.Snapshot, item =>
            item.PluginId == "myavalonia.plugin.g4-config-failure" &&
            item.Code == HostDiagnosticCodes.PluginServiceRegistrationFailed &&
            item.Disposition == HostDiagnosticDisposition.Continue);
        Assert.Contains(composition.Diagnostics.Snapshot, item =>
            item.PluginId == "myavalonia.plugin.g4-provider-failure" &&
            item.Code == HostDiagnosticCodes.PluginContainerBuildFailed &&
            item.Disposition == HostDiagnosticDisposition.Continue);
    }

    [Fact]
    public void 保留端口和贡献根违规记录专用脱敏诊断且不构建Provider()
    {
        using var composition = Compose(
            ("myavalonia.plugin.g4-forbidden-port", new ForbiddenHostPortModule()),
            ("myavalonia.plugin.g4-forbidden-root", new ForbiddenContributionRootModule()),
            ("myavalonia.plugin.g4-forbidden-valid", new ValidLifecycleModule()));

        Assert.Equal(
            [new PluginId("myavalonia.plugin.g4-forbidden-valid")],
            composition.PluginProviders.AvailablePluginIds);
        Assert.Equal(1, composition.DocumentScopes.ManagerCount);
        Assert.Equal(2, composition.Diagnostics.Snapshot.Count(item =>
            item.Code == HostDiagnosticCodes.PluginHostServiceRegistrationForbidden));
        var rootDiagnostic = Assert.Single(composition.Diagnostics.Snapshot, item =>
            item.Code == HostDiagnosticCodes.PluginContributionServiceRegistrationForbidden);
        Assert.Null(rootDiagnostic.StableId);
        Assert.Null(rootDiagnostic.TechnicalDetail);
        Assert.DoesNotContain(composition.Diagnostics.Snapshot, item =>
            (item.PluginId is "myavalonia.plugin.g4-forbidden-port" or
                "myavalonia.plugin.g4-forbidden-root") &&
            item.Code == HostDiagnosticCodes.PluginContainerBuildFailed);
    }

    [Fact]
    public void 模块公共构造失败发生在HostProvider建立后且只隔离自身()
    {
        using var composition = ComposeTypes(
            ("myavalonia.plugin.g4-constructor-failure", typeof(ThrowingConstructorModule)),
            ("myavalonia.plugin.g4-constructor-valid", typeof(ValidLifecycleModule)));

        Assert.Equal(
            [new PluginId("myavalonia.plugin.g4-constructor-valid")],
            composition.PluginProviders.AvailablePluginIds);
        Assert.Contains(composition.Diagnostics.Snapshot, item =>
            item.PluginId == "myavalonia.plugin.g4-constructor-failure" &&
            item.Code == HostDiagnosticCodes.PluginModuleActivationFailed &&
            item.Disposition == HostDiagnosticDisposition.Continue);
    }

    [Fact]
    public void 插件Provider按规范PluginId建立并在宿主之前逆序释放()
    {
        var releaseOrder = new List<string>();
        var composition = ComposeWithHostRelease(releaseOrder,
            ("myavalonia.plugin.g4-a", new DisposableLifecycleModuleA(releaseOrder)),
            ("myavalonia.plugin.g4-b", new DisposableLifecycleModuleB(releaseOrder)));

        composition.Dispose();

        Assert.Equal(["b", "a", "host"], releaseOrder);
    }

    [Fact]
    public void 每插件DocumentScope使用本插件服务并可独立关闭()
    {
        using var composition = Compose(
            ("myavalonia.plugin.g4-doc-a", new ScopedDocumentModuleA()),
            ("myavalonia.plugin.g4-doc-b", new ScopedDocumentModuleB()));
        Assert.Equal(2, composition.DocumentScopes.ManagerCount);
        var activator = composition.HostProvider.GetRequiredService<PluginContributionActivator>();
        using var firstActivation = activator.ActivateDocument(
            new DocumentTypeId("myavalonia.plugin.g4-doc-a.document.sample"));
        using var secondActivation = activator.ActivateDocument(
            new DocumentTypeId("myavalonia.plugin.g4-doc-b.document.sample"));
        var firstDocument = Assert.IsType<ScopedPluginDocument>(firstActivation.Model);
        var secondDocument = Assert.IsType<ScopedPluginDocumentB>(secondActivation.Model);

        Assert.Equal("myavalonia.plugin.g4-doc-a", firstDocument.Marker.PluginId);
        Assert.Equal("myavalonia.plugin.g4-doc-b", secondDocument.Marker.PluginId);
        Assert.True(composition.DocumentScopes.Release(firstDocument));
        Assert.True(firstDocument.IsDisposed);
        Assert.False(secondDocument.IsDisposed);
        Assert.True(composition.DocumentScopes.Release(secondDocument));
        Assert.True(secondDocument.IsDisposed);
    }

    [Fact]
    public void 四个最终Sdk测试插件分别构建私有Provider并形成可用Registry()
    {
        using var composition = Compose(
            ("myavalonia.plugin.g4-one", new ValidLifecycleModule()),
            ("myavalonia.plugin.g4-two", new ValidLifecycleModule()),
            ("myavalonia.plugin.g4-three", new ValidLifecycleModule()),
            ("myavalonia.plugin.g4-four", new ValidLifecycleModule()));

        Assert.Equal(4, composition.PluginProviders.AvailablePluginIds.Count);
        Assert.Equal(4, composition.Registry.Plugins.Count);
        Assert.DoesNotContain(composition.Diagnostics.Snapshot, item =>
            item.Phase == HostDiagnosticPhase.PluginServiceRegistration);
    }

    [Fact]
    public void 越权Document与Tool在局部Seal隔离且无冲突插件继续发布()
    {
        var released = new List<string>();
        using var composition = Compose(
            ("myavalonia.plugin.g5-doc-a", new DocumentConflictModuleA(released)),
            ("myavalonia.plugin.g5-doc-b", new DocumentConflictModuleB(released)),
            ("myavalonia.plugin.g5-tool-a", new ToolConflictModuleA(released)),
            ("myavalonia.plugin.g5-tool-b", new ToolConflictModuleB(released)),
            ("myavalonia.plugin.g5-valid", new ValidLifecycleModule()));

        Assert.Equal(
            [new PluginId("myavalonia.plugin.g5-valid")],
            composition.PluginProviders.AvailablePluginIds);
        Assert.False(composition.Registry.DocumentDescriptors.ContainsKey(
            new DocumentTypeId("shared.document.collision")));
        Assert.False(composition.Registry.ToolDescriptors.ContainsKey(
            new ToolTypeId("shared.tool.collision")));
        // 生命周期根尚未由 Host 提交，违规插件不会构造 Provider，也就没有需要事后释放的实例。
        Assert.Empty(released);
        Assert.Equal(1, composition.DocumentScopes.ManagerCount);
        Assert.Equal(2, composition.Diagnostics.Snapshot.Count(item =>
            item.Code == HostDiagnosticCodes.DocumentIdOwnerMismatch));
        Assert.Equal(2, composition.Diagnostics.Snapshot.Count(item =>
            item.Code == HostDiagnosticCodes.ToolIdOwnerMismatch));
    }

    [Fact]
    public void 插件声明Host命名空间时在Provider构建前隔离且Registry只保留真实插件()
    {
        var released = new List<string>();
        using var composition = Compose(
            ("myavalonia.plugin.g5-host-conflict", new HostConflictModule(released)),
            ("myavalonia.plugin.g5-host-valid", new ValidLifecycleModule()));

        Assert.False(composition.Registry.DocumentDescriptors.ContainsKey(
            HostExtensionIds.WelcomeDocument));
        Assert.Single(composition.Registry.Plugins);
        Assert.DoesNotContain(
            new PluginId("myavalonia.plugin.g5-host-conflict"),
            composition.PluginProviders.AvailablePluginIds);
        Assert.Empty(released);
        Assert.Equal(1, composition.DocumentScopes.ManagerCount);
        var diagnostic = Assert.Single(composition.Diagnostics.Snapshot, item =>
            item.Code == HostDiagnosticCodes.DocumentIdOwnerMismatch);
        Assert.Equal(
            HostExtensionIds.WelcomeDocument.Value,
            diagnostic.StableId);
        Assert.Null(diagnostic.TechnicalDetail);
    }

    private static Composition Compose(
        params (string PluginId, IPluginModule Module)[] modules)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"myavalonia-g4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var catalog = PluginModuleCatalog.CreateForTests(modules.Select(item =>
            (new PluginId(item.PluginId), item.Module)));
        return ComposeCore(directory, catalog, hostReleaseOrder: null);
    }

    private static Composition ComposeTypes(
        params (string PluginId, Type ModuleType)[] modules)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"myavalonia-g4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var catalog = PluginModuleCatalog.CreateForTests(modules.Select(item =>
            (new PluginId(item.PluginId), item.ModuleType)));
        return ComposeCore(directory, catalog, hostReleaseOrder: null);
    }

    private static Composition ComposeWithHostRelease(
        List<string> releaseOrder,
        params (string PluginId, IPluginModule Module)[] modules)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"myavalonia-g4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var catalog = PluginModuleCatalog.CreateForTests(modules.Select(item =>
            (new PluginId(item.PluginId), item.Module)));
        return ComposeCore(directory, catalog, releaseOrder);
    }

    private static Composition ComposeCore(
        string directory,
        PluginModuleCatalog catalog,
        List<string>? hostReleaseOrder)
    {
        var diagnostics = HostDiagnosticSession.Start(directory);
        var hostServices = new ServiceCollection();
        var registryBuilder = new PluginRegistryBuilder();
        var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        hostServices.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        hostServices.AddViewModels();
        hostServices.AddSingleton(diagnostics);
        hostServices.AddSingleton<IHostDiagnosticSink>(diagnostics);
        if (hostReleaseOrder is not null)
        {
            hostServices.AddSingleton(_ => new HostReleaseProbe(hostReleaseOrder));
        }
        hostServices.AddSingleton(catalog);
        var baseline = hostServices.ToArray();
        var hostProvider = hostServices.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        hostProvider.GetService<HostReleaseProbe>();
        pluginProviders.Compose(
            catalog,
            hostProvider,
            registryBuilder,
            documentScopes,
            diagnostics);
        var registry = hostProvider.GetRequiredService<PluginRegistry>();
        return new Composition(
            directory,
            diagnostics,
            hostServices,
            baseline,
            hostProvider,
            pluginProviders,
            documentScopes,
            registry);
    }

    private sealed class Composition(
        string directory,
        HostDiagnosticSession diagnostics,
        IServiceCollection hostServices,
        ServiceDescriptor[] hostBaseline,
        ServiceProvider hostProvider,
        PluginProviderOwner pluginProviders,
        DocumentScopeRegistry documentScopes,
        PluginRegistry registry) : IDisposable
    {
        private bool _disposed;

        internal HostDiagnosticSession Diagnostics { get; } = diagnostics;
        internal IServiceCollection HostServices { get; } = hostServices;
        internal ServiceDescriptor[] HostBaseline { get; } = hostBaseline;
        internal ServiceProvider HostProvider { get; } = hostProvider;
        internal PluginProviderOwner PluginProviders { get; } = pluginProviders;
        internal DocumentScopeRegistry DocumentScopes { get; } = documentScopes;
        internal PluginRegistry Registry { get; } = registry;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DocumentScopes.CloseAll();
            PluginProviders.Dispose();
            HostProvider.Dispose();
            Diagnostics.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    public sealed class ClearAndReplaceOwnServicesModule : IPluginModule
    {
        internal static int InitialServiceCount { get; set; }

        public void Configure(IPluginRegistration context)
        {
            InitialServiceCount = context.Services.Count;
            context.Services.AddSingleton<FirstPrivateService>();
            context.Services.Clear();
            context.Services.AddSingleton<PrivateSingleton>();
        }
    }

    public sealed class FirstPrivateModule : IPluginModule
    {
        public void Configure(IPluginRegistration context) =>
            context.Services.AddSingleton<FirstPrivateService>();
    }

    public sealed class SecondPrivateModule : IPluginModule
    {
        public void Configure(IPluginRegistration context) =>
            context.Services.AddSingleton<SecondPrivateService>();
    }

    public sealed class NativeDiFeaturesModule : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            context.Services.AddSingleton<IPrivateFormatter, FirstFormatter>();
            context.Services.AddSingleton<IPrivateFormatter, SecondFormatter>();
            context.Services.AddKeyedSingleton<IPrivateFormatter, FirstFormatter>("first");
            context.Services.AddSingleton(typeof(IPrivateBox<>), typeof(PrivateBox<>));
            context.Services.AddTransient<NativeDiProbe>();
        }
    }

    public sealed class ForbiddenHostPortModule : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            context.Services.AddSingleton<IDocumentLifetime, PrivateDocumentLifetime>();
            context.Services.AddKeyedSingleton<IDocumentLifetime, PrivateDocumentLifetime>("shadow");
        }
    }

    public sealed class ForbiddenContributionRootModule : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            context.Services.AddTransient<ForbiddenRootDocument>();
            context.AddDocument<ForbiddenRootDocument, EmptyView>(
                Document(context.PluginId));
        }
    }

    public sealed class ThrowingConfigureModule : IPluginModule
    {
        public void Configure(IPluginRegistration context) =>
            throw new ApplicationException("测试异常正文不得进入持久诊断。");
    }

    public sealed class ThrowingConstructorModule : IPluginModule
    {
        public ThrowingConstructorModule() =>
            throw new ApplicationException("模块构造测试异常正文。");

        public void Configure(IPluginRegistration context) { }
    }

    public sealed class BrokenProviderModule : IPluginModule
    {
        public void Configure(IPluginRegistration context) =>
            context.UseLifecycle<BrokenLifecycle>();
    }

    public sealed class ValidLifecycleModule : IPluginModule
    {
        public void Configure(IPluginRegistration context) =>
            context.UseLifecycle<ValidLifecycle>();
    }

    public sealed class DisposableLifecycleModuleA(List<string> releaseOrder) : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            context.Services.AddSingleton(releaseOrder);
            context.UseLifecycle<DisposableLifecycleA>();
        }
    }

    public sealed class DisposableLifecycleModuleB(List<string> releaseOrder) : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            context.Services.AddSingleton(releaseOrder);
            context.UseLifecycle<DisposableLifecycleB>();
        }
    }

    public sealed class ScopedDocumentModuleA : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            context.Services.AddSingleton(new PluginMarker(context.PluginId.Value));
            context.AddDocument<ScopedPluginDocument, EmptyView>(
                Document(context.PluginId));
        }
    }

    public sealed class ScopedDocumentModuleB : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            context.Services.AddSingleton(new PluginMarker(context.PluginId.Value));
            context.AddDocument<ScopedPluginDocumentB, EmptyView>(
                Document(context.PluginId));
        }
    }

    public sealed class DocumentConflictModuleA(List<string> released) : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            AddRejectedLease(context, released);
            context.AddDocument<ConflictDocumentA, ConflictViewA>(ConflictDocument());
        }
    }

    public sealed class DocumentConflictModuleB(List<string> released) : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            AddRejectedLease(context, released);
            context.AddDocument<ConflictDocumentB, ConflictViewB>(ConflictDocument());
        }
    }

    public sealed class ToolConflictModuleA(List<string> released) : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            AddRejectedLease(context, released);
            context.AddTool<ConflictToolA, ConflictViewA>(ConflictTool());
        }
    }

    public sealed class ToolConflictModuleB(List<string> released) : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            AddRejectedLease(context, released);
            context.AddTool<ConflictToolB, ConflictViewB>(ConflictTool());
        }
    }

    public sealed class HostConflictModule(List<string> released) : IPluginModule
    {
        public void Configure(IPluginRegistration context)
        {
            AddRejectedLease(context, released);
            context.AddDocument<HostConflictDocument, ConflictViewA>(new DocumentDescriptor(
                HostExtensionIds.WelcomeDocument,
                "伪造 Welcome",
                "必须被 Host 内建贡献压制",
                "测试"));
        }
    }

    private static void AddRejectedLease(
        IPluginRegistration context,
        List<string> released)
    {
        context.Services.AddSingleton(new PluginMarker(context.PluginId.Value));
        context.Services.AddSingleton(released);
        context.UseLifecycle<RejectedLeaseLifecycle>();
    }

    public interface IPrivateFormatter;
    public sealed class FirstFormatter : IPrivateFormatter;
    public sealed class SecondFormatter : IPrivateFormatter;
    public interface IPrivateBox<T>;
    public sealed class PrivateBox<T> : IPrivateBox<T>;
    public sealed class PrivateSingleton;
    public sealed class FirstPrivateService;
    public sealed class SecondPrivateService;

    public sealed class PrivateDocumentLifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }

    public sealed class NativeDiProbe(
        IEnumerable<IPrivateFormatter> formatters,
        [FromKeyedServices("first")] IPrivateFormatter keyedFormatter,
        IPrivateBox<string> box)
    {
        internal IReadOnlyList<IPrivateFormatter> Formatters { get; } = formatters.ToArray();
        internal IPrivateFormatter KeyedFormatter { get; } = keyedFormatter;
        internal IPrivateBox<string> Box { get; } = box;
    }

    public interface IMissingDependency;

    public sealed class BrokenLifecycle(IMissingDependency missing) : IPluginLifecycle
    {
        private readonly IMissingDependency _missing = missing;
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _ = _missing;
            return Task.CompletedTask;
        }
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ValidLifecycle : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class RejectedLeaseLifecycle(
        PluginMarker marker,
        List<string> released) : IPluginLifecycle, IDisposable
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => released.Add(marker.PluginId);
    }

    public sealed class DisposableLifecycleA(List<string> releaseOrder) : IPluginLifecycle, IDisposable
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => releaseOrder.Add("a");
    }

    public sealed class DisposableLifecycleB(List<string> releaseOrder) : IPluginLifecycle, IDisposable
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => releaseOrder.Add("b");
    }

    public sealed record PluginMarker(string PluginId);

    private sealed class HostReleaseProbe(List<string> releaseOrder) : IDisposable
    {
        public void Dispose() => releaseOrder.Add("host");
    }

    public sealed class ScopedPluginDocument(PluginMarker marker) :
        Document,
        MyAvaloniaManagement.PluginSdk.IPluginDocument,
        IDisposable
    {
        internal PluginMarker Marker { get; } = marker;
        internal bool IsDisposed { get; private set; }
        public DocumentPresentationState Presentation { get; } = new("Scope 测试");
        public event EventHandler? PresentationChanged
        {
            add { }
            remove { }
        }
        public ValueTask InitializeAsync(
            DocumentActivation context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Dispose() => IsDisposed = true;
    }

    /// <summary>第二插件使用独立的精确模型类型，以验证 Scope 而不触发 G5 全局模型冲突。</summary>
    public sealed class ScopedPluginDocumentB(PluginMarker marker) :
        Document,
        MyAvaloniaManagement.PluginSdk.IPluginDocument,
        IDisposable
    {
        internal PluginMarker Marker { get; } = marker;
        internal bool IsDisposed { get; private set; }
        public DocumentPresentationState Presentation { get; } = new("Scope 测试 B");
        public event EventHandler? PresentationChanged
        {
            add { }
            remove { }
        }
        public ValueTask InitializeAsync(
            DocumentActivation context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Dispose() => IsDisposed = true;
    }

    public sealed class ConflictDocumentA : TestPluginDocument;
    public sealed class ConflictDocumentB : TestPluginDocument;
    public sealed class HostConflictDocument : TestPluginDocument;
    public sealed class ForbiddenRootDocument : TestPluginDocument;

    public class TestPluginDocument : MyAvaloniaManagement.PluginSdk.IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("冲突测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivation context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    public sealed class ConflictToolA;
    public sealed class ConflictToolB;
    public sealed class ConflictViewA : UserControl;
    public sealed class ConflictViewB : UserControl;

    private static DocumentDescriptor ConflictDocument() => new(
        new DocumentTypeId("shared.document.collision"),
        "冲突 Document",
        "验证跨插件 ID 冲突",
        "测试");

    private static ToolDescriptor ConflictTool() => new(
        new ToolTypeId("shared.tool.collision"),
        "冲突 Tool",
        "验证跨插件 ID 冲突",
        ToolDockSide.Left,
        ToolCloseBehavior.Hide);

    private static DocumentDescriptor Document(PluginId ownerId) => new(
        new DocumentTypeId($"{ownerId.Value}.document.sample"),
        "G4 Scope 测试 Document",
        "验证每插件 Document Scope",
        "测试");

    public sealed class EmptyView : UserControl;
}
