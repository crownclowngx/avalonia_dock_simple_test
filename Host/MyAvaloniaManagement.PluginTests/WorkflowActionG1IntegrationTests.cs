using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>使用两个真实插件、两个 ALC 和两个私有 Provider 验证 G1 端到端边界。</summary>
public sealed class WorkflowActionG1IntegrationTests
{
    [Fact]
    public async Task Consumer不引用Provider也能通过CallerBoundRun调用Action()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "WorkflowActionG1Fixtures");
        var snapshot = AssemblyLoaderHelper.Discover(fixtureRoot);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(2, snapshot.Assemblies.Count);
        var providerAssembly = snapshot.Assemblies.Single(assembly =>
            assembly.GetName().Name == "WorkflowActionG1.Provider");
        var consumerAssembly = snapshot.Assemblies.Single(assembly =>
            assembly.GetName().Name == "WorkflowActionG1.Consumer");
        Assert.DoesNotContain(
            consumerAssembly.GetReferencedAssemblies(),
            reference => reference.Name == providerAssembly.GetName().Name);
        Assert.NotSame(
            AssemblyLoadContext.GetLoadContext(providerAssembly),
            AssemblyLoadContext.GetLoadContext(consumerAssembly));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(IWorkflowActionGateway).Assembly));

        var catalog = PluginModuleCatalog.Discover(snapshot);
        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"workflow-action-g1-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var hostProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        try
        {
            pluginProviders.Compose(
                catalog, hostProvider, registryBuilder, documentScopes, diagnostics);
            var registry = hostProvider.GetRequiredService<PluginRegistry>();
            hostProvider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                registry,
                hostProvider.GetRequiredService<PluginAvailabilityReadModel>());
            Assert.Single(registry.WorkflowActions);
            Assert.Equal(2, registry.Plugins.Count);

            var providerId = new PluginId("myavalonia.plugin.workflow-g1-provider");
            var consumerId = new PluginId("myavalonia.plugin.workflow-g1-consumer");
            Assert.Throws<InvalidOperationException>(() => pluginProviders.GetRequiredService(
                providerId, typeof(IWorkflowActionGateway)));
            var gateway = Assert.IsAssignableFrom<IWorkflowActionGateway>(
                pluginProviders.GetRequiredService(consumerId, typeof(IWorkflowActionGateway)));
            Assert.Single(gateway.GetAvailableActions());
            await using var run = gateway.CreateRun();
            var progress = new List<WorkflowActionProgress>();
            var result = await run.InvokeAsync(
                new WorkflowActionInvocationRequest(
                    new WorkflowActionId("myavalonia.plugin.workflow-g1-provider.workflow.echo"),
                    JsonSerializer.SerializeToElement(new { value = "跨 ALC" })),
                new InlineProgress(progress.Add),
                CancellationToken.None);

            Assert.Equal(WorkflowActionInvocationStatus.Succeeded, result.Status);
            Assert.Equal("跨 ALC", result.Output!.Value.GetProperty("echoed").GetString());
            Assert.Equal(consumerId.Value, result.Output.Value.GetProperty("caller").GetString());
            Assert.Single(progress);
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    private sealed class InlineProgress(Action<WorkflowActionProgress> report)
        : IProgress<WorkflowActionProgress>
    {
        public void Report(WorkflowActionProgress value) => report(value);
    }
}
