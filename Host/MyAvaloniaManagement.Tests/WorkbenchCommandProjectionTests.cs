using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 G5 菜单排序、状态政策、快捷键冲突与 owner 可用性投影。</summary>
public sealed class WorkbenchCommandProjectionTests
{
    private static readonly PluginId Alpha = new("myavalonia.plugin.g5-alpha");
    private static readonly PluginId Beta = new("myavalonia.plugin.g5-beta");
    private static readonly DocumentTypeId AlphaDocument =
        new("myavalonia.plugin.g5-alpha.document.main");
    private static readonly DocumentTypeId BetaDocument =
        new("myavalonia.plugin.g5-beta.document.main");

    [Fact]
    public void 四个共享位置按组顺序稳定投影且分隔符没有悬空()
    {
        using var first = CreateCatalogContext(reverseRegistrationOrder: false);
        using var second = CreateCatalogContext(reverseRegistrationOrder: true);
        var firstPresentation = first.Provider.GetRequiredService<WorkbenchCommandPresentation>();
        var secondPresentation = second.Provider.GetRequiredService<WorkbenchCommandPresentation>();

        Assert.Equal(
            ["打开…", "保存", "|", "Alpha File"],
            Snapshot(firstPresentation.Menu, WorkbenchMenuLocations.FileShared));
        Assert.Equal(
            ["|", "Alpha View"],
            Snapshot(firstPresentation.Menu, WorkbenchMenuLocations.ViewShared));
        Assert.Equal(
            ["Alpha Empty", "|", "Alpha Edit A", "Alpha Edit Z", "|", "Beta Workflow"],
            Snapshot(firstPresentation.Menu, WorkbenchMenuLocations.ToolsShared));
        Assert.Equal(
            ["Beta Help"],
            Snapshot(firstPresentation.Menu, WorkbenchMenuLocations.HelpShared));

        foreach (var location in AllLocations())
        {
            var expected = Snapshot(firstPresentation.Menu, location);
            Assert.Equal(expected, Snapshot(secondPresentation.Menu, location));
            Assert.False(expected.FirstOrDefault() == "|" &&
                         location != WorkbenchMenuLocations.ViewShared);
            Assert.False(expected.LastOrDefault() == "|");
            Assert.DoesNotContain(expected.Zip(expected.Skip(1)), pair =>
                pair.First == "|" && pair.Second == "|");
        }
    }

    [Fact]
    public async Task Hide和Disable随当前Target与CanExecute使用同一状态事实()
    {
        using var context = CreateTargetContext();
        var presentation = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
        _ = context.CreateMainWindowViewModel();
        var initial = presentation.Menu.GetItems(WorkbenchMenuLocations.ToolsShared)
            .OfType<WorkbenchMenuCommandProjectionEntry>()
            .ToArray();

        var disabledOnly = Assert.Single(initial);
        Assert.EndsWith("menu-disable", disabledOnly.PlacementId.Value, StringComparison.Ordinal);
        Assert.Equal("Target Command", disabledOnly.Header);
        Assert.False(disabledOnly.Command.IsEnabled);

        var adapter = await context.Workspace.CreateAndPublishDocumentAsync(
            WorkbenchCommandG3TestContext.DocumentType,
            new NewDocumentActivation("G5 Target"));
        GetDocumentDock(context).ActiveDockable = adapter;
        var active = presentation.Menu.GetItems(WorkbenchMenuLocations.ToolsShared)
            .OfType<WorkbenchMenuCommandProjectionEntry>()
            .ToArray();

        Assert.Equal(2, active.Length);
        Assert.Contains(active, item => item.PlacementId.Value.EndsWith(
            "menu-hide", StringComparison.Ordinal));
        Assert.Contains(active, item => item.PlacementId.Value.EndsWith(
            "menu-disable", StringComparison.Ordinal));
        Assert.All(active, item => Assert.True(item.Command.IsEnabled));

        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        target.AllowExecute = false;
        target.RaiseStateChanged(WorkbenchCommandG3TestContext.Command);
        var businessDisabled = presentation.Menu.GetItems(WorkbenchMenuLocations.ToolsShared)
            .OfType<WorkbenchMenuCommandProjectionEntry>()
            .ToArray();
        Assert.Equal(2, businessDisabled.Length);
        Assert.All(businessDisabled, item => Assert.False(item.Command.IsEnabled));
    }

    [Fact]
    public void Host快捷键优先且跨插件冲突双禁用但菜单命令保留()
    {
        var diagnostics = new RecordingWorkbenchCommandDiagnosticSink();
        using var context = CreateCatalogContext(
            reverseRegistrationOrder: false,
            diagnostics);
        var presentation = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
        var bindings = presentation.KeyBindings.Items;

        Assert.Collection(
            bindings,
            host =>
            {
                Assert.Equal(HostWorkbenchCommandIds.SaveDocument, host.CommandId);
                Assert.Equal(Key.S, host.Key);
                Assert.Equal(KeyModifiers.Control, host.Modifiers);
            },
            plugin =>
            {
                Assert.Equal(new CommandId($"{Alpha.Value}.command.shortcut-active"), plugin.CommandId);
                Assert.Equal(Key.R, plugin.Key);
                Assert.Equal(KeyModifiers.Control, plugin.Modifiers);
            });
        Assert.Equal(3, diagnostics.Drafts.Count(item =>
            item.Code == HostDiagnosticCodes.WorkbenchKeyGestureConflict));
        Assert.All(
            diagnostics.Drafts.Where(item =>
                item.Code == HostDiagnosticCodes.WorkbenchKeyGestureConflict),
            item =>
            {
                Assert.NotNull(item.PluginId);
                Assert.StartsWith(item.PluginId!.Value, item.StableId, StringComparison.Ordinal);
                Assert.Null(item.Exception);
            });

        var menuHeaders = AllLocations()
            .SelectMany(location => Snapshot(presentation.Menu, location))
            .ToArray();
        Assert.Contains("Alpha File", menuHeaders);
        Assert.Contains("Beta Workflow", menuHeaders);
    }

    [Fact]
    public void Owner不可用时菜单和快捷键同步移除并在恢复后重建()
    {
        using var context = CreateCatalogContext(reverseRegistrationOrder: false);
        var presentation = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
        var states = context.Provider.GetRequiredService<PluginLifecycleStateStore>();
        var availability = context.Provider.GetRequiredService<PluginAvailabilityReadModel>();
        var availabilityChanges = 0;
        availability.AvailabilityChanged += (_, args) =>
        {
            Assert.Equal(Alpha, args.PluginId);
            availabilityChanges++;
        };

        Assert.Contains(
            "Alpha Empty",
            Snapshot(presentation.Menu, WorkbenchMenuLocations.ToolsShared));
        Assert.Contains(
            presentation.KeyBindings.Items,
            item => item.CommandId.Value == $"{Alpha.Value}.command.shortcut-active");

        states.SetState(new PluginLifecycleState(Alpha, PluginLifecycleStatus.NotStarted));

        Assert.DoesNotContain(
            "Alpha Empty",
            Snapshot(presentation.Menu, WorkbenchMenuLocations.ToolsShared));
        Assert.DoesNotContain(
            presentation.KeyBindings.Items,
            item => item.CommandId.Value.StartsWith(Alpha.Value, StringComparison.Ordinal));
        Assert.Equal(1, availabilityChanges);

        states.SetState(new PluginLifecycleState(Alpha, PluginLifecycleStatus.Ready));

        Assert.Contains(
            "Alpha Empty",
            Snapshot(presentation.Menu, WorkbenchMenuLocations.ToolsShared));
        Assert.Contains(
            presentation.KeyBindings.Items,
            item => item.CommandId.Value == $"{Alpha.Value}.command.shortcut-active");
        Assert.Equal(2, availabilityChanges);
    }

    [Fact]
    public void BeginShutdown逐Owner撤回且异常观察者不阻断其他通知()
    {
        using var context = CreateCatalogContext(reverseRegistrationOrder: false);
        var states = context.Provider.GetRequiredService<PluginLifecycleStateStore>();
        var availability = context.Provider.GetRequiredService<PluginAvailabilityReadModel>();
        var observed = new List<PluginId>();
        availability.AvailabilityChanged += (_, _) =>
            throw new InvalidOperationException("测试观察者失败");
        availability.AvailabilityChanged += (_, args) => observed.Add(args.PluginId);

        states.BeginShutdown();
        states.BeginShutdown();
        states.SetState(new PluginLifecycleState(Alpha, PluginLifecycleStatus.Ready));

        Assert.Equal([Alpha, Beta], observed.OrderBy(item => item.Value, StringComparer.Ordinal));
        Assert.False(availability.IsAvailable(Alpha));
        Assert.False(availability.IsAvailable(Beta));
    }

    [Fact]
    public void CommandStore拒绝未知和释放后访问且唯一缓存只释放一次()
    {
        using var context = CreateCatalogContext(reverseRegistrationOrder: false);
        var store = new WorkbenchPresentationCommandStore(
            context.Provider.GetRequiredService<WorkbenchCommandCatalog>(),
            context.Provider.GetRequiredService<Business.Commands.State.WorkbenchCommandStateQuery>(),
            context.Provider.GetRequiredService<WorkbenchCommandExecutor>(),
            Dispatcher.UIThread);

        var first = store.Get(HostWorkbenchCommandIds.OpenDocument);
        Assert.Same(first, store.Get(HostWorkbenchCommandIds.OpenDocument));
        Assert.Throws<InvalidOperationException>(() =>
            store.Get(new CommandId("myavalonia.host.command.unknown")));

        store.Dispose();
        store.Dispose();
        Assert.False(first.CanExecute(null));
        Assert.Throws<ObjectDisposedException>(() =>
            store.Get(HostWorkbenchCommandIds.OpenDocument));
    }

    [Fact]
    public void Menu和KeyProjection重复释放后拒绝读取且迟到可用性通知安全()
    {
        using var context = CreateCatalogContext(reverseRegistrationOrder: false);
        var presentation = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
        var menu = Assert.IsType<WorkbenchMenuProjection>(presentation.Menu);
        var keys = Assert.IsType<WorkbenchKeyBindingProjection>(presentation.KeyBindings);
        var states = context.Provider.GetRequiredService<PluginLifecycleStateStore>();

        menu.Dispose();
        menu.Dispose();
        keys.Dispose();
        keys.Dispose();
        states.SetState(new PluginLifecycleState(Alpha, PluginLifecycleStatus.NotStarted));

        Assert.Throws<ObjectDisposedException>(() =>
            menu.GetItems(WorkbenchMenuLocations.ToolsShared));
        Assert.Throws<ObjectDisposedException>(() => _ = keys.Items);
    }

    private static TestHostContext CreateTargetContext() => new(
        configureServices: services =>
        {
            services.AddSingleton<WorkbenchCommandG3Probe>();
            services.AddScoped<WorkbenchCommandG3Document>();
        },
        configureContributions: (_, builder) =>
        {
            builder.AddDocument(
                TestPluginIds.Owner,
                new DocumentDescriptor(
                    WorkbenchCommandG3TestContext.DocumentType,
                    "G5 Target",
                    "验证 Hide 与 Disable",
                    "测试"),
                typeof(WorkbenchCommandG3Document),
                typeof(UserControl),
                static () => new UserControl(),
                isPersistable: false);
            builder.AddDocumentCommand(
                TestPluginIds.Owner,
                new CommandDescriptor(
                    WorkbenchCommandG3TestContext.Command,
                    "Target Command",
                    "验证 Target 状态"),
                WorkbenchCommandG3TestContext.DocumentType);
            builder.AddMenuCommandContribution(
                TestPluginIds.Owner,
                new MenuCommandContributionDescriptor(
                    Placement(TestPluginIds.Owner, "menu-hide"),
                    WorkbenchCommandG3TestContext.Command,
                    WorkbenchMenuLocations.ToolsShared,
                    "target",
                    0,
                    MenuCommandTargetUnavailableBehavior.Hide));
            builder.AddMenuCommandContribution(
                TestPluginIds.Owner,
                new MenuCommandContributionDescriptor(
                    Placement(TestPluginIds.Owner, "menu-disable"),
                    WorkbenchCommandG3TestContext.Command,
                    WorkbenchMenuLocations.ToolsShared,
                    "target",
                    10,
                    MenuCommandTargetUnavailableBehavior.Disable));
        },
        useProductionWorkbenchPresentation: true);

    private static TestHostContext CreateCatalogContext(
        bool reverseRegistrationOrder,
        RecordingWorkbenchCommandDiagnosticSink? diagnostics = null) => new(
        configureServices: services =>
        {
            services.AddScoped<ProjectionAlphaDocument>();
            services.AddScoped<ProjectionBetaDocument>();
            if (diagnostics is not null)
            {
                services.AddSingleton<IHostDiagnosticSink>(diagnostics);
            }
        },
        configureContributions: (_, builder) =>
        {
            Action alpha = () => AddAlpha(builder);
            Action beta = () => AddBeta(builder);
            if (reverseRegistrationOrder)
            {
                beta();
                alpha();
            }
            else
            {
                alpha();
                beta();
            }
        },
        useProductionWorkbenchPresentation: true);

    private static void AddAlpha(PluginRegistryBuilder builder)
    {
        builder.AddDocument(
            Alpha,
            new DocumentDescriptor(AlphaDocument, "Alpha", "G5 Alpha", "测试"),
            typeof(ProjectionAlphaDocument),
            typeof(UserControl),
            static () => new UserControl(),
            isPersistable: false);
        AddCommandWithMenu(builder, Alpha, AlphaDocument, "file", "Alpha File",
            WorkbenchMenuLocations.FileShared, "alpha", 0);
        AddCommandWithMenu(builder, Alpha, AlphaDocument, "view", "Alpha View",
            WorkbenchMenuLocations.ViewShared, "alpha", 0);
        AddCommandWithMenu(builder, Alpha, AlphaDocument, "tools-empty", "Alpha Empty",
            WorkbenchMenuLocations.ToolsShared, string.Empty, 50);
        AddCommandWithMenu(builder, Alpha, AlphaDocument, "tools-edit-z", "Alpha Edit Z",
            WorkbenchMenuLocations.ToolsShared, "editing", 20);
        AddCommandWithMenu(builder, Alpha, AlphaDocument, "tools-edit-a", "Alpha Edit A",
            WorkbenchMenuLocations.ToolsShared, "editing", 10);
        AddCommandWithKey(builder, Alpha, AlphaDocument, "shortcut-host", Key.S);
        AddCommandWithKey(builder, Alpha, AlphaDocument, "shortcut-shared", Key.K);
        AddCommandWithKey(builder, Alpha, AlphaDocument, "shortcut-active", Key.R);
    }

    private static void AddBeta(PluginRegistryBuilder builder)
    {
        builder.AddDocument(
            Beta,
            new DocumentDescriptor(BetaDocument, "Beta", "G5 Beta", "测试"),
            typeof(ProjectionBetaDocument),
            typeof(UserControl),
            static () => new UserControl(),
            isPersistable: false);
        AddCommandWithMenu(builder, Beta, BetaDocument, "workflow", "Beta Workflow",
            WorkbenchMenuLocations.ToolsShared, "workflow", 0);
        AddCommandWithMenu(builder, Beta, BetaDocument, "help", "Beta Help",
            WorkbenchMenuLocations.HelpShared, string.Empty, 0);
        AddCommandWithKey(builder, Beta, BetaDocument, "shortcut-shared", Key.K);
    }

    private static void AddCommandWithMenu(
        PluginRegistryBuilder builder,
        PluginId owner,
        DocumentTypeId documentTypeId,
        string suffix,
        string displayName,
        MenuLocationId locationId,
        string group,
        int order)
    {
        var commandId = new CommandId($"{owner.Value}.command.{suffix}");
        builder.AddDocumentCommand(
            owner,
            new CommandDescriptor(commandId, displayName, "G5 菜单投影测试"),
            documentTypeId);
        builder.AddMenuCommandContribution(
            owner,
            new MenuCommandContributionDescriptor(
                Placement(owner, $"menu-{suffix}"),
                commandId,
                locationId,
                group,
                order,
                MenuCommandTargetUnavailableBehavior.Disable));
    }

    private static void AddCommandWithKey(
        PluginRegistryBuilder builder,
        PluginId owner,
        DocumentTypeId documentTypeId,
        string suffix,
        Key key)
    {
        var commandId = new CommandId($"{owner.Value}.command.{suffix}");
        builder.AddDocumentCommand(
            owner,
            new CommandDescriptor(commandId, suffix, "G5 快捷键投影测试"),
            documentTypeId);
        builder.AddKeyBindingContribution(
            owner,
            new KeyBindingContributionDescriptor(
                Placement(owner, $"key-{suffix}"),
                commandId,
                key,
                KeyModifiers.Control));
    }

    private static CommandPlacementId Placement(PluginId owner, string suffix) =>
        new($"{owner.Value}.command-placement.{suffix}");

    private static string[] Snapshot(
        IWorkbenchMenuProjection projection,
        MenuLocationId locationId) => projection.GetItems(locationId)
        .Select(item => item switch
        {
            WorkbenchMenuCommandProjectionEntry command => command.Header,
            WorkbenchMenuSeparatorProjectionEntry => "|",
            _ => "?",
        })
        .ToArray();

    private static MenuLocationId[] AllLocations() =>
    [
        WorkbenchMenuLocations.FileShared,
        WorkbenchMenuLocations.ViewShared,
        WorkbenchMenuLocations.ToolsShared,
        WorkbenchMenuLocations.HelpShared,
    ];

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(context.Workspace.DockFactory.GetDockable<IDocumentDock>(
            Business.Layout.DockLayoutIds.Documents));

    private sealed class ProjectionAlphaDocument : ProjectionDocument;

    private sealed class ProjectionBetaDocument : ProjectionDocument;

    private abstract class ProjectionDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation => new("G5 Projection");

        public event EventHandler? PresentationChanged
        {
            add { }
            remove { }
        }

        public ValueTask InitializeAsync(
            DocumentActivation context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
