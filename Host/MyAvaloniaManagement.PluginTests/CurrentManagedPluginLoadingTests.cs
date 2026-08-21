using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用四个当前 Managed Plugin 的真实构建输出验证统一入口、deps 解析和公共契约共享。
/// </summary>
public sealed class CurrentManagedPluginLoadingTests
{
    [Fact]
    public void G10最终测试Zip通过真实发现组合并发布两个Document()
    {
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_G10_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            // 普通回归不构建 G10 测试 ZIP；专项脚本会设置变量并单独执行本测试。
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(packageRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("DaTangAccountingHelpPlug", assembly.GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);
        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"datang-g10-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            pluginProviders.Compose(
                catalog, provider, registryBuilder, documentScopes, diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();
            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal("myavalonia.plugin.datang-accounting-help", plugin.Manifest.PluginId.Value);
            Assert.Equal(2, plugin.DocumentTypes.Count);
            Assert.Empty(plugin.ToolTypes);
            Assert.All(plugin.DocumentTypes, modelType =>
                Assert.Equal("DaTangAccountingHelpPlug", modelType.Assembly.GetName().Name));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    [Fact]
    public void G9最终测试Zip通过真实发现组合并发布完整Registry()
    {
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_G9_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            // 普通单元回归没有构建测试 ZIP；G9 专项脚本必须设置该环境变量并单独执行本测试。
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(packageRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("MyPlugTest", assembly.GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(),
            $"my-plug-test-g9-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            pluginProviders.Compose(
                catalog,
                provider,
                registryBuilder,
                documentScopes,
                diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();
            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal("myavalonia.plugin.my-plug-test", plugin.Manifest.PluginId.Value);
            Assert.Equal(4, plugin.DocumentTypes.Count);
            Assert.Single(plugin.ToolTypes);
            Assert.All(plugin.DocumentTypes, modelType =>
                Assert.Equal("MyPlugTest", modelType.Assembly.GetName().Name));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("BiliDownloader/BiliDownloader", "BiliDownloader", "BiliDownloader", "myavalonia.plugin.bili-downloader", false)]
    [InlineData("MyPlugTest/MyPlugTest", "MyPlugTest", "MyPlugTest", "myavalonia.plugin.my-plug-test", true)]
    [InlineData("DaTangAccountingHelpPlug/DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "DaTang", "myavalonia.plugin.datang-accounting-help", true)]
    [InlineData("MySmallTools/MySmallTools", "MySmallTools", "SmallTools", "myavalonia.plugin.my-small-tools", false)]
    public void 真实业务插件构建目录只接受已经迁移的V2入口(
        string projectPath,
        string assemblyName,
        string directoryName,
        string pluginId,
        bool expectedV2Entry)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException("无法确定测试构建配置。");
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_G3_PACKAGE_ROOT");
        var pluginDirectory = string.IsNullOrWhiteSpace(packageRoot)
            ? Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "Plugins",
                projectPath.Replace('/', Path.DirectorySeparatorChar),
                "bin",
                configuration,
                "net10.0"))
            : Path.GetFullPath(Path.Combine(packageRoot, "Controls", directoryName));
        var pluginAssemblyPath = Path.Combine(
            pluginDirectory,
            assemblyName + ".dll");

        Assert.True(
            File.Exists(pluginAssemblyPath),
            $"插件构建输出不存在：{pluginAssemblyPath}");
        Assert.True(
            File.Exists(Path.Combine(pluginDirectory, assemblyName + ".deps.json")),
            $"插件缺少标准 deps 入口：{assemblyName}");
        Assert.True(
            PluginManifestReader.TryRead(
                pluginDirectory,
                out var manifest,
                out var manifestErrorCode,
                out var manifestErrorDetail),
            $"插件清单无效：{manifestErrorCode}: {manifestErrorDetail}");
        Assert.Equal(pluginId, manifest!.PluginId.Value);
        Assert.Equal(assemblyName + ".dll", manifest.EntryPoint.Assembly);

        var context = new PluginLoadContext(pluginDirectory);
        var pluginAssembly = context.LoadFromAssemblyPath(pluginAssemblyPath);
        Assert.True(PluginCompatibilityEvaluator.HasMatchingPluginVersion(
            manifest,
            pluginAssembly.GetName().Version));
        var entryType = pluginAssembly.GetType(
            manifest.EntryPoint.Type, throwOnError: false, ignoreCase: false);
        var accepted = PluginModulePreflight.TryValidate(
            entryType, out var validatedType, out var entryCode, out _);
        Assert.Equal(expectedV2Entry, accepted);
        if (expectedV2Entry)
        {
            Assert.Same(entryType, validatedType);
            Assert.Null(entryCode);
        }
        else
        {
            Assert.Null(validatedType);
            Assert.Equal(HostDiagnosticCodes.PluginEntryInvalid, entryCode);
        }
        Assert.Equal(pluginId, manifest.PluginId.Value);
    }
}
