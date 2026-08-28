using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用 G0 从独立 WorkflowStudio 仓库生成的真实 ZIP，冻结 Command 引入前的加载与注册基线。
/// </summary>
/// <remarks>
/// 本测试只验证既有 Host Loader、独立 ALC、插件 Provider 和不可变 Registry，不引入任何 Command
/// 生产契约。普通回归没有外部包目录时不会越权定位开发机仓库；G0 专项门禁通过环境变量提供实体输入。
/// </remarks>
public sealed class WorkbenchCommandG0ExternalPackageTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G0_WORKFLOW_PLUGIN_ROOT";

    [Fact]
    public void WorkflowStudio真实Zip通过Host发现组合并保持既有Document事实()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            // 外部仓库不是 Host 解决方案的一部分。普通单测不能猜测其绝对路径或临时生成假包，
            // 专项门禁会设置环境变量并用测试过滤器确保本方法真实执行。
            return;
        }

        var pluginRoot = Path.GetFullPath(configuredRoot);
        var snapshot = AssemblyLoaderHelper.Discover(pluginRoot);
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("WorkflowStudio.Plugin", assembly.GetName().Name);
        Assert.NotSame(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(assembly));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(PluginId).Assembly));

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"workbench-command-g0-{Guid.NewGuid():N}");
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
            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal("myavalonia.plugin.workflow-studio", plugin.Manifest.PluginId.Value);
            Assert.Equal(new Version(1, 1, 0, 0), plugin.Manifest.PluginVersion);
            Assert.Single(plugin.DocumentTypes);
            Assert.Empty(plugin.ToolTypes);
            Assert.Empty(registry.WorkflowActions);

            var document = Assert.Single(registry.Documents);
            Assert.Equal(
                "myavalonia.plugin.workflow-studio.document.studio",
                document.Descriptor.DocumentTypeId.Value);
            Assert.Equal("WorkflowStudio.Plugin", document.ModelType.Assembly.GetName().Name);
            Assert.Equal(
                ["myavalonia.plugin.workflow-studio"],
                pluginProviders.AvailablePluginIds.Select(pluginId => pluginId.Value));
        }
        finally
        {
            // Document Scope 与 Provider 由不同所有者释放。即使断言失败，也要先关闭全部 Scope，
            // 再由 using 释放插件 Provider，保持真实 Host 的资源顺序不被测试捷径改变。
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }
}
