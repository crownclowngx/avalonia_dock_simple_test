using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Plugin;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.Plugin;
using MyPlugTest.Plugin;
using MySmallTools.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证 G4 的核心所有权边界：宿主、插件和 Document 分别由不同容器拥有，失败与释放不会越界。
/// </summary>
public sealed class PluginContainerIsolationTests
{
    [Fact]
    public void 插件配置只能修改自己的服务集合且宿主描述符保持逐项不变()
    {
        using var composition = Compose(
            ("myavalonia.plugin.g4-mutation", new ClearAndReplaceOwnServicesModule()));

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
        var first = composition.Registry.CreateDocument(new DocumentCreationParams(
            new DocumentTypeId("myavalonia.plugin.g4-doc-a.document.sample")));
        var second = composition.Registry.CreateDocument(new DocumentCreationParams(
            new DocumentTypeId("myavalonia.plugin.g4-doc-b.document.sample")));
        var firstDocument = Assert.IsType<ScopedPluginDocument>(first);
        var secondDocument = Assert.IsType<ScopedPluginDocument>(second);

        Assert.Equal("myavalonia.plugin.g4-doc-a", firstDocument.Marker.PluginId);
        Assert.Equal("myavalonia.plugin.g4-doc-b", secondDocument.Marker.PluginId);
        Assert.True(composition.DocumentScopes.Release(firstDocument));
        Assert.True(firstDocument.IsDisposed);
        Assert.False(secondDocument.IsDisposed);
        Assert.True(composition.DocumentScopes.Release(secondDocument));
        Assert.True(secondDocument.IsDisposed);
    }

    [Fact]
    public void 四个真实插件分别构建私有Provider并形成可用Registry()
    {
        using var composition = Compose(
            ("myavalonia.plugin.bili-downloader", new BiliDownloaderPluginModule()),
            ("myavalonia.plugin.datang-accounting-help", new DaTangAccountingHelpPluginModule()),
            ("myavalonia.plugin.my-plug-test", new MyPlugTestPluginModule()),
            ("myavalonia.plugin.my-small-tools", new MySmallToolsPluginModule()));

        Assert.Equal(4, composition.PluginProviders.AvailablePluginIds.Count);
        Assert.Equal(4, composition.Registry.Plugins.Count);
        Assert.DoesNotContain(composition.Diagnostics.Snapshot, item =>
            item.Phase == HostDiagnosticPhase.PluginServiceRegistration);
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
        public void Configure(IPluginRegistrationContext context)
        {
            var hostPort = context.Services.Single(descriptor =>
                descriptor.ServiceType == typeof(IHostEventBus));
            context.Services.Remove(hostPort);
            context.Services.AddSingleton<IHostEventBus, PrivateEventBus>();
            context.Services.AddSingleton<PrivateSingleton>();
        }
    }

    public sealed class FirstPrivateModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.Services.AddSingleton<FirstPrivateService>();
    }

    public sealed class SecondPrivateModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.Services.AddSingleton<SecondPrivateService>();
    }

    public sealed class NativeDiFeaturesModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton<IPrivateFormatter, FirstFormatter>();
            context.Services.AddSingleton<IPrivateFormatter, SecondFormatter>();
            context.Services.AddKeyedSingleton<IPrivateFormatter, FirstFormatter>("first");
            context.Services.AddSingleton(typeof(IPrivateBox<>), typeof(PrivateBox<>));
            context.Services.AddTransient<NativeDiProbe>();
        }
    }

    public sealed class ThrowingConfigureModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            throw new ApplicationException("测试异常正文不得进入持久诊断。");
    }

    public sealed class ThrowingConstructorModule : IPluginModule
    {
        public ThrowingConstructorModule() =>
            throw new ApplicationException("模块构造测试异常正文。");

        public void Configure(IPluginRegistrationContext context) { }
    }

    public sealed class BrokenProviderModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.AddLifecycle<BrokenLifecycle>();
    }

    public sealed class ValidLifecycleModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.AddLifecycle<ValidLifecycle>();
    }

    public sealed class DisposableLifecycleModuleA(List<string> releaseOrder) : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton(releaseOrder);
            context.AddLifecycle<DisposableLifecycleA>();
        }
    }

    public sealed class DisposableLifecycleModuleB(List<string> releaseOrder) : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton(releaseOrder);
            context.AddLifecycle<DisposableLifecycleB>();
        }
    }

    public sealed class ScopedDocumentModuleA : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton(new PluginMarker(context.PluginId.Value));
            context.Services.AddScoped<ScopedPluginDocument>();
            context.AddDocument<ScopedPluginDocumentStrategyA>();
        }
    }

    public sealed class ScopedDocumentModuleB : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton(new PluginMarker(context.PluginId.Value));
            context.Services.AddScoped<ScopedPluginDocument>();
            context.AddDocument<ScopedPluginDocumentStrategyB>();
        }
    }

    public interface IPrivateFormatter;
    public sealed class FirstFormatter : IPrivateFormatter;
    public sealed class SecondFormatter : IPrivateFormatter;
    public interface IPrivateBox<T>;
    public sealed class PrivateBox<T> : IPrivateBox<T>;
    public sealed class PrivateSingleton;
    public sealed class FirstPrivateService;
    public sealed class SecondPrivateService;

    public sealed class PrivateEventBus : IHostEventBus
    {
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class =>
            new EmptySubscription();

        public void Publish<TEvent>(TEvent message) where TEvent : class { }

        private sealed class EmptySubscription : IDisposable
        {
            public void Dispose() { }
        }
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
        public int Order => 0;
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _ = _missing;
            return Task.CompletedTask;
        }
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ValidLifecycle : IPluginLifecycle
    {
        public int Order => 0;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class DisposableLifecycleA(List<string> releaseOrder) : IPluginLifecycle, IDisposable
    {
        public int Order => 0;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => releaseOrder.Add("a");
    }

    public sealed class DisposableLifecycleB(List<string> releaseOrder) : IPluginLifecycle, IDisposable
    {
        public int Order => 0;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => releaseOrder.Add("b");
    }

    public sealed record PluginMarker(string PluginId);

    private sealed class HostReleaseProbe(List<string> releaseOrder) : IDisposable
    {
        public void Dispose() => releaseOrder.Add("host");
    }

    public sealed class ScopedPluginDocument(PluginMarker marker) : Document, IDisposable
    {
        internal PluginMarker Marker { get; } = marker;
        internal bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    public sealed class ScopedPluginDocumentStrategyA(
        PluginMarker marker,
        IDocumentScopeFactory scopeFactory) : IDocumentCreationStrategy
    {
        public Document CreateDocument(DocumentCreationParams @params) =>
            scopeFactory.CreateDocument<ScopedPluginDocument>();

        public DocumentMetadata GetMetadata() => new(
            new DocumentTypeId($"{marker.PluginId}.document.sample"),
            "G4 Scope 测试 Document");
    }

    public sealed class ScopedPluginDocumentStrategyB(
        PluginMarker marker,
        IDocumentScopeFactory scopeFactory) : IDocumentCreationStrategy
    {
        public Document CreateDocument(DocumentCreationParams @params) =>
            scopeFactory.CreateDocument<ScopedPluginDocument>();

        public DocumentMetadata GetMetadata() => new(
            new DocumentTypeId($"{marker.PluginId}.document.sample"),
            "G4 Scope 测试 Document");
    }
}
