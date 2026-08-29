using System.Reflection;
using System.Runtime.Loader;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>同时使用 WorkflowStudio 与 ClassicGame 实体包验证 G10 跨仓注册和实例路由。</summary>
/// <remarks>
/// 两个外部仓库不进入 Host 解决方案，本测试也不引用其源码类型。G10 门禁把两份确定性 ZIP
/// 解压到同一 Controls 根后，本测试只通过生产 Loader、独立 ALC、Registry 和 Document Scope
/// 观察公共契约，防止分别绿色的插件在共同加载时发生身份、菜单或快捷键冲突。
/// </remarks>
public sealed class WorkbenchCommandG10CrossRepositoryPackageTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G10_EXTERNAL_PLUGIN_ROOT";

    private static readonly PluginId StudioOwner =
        new("myavalonia.plugin.workflow-studio");
    private static readonly PluginId ClassicGameOwner =
        new("myavalonia.plugin.classic.game");
    private static readonly DocumentTypeId StudioDocument =
        new("myavalonia.plugin.workflow-studio.document.studio");
    private static readonly DocumentTypeId GomokuDocument =
        new("myavalonia.plugin.classic.game.document.gomoku");
    private static readonly CommandId ValidateCommand =
        new("myavalonia.plugin.workflow-studio.command.validate");
    private static readonly CommandId GomokuUndoCommand =
        new("myavalonia.plugin.classic.game.command.gomoku.undo");

    [Fact]
    public async Task 两个实体包共同加载时二十五条命令无冲突且目标仍按当前实例隔离()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(configuredRoot));
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(2, snapshot.Assemblies.Count);
        Assert.Equal(
            ["ClassicGamePlugin.Plugin", "WorkflowStudio.Plugin"],
            snapshot.Assemblies.Select(assembly => assembly.GetName().Name).Order());
        Assert.All(snapshot.Assemblies, assembly =>
        {
            Assert.NotSame(
                AssemblyLoadContext.Default,
                AssemblyLoadContext.GetLoadContext(assembly));
        });
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(CommandId).Assembly));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(CommandDescriptor).Assembly));

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(),
            $"workbench-command-g10-packages-{Guid.NewGuid():N}");
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
            Assert.Equal(2, registry.Plugins.Count);
            Assert.Equal(14, registry.Documents.Count);
            Assert.Equal(25, registry.WorkbenchCommands.Count);
            Assert.Equal(25, registry.MenuCommandContributions.Count);
            Assert.Equal(5, registry.KeyBindingContributions.Count);
            Assert.Equal(
                25,
                registry.WorkbenchCommands
                    .Select(command => command.Descriptor.CommandId)
                    .Distinct()
                    .Count());
            Assert.Equal(
                [ClassicGameOwner, StudioOwner],
                registry.Plugins.Select(plugin => plugin.Manifest.PluginId).OrderBy(id => id.Value));
            Assert.All(
                registry.WorkbenchCommands,
                command => Assert.Contains(
                    registry.Documents,
                    document => document.OwnerId == command.OwnerId &&
                        document.Descriptor.DocumentTypeId == command.TargetDocumentTypeId));
            Assert.Equal(
                5,
                registry.KeyBindingContributions
                    .Select(binding => new KeyGesture(
                        binding.Descriptor.Key,
                        binding.Descriptor.Modifiers))
                    .Distinct()
                    .Count());

            var studioRegistration = Assert.Single(
                registry.Documents,
                item => item.Descriptor.DocumentTypeId == StudioDocument);
            var gomokuRegistration = Assert.Single(
                registry.Documents,
                item => item.Descriptor.DocumentTypeId == GomokuDocument);
            var studioScopes = pluginProviders.GetDocumentScopeManager(StudioOwner);
            var gameScopes = pluginProviders.GetDocumentScopeManager(ClassicGameOwner);
            using var studio = studioScopes.CreateDocument(studioRegistration.ModelType);
            using var firstGame = gameScopes.CreateDocument(gomokuRegistration.ModelType);
            using var secondGame = gameScopes.CreateDocument(gomokuRegistration.ModelType);
            await studio.Model.InitializeAsync(
                new NewDocumentActivation("G10 Studio"),
                studio.ClosingToken);
            await firstGame.Model.InitializeAsync(
                new NewDocumentActivation("G10 Gomoku A"),
                firstGame.ClosingToken);
            await secondGame.Model.InitializeAsync(
                new NewDocumentActivation("G10 Gomoku B"),
                secondGame.ClosingToken);

            var studioTarget = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(studio.Model);
            var firstTarget = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(firstGame.Model);
            var secondTarget = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(secondGame.Model);
            Assert.True(studioTarget.CanExecute(ValidateCommand));
            Assert.False(firstTarget.CanExecute(GomokuUndoCommand));
            Assert.False(secondTarget.CanExecute(GomokuUndoCommand));

            PlayGomokuPosition(firstGame.Model, 0, 0);
            Assert.True(firstTarget.CanExecute(GomokuUndoCommand));
            Assert.False(secondTarget.CanExecute(GomokuUndoCommand));
            await firstTarget.ExecuteAsync(GomokuUndoCommand, CancellationToken.None);
            Assert.Equal(0, ReadGomokuMoveCount(firstGame.Model));
            Assert.Equal(0, ReadGomokuMoveCount(secondGame.Model));

            firstGame.Dispose();
            Assert.False(firstTarget.CanExecute(GomokuUndoCommand));
            Assert.True(studioTarget.CanExecute(ValidateCommand));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    /// <summary>仅在真实包测试中通过非 public 游戏入口建立可撤销状态，不给生产插件增加测试 API。</summary>
    private static void PlayGomokuPosition(object model, int row, int column)
    {
        var viewModel = model.GetType().GetProperty("ViewModel")!.GetValue(model)!;
        var positionType = model.GetType().Assembly.GetType(
            "ClassicGamePlugin.Features.Gomoku.Domain.GomokuPosition",
            throwOnError: true)!;
        var position = Activator.CreateInstance(
            positionType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [row, column],
            culture: null)!;
        viewModel.GetType().GetMethod(
            "PlayPosition",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewModel, [position]);
    }

    private static int ReadGomokuMoveCount(object model)
    {
        var viewModel = model.GetType().GetProperty("ViewModel")!.GetValue(model)!;
        return Assert.IsType<int>(
            viewModel.GetType().GetProperty("MoveCount")!.GetValue(viewModel));
    }
}
