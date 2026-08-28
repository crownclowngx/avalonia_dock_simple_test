using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>验证候选 Host 3.3 继续加载由公开 3.0、3.1 和 3.2 模板生成的旧插件包。</summary>
public sealed class WorkbenchCommandG6LegacyPackageTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G6_LEGACY_PLUGIN_ROOT";

    [Fact]
    public void 三个旧Sdk插件在新Host中保持零Command兼容路径()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            // 旧模板实体包由 G6 专项门禁生成；普通回归不依赖网络或忽略目录。
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(configuredRoot));
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(3, snapshot.Assemblies.Count);
        Assert.Equal(
            3,
            snapshot.Assemblies
                .Select(AssemblyLoadContext.GetLoadContext)
                .Distinct()
                .Count());
        Assert.All(
            snapshot.Assemblies,
            assembly => Assert.NotSame(
                AssemblyLoadContext.Default,
                AssemblyLoadContext.GetLoadContext(assembly)));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(PluginId).Assembly));

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"workbench-command-g6-legacy-{Guid.NewGuid():N}");
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
        services.AddSingleton(PluginModuleCatalog.Discover(snapshot));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        try
        {
            pluginProviders.Compose(
                provider.GetRequiredService<PluginModuleCatalog>(),
                provider,
                registryBuilder,
                documentScopes,
                diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();

            Assert.Equal(3, registry.Plugins.Count);
            Assert.Equal(3, registry.Documents.Count);
            Assert.Empty(registry.WorkbenchCommands);
            Assert.Empty(registry.MenuCommandContributions);
            Assert.Empty(registry.KeyBindingContributions);
            Assert.Equal(
                ["3.0.0.0", "3.1.0.0", "3.2.0.0"],
                registry.Plugins
                    .Select(plugin => PluginVersionText.Format(plugin.Manifest.Sdk.MinInclusive))
                    .Order(StringComparer.Ordinal)
                    .ToArray());
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }
}
