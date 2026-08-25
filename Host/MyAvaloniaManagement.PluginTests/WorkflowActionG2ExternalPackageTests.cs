using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用 G2 从真实候选 NuGet 和通用模板生成的两个外部插件验证最终传播边界。
/// 普通测试运行没有外部包目录时不重复打包；G2 专项门禁设置环境变量后，本测试必须完整执行。
/// </summary>
public sealed class WorkflowActionG2ExternalPackageTests
{
    [Fact]
    public async Task 外部模板Provider与Consumer通过真实Host完成调用并释放Scope()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(
            "MYAVALONIA_WORKFLOW_G2_PLUGIN_ROOT");
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            // 外部模板门禁需要先生成候选包和两个 ZIP。普通回归继续覆盖 G1 内置夹具，
            // 这里遵循现有真实包测试惯例，不在没有实体输入时伪造源码引用。
            return;
        }

        var pluginRoot = Path.GetFullPath(configuredRoot);
        var snapshot = AssemblyLoaderHelper.Discover(pluginRoot);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(2, snapshot.Assemblies.Count);

        var providerAssembly = snapshot.Assemblies.Single(assembly =>
            assembly.GetName().Name == "WorkflowProviderProbe.Plugin");
        var consumerAssembly = snapshot.Assemblies.Single(assembly =>
            assembly.GetName().Name == "WorkflowConsumerProbe.Plugin");
        Assert.DoesNotContain(
            consumerAssembly.GetReferencedAssemblies(),
            reference => reference.Name == providerAssembly.GetName().Name);
        Assert.NotSame(
            AssemblyLoadContext.GetLoadContext(providerAssembly),
            AssemblyLoadContext.GetLoadContext(consumerAssembly));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(IWorkflowActionGateway).Assembly));

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"workflow-action-g2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(PluginModuleCatalog.Discover(snapshot));
        using var hostProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        const string sensitiveProbe = "G2-SENSITIVE-PROBE-MUST-NOT-ENTER-DIAGNOSTICS";
        try
        {
            pluginProviders.Compose(
                hostProvider.GetRequiredService<PluginModuleCatalog>(),
                hostProvider,
                registryBuilder,
                documentScopes,
                diagnostics);
            var registry = hostProvider.GetRequiredService<PluginRegistry>();
            hostProvider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                registry,
                hostProvider.GetRequiredService<PluginAvailabilityReadModel>());

            Assert.Equal(2, registry.Plugins.Count);
            Assert.Single(registry.WorkflowActions);
            var consumerId = new PluginId("myavalonia.plugin.workflow-g2-consumer");
            var gateway = Assert.IsAssignableFrom<IWorkflowActionGateway>(
                pluginProviders.GetRequiredService(
                    consumerId,
                    typeof(IWorkflowActionGateway)));

            await using (var run = gateway.CreateRun())
            {
                var result = await run.InvokeAsync(
                    new WorkflowActionInvocationRequest(
                        new WorkflowActionId(
                            "myavalonia.plugin.workflow-g2-provider.workflow.echo"),
                        JsonSerializer.SerializeToElement(new { value = sensitiveProbe })),
                    progress: null,
                    CancellationToken.None);

                Assert.Equal(WorkflowActionInvocationStatus.Succeeded, result.Status);
                Assert.Equal(
                    sensitiveProbe,
                    result.Output!.Value.GetProperty("echoed").GetString());
                Assert.Equal(
                    consumerId.Value,
                    result.Output.Value.GetProperty("caller").GetString());
            }

            // Handler 类型来自外部模板生成的 Provider ALC。通过反射只读取测试探针计数，
            // 不把该类型引入 Host 生产契约；计数归零证明 invocation scope 已异步释放。
            var handlerType = providerAssembly.GetType(
                "WorkflowProviderProbe.Plugin.EchoHandler",
                throwOnError: true,
                ignoreCase: false)!;
            Assert.Equal(0, ReadStaticCounter(handlerType, "ActiveInstances"));
            var createdInstances = ReadStaticCounter(handlerType, "CreatedInstances");
            Assert.True(createdInstances > 0);
            Assert.Equal(
                createdInstances,
                ReadStaticCounter(handlerType, "DisposedInstances"));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
        }

        try
        {
            var diagnosticText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(diagnosticsRoot, "*", SearchOption.AllDirectories)
                    .Select(File.ReadAllText));
            Assert.DoesNotContain(sensitiveProbe, diagnosticText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    private static int ReadStaticCounter(Type handlerType, string propertyName) =>
        Assert.IsType<int>(handlerType.GetProperty(propertyName)?.GetValue(null));
}
