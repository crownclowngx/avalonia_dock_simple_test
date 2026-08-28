using System.Reflection;
using System.Runtime.Loader;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>使用 G8 ClassicGame 真实 ZIP 验证 13 个游戏的 Command 3.3 独立消费与多实例 Target。</summary>
/// <remarks>
/// 外部仓库不进入 Host 解决方案，也没有源码 ProjectReference。本测试只接受专项门禁解压后的实体包目录，
/// 再经过生产 Loader、独立 ALC、插件 Provider、不可变 Registry 和真实 Document Scope。用于建立棋局状态的
/// 反射只存在于测试程序集，不向外部插件添加 public 测试接缝，也不参与生产命令发现或路由。
/// </remarks>
public sealed class WorkbenchCommandG8ClassicGameExternalPackageTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G8_CLASSIC_GAME_PLUGIN_ROOT";

    private static readonly PluginId ClassicGameOwner =
        new("myavalonia.plugin.classic.game");
    private static readonly DocumentTypeId GomokuDocument =
        new("myavalonia.plugin.classic.game.document.gomoku");
    private static readonly CommandId RestartCommand =
        new("myavalonia.plugin.classic.game.command.gomoku.restart");
    private static readonly CommandId UndoCommand =
        new("myavalonia.plugin.classic.game.command.gomoku.undo");

    [Fact]
    public async Task 真实ClassicGame包注册二十二条命令并保持十三游戏可用与五子棋多实例隔离()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(configuredRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("ClassicGamePlugin.Plugin", assembly.GetName().Name);
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
            $"workbench-command-g8-classic-game-{Guid.NewGuid():N}");
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
            Assert.Equal(ClassicGameOwner, plugin.Manifest.PluginId);
            Assert.Equal(new Version(1, 1, 0, 0), plugin.Manifest.PluginVersion);
            Assert.Equal(new Version(3, 3, 0, 0), plugin.Manifest.Sdk.MinInclusive);
            Assert.Equal(13, registry.Documents.Count);
            Assert.Equal(22, registry.WorkbenchCommands.Count);
            Assert.Equal(22, registry.MenuCommandContributions.Count);
            Assert.Equal(2, registry.KeyBindingContributions.Count);

            var commands = registry.WorkbenchCommands
                .OrderBy(item => item.Descriptor.CommandId.Value)
                .ToArray();
            Assert.All(
                commands,
                command =>
                {
                    Assert.Equal(ClassicGameOwner, command.OwnerId);
                    Assert.Contains(
                        registry.Documents,
                        item => item.Descriptor.DocumentTypeId == command.TargetDocumentTypeId);
                });
            Assert.All(
                registry.MenuCommandContributions,
                contribution =>
                {
                    Assert.Equal(
                        WorkbenchMenuLocations.ToolsShared,
                        contribution.Descriptor.LocationId);
                    Assert.StartsWith(
                        "classic-game.",
                        contribution.Descriptor.Group,
                        StringComparison.Ordinal);
                    Assert.True(contribution.Descriptor.Order is 0 or 10);
                    Assert.Equal(
                        MenuCommandTargetUnavailableBehavior.Hide,
                        contribution.Descriptor.TargetUnavailableBehavior);
                });
            Assert.Contains(
                registry.KeyBindingContributions,
                item => item.Descriptor.CommandId == RestartCommand &&
                    item.Descriptor.Key == Key.R &&
                    item.Descriptor.Modifiers == (KeyModifiers.Control | KeyModifiers.Shift));
            Assert.Contains(
                registry.KeyBindingContributions,
                item => item.Descriptor.CommandId == UndoCommand &&
                    item.Descriptor.Key == Key.Z &&
                    item.Descriptor.Modifiers == KeyModifiers.Control);

            // 先逐一通过生产 Scope 构造并执行 13 个真实 Document 的 Restart。只有确实已有 Undo
            // 语义的 9 个游戏才应出现在 Catalog；新局状态都应不可撤销。
            var undoGames = new HashSet<string>(StringComparer.Ordinal)
            {
                "spider-solitaire", "reversi", "gomoku", "go", "xiangqi",
                "sudoku", "sokoban", "freecell", "chinese-checkers",
            };
            var scopeManager = pluginProviders.GetDocumentScopeManager(ClassicGameOwner);
            foreach (var registeredDocument in registry.Documents)
            {
                var gameKey = registeredDocument.Descriptor.DocumentTypeId.Value[
                    "myavalonia.plugin.classic.game.document.".Length..];
                var restart = new CommandId(
                    $"myavalonia.plugin.classic.game.command.{gameKey}.restart");
                var undo = new CommandId(
                    $"myavalonia.plugin.classic.game.command.{gameKey}.undo");
                Assert.Contains(commands, item =>
                    item.Descriptor.CommandId == restart &&
                    item.TargetDocumentTypeId == registeredDocument.Descriptor.DocumentTypeId);
                Assert.Equal(
                    undoGames.Contains(gameKey),
                    commands.Any(item =>
                        item.Descriptor.CommandId == undo &&
                        item.TargetDocumentTypeId == registeredDocument.Descriptor.DocumentTypeId));

                using var scope = scopeManager.CreateDocument(registeredDocument.ModelType);
                await scope.Model.InitializeAsync(
                    new NewDocumentActivation(registeredDocument.Descriptor.DisplayName),
                    scope.ClosingToken);
                var target = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(scope.Model);
                Assert.True(target.CanExecute(restart));
                await target.ExecuteAsync(restart, CancellationToken.None);
                if (undoGames.Contains(gameKey))
                {
                    Assert.False(target.CanExecute(undo));
                }
            }

            var document = Assert.Single(
                registry.Documents,
                item => item.Descriptor.DocumentTypeId == GomokuDocument);
            using var first = scopeManager.CreateDocument(document.ModelType);
            using var second = scopeManager.CreateDocument(document.ModelType);
            await first.Model.InitializeAsync(
                new NewDocumentActivation("五子棋 A"),
                first.ClosingToken);
            await second.Model.InitializeAsync(
                new NewDocumentActivation("五子棋 B"),
                second.ClosingToken);
            var firstTarget = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(first.Model);
            var secondTarget = Assert.IsAssignableFrom<IWorkbenchDocumentCommandTarget>(second.Model);

            Assert.True(firstTarget.CanExecute(RestartCommand));
            Assert.False(firstTarget.CanExecute(UndoCommand));
            Assert.False(secondTarget.CanExecute(UndoCommand));

            PlayPosition(first.Model, 0, 0);
            Assert.True(firstTarget.CanExecute(UndoCommand));
            Assert.False(secondTarget.CanExecute(UndoCommand));
            Assert.Equal(1, ReadIntProperty(first.Model, "MoveCount"));
            Assert.Equal(0, ReadIntProperty(second.Model, "MoveCount"));

            await firstTarget.ExecuteAsync(UndoCommand, CancellationToken.None);
            Assert.Equal(0, ReadIntProperty(first.Model, "MoveCount"));
            PlayPosition(first.Model, 1, 1);
            PlayPosition(second.Model, 2, 2);
            await firstTarget.ExecuteAsync(RestartCommand, CancellationToken.None);
            Assert.Equal(0, ReadIntProperty(first.Model, "MoveCount"));
            Assert.Equal(1, ReadIntProperty(second.Model, "MoveCount"));

            first.Dispose();
            Assert.False(firstTarget.CanExecute(RestartCommand));
            Assert.False(firstTarget.CanExecute(UndoCommand));
            Assert.True(secondTarget.CanExecute(RestartCommand));
            Assert.True(secondTarget.CanExecute(UndoCommand));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    /// <summary>仅在真实包测试中驱动五子棋内部落子入口，不扩大外部插件生产 API。</summary>
    private static void PlayPosition(IPluginDocument model, int row, int column)
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

    private static int ReadIntProperty(IPluginDocument model, string propertyName)
    {
        var viewModel = model.GetType().GetProperty("ViewModel")!.GetValue(model)!;
        return Assert.IsType<int>(viewModel.GetType().GetProperty(propertyName)!.GetValue(viewModel));
    }
}
