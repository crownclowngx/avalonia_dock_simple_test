using System.Reflection;
using System.Runtime.Loader;
using DaTangAccountingHelpPlug.Create;
using DaTangAccountingHelpPlug.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证 G4 后插件只能沿严格清单、deps、唯一模块和 DI 激活这一条路径进入宿主。
/// </summary>
/// <remarks>
/// 设计意图：这些测试同时覆盖“允许什么”和“必须拒绝什么”。拒绝场景使用真实目录快照，
/// 防止未来只修改 Catalog 单元测试，却在物理加载入口重新引入 Legacy 回退。
/// </remarks>
public sealed class ManagedOnlyPluginLoadingTests
{
    [Fact]
    public void 有效deps与唯一模块形成可配置Catalog()
    {
        var snapshot = AssemblyLoaderHelper.Discover("PluginIsolationFixtures");

        Assert.Equal(2, snapshot.Assemblies.Count);
        Assert.DoesNotContain(snapshot.Diagnostics, item =>
            item.Code is HostDiagnosticCodes.PluginDependencyManifestMissing
                or HostDiagnosticCodes.PluginModuleMissing
                or HostDiagnosticCodes.PluginModuleMultiple
                or HostDiagnosticCodes.PluginModuleConstructorInvalid);

        var catalog = PluginModuleCatalog.Discover(snapshot);
        Assert.Equal(
            ["myavalonia.plugin.isolation-v1", "myavalonia.plugin.isolation-v2"],
            catalog.Entries.Select(entry => entry.Manifest!.PluginId.Value));
    }

    [Fact]
    public void 缺少清单时不加载入口程序集()
    {
        WithCopiedPluginFixture(
            removeFileName: PluginManifestReader.FileName,
            assertion: snapshot =>
            {
                Assert.Empty(snapshot.Assemblies);
                Assert.Contains(snapshot.Diagnostics, item =>
                    item.Code == HostDiagnosticCodes.PluginManifestMissing);
            });
    }

    [Fact]
    public void 缺少deps时隔离当前目录且不加载入口程序集()
    {
        var snapshot = AssemblyLoaderHelper.Discover("ManagedOnlyMissingDepsFixtures");

        var loaded = Assert.Single(snapshot.Assemblies);
        Assert.Equal("PluginIsolation.PluginV2", loaded.GetName().Name);
        Assert.Contains(snapshot.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.PluginDependencyManifestMissing &&
            item.PluginDirectory == "PluginV1");
    }

    [Fact]
    public void 只有无参策略但没有模块的旧式程序集在激活前被隔离()
    {
        var snapshot = AssemblyLoaderHelper.Discover("ManagedOnlyFixtures");

        Assert.Empty(snapshot.Assemblies);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(HostDiagnosticCodes.PluginModuleMissing, diagnostic.Code);
        Assert.Equal("LegacyNoModule", diagnostic.PluginDirectory);
        Assert.DoesNotContain(
            typeof(PluginRegistry).Assembly.GetTypes(),
            type => type.Name == "PluginStrategyActivator");
    }

    [Fact]
    public void 多模块返回稳定结构诊断()
    {
        var accepted = PluginModulePreflight.TryValidate(
            [typeof(FirstModule), typeof(SecondModule)],
            out var moduleType,
            out var errorCode,
            out _);

        Assert.False(accepted);
        Assert.Null(moduleType);
        Assert.Equal(HostDiagnosticCodes.PluginModuleMultiple, errorCode);
    }

    [Fact]
    public void 模块缺少public无参构造时返回稳定结构诊断()
    {
        var accepted = PluginModulePreflight.TryValidate(
            [typeof(ConstructorDependency), typeof(ModuleWithoutPublicParameterlessConstructor)],
            out var moduleType,
            out var errorCode,
            out _);

        Assert.False(accepted);
        Assert.Null(moduleType);
        Assert.Equal(HostDiagnosticCodes.PluginModuleConstructorInvalid, errorCode);
    }

    [Fact]
    public void Managed策略只提供DI构造仍可创建()
    {
        var services = new ServiceCollection();
        services.AddDocumentScopeManagement();
        new DaTangAccountingHelpPluginModule().Configure(new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.datang-accounting-help"), services));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        Assert.Null(typeof(InvoiceInfoImportDocumentStrategy).GetConstructor(Type.EmptyTypes));
        var strategy = ActivatorUtilities.CreateInstance<InvoiceInfoImportDocumentStrategy>(provider);
        var document = strategy.CreateDocument(
            new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId));

        Assert.NotNull(document);
        Assert.True(provider.GetRequiredService<DocumentScopeManager>().Release(document));
    }

    [Fact]
    public void deps声明的同名不同版本私有依赖仍按插件隔离()
    {
        var snapshot = AssemblyLoaderHelper.Discover("PluginIsolationFixtures");
        var pluginV1 = Assert.Single(snapshot.Assemblies, assembly =>
            assembly.GetName().Name == "PluginIsolation.PluginV1");
        var pluginV2 = Assert.Single(snapshot.Assemblies, assembly =>
            assembly.GetName().Name == "PluginIsolation.PluginV2");

        var contextV1 = Assert.IsType<PluginLoadContext>(
            AssemblyLoadContext.GetLoadContext(pluginV1));
        var contextV2 = Assert.IsType<PluginLoadContext>(
            AssemblyLoadContext.GetLoadContext(pluginV2));
        var dependencyV1 = contextV1.LoadFromAssemblyName(
            new AssemblyName("PluginIsolation.Dependency, Version=1.0.0.0"));
        var dependencyV2 = contextV2.LoadFromAssemblyName(
            new AssemblyName("PluginIsolation.Dependency, Version=2.0.0.0"));

        Assert.Equal(new Version(1, 0, 0, 0), dependencyV1.GetName().Version);
        Assert.Equal(new Version(2, 0, 0, 0), dependencyV2.GetName().Version);
        Assert.NotSame(contextV1, contextV2);
        Assert.NotSame(dependencyV1, dependencyV2);
        Assert.Same(contextV1, AssemblyLoadContext.GetLoadContext(dependencyV1));
        Assert.Same(contextV2, AssemblyLoadContext.GetLoadContext(dependencyV2));
    }

    [Fact]
    public void 四个真实插件均具有唯一Managed模块且模块不再自报所有者()
    {
        Assembly[] assemblies =
        [
            typeof(BiliDownloader.Plugin.BiliDownloaderPluginModule).Assembly,
            typeof(DaTangAccountingHelpPluginModule).Assembly,
            typeof(MyPlugTest.Plugin.MyPlugTestPluginModule).Assembly,
            typeof(MySmallTools.Plugin.MySmallToolsPluginModule).Assembly,
        ];

        var catalog = PluginModuleCatalog.Discover(assemblies);

        Assert.Equal(4, catalog.Modules.Count);
        Assert.Equal(4, catalog.Entries.Select(entry => entry.Assembly).Distinct().Count());
        Assert.DoesNotContain(
            typeof(IPluginModule).GetProperties(),
            property => property.Name == "PluginId");
    }

    private static void WithCopiedPluginFixture(
        string removeFileName,
        Action<PluginDiscoverySnapshot> assertion)
    {
        var rootName = "ManagedOnly-" + Guid.NewGuid().ToString("N");
        var rootPath = Path.Combine(AppContext.BaseDirectory, rootName);
        var pluginPath = Path.Combine(rootPath, "PluginV1");

        try
        {
            CopyFixtureDirectory("PluginV1", pluginPath);
            File.Delete(Path.Combine(pluginPath, removeFileName));
            assertion(AssemblyLoaderHelper.Discover(rootName));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static void CopyFixtureDirectory(string fixtureName, string destinationPath)
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "PluginIsolationFixtures",
            fixtureName);
        Directory.CreateDirectory(destinationPath);
        foreach (var sourceFile in Directory.GetFiles(sourcePath))
        {
            File.Copy(
                sourceFile,
                Path.Combine(destinationPath, Path.GetFileName(sourceFile)));
        }
    }

    private sealed class FirstModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            ArgumentNullException.ThrowIfNull(context);
    }

    private sealed class SecondModule : IPluginModule
    {
        public void Configure(IPluginRegistrationContext context) =>
            ArgumentNullException.ThrowIfNull(context);
    }

    private sealed class ConstructorDependency;

    private sealed class ModuleWithoutPublicParameterlessConstructor : IPluginModule
    {
        private readonly ConstructorDependency _dependency;

        private ModuleWithoutPublicParameterlessConstructor(ConstructorDependency dependency)
        {
            _dependency = dependency;
        }

        public void Configure(IPluginRegistrationContext context)
        {
            _ = _dependency;
            ArgumentNullException.ThrowIfNull(context);
        }
    }
}
