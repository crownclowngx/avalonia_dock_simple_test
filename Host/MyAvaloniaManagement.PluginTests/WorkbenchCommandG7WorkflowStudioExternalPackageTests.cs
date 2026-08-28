using System.Collections;
using System.Windows.Input;
using System.Runtime.Loader;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>使用 G7 外部 WorkflowStudio 真实 ZIP 验证 Command 3.3 独立消费与当前实例 Target。</summary>
/// <remarks>
/// 外部仓库不进入 Host 解决方案，也没有源码 ProjectReference。本测试只接受专项门禁解压后的实体包目录，
/// 再经过生产 Loader、独立 ALC、插件 Provider、不可变 Registry 和真实 Document Scope；缺少实体输入时，
/// 普通 Host 回归安全返回，由 G7 脚本使用过滤器和环境变量保证专项路径真实执行。
/// </remarks>
public sealed class WorkbenchCommandG7WorkflowStudioExternalPackageTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_PLUGIN_ROOT";
    private const string PackageWithActionRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_WITH_ACTION_ROOT";

    private static readonly PluginId StudioOwner =
        new("myavalonia.plugin.workflow-studio");
    private static readonly DocumentTypeId StudioDocument =
        new("myavalonia.plugin.workflow-studio.document.studio");
    private static readonly CommandId ValidateCommand =
        new("myavalonia.plugin.workflow-studio.command.validate");
    private static readonly CommandId RunCommand =
        new("myavalonia.plugin.workflow-studio.command.run");
    private static readonly CommandId CancelCommand =
        new("myavalonia.plugin.workflow-studio.command.cancel");

    [Fact]
    public async Task 真实Studio包注册三条命令并保持两个Document实例隔离()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(configuredRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("WorkflowStudio.Plugin", assembly.GetName().Name);
        Assert.NotSame(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(assembly));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(CommandId).Assembly));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(CommandDescriptor).Assembly));

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(),
            $"workbench-command-g7-{Guid.NewGuid():N}");
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
            provider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                registry,
                provider.GetRequiredService<PluginAvailabilityReadModel>());

            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal(StudioOwner, plugin.Manifest.PluginId);
            Assert.Equal(new Version(1, 2, 0, 0), plugin.Manifest.PluginVersion);
            Assert.Equal(new Version(3, 3, 0, 0), plugin.Manifest.Sdk.MinInclusive);
            var document = Assert.Single(registry.Documents);
            Assert.Equal(StudioDocument, document.Descriptor.DocumentTypeId);
            Assert.Equal(3, registry.WorkbenchCommands.Count);
            Assert.Equal(3, registry.MenuCommandContributions.Count);
            Assert.Equal(3, registry.KeyBindingContributions.Count);
            Assert.Equal(
                [CancelCommand, RunCommand, ValidateCommand],
                registry.WorkbenchCommands
                    .Select(item => item.Descriptor.CommandId)
                    .OrderBy(item => item.Value));
            Assert.All(
                registry.WorkbenchCommands,
                command =>
                {
                    Assert.Equal(StudioOwner, command.OwnerId);
                    Assert.Equal(StudioDocument, command.TargetDocumentTypeId);
                });
            Assert.Equal(
                [0, 10, 20],
                registry.MenuCommandContributions
                    .OrderBy(item => item.Descriptor.Order)
                    .Select(item => item.Descriptor.Order));
            Assert.All(
                registry.MenuCommandContributions,
                contribution =>
                {
                    Assert.Equal(WorkbenchMenuLocations.ToolsShared,
                        contribution.Descriptor.LocationId);
                    Assert.Equal("workflow", contribution.Descriptor.Group);
                    Assert.Equal(
                        MenuCommandTargetUnavailableBehavior.Hide,
                        contribution.Descriptor.TargetUnavailableBehavior);
                });
            Assert.Contains(
                registry.KeyBindingContributions,
                item => item.Descriptor.CommandId == ValidateCommand &&
                    item.Descriptor.Key == Key.F6 &&
                    item.Descriptor.Modifiers == KeyModifiers.None);
            Assert.Contains(
                registry.KeyBindingContributions,
                item => item.Descriptor.CommandId == RunCommand &&
                    item.Descriptor.Key == Key.F5 &&
                    item.Descriptor.Modifiers == KeyModifiers.None);
            Assert.Contains(
                registry.KeyBindingContributions,
                item => item.Descriptor.CommandId == CancelCommand &&
                    item.Descriptor.Key == Key.F5 &&
                    item.Descriptor.Modifiers == KeyModifiers.Shift);

            var scopeManager = pluginProviders.GetDocumentScopeManager(StudioOwner);
            using var first = scopeManager.CreateDocument(document.ModelType);
            using var second = scopeManager.CreateDocument(document.ModelType);
            await first.Model.InitializeAsync(
                new NewDocumentActivation("Studio A"),
                first.ClosingToken);
            await second.Model.InitializeAsync(
                new NewDocumentActivation("Studio B"),
                second.ClosingToken);
            var firstTarget = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(first.Model);
            var secondTarget = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(second.Model);

            Assert.True(firstTarget.CanExecute(ValidateCommand));
            Assert.False(firstTarget.CanExecute(RunCommand));
            Assert.False(firstTarget.CanExecute(CancelCommand));
            Assert.True(secondTarget.CanExecute(ValidateCommand));
            await firstTarget.ExecuteAsync(ValidateCommand, CancellationToken.None);

            first.Dispose();

            Assert.False(firstTarget.CanExecute(ValidateCommand));
            Assert.False(firstTarget.CanExecute(RunCommand));
            Assert.False(firstTarget.CanExecute(CancelCommand));
            Assert.True(secondTarget.CanExecute(ValidateCommand));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task 真实StudioRun继续进入CallerBoundGateway和跨AlcAction()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageWithActionRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(configuredRoot));
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(2, snapshot.Assemblies.Count);
        Assert.All(
            snapshot.Assemblies,
            assembly => Assert.NotSame(
                AssemblyLoadContext.Default,
                AssemblyLoadContext.GetLoadContext(assembly)));

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(),
            $"workbench-command-g7-action-{Guid.NewGuid():N}");
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
            provider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                registry,
                provider.GetRequiredService<PluginAvailabilityReadModel>());
            var action = Assert.Single(registry.WorkflowActions);
            Assert.Equal(
                "myavalonia.plugin.workflow-g1-provider.workflow.echo",
                action.Descriptor.Id.Value);
            var document = Assert.Single(
                registry.Documents,
                item => item.Descriptor.DocumentTypeId == StudioDocument);
            var scopeManager = pluginProviders.GetDocumentScopeManager(StudioOwner);
            using var lease = scopeManager.CreateDocument(document.ModelType);
            await lease.Model.InitializeAsync(
                new NewDocumentActivation("Studio Action"),
                lease.ClosingToken);
            var target = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(lease.Model);

            ConfigureSingleEchoStep(lease.Model);
            await target.ExecuteAsync(ValidateCommand, CancellationToken.None);

            Assert.True(target.CanExecute(RunCommand));
            Assert.False(target.CanExecute(CancelCommand));
            await target.ExecuteAsync(RunCommand, CancellationToken.None);

            Assert.Equal("工作流执行成功。", ReadStringProperty(lease.Model, "RunStatus"));
            Assert.True(target.CanExecute(RunCommand));
            Assert.False(target.CanExecute(CancelCommand));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    /// <summary>只通过 Studio 的公开绑定面建立一个 echo 步骤，不引用外部程序集中的具体类型。</summary>
    private static void ConfigureSingleEchoStep(IPluginDocument model)
    {
        var modelType = model.GetType();
        var actions = Assert.IsAssignableFrom<IEnumerable>(
            modelType.GetProperty("AvailableActions")?.GetValue(model));
        var action = Assert.Single(actions.Cast<object>());
        modelType.GetProperty("SelectedAction")!.SetValue(model, action);
        Assert.IsAssignableFrom<ICommand>(
            modelType.GetProperty("AddStepCommand")?.GetValue(model)).Execute(null);
        var steps = Assert.IsAssignableFrom<IEnumerable>(
            modelType.GetProperty("Steps")?.GetValue(model));
        var step = Assert.Single(steps.Cast<object>());
        var arguments = Assert.IsAssignableFrom<IEnumerable>(
            step.GetType().GetProperty("Arguments")?.GetValue(step));
        var argument = Assert.Single(arguments.Cast<object>());
        Assert.Equal("value", ReadStringProperty(argument, "Name"));
        argument.GetType().GetProperty("Value")!.SetValue(argument, "\"host-g7\"");
    }

    private static string ReadStringProperty(object target, string propertyName) =>
        Assert.IsType<string>(target.GetType().GetProperty(propertyName)?.GetValue(target));
}
