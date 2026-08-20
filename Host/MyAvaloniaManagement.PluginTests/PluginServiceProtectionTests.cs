using BiliDownloader.Plugin;
using DaTangAccountingHelpPlug.Plugin;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyPlugTest.Plugin;
using MySmallTools.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证 G6 服务注册事务只提交插件私有增量，并在根容器建立前拒绝宿主注册变更。
/// </summary>
public sealed class PluginServiceProtectionTests
{
    public static TheoryData<Type> MutationModules => new()
    {
        { typeof(RemoveHostServiceModule) },
        { typeof(ReplaceHostServiceModule) },
        { typeof(ClearHostServicesModule) },
        { typeof(ReorderHostServicesModule) },
        { typeof(AddProtectedServiceModule) },
        { typeof(AddKeyedProtectedServiceModule) },
    };

    [Fact]
    public void 插件私有生命周期开放泛型和多实现可原子提交()
    {
        WithComposition(
            [(typeof(AdditiveServicesModule), "myavalonia.plugin.g6-additive")],
            (services, builder, catalog, diagnostics) =>
            {
                catalog.Configure(services, builder, diagnostics);

                using var provider = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true,
                });
                Assert.NotNull(provider.GetRequiredService<PrivateSingleton>());
                Assert.NotNull(provider.GetRequiredService<PrivateTransient>());
                Assert.Equal(2, provider.GetServices<IPrivateFormatter>().Count());
                Assert.IsType<FirstPrivateFormatter>(
                    provider.GetRequiredKeyedService<IPrivateFormatter>("first"));
                using var scope = provider.CreateScope();
                Assert.NotNull(scope.ServiceProvider.GetRequiredService<PrivateScoped>());
                Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPrivateBox<string>>());
            });
    }

    [Theory]
    [MemberData(nameof(MutationModules))]
    public void 修改既有描述符或追加宿主服务在容器构建前失败(Type moduleType)
    {
        WithComposition(
            [(moduleType, "myavalonia.plugin.g6-mutation")],
            (services, builder, catalog, diagnostics) =>
            {
                var baseline = services.ToArray();

                var exception = Assert.Throws<HostCompositionException>(() =>
                    catalog.Configure(services, builder, diagnostics));

                Assert.Contains(
                    exception.Diagnostics,
                    item => item.Code == HostDiagnosticCodes.PluginHostServiceMutation);
                Assert.Equal(baseline.Length, services.Count);
                Assert.All(baseline, (descriptor, index) =>
                    Assert.Same(descriptor, services[index]));

                var diagnostic = Assert.Single(diagnostics.Snapshot, item =>
                    item.Code == HostDiagnosticCodes.PluginHostServiceMutation);
                Assert.Equal("myavalonia.plugin.g6-mutation", diagnostic.PluginId);
                Assert.Equal(HostDiagnosticPhase.PluginServiceRegistration, diagnostic.Phase);
                Assert.Equal(HostDiagnosticSeverity.Fatal, diagnostic.Severity);
                Assert.Equal(HostDiagnosticDisposition.AbortStartup, diagnostic.Disposition);
                // G15 将诊断边界前移到记录构造处。服务类型、生命周期和违规描述
                // 都属于可由插件间接影响的自由文本，不应再进入 schema 1 的兼容字段。
                Assert.Null(diagnostic.TechnicalDetail);
                Assert.Null(diagnostic.ExceptionType);
            });
    }

    [Fact]
    public void 模块抛异常时新增描述符不会进入正式集合()
    {
        WithComposition(
            [(typeof(ThrowAfterAddModule), "myavalonia.plugin.g6-throw")],
            (services, builder, catalog, diagnostics) =>
            {
                var baseline = services.ToArray();

                var exception = Assert.Throws<HostCompositionException>(() =>
                    catalog.Configure(services, builder, diagnostics));

                Assert.Contains(exception.Diagnostics, item =>
                    item.Code == HostDiagnosticCodes.PluginServiceRegistrationFailed);
                Assert.Equal(baseline.Length, services.Count);
                Assert.DoesNotContain(services, descriptor =>
                    descriptor.ServiceType == typeof(UncommittedService));
            });
    }

    [Fact]
    public void 模块返回后修改保存的工作集合不影响宿主()
    {
        CapturingModule.CapturedServices = null;
        WithComposition(
            [(typeof(CapturingModule), "myavalonia.plugin.g6-captured")],
            (services, builder, catalog, diagnostics) =>
            {
                catalog.Configure(services, builder, diagnostics);
                var committedCount = services.Count;
                var captured = Assert.IsAssignableFrom<IServiceCollection>(
                    CapturingModule.CapturedServices);

                captured.AddSingleton<LateService>();

                Assert.Equal(committedCount, services.Count);
                using var provider = services.BuildServiceProvider();
                Assert.NotNull(provider.GetRequiredService<CommittedService>());
                Assert.Null(provider.GetService<LateService>());
            });
    }

    [Fact]
    public void 前序插件提交后后序违规插件的全部增量被丢弃()
    {
        WithComposition(
            [
                (typeof(MyPlugTestPluginModule), "myavalonia.plugin.aaa-valid"),
                (typeof(AddPrivateThenProtectedModule), "myavalonia.plugin.zzz-invalid"),
            ],
            (services, builder, catalog, diagnostics) =>
            {
                Assert.Throws<HostCompositionException>(() =>
                    catalog.Configure(services, builder, diagnostics));

                Assert.Contains(services, descriptor =>
                    descriptor.ServiceType.FullName == "MyPlugTest.Services.IExcelFileDialogService");
                Assert.DoesNotContain(services, descriptor =>
                    descriptor.ServiceType == typeof(UncommittedService));
                Assert.Contains(diagnostics.Snapshot, item =>
                    item.PluginId == "myavalonia.plugin.zzz-invalid" &&
                    item.Code == HostDiagnosticCodes.PluginHostServiceMutation);
            });
    }

    [Fact]
    public void 四个真实插件通过保护链并形成可用Registry()
    {
        WithComposition(
            [
                (typeof(BiliDownloaderPluginModule), "myavalonia.plugin.bili-downloader"),
                (typeof(DaTangAccountingHelpPluginModule), "myavalonia.plugin.datang-accounting-help"),
                (typeof(MyPlugTestPluginModule), "myavalonia.plugin.my-plug-test"),
                (typeof(MySmallToolsPluginModule), "myavalonia.plugin.my-small-tools"),
            ],
            (services, builder, catalog, diagnostics) =>
            {
                catalog.Configure(services, builder, diagnostics);

                using var provider = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true,
                });
                var registry = provider.GetRequiredService<PluginRegistry>();
                Assert.Equal(4, registry.Plugins.Count);
                Assert.NotNull(provider.GetRequiredService<ManagementFactory>());
                Assert.DoesNotContain(diagnostics.Snapshot, item =>
                    item.Phase == HostDiagnosticPhase.PluginServiceRegistration);
            });
    }

    private static void WithComposition(
        IReadOnlyList<(Type ModuleType, string PluginId)> modules,
        Action<IServiceCollection, PluginRegistryBuilder, PluginModuleCatalog, HostDiagnosticSession> assertion)
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"myavalonia-g6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            using var diagnostics = HostDiagnosticSession.Start(dataDirectory);
            var services = new ServiceCollection();
            var builder = new PluginRegistryBuilder();
            services.AddApplicationServices(builder);
            services.AddViewModels();
            services.AddSingleton(diagnostics);
            services.AddSingleton<IHostDiagnosticSink>(diagnostics);

            var catalog = CreateCatalog(modules);
            // 与 HostRuntime 保持相同顺序：Catalog 必须先进入保护基线。
            services.AddSingleton(catalog);
            assertion(services, builder, catalog, diagnostics);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static PluginModuleCatalog CreateCatalog(
        IReadOnlyList<(Type ModuleType, string PluginId)> modules)
    {
        var assemblies = modules.Select(item => item.ModuleType.Assembly).ToArray();
        var manifests = modules.ToDictionary(
            item => item.ModuleType.Assembly,
            item => new PluginManifest(
                PluginManifestReader.CurrentSchemaVersion,
                new PluginId(item.PluginId),
                new Version(1, 0, 0, 0),
                item.ModuleType.Assembly.GetName().Name + ".dll",
                new PluginVersionRange(new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)),
                new PluginVersionRange(new Version(1, 0, 0, 0), new Version(2, 0, 0, 0))));
        var moduleTypes = modules.ToDictionary(
            item => item.ModuleType.Assembly,
            item => item.ModuleType);
        var types = modules.ToDictionary(
            item => item.ModuleType.Assembly,
            item => (IReadOnlyList<Type>)[item.ModuleType]);

        return PluginModuleCatalog.Discover(new PluginDiscoverySnapshot(
            assemblies,
            types,
            manifests,
            moduleTypes,
            diagnostics: []));
    }

    public sealed class AdditiveServicesModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton<PrivateSingleton>();
            context.Services.AddScoped<PrivateScoped>();
            context.Services.AddTransient<PrivateTransient>();
            context.Services.AddSingleton<IPrivateFormatter, FirstPrivateFormatter>();
            context.Services.AddSingleton<IPrivateFormatter, SecondPrivateFormatter>();
            context.Services.AddKeyedSingleton<IPrivateFormatter, FirstPrivateFormatter>("first");
            context.Services.AddScoped(typeof(IPrivateBox<>), typeof(PrivateBox<>));
        }
    }

    public sealed class RemoveHostServiceModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            var descriptor = context.Services.First(item =>
                item.ServiceType == typeof(IDocumentScopeFactory));
            context.Services.Remove(descriptor);
        }
    }

    public sealed class ReplaceHostServiceModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.Services.Replace(ServiceDescriptor.Singleton<
                IDocumentScopeFactory, HijackDocumentScopeFactory>());
    }

    public sealed class ClearHostServicesModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.Services.Clear();
    }

    public sealed class ReorderHostServicesModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            var first = context.Services[0];
            context.Services.RemoveAt(0);
            context.Services.Add(first);
        }
    }

    public sealed class AddProtectedServiceModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.Services.AddSingleton<IDocumentScopeFactory, HijackDocumentScopeFactory>();
    }

    public sealed class AddKeyedProtectedServiceModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            context.Services.AddKeyedSingleton<IDocumentScopeFactory, HijackDocumentScopeFactory>(
                "hijack");
    }

    public sealed class ThrowAfterAddModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton<UncommittedService>();
            throw new InvalidOperationException("expected test failure");
        }
    }

    public sealed class CapturingModule : IPluginModule
    {
        internal static IServiceCollection? CapturedServices { get; set; }

        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton<CommittedService>();
            CapturedServices = context.Services;
        }
    }

    public sealed class AddPrivateThenProtectedModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context)
        {
            context.Services.AddSingleton<UncommittedService>();
            context.Services.AddSingleton<IDocumentScopeFactory, HijackDocumentScopeFactory>();
        }
    }

    public sealed class HijackDocumentScopeFactory : IDocumentScopeFactory
    {
        public TDocument CreateDocument<TDocument>() where TDocument : Document =>
            throw new NotSupportedException();
    }

    public interface IPrivateFormatter;

    public sealed class FirstPrivateFormatter : IPrivateFormatter;

    public sealed class SecondPrivateFormatter : IPrivateFormatter;

    public interface IPrivateBox<T>;

    public sealed class PrivateBox<T> : IPrivateBox<T>;

    public sealed class PrivateSingleton;

    public sealed class PrivateScoped;

    public sealed class PrivateTransient;

    public sealed class CommittedService;

    public sealed class LateService;

    public sealed class UncommittedService;
}
