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
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>使用外部 ClassicGame 1.1.0 真实包验证 G8 全游戏菜单、快捷键和当前实例路由。</summary>
/// <remarks>
/// 测试不引用 ClassicGame 源项目，也不把外部类型加载到默认 ALC。专项脚本提供真实 ZIP 解压目录，
/// 本类再通过生产 Loader、插件 Provider、Workspace、MainWindow XAML 和 Headless 输入完成多实例用户路径验收。
/// </remarks>
public sealed class WorkbenchCommandG8ClassicGameUiTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G8_CLASSIC_GAME_PLUGIN_ROOT";

    [AvaloniaFact]
    public async Task 十三游戏菜单与五子棋快捷键随当前真实Document实例投影且关闭无残留()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        using var context = new ExternalClassicGameUiContext(Path.GetFullPath(configuredRoot));
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        try
        {
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "重新开始当前五子棋"));
            var restartBinding = Assert.Single(
                window.KeyBindings,
                item => item.Gesture == new KeyGesture(
                    Key.R,
                    KeyModifiers.Control | KeyModifiers.Shift));
            var undoBinding = Assert.Single(
                window.KeyBindings,
                item => item.Gesture == new KeyGesture(Key.Z, KeyModifiers.Control));
            Assert.False(restartBinding.Command!.CanExecute(null));
            Assert.False(undoBinding.Command!.CanExecute(null));

            // 每个真实 Document 至少投影自己的 Restart；只有已有 Undo 业务语义的 9 个游戏
            // 才投影 Undo。逐个关闭可同时证明非活动游戏采用 Hide 且不会残留菜单订阅。
            foreach (var game in ExternalClassicGameUiContext.Games)
            {
                var current = await context.CreateDocumentAsync(game.DocumentTypeId);
                var currentDock = context.GetDocumentDock();
                currentDock.ActiveDockable = current;
                await FlushUiAsync();

                Assert.Single(
                    window.GetLogicalDescendants().OfType<MenuItem>(),
                    item => Equals(item.Header, $"重新开始当前{game.DisplayName}"));
                if (game.HasUndo)
                {
                    var item = Assert.Single(
                        window.GetLogicalDescendants().OfType<MenuItem>(),
                        candidate => Equals(candidate.Header, $"撤销当前{game.DisplayName}"));
                    Assert.False(item.IsEnabled);
                }
                else
                {
                    Assert.DoesNotContain(
                        window.GetLogicalDescendants().OfType<MenuItem>(),
                        item => Equals(item.Header, $"撤销当前{game.DisplayName}"));
                }

                context.Workspace.DockFactory.CloseDockable(current);
                currentDock.ActiveDockable = null;
                await FlushUiAsync();
            }

            var first = await context.CreateDocumentAsync();
            var dock = context.GetDocumentDock();
            dock.ActiveDockable = first;
            await FlushUiAsync();

            var restartItem = Assert.Single(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "重新开始当前五子棋"));
            var undoItem = Assert.Single(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "撤销当前五子棋"));
            Assert.True(restartItem.IsEnabled);
            Assert.False(undoItem.IsEnabled);
            Assert.Same(restartItem.Command, restartBinding.Command);
            Assert.Same(undoItem.Command, undoBinding.Command);

            PlayPosition(first.Model, 0, 0);
            await FlushUiAsync();
            Assert.Equal(1, ReadMoveCount(first.Model));
            Assert.True(Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(first.Model)
                .CanExecute(new CommandId("myavalonia.plugin.classic.game.command.gomoku.undo")));
            Assert.True(FindMenuItem(window, "撤销当前五子棋").IsEnabled);

            var second = await context.CreateDocumentAsync();
            dock.ActiveDockable = second;
            await FlushUiAsync();
            Assert.False(FindMenuItem(window, "撤销当前五子棋").IsEnabled);
            PlayPosition(second.Model, 2, 2);
            await FlushUiAsync();
            Assert.True(FindMenuItem(window, "撤销当前五子棋").IsEnabled);

            dock.ActiveDockable = first;
            await FlushUiAsync();
            Assert.True(FindMenuItem(window, "撤销当前五子棋").IsEnabled);
            window.KeyPressQwerty(
                PhysicalKey.R,
                RawInputModifiers.Control | RawInputModifiers.Shift);
            await FlushUiAsync();
            Assert.Equal(0, ReadMoveCount(first.Model));
            Assert.Equal(1, ReadMoveCount(second.Model));

            dock.ActiveDockable = second;
            await FlushUiAsync();
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            await FlushUiAsync();
            Assert.Equal(0, ReadMoveCount(second.Model));

            context.Workspace.DockFactory.CloseDockable(first);
            dock.ActiveDockable = second;
            PlayPosition(second.Model, 3, 3);
            await FlushUiAsync();
            Assert.True(FindMenuItem(window, "重新开始当前五子棋").IsEnabled);
            Assert.True(FindMenuItem(window, "撤销当前五子棋").IsEnabled);

            dock.ActiveDockable = null;
            await FlushUiAsync();
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "重新开始当前五子棋"));
            Assert.False(restartBinding.Command.CanExecute(null));
            Assert.False(undoBinding.Command.CanExecute(null));
        }
        finally
        {
            window.Close();
        }

        Assert.Empty(window.KeyBindings);
    }

    private static async Task FlushUiAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static MenuItem FindMenuItem(MainWindow window, string header) =>
        Assert.Single(
            window.GetLogicalDescendants().OfType<MenuItem>(),
            item => Equals(item.Header, header));

    /// <summary>仅在 Headless 真实包测试中驱动内部落子入口，不添加生产 public 测试 API。</summary>
    private static void PlayPosition(object model, int row, int column)
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

    private static int ReadMoveCount(object model)
    {
        var viewModel = model.GetType().GetProperty("ViewModel")!.GetValue(model)!;
        return Assert.IsType<int>(
            viewModel.GetType().GetProperty("MoveCount")!.GetValue(viewModel));
    }

    /// <summary>为一个真实外部 ClassicGame 包建立完整但不访问用户数据的 Headless Host 组合。</summary>
    private sealed class ExternalClassicGameUiContext : IDisposable
    {
        private static readonly DocumentTypeId GomokuDocument =
            new("myavalonia.plugin.classic.game.document.gomoku");

        internal static IReadOnlyList<GameExpectation> Games { get; } =
        [
            new("minesweeper", "扫雷", false),
            new("spider-solitaire", "蜘蛛纸牌", true),
            new("reversi", "黑白棋", true),
            new("gomoku", "五子棋", true),
            new("go", "围棋", true),
            new("xiangqi", "中国象棋", true),
            new("2048", "2048", false),
            new("sudoku", "数独", true),
            new("sokoban", "推箱子", true),
            new("tetris", "俄罗斯方块", false),
            new("freecell", "空当接龙", true),
            new("match3", "消消乐", false),
            new("chinese-checkers", "中国跳棋", true),
        ];

        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        internal ExternalClassicGameUiContext(string pluginRoot)
        {
            TempDirectory = Path.Combine(
                Path.GetTempPath(),
                "MyAvaloniaManagement.UiTests",
                $"workbench-command-g8-classic-game-{Guid.NewGuid():N}");
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

        internal Task<ManagedDocumentDockable> CreateDocumentAsync() =>
            CreateDocumentAsync(GomokuDocument);

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

        internal sealed record GameExpectation(
            string Key,
            string DisplayName,
            bool HasUndo)
        {
            internal DocumentTypeId DocumentTypeId { get; } =
                new($"myavalonia.plugin.classic.game.document.{Key}");
        }
    }
}
