using System.Reflection;
using System.Runtime.Loader;
using DaTangAccountingHelpPlug.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证插件只能沿 manifest v2、deps、精确入口类型和 DI 激活这一条路径进入宿主。
/// </summary>
/// <remarks>
/// 设计意图：这些测试同时覆盖“允许什么”和“必须拒绝什么”。拒绝场景使用真实目录快照，
/// 防止未来只修改 Catalog 单元测试，却在物理加载入口重新引入 Legacy 回退。
/// </remarks>
public sealed class ManagedOnlyPluginLoadingTests
{
    [Fact]
    public void 有效deps与精确入口形成可配置Catalog()
    {
        var snapshot = AssemblyLoaderHelper.Discover("PluginIsolationFixtures");

        Assert.Equal(2, snapshot.Assemblies.Count);
        Assert.DoesNotContain(snapshot.Diagnostics, item =>
            item.Code is HostDiagnosticCodes.PluginDependencyManifestMissing
                or HostDiagnosticCodes.PluginEntryInvalid);

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
    public void 入口程序集缺失时在模块构造前隔离目录()
    {
        WithCopiedPluginFixture(
            removeFileName: "PluginIsolation.PluginV1.dll",
            assertion: snapshot =>
            {
                Assert.Empty(snapshot.Assemblies);
                Assert.Contains(snapshot.Diagnostics, item =>
                    item.Code == HostDiagnosticCodes.PluginEntryInvalid);
                Assert.Empty(PluginModuleCatalog.Discover(snapshot).Entries);
            });
    }

    [Theory]
    [InlineData("PluginIsolation.Plugin.MissingPluginModule")]
    [InlineData("pluginisolation.plugin.IsolationPluginModule")]
    [InlineData("PluginIsolation.Plugin.InternalPluginModule")]
    [InlineData("PluginIsolation.Plugin.AbstractPluginModule")]
    [InlineData("PluginIsolation.Plugin.WrongContractEntry")]
    [InlineData("PluginIsolation.Plugin.PrivateConstructorPluginModule")]
    [InlineData("PluginIsolation.Plugin.GenericPluginModule`1")]
    public void 精确类型解析与结构负例在构造和Configure前返回统一诊断(string entryType)
    {
        // 生产 PluginLoadContext 明确不可回收，因此这里复用固定物理夹具，不为每个负例复制并加载
        // 一个最终无法在测试进程内删除的 DLL 目录。解析和 Preflight 调用与 Loader 使用同一参数。
        var snapshot = AssemblyLoaderHelper.Discover("PluginIsolationFixtures");
        var assembly = Assert.Single(snapshot.Assemblies, candidate =>
            candidate.GetName().Name == "PluginIsolation.PluginV1");
        var resolvedType = assembly.GetType(entryType, throwOnError: false, ignoreCase: false);

        Assert.False(PluginModulePreflight.TryValidate(
            resolvedType, out var moduleType, out var errorCode, out _));
        Assert.Null(moduleType);
        Assert.Equal(HostDiagnosticCodes.PluginEntryInvalid, errorCode);
    }

    [Fact]
    public void 清单入口未实现V2模块时在激活前被隔离()
    {
        var snapshot = AssemblyLoaderHelper.Discover("ManagedOnlyFixtures");

        Assert.Empty(snapshot.Assemblies);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(HostDiagnosticCodes.PluginEntryInvalid, diagnostic.Code);
        Assert.Equal("NoModule", diagnostic.PluginDirectory);
        Assert.DoesNotContain(
            typeof(PluginRegistry).Assembly.GetTypes(),
            type => type.Name == "PluginStrategyActivator");
    }

    [Fact]
    public void 未声明的第二模块不会改变精确入口验证结果()
    {
        var snapshot = AssemblyLoaderHelper.Discover("PluginIsolationFixtures");

        var catalog = PluginModuleCatalog.Discover(snapshot);
        var declared = Assert.Single(catalog.Entries, entry =>
            entry.Assembly.GetName().Name == "PluginIsolation.PluginV1");
        Assert.Equal("PluginIsolation.Plugin.IsolationPluginModule", declared.ModuleType.FullName);
        Assert.DoesNotContain(catalog.Entries, entry =>
            entry.ModuleType.Name == "UndeclaredPluginModule");
    }

    [Fact]
    public void 模块缺少public无参构造时返回稳定结构诊断()
    {
        var accepted = PluginModulePreflight.TryValidate(
            typeof(ModuleWithoutPublicParameterlessConstructor),
            out var moduleType,
            out var errorCode,
            out _);

        Assert.False(accepted);
        Assert.Null(moduleType);
        Assert.Equal(HostDiagnosticCodes.PluginEntryInvalid, errorCode);
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
    public void 四个真实业务插件全部进入最终V3入口()
    {
        var biliAssembly = typeof(BiliDownloader.Plugin.BiliDownloaderPluginModule).Assembly;
        var biliModule = Assert.Single(biliAssembly.ExportedTypes, type =>
            typeof(IPluginModule).IsAssignableFrom(type) && !type.IsAbstract);
        Assert.True(PluginModulePreflight.TryValidate(
            biliModule, out var validatedBili, out var biliError, out _));
        Assert.Same(biliModule, validatedBili);
        Assert.Null(biliError);

        var myPlugTestAssembly = typeof(MyPlugTest.Plugin.MyPlugTestPluginModule).Assembly;
        var myPlugTestModule = Assert.Single(myPlugTestAssembly.ExportedTypes, type =>
            typeof(IPluginModule).IsAssignableFrom(type) && !type.IsAbstract);
        Assert.True(PluginModulePreflight.TryValidate(
            myPlugTestModule, out var validatedMyPlugTest, out var myPlugTestError, out _));
        Assert.Same(myPlugTestModule, validatedMyPlugTest);
        Assert.Null(myPlugTestError);

        var daTangAssembly = typeof(DaTangAccountingHelpPluginModule).Assembly;
        var daTangModule = Assert.Single(daTangAssembly.ExportedTypes, type =>
            typeof(IPluginModule).IsAssignableFrom(type) && !type.IsAbstract);
        Assert.True(PluginModulePreflight.TryValidate(
            daTangModule, out var validatedDaTang, out var daTangError, out _));
        Assert.Same(daTangModule, validatedDaTang);
        Assert.Null(daTangError);

        var mySmallToolsAssembly = typeof(MySmallTools.Plugin.MySmallToolsPluginModule).Assembly;
        var mySmallToolsModule = Assert.Single(mySmallToolsAssembly.ExportedTypes, type =>
            typeof(IPluginModule).IsAssignableFrom(type) && !type.IsAbstract);
        Assert.True(PluginModulePreflight.TryValidate(
            mySmallToolsModule, out var validatedMySmallTools, out var mySmallToolsError, out _));
        Assert.Same(mySmallToolsModule, validatedMySmallTools);
        Assert.Null(mySmallToolsError);

        Assert.DoesNotContain(
            typeof(IPluginModule).GetProperties(),
            property => property.Name == "PluginId");
    }

    private static void WithCopiedPluginFixture(
        Action<PluginDiscoverySnapshot> assertion,
        string? removeFileName = null)
    {
        var rootName = "ManagedOnly-" + Guid.NewGuid().ToString("N");
        var rootPath = Path.Combine(AppContext.BaseDirectory, rootName);
        var pluginPath = Path.Combine(rootPath, "PluginV1");

        try
        {
            CopyFixtureDirectory("PluginV1", pluginPath);
            if (removeFileName is not null)
            {
                File.Delete(Path.Combine(pluginPath, removeFileName));
            }
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

    private sealed class ConstructorDependency;

    private sealed class ModuleWithoutPublicParameterlessConstructor : IPluginModule
    {
        private readonly ConstructorDependency _dependency;

        private ModuleWithoutPublicParameterlessConstructor(ConstructorDependency dependency)
        {
            _dependency = dependency;
        }

        public void Configure(IPluginRegistration context)
        {
            _ = _dependency;
            ArgumentNullException.ThrowIfNull(context);
        }
    }
}
