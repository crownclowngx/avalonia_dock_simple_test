using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>使用 G6 候选包和 Templates 1.3.0 生成的两个真实外部插件验证独立消费边界。</summary>
/// <remarks>
/// 普通回归没有外部 ZIP 输入时不临时打包，也不猜测开发机目录；G6 专项门禁通过环境变量提供
/// 两个已经由独立 NuGet 缓存还原并确定性打包的插件目录。
/// </remarks>
public sealed class WorkbenchCommandG6ExternalPackageTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G6_PLUGIN_ROOT";

    [Fact]
    public async Task 两个模板插件经独立Alc注册并执行各自DocumentCommand()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            // 外部消费是 G6 聚合门禁的实体输入，不属于普通单测的源码夹具。
            return;
        }

        var pluginRoot = Path.GetFullPath(configuredRoot);
        var snapshot = AssemblyLoaderHelper.Discover(pluginRoot);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(2, snapshot.Assemblies.Count);

        var loadContexts = snapshot.Assemblies
            .Select(AssemblyLoadContext.GetLoadContext)
            .ToArray();
        Assert.All(loadContexts, context => Assert.NotSame(AssemblyLoadContext.Default, context));
        Assert.NotSame(loadContexts[0], loadContexts[1]);
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(CommandId).Assembly));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(CommandDescriptor).Assembly));

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"workbench-command-g6-{Guid.NewGuid():N}");
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
            Assert.Equal(2, registry.Plugins.Count);
            Assert.Equal(2, registry.Documents.Count);
            Assert.Equal(2, registry.WorkbenchCommands.Count);
            Assert.Equal(2, registry.MenuCommandContributions.Count);
            Assert.Empty(registry.KeyBindingContributions);
            Assert.All(
                registry.Plugins,
                plugin => Assert.Equal(new Version(3, 3, 0, 0), plugin.Manifest.Sdk.MinInclusive));
            Assert.All(
                registry.MenuCommandContributions,
                contribution => Assert.Equal(
                    WorkbenchMenuLocations.ToolsShared,
                    contribution.Descriptor.LocationId));

            foreach (var registration in registry.Documents)
            {
                // 模板模型没有构造依赖。这里直接创建实例是包边界探针，不替代生产
                // PluginContributionActivator/Document Scope；G3–G5 已覆盖真实活动实例路由。
                var model = Activator.CreateInstance(registration.ModelType);
                var target = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(model);
                var command = Assert.Single(
                    registry.WorkbenchCommands,
                    item => item.OwnerId == registration.OwnerId);

                Assert.Equal(registration.Descriptor.DocumentTypeId, command.TargetDocumentTypeId);
                Assert.True(target.CanExecute(command.Descriptor.CommandId));
                await target.ExecuteAsync(command.Descriptor.CommandId, CancellationToken.None);
                Assert.False(target.CanExecute(command.Descriptor.CommandId));
                Assert.Equal(
                    "Workbench Command 已在当前文档实例执行",
                    registration.ModelType.GetProperty("Message")?.GetValue(model));
            }
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }
}
