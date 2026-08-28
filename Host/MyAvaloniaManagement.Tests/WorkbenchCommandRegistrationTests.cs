using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 G1 Command 声明的局部 Seal、全局冲突隔离和不可变 Registry。</summary>
public sealed class WorkbenchCommandRegistrationTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.command-test");

    [Fact]
    public void 任意声明顺序均在Seal后冻结且不增加Command服务根()
    {
        var builder = new PluginRegistryBuilder();
        var services = new ServiceCollection();
        var registration = new PluginRegistration(Owner, services, builder);
        var command = Command(Owner, "save");

        registration.AddMenuCommandContribution(new MenuCommandContributionDescriptor(
            Placement(Owner, "menu-save"),
            command.CommandId,
            WorkbenchMenuLocations.FileShared,
            "document",
            10,
            MenuCommandTargetUnavailableBehavior.Disable));
        registration.AddKeyBindingContribution(new KeyBindingContributionDescriptor(
            Placement(Owner, "key-save"),
            command.CommandId,
            Key.S,
            KeyModifiers.Control));
        registration.AddDocumentCommand(command, Document(Owner));
        registration.AddDocument<TestDocument, EmptyView>(DocumentDescriptor(Owner));

        registration.Seal();
        var hostRoots = registration.GetHostOwnedServiceDescriptors();
        var root = Assert.Single(hostRoots);
        Assert.Equal(typeof(TestDocument), root.ServiceType);
        Assert.Equal(ServiceLifetime.Scoped, root.Lifetime);

        var registry = builder.Build(catalog: null);
        var frozenCommand = Assert.Single(registry.WorkbenchCommands);
        Assert.Equal(Owner, frozenCommand.OwnerId);
        Assert.Same(command, frozenCommand.Descriptor);
        Assert.Equal(Document(Owner), frozenCommand.TargetDocumentTypeId);
        Assert.Equal(MenuCommandTargetUnavailableBehavior.Disable,
            Assert.Single(registry.MenuCommandContributions)
                .Descriptor.TargetUnavailableBehavior);
        Assert.Equal(KeyModifiers.Control,
            Assert.Single(registry.KeyBindingContributions).Descriptor.Modifiers);
        Assert.Contains(Owner, registry.DeclaredOwnerIds);
        Assert.True(registry.TryGetWorkbenchCommand(command.CommandId, out var queried));
        Assert.Same(frozenCommand, queried);
    }

    [Fact]
    public void Seal区分CommandTarget和Placement的所有权越界()
    {
        var registration = Registration(Owner, out _);
        registration.AddDocument<TestDocument, EmptyView>(DocumentDescriptor(Owner));
        registration.AddDocumentCommand(
            new CommandDescriptor(
                new CommandId("myavalonia.plugin.other.command.run"), "越权命令", "测试"),
            new DocumentTypeId("myavalonia.plugin.other.document.sample"));
        registration.AddMenuCommandContribution(new MenuCommandContributionDescriptor(
            new CommandPlacementId("myavalonia.plugin.other.command-placement.menu"),
            new CommandId("myavalonia.plugin.other.command.run"),
            WorkbenchMenuLocations.ToolsShared,
            "",
            0));

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandIdOwnerMismatch);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandTargetDocumentOwnerMismatch);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandPlacementIdOwnerMismatch);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandPlacementCommandOwnerMismatch);
    }

    [Fact]
    public void Seal拒绝未登记Target未知Command和未开放菜单位置()
    {
        var registration = Registration(Owner, out _);
        registration.AddDocumentCommand(Command(Owner, "run"),
            new DocumentTypeId($"{Owner.Value}.document.missing"));
        registration.AddMenuCommandContribution(new MenuCommandContributionDescriptor(
            Placement(Owner, "menu-missing"),
            new CommandId($"{Owner.Value}.command.missing"),
            new MenuLocationId("myavalonia.host.menu.private"),
            "",
            0));

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandTargetDocumentNotRegistered);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandPlacementCommandNotRegistered);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchMenuLocationUnsupported);
    }

    [Fact]
    public void Seal拒绝重复Command跨类型Placement和本插件Gesture()
    {
        var registration = Registration(Owner, out _);
        registration.AddDocument<TestDocument, EmptyView>(DocumentDescriptor(Owner));
        var command = Command(Owner, "run");
        registration.AddDocumentCommand(command, Document(Owner));
        registration.AddDocumentCommand(
            new CommandDescriptor(command.CommandId, "重复", "测试"),
            Document(Owner));
        var sharedPlacement = Placement(Owner, "shared");
        registration.AddMenuCommandContribution(new MenuCommandContributionDescriptor(
            sharedPlacement, command.CommandId, WorkbenchMenuLocations.ToolsShared, "", 0));
        registration.AddKeyBindingContribution(new KeyBindingContributionDescriptor(
            sharedPlacement, command.CommandId, Key.K, KeyModifiers.Control));
        registration.AddKeyBindingContribution(new KeyBindingContributionDescriptor(
            Placement(Owner, "key-two"), command.CommandId, Key.K, KeyModifiers.Control));

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandIdDuplicate);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandPlacementIdDuplicate);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == HostDiagnosticCodes.WorkbenchKeyGestureDuplicate);
    }

    [Fact]
    public void Seal和Build后均不能继续写入()
    {
        var registration = Registration(Owner, out var builder);
        registration.AddDocument<TestDocument, EmptyView>(DocumentDescriptor(Owner));
        registration.AddDocumentCommand(Command(Owner, "run"), Document(Owner));
        registration.Seal();

        Assert.Throws<InvalidOperationException>(() => registration.AddDocumentCommand(
            Command(Owner, "late"), Document(Owner)));
        builder.Build(catalog: null);
        Assert.Throws<InvalidOperationException>(() => builder.AddDocumentCommand(
            Owner, Command(Owner, "after-build"), Document(Owner)));
    }

    [Fact]
    public void 全局CommandId冲突整体排除两个Owner及其全部贡献()
    {
        var builder = new PluginRegistryBuilder();
        var first = new PluginId("myavalonia.plugin.first-command");
        var second = new PluginId("myavalonia.plugin.second-command");
        AddDocument(builder, first, typeof(FirstDocument));
        AddDocument(builder, second, typeof(SecondDocument));
        var shared = new CommandId("shared.command.collision");
        builder.AddDocumentCommand(first,
            new CommandDescriptor(shared, "第一", "测试"), Document(first));
        builder.AddDocumentCommand(second,
            new CommandDescriptor(shared, "第二", "测试"), Document(second));

        var registry = builder.Build(catalog: null);

        Assert.Empty(registry.Documents);
        Assert.Empty(registry.WorkbenchCommands);
        Assert.Empty(registry.DeclaredOwnerIds);
    }

    [Fact]
    public void 全局PlacementId冲突整体排除两个Owner()
    {
        var builder = new PluginRegistryBuilder();
        var first = new PluginId("myavalonia.plugin.first-placement");
        var second = new PluginId("myavalonia.plugin.second-placement");
        AddDocumentAndCommand(builder, first, typeof(FirstDocument));
        AddDocumentAndCommand(builder, second, typeof(SecondDocument));
        var shared = new CommandPlacementId("shared.command-placement.collision");
        builder.AddMenuCommandContribution(first, new MenuCommandContributionDescriptor(
            shared, Command(first, "run").CommandId, WorkbenchMenuLocations.ToolsShared, "", 0));
        builder.AddKeyBindingContribution(second, new KeyBindingContributionDescriptor(
            shared, Command(second, "run").CommandId, Key.K, KeyModifiers.Control));

        var registry = builder.Build(catalog: null);

        Assert.Empty(registry.Documents);
        Assert.Empty(registry.WorkbenchCommands);
        Assert.Empty(registry.MenuCommandContributions);
        Assert.Empty(registry.KeyBindingContributions);
    }

    [Fact]
    public void 跨插件相同Gesture在G1保留两份冻结事实()
    {
        var builder = new PluginRegistryBuilder();
        var first = new PluginId("myavalonia.plugin.first-gesture");
        var second = new PluginId("myavalonia.plugin.second-gesture");
        AddDocumentAndCommand(builder, first, typeof(FirstDocument));
        AddDocumentAndCommand(builder, second, typeof(SecondDocument));
        builder.AddKeyBindingContribution(first, new KeyBindingContributionDescriptor(
            Placement(first, "key"), Command(first, "run").CommandId,
            Key.K, KeyModifiers.Control));
        builder.AddKeyBindingContribution(second, new KeyBindingContributionDescriptor(
            Placement(second, "key"), Command(second, "run").CommandId,
            Key.K, KeyModifiers.Control));

        var registry = builder.Build(catalog: null);

        Assert.Equal(2, registry.WorkbenchCommands.Count);
        Assert.Equal(2, registry.KeyBindingContributions.Count);
        Assert.Equal(2, registry.Documents.Count);
    }

    [Fact]
    public void 全局纵深防线对缺失局部Seal的冲突仍生成稳定诊断()
    {
        var builder = new PluginRegistryBuilder();
        var first = new PluginId("myavalonia.plugin.first-defense");
        var second = new PluginId("myavalonia.plugin.second-defense");
        var sharedCommand = new CommandId("shared.command.defense");
        var sharedPlacement = new CommandPlacementId("shared.command-placement.defense");
        builder.AddDocumentCommand(first,
            new CommandDescriptor(sharedCommand, "第一", "测试"), Document(first));
        builder.AddDocumentCommand(second,
            new CommandDescriptor(sharedCommand, "第二", "测试"), Document(second));
        builder.AddMenuCommandContribution(first, new MenuCommandContributionDescriptor(
            sharedPlacement, sharedCommand, WorkbenchMenuLocations.FileShared, "", 0));
        builder.AddKeyBindingContribution(second, new KeyBindingContributionDescriptor(
            sharedPlacement, sharedCommand, Key.K, KeyModifiers.Control));
        var diagnostics = new RecordingDiagnosticSink();

        var registry = builder.Build(catalog: null, diagnosticSink: diagnostics);

        Assert.Empty(registry.WorkbenchCommands);
        Assert.Equal(4, diagnostics.Drafts.Count);
        Assert.Equal(2, diagnostics.Drafts.Count(item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandIdDuplicate));
        Assert.Equal(2, diagnostics.Drafts.Count(item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandPlacementIdDuplicate));
        Assert.All(diagnostics.Drafts, draft => Assert.NotNull(draft.AssemblyName));
    }

    [Fact]
    public void Registry构造和读取均执行防御性复制()
    {
        var first = new PluginWorkbenchCommandRegistration(
            Owner, Command(Owner, "first"), Document(Owner));
        var second = new PluginWorkbenchCommandRegistration(
            Owner, Command(Owner, "second"), Document(Owner));
        var input = new[] { first };
        var registry = new PluginRegistry(
            [], [], [], [], workbenchCommands: input);

        input[0] = second;
        var returned = Assert.IsType<PluginWorkbenchCommandRegistration[]>(
            registry.WorkbenchCommands);
        returned[0] = second;

        Assert.Same(first, Assert.Single(registry.WorkbenchCommands));
        Assert.False(registry.TryGetWorkbenchCommand(second.Descriptor.CommandId, out _));
        Assert.Equal(
            ["Descriptor", "OwnerId", "TargetDocumentTypeId"],
            typeof(PluginWorkbenchCommandRegistration).GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.DoesNotContain(
            typeof(PluginWorkbenchCommandRegistration).GetProperties(),
            property => property.PropertyType.Name.Contains("Provider", StringComparison.Ordinal) ||
                        property.PropertyType.Name.Contains("Scope", StringComparison.Ordinal) ||
                        property.PropertyType.Name.Contains("ICommand", StringComparison.Ordinal) ||
                        typeof(Delegate).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public void 旧插件零声明Registry的Command集合为空()
    {
        var registration = Registration(Owner, out var builder);
        registration.AddDocument<TestDocument, EmptyView>(DocumentDescriptor(Owner));
        registration.Seal();

        var registry = builder.Build(catalog: null);

        Assert.Single(registry.Documents);
        Assert.Empty(registry.WorkbenchCommands);
        Assert.Empty(registry.MenuCommandContributions);
        Assert.Empty(registry.KeyBindingContributions);
    }

    private static PluginRegistration Registration(
        PluginId owner,
        out PluginRegistryBuilder builder)
    {
        builder = new PluginRegistryBuilder();
        return new PluginRegistration(owner, new ServiceCollection(), builder);
    }

    private static CommandDescriptor Command(PluginId owner, string suffix) =>
        new(new CommandId($"{owner.Value}.command.{suffix}"), "测试命令", "G1 注册测试");

    private static CommandPlacementId Placement(PluginId owner, string suffix) =>
        new($"{owner.Value}.command-placement.{suffix}");

    private static DocumentTypeId Document(PluginId owner) =>
        new($"{owner.Value}.document.sample");

    private static DocumentDescriptor DocumentDescriptor(PluginId owner) =>
        new(Document(owner), "测试 Document", "G1 注册测试", "测试");

    private static void AddDocument(
        PluginRegistryBuilder builder,
        PluginId owner,
        Type modelType) =>
        builder.AddDocument(
            owner,
            DocumentDescriptor(owner),
            modelType,
            typeof(EmptyView),
            static () => new EmptyView(),
            false);

    private static void AddDocumentAndCommand(
        PluginRegistryBuilder builder,
        PluginId owner,
        Type modelType)
    {
        AddDocument(builder, owner, modelType);
        builder.AddDocumentCommand(owner, Command(owner, "run"), Document(owner));
    }

    private class TestDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivation activation,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FirstDocument : TestDocument;
    private sealed class SecondDocument : TestDocument;
    private sealed class EmptyView : UserControl;

    private sealed class RecordingDiagnosticSink : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Drafts { get; } = [];

        public HostDiagnosticRecord Report(HostDiagnosticDraft draft)
        {
            Drafts.Add(draft);
            return new HostDiagnosticRecord
            {
                SessionId = Guid.Empty,
                Sequence = Drafts.Count,
                TimestampUtc = DateTimeOffset.UnixEpoch,
                Code = draft.Code,
                Severity = HostDiagnosticSeverity.Error,
                Phase = draft.Phase,
                Disposition = HostDiagnosticDisposition.Continue,
                UserMessage = "测试诊断",
            };
        }
    }
}
