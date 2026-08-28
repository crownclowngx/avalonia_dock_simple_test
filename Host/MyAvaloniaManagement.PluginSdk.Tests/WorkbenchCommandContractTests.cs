using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>验证 G1 Workbench Command public 契约、描述符和兼容注册扩展。</summary>
public sealed class WorkbenchCommandContractTests
{
    [Fact]
    public void 三类身份共享严格词法规约但保持独立类型()
    {
        var value = "myavalonia.plugin.sample.command.open-file";
        var commandId = CommandId.Parse(value);
        var placementId = CommandPlacementId.Parse(value);
        var locationId = MenuLocationId.Parse(value);

        Assert.Equal(value, commandId.Value);
        Assert.Equal(value, placementId.Value);
        Assert.Equal(value, locationId.Value);
        Assert.Equal(commandId, new CommandId(value));
        Assert.NotEqual(typeof(CommandId), typeof(CommandPlacementId));
        Assert.True(CommandId.TryParse(new string('a', 128), out _));
        Assert.True(CommandPlacementId.TryParse(value, out _));
        Assert.True(MenuLocationId.TryParse(value, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Upper.case")]
    [InlineData("with/slash")]
    [InlineData("with:colon")]
    [InlineData("empty..segment")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    public void 三类身份TryParse对非法输入稳定返回False(string? value)
    {
        Assert.False(CommandId.TryParse(value, out var commandId));
        Assert.False(CommandPlacementId.TryParse(value, out var placementId));
        Assert.False(MenuLocationId.TryParse(value, out var locationId));
        Assert.Null(commandId);
        Assert.Null(placementId);
        Assert.Null(locationId);
    }

    [Fact]
    public void 身份构造拒绝null和超过长度上限()
    {
        Assert.Throws<ArgumentNullException>(() => new CommandId(null!));
        Assert.Throws<ArgumentNullException>(() => new CommandPlacementId(null!));
        Assert.Throws<ArgumentNullException>(() => new MenuLocationId(null!));
        Assert.Throws<ArgumentException>(() => new CommandId(new string('a', 129)));
        Assert.Throws<ArgumentException>(() => new CommandPlacementId(new string('a', 129)));
        Assert.Throws<ArgumentException>(() => new MenuLocationId(new string('a', 129)));
    }

    [Fact]
    public void Target接口保持冻结的单命令事件和可等待执行形状()
    {
        var eventArgs = new WorkbenchCommandStateChangedEventArgs(Command());
        Assert.Equal(Command(), eventArgs.CommandId);
        Assert.Throws<ArgumentNullException>(() =>
            new WorkbenchCommandStateChangedEventArgs(null!));

        var target = typeof(IWorkbenchDocumentCommandTarget);
        Assert.Equal(
            ["CanExecute", "ExecuteAsync"],
            target.GetMethods()
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        var changed = Assert.Single(target.GetEvents());
        Assert.Equal("CommandStateChanged", changed.Name);
        Assert.Equal(
            typeof(EventHandler<WorkbenchCommandStateChangedEventArgs>),
            changed.EventHandlerType);
        Assert.Equal(typeof(bool), target.GetMethod("CanExecute")!.ReturnType);
        var execute = target.GetMethod("ExecuteAsync")!;
        Assert.Equal(typeof(ValueTask), execute.ReturnType);
        Assert.Equal(
            [typeof(CommandId), typeof(CancellationToken)],
            execute.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Fact]
    public void 描述符冻结纯数据和四个共享位置()
    {
        var command = new CommandDescriptor(Command(), "保存", "保存当前文档", "save-icon");
        var menu = new MenuCommandContributionDescriptor(
            Placement("menu-save"),
            command.CommandId,
            WorkbenchMenuLocations.FileShared,
            "document",
            20);
        var key = new KeyBindingContributionDescriptor(
            Placement("key-save"),
            command.CommandId,
            Key.S,
            KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal("保存", command.DisplayName);
        Assert.Equal("保存当前文档", command.Description);
        Assert.Equal("save-icon", command.IconPath);
        Assert.Equal(MenuCommandTargetUnavailableBehavior.Hide, menu.TargetUnavailableBehavior);
        Assert.Equal("document", menu.Group);
        Assert.Equal(20, menu.Order);
        Assert.Equal(Key.S, key.Key);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, key.Modifiers);
        Assert.Equal("myavalonia.host.menu.file.shared", WorkbenchMenuLocations.FileShared.Value);
        Assert.Equal("myavalonia.host.menu.view.shared", WorkbenchMenuLocations.ViewShared.Value);
        Assert.Equal("myavalonia.host.menu.tools.shared", WorkbenchMenuLocations.ToolsShared.Value);
        Assert.Equal("myavalonia.host.menu.help.shared", WorkbenchMenuLocations.HelpShared.Value);
    }

    [Fact]
    public void 描述符拒绝空文本null非法枚举和非法Gesture()
    {
        Assert.Throws<ArgumentException>(() => new CommandDescriptor(Command(), " ", "说明"));
        Assert.Throws<ArgumentNullException>(() => new CommandDescriptor(Command(), "命令", null!));
        Assert.Throws<ArgumentNullException>(() => new CommandDescriptor(Command(), "命令", "", null!));
        Assert.Throws<ArgumentNullException>(() => new MenuCommandContributionDescriptor(
            Placement("menu"), Command(), WorkbenchMenuLocations.FileShared, null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MenuCommandContributionDescriptor(
            Placement("menu"), Command(), WorkbenchMenuLocations.FileShared, "", 0,
            (MenuCommandTargetUnavailableBehavior)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyBindingContributionDescriptor(
            Placement("key"), Command(), Key.None, KeyModifiers.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyBindingContributionDescriptor(
            Placement("key"), Command(), (Key)(-1), KeyModifiers.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyBindingContributionDescriptor(
            Placement("key"), Command(), Key.S, (KeyModifiers)(1 << 30)));
    }

    [Fact]
    public void 可选注册扩展向兼容Host原样转发()
    {
        IPluginRegistration registration = new CompatibleRegistration();
        var command = new CommandDescriptor(Command(), "命令", "测试");
        var documentTypeId = Document();
        var menu = new MenuCommandContributionDescriptor(
            Placement("menu"), Command(), WorkbenchMenuLocations.ToolsShared, "", 0);
        var key = new KeyBindingContributionDescriptor(
            Placement("key"), Command(), Key.K, KeyModifiers.Control);

        registration.AddDocumentCommand(command, documentTypeId);
        registration.AddMenuCommandContribution(menu);
        registration.AddKeyBindingContribution(key);

        var compatible = Assert.IsType<CompatibleRegistration>(registration);
        Assert.Same(command, compatible.CommandDescriptor);
        Assert.Same(documentTypeId, compatible.TargetDocumentTypeId);
        Assert.Same(menu, compatible.MenuDescriptor);
        Assert.Same(key, compatible.KeyDescriptor);
    }

    [Fact]
    public void 旧Host仅在调用新扩展时给出稳定错误()
    {
        IPluginRegistration registration = new LegacyRegistration();

        var exception = Assert.Throws<NotSupportedException>(() =>
            registration.AddDocumentCommand(
                new CommandDescriptor(Command(), "命令", "测试"),
                Document()));

        Assert.Equal(
            "当前 Host 不支持 Workbench Command；需要 Plugin SDK/Host 3.3.0 或更高版本。",
            exception.Message);
        Assert.Throws<NotSupportedException>(() => registration.AddMenuCommandContribution(
            new MenuCommandContributionDescriptor(
                Placement("menu"), Command(), WorkbenchMenuLocations.HelpShared, "", 0)));
        Assert.Throws<NotSupportedException>(() => registration.AddKeyBindingContribution(
            new KeyBindingContributionDescriptor(
                Placement("key"), Command(), Key.F1, KeyModifiers.None)));
    }

    [Fact]
    public void 扩展方法在能力检查前拒绝null参数()
    {
        IPluginRegistration registration = new LegacyRegistration();
        Assert.Throws<ArgumentNullException>(() =>
            WorkbenchCommandRegistrationExtensions.AddDocumentCommand(
                null!, new CommandDescriptor(Command(), "命令", ""), Document()));
        Assert.Throws<ArgumentNullException>(() => registration.AddDocumentCommand(null!, Document()));
        Assert.Throws<ArgumentNullException>(() => registration.AddDocumentCommand(
            new CommandDescriptor(Command(), "命令", ""), null!));
        Assert.Throws<ArgumentNullException>(() => registration.AddMenuCommandContribution(null!));
        Assert.Throws<ArgumentNullException>(() => registration.AddKeyBindingContribution(null!));
    }

    private static CommandId Command() =>
        new("myavalonia.plugin.contract-test.command.sample");

    private static CommandPlacementId Placement(string suffix) =>
        new($"myavalonia.plugin.contract-test.command-placement.{suffix}");

    private static DocumentTypeId Document() =>
        new("myavalonia.plugin.contract-test.document.sample");

    private class LegacyRegistration : IPluginRegistration
    {
        public PluginId PluginId { get; } = new("myavalonia.plugin.contract-test");
        public IServiceCollection Services { get; } = new ServiceCollection();
        public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle
        {
        }
        public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPluginDocument
            where TView : Control, new()
        {
        }
        public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPersistablePluginDocument
            where TView : Control, new()
        {
        }
        public void AddTool<TTool, TView>(ToolDescriptor descriptor)
            where TTool : class
            where TView : Control, new()
        {
        }
    }

    private sealed class CompatibleRegistration : LegacyRegistration, IWorkbenchCommandRegistration
    {
        internal CommandDescriptor? CommandDescriptor { get; private set; }
        internal DocumentTypeId? TargetDocumentTypeId { get; private set; }
        internal MenuCommandContributionDescriptor? MenuDescriptor { get; private set; }
        internal KeyBindingContributionDescriptor? KeyDescriptor { get; private set; }

        public void AddDocumentCommand(
            CommandDescriptor descriptor,
            DocumentTypeId targetDocumentTypeId)
        {
            CommandDescriptor = descriptor;
            TargetDocumentTypeId = targetDocumentTypeId;
        }

        public void AddMenuCommandContribution(MenuCommandContributionDescriptor descriptor) =>
            MenuDescriptor = descriptor;

        public void AddKeyBindingContribution(KeyBindingContributionDescriptor descriptor) =>
            KeyDescriptor = descriptor;
    }
}
