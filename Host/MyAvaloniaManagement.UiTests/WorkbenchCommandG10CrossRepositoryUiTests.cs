using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>以同一 MainWindow 验证 G10 两个外部插件的菜单、快捷键、Palette 与释放闭环。</summary>
/// <remarks>
/// 测试输入只能是 G10 门禁生成的实体包组合目录。Host 仍独占 Avalonia 控件和订阅；测试通过
/// 活动 Document 切换观察投影，不读取外部插件 Provider，也不让测试反射参与生产命令发现。
/// </remarks>
public sealed class WorkbenchCommandG10CrossRepositoryUiTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G10_EXTERNAL_PLUGIN_ROOT";

    private static readonly DocumentTypeId StudioDocument =
        new("myavalonia.plugin.workflow-studio.document.studio");
    private static readonly DocumentTypeId GomokuDocument =
        new("myavalonia.plugin.classic.game.document.gomoku");

    [AvaloniaFact]
    public async Task Studio与五子棋切换时全部投影只指向当前实例且关闭后无订阅残留()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        using var context = new CrossRepositoryUiContext(Path.GetFullPath(configuredRoot));
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        try
        {
            Assert.Equal(5, ExternalBindings(window).Count());
            AssertExternalMenuAbsent(window);

            var studio = await context.CreateDocumentAsync(StudioDocument);
            var dock = context.GetDocumentDock();
            dock.ActiveDockable = studio;
            await FlushUiAsync();
            AssertWorkflowMenu(window);
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "重新开始当前五子棋"));
            Assert.Equal(3, ExternalPaletteCommands(window).Count);

            var firstGame = await context.CreateDocumentAsync(GomokuDocument);
            var secondGame = await context.CreateDocumentAsync(GomokuDocument);
            dock.ActiveDockable = firstGame;
            await FlushUiAsync();
            AssertGomokuMenu(window, undoEnabled: false);
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "验证当前工作流"));
            Assert.Equal(2, ExternalPaletteCommands(window).Count);

            PlayGomokuPosition(firstGame.Model, 0, 0);
            await FlushUiAsync();
            AssertGomokuMenu(window, undoEnabled: true);
            dock.ActiveDockable = secondGame;
            await FlushUiAsync();
            AssertGomokuMenu(window, undoEnabled: false);
            PlayGomokuPosition(secondGame.Model, 2, 2);
            await FlushUiAsync();
            AssertGomokuMenu(window, undoEnabled: true);

            dock.ActiveDockable = firstGame;
            await FlushUiAsync();
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            await FlushUiAsync();
            Assert.Equal(0, ReadGomokuMoveCount(firstGame.Model));
            Assert.Equal(1, ReadGomokuMoveCount(secondGame.Model));

            // 重复关闭和新建能暴露旧 Target、菜单或 Palette 订阅未解除造成的重复项。
            context.Workspace.DockFactory.CloseDockable(firstGame);
            context.Workspace.DockFactory.CloseDockable(secondGame);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var current = await context.CreateDocumentAsync(GomokuDocument);
                dock.ActiveDockable = current;
                await FlushUiAsync();
                AssertGomokuMenu(window, undoEnabled: false);
                Assert.Equal(2, ExternalPaletteCommands(window).Count);
                context.Workspace.DockFactory.CloseDockable(current);
                dock.ActiveDockable = null;
                await FlushUiAsync();
                AssertExternalMenuAbsent(window);
                Assert.Empty(ExternalPaletteCommands(window));
            }

            dock.ActiveDockable = studio;
            await FlushUiAsync();
            AssertWorkflowMenu(window);
            context.Workspace.DockFactory.CloseDockable(studio);
            dock.ActiveDockable = null;
            await FlushUiAsync();
            AssertExternalMenuAbsent(window);
            Assert.Empty(ExternalPaletteCommands(window));
        }
        finally
        {
            window.Close();
        }

        Assert.Empty(window.KeyBindings);
        context.Provider.GetRequiredService<PluginLifecycleStateStore>().SetState(
            new PluginLifecycleState(
                new PluginId("myavalonia.plugin.workflow-studio"),
                PluginLifecycleStatus.NotStarted));
        await FlushUiAsync();
        Assert.Empty(window.KeyBindings);
    }

    private static IReadOnlyList<CommandId> ExternalPaletteCommands(MainWindow window)
    {
        window.OpenCommandPalette();
        Dispatcher.UIThread.RunJobs();
        var list = Assert.Single(
            window.GetLogicalDescendants().OfType<ListBox>(),
            item => item.Name == "PaletteItems");
        var commands = list.Items
            .Cast<WorkbenchCommandPaletteProjectionEntry>()
            .Select(item => item.CommandId)
            .Where(id => id.Value.StartsWith(
                "myavalonia.plugin.workflow-studio.command.",
                StringComparison.Ordinal) ||
                id.Value.StartsWith(
                    "myavalonia.plugin.classic.game.command.",
                    StringComparison.Ordinal))
            .ToArray();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        return commands;
    }

    private static IEnumerable<KeyBinding> ExternalBindings(MainWindow window) =>
        window.KeyBindings.Where(item => item.Gesture is not null &&
            (item.Gesture.Key is Key.F5 or Key.F6 or Key.Z or Key.R));

    private static void AssertWorkflowMenu(MainWindow window)
    {
        Assert.Single(window.GetLogicalDescendants().OfType<MenuItem>(),
            item => Equals(item.Header, "验证当前工作流"));
        Assert.Single(window.GetLogicalDescendants().OfType<MenuItem>(),
            item => Equals(item.Header, "运行当前工作流"));
        Assert.Single(window.GetLogicalDescendants().OfType<MenuItem>(),
            item => Equals(item.Header, "取消当前工作流"));
    }

    private static void AssertGomokuMenu(MainWindow window, bool undoEnabled)
    {
        Assert.Single(window.GetLogicalDescendants().OfType<MenuItem>(),
            item => Equals(item.Header, "重新开始当前五子棋"));
        var undo = Assert.Single(window.GetLogicalDescendants().OfType<MenuItem>(),
            item => Equals(item.Header, "撤销当前五子棋"));
        Assert.Equal(undoEnabled, undo.IsEnabled);
    }

    private static void AssertExternalMenuAbsent(MainWindow window)
    {
        foreach (var header in new[]
                 {
                     "验证当前工作流", "运行当前工作流", "取消当前工作流",
                     "重新开始当前五子棋", "撤销当前五子棋",
                 })
        {
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, header));
        }
    }

    private static Task FlushUiAsync() => Dispatcher.UIThread.InvokeAsync(
        () => { },
        DispatcherPriority.Background).GetTask();

    /// <summary>仅为跨仓 Headless 验收建立五子棋可撤销状态，不改变插件生产接口。</summary>
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

    /// <summary>只组合两个外部实体包和 Host 现有服务，不拥有插件业务类型或命令状态。</summary>
    private sealed class CrossRepositoryUiContext : IDisposable
    {
        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        internal CrossRepositoryUiContext(string pluginRoot)
        {
            TempDirectory = Path.Combine(
                Path.GetTempPath(),
                "MyAvaloniaManagement.UiTests",
                $"workbench-command-g10-{Guid.NewGuid():N}");
            Directory.CreateDirectory(TempDirectory);
            _diagnostics = HostDiagnosticSession.Start(
                Path.Combine(TempDirectory, "diagnostics"));
            _pluginProviders = new PluginProviderOwner();
            _documentScopes = new DocumentScopeRegistry();
            var registryBuilder = new PluginRegistryBuilder();
            var snapshot = AssemblyLoaderHelper.Discover(pluginRoot);
            Assert.Empty(snapshot.Diagnostics);
            var services = new ServiceCollection();
            services.AddApplicationServices(registryBuilder, _pluginProviders, _documentScopes);
            services.AddViewModels();
            services.AddSingleton<IHostStorageService>(new UiStorageService());
            services.AddSingleton(new DockLayoutStore(
                Path.Combine(TempDirectory, DockLayoutStore.LayoutFileName)));
            services.AddSingleton(new AppearanceSettingsStore(
                Path.Combine(TempDirectory, AppearanceSettingsStore.SettingsFileName)));
            services.AddSingleton(_diagnostics);
            services.AddSingleton<IHostDiagnosticSink>(_diagnostics);
            services.AddSingleton(PluginModuleCatalog.Discover(snapshot));
            Provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
            _pluginProviders.Compose(
                Provider.GetRequiredService<PluginModuleCatalog>(),
                Provider,
                registryBuilder,
                _documentScopes,
                _diagnostics);
            var registry = Provider.GetRequiredService<PluginRegistry>();
            Provider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                registry,
                Provider.GetRequiredService<PluginAvailabilityReadModel>());
            Workspace = Provider.GetRequiredService<WorkspaceSession>();
            ViewModel = Provider.GetRequiredService<MainWindowViewModel>();
        }

        internal string TempDirectory { get; }

        internal ServiceProvider Provider { get; }

        internal WorkspaceSession Workspace { get; }

        internal MainWindowViewModel ViewModel { get; }

        internal async Task<ManagedDocumentDockable> CreateDocumentAsync(
            DocumentTypeId documentTypeId)
        {
            var existingModels = GetDocumentDock().VisibleDockables!
                .OfType<ManagedDocumentDockable>()
                .Select(item => item.Model)
                .ToHashSet(ReferenceEqualityComparer.Instance);
            var result = await Provider
                .GetRequiredService<DocumentPersistenceCoordinator>()
                .CreateDocumentAsync(documentTypeId);
            Provider.GetRequiredService<DocumentOperationState>().Apply(result);
            return GetDocumentDock().VisibleDockables!
                .OfType<ManagedDocumentDockable>()
                .Single(item =>
                    item.Registration.Descriptor.DocumentTypeId == documentTypeId &&
                    !existingModels.Contains(item.Model));
        }

        internal DocumentDock GetDocumentDock() =>
            Assert.IsType<DocumentDock>(Workspace.DockFactory.GetDockable<IDocumentDock>(
                DockLayoutIds.Documents));

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _documentScopes.CloseAll();
            _pluginProviders.Dispose();
            Provider.Dispose();
            _diagnostics.Dispose();
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
        }
    }
}
