using Avalonia.Controls;
using Avalonia.Input;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>用冻结数据验证局部规则，避免依靠 Provider 或模型构造间接验证校验器。</summary>
public sealed class PluginContributionValidatorTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.v12-validator");
    private static readonly DocumentTypeId Document = new(Owner.Value + ".document.main");
    private static readonly CommandId Command = new(Owner.Value + ".command.main");

    [Fact]
    public void 集合快照不随Builder追加改变且不能通过强转改写()
    {
        var builder = new PluginRegistryBuilder();
        var descriptor = new DocumentDescriptor(Document, "页面", "测试", "测试");
        builder.AddDocument(Owner, descriptor, typeof(Model), typeof(UserControl),
            () => throw new InvalidOperationException("禁止执行工厂"), false);
        var snapshot = builder.CaptureContributions();
        builder.AddDocument(Owner, descriptor, typeof(Model), typeof(UserControl),
            () => throw new InvalidOperationException("禁止执行工厂"), false);

        Assert.Empty(PluginContributionValidator.Validate(snapshot, Owner));
        Assert.Same(descriptor, Assert.Single(snapshot.Documents).Descriptor);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PluginRegistryBuilder.DocumentDeclaration>)snapshot.Documents).Clear());
        Assert.Contains(PluginContributionValidator.Validate(builder.CaptureContributions(), Owner),
            item => item.Code == "DOCUMENT_ID_DUPLICATE");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Command和Placement可先于Document声明且校验没有激活副作用(bool commandFirst)
    {
        var builder = new PluginRegistryBuilder();
        void AddDocument() => builder.AddDocument(Owner, new(Document, "页面", "测试", "测试"),
            typeof(Model), typeof(UserControl), () => throw new InvalidOperationException("禁止执行工厂"), false);
        void AddCommand()
        {
            builder.AddMenuCommandContribution(Owner, new(new(Owner.Value + ".command-placement.menu"),
                Command, WorkbenchMenuLocations.ToolsShared, "", 0));
            builder.AddDocumentCommand(Owner, new(Command, "操作", "测试"), Document);
        }
        if (commandFirst) { AddCommand(); AddDocument(); }
        else { AddDocument(); AddCommand(); }
        Assert.Empty(PluginContributionValidator.Validate(builder.CaptureContributions(), Owner));
        Assert.Single(builder.Build(null).Documents);
    }

    [Fact]
    public void 声明关系错误与重复快捷键分别报告且保留规则顺序()
    {
        var builder = new PluginRegistryBuilder();
        builder.AddDocumentCommand(Owner, new(Command, "操作", "测试"), Document);
        builder.AddMenuCommandContribution(Owner, new(new(Owner.Value + ".command-placement.menu"),
            new(Owner.Value + ".command.missing"), WorkbenchMenuLocations.ToolsShared, "", 0));
        foreach (var suffix in new[] { "first", "second" })
            builder.AddKeyBindingContribution(Owner, new(new(Owner.Value + ".command-placement." + suffix),
                Command, Key.K, KeyModifiers.Control));
        Assert.Equal(
            [HostDiagnosticCodes.WorkbenchCommandTargetDocumentNotRegistered,
                HostDiagnosticCodes.WorkbenchCommandPlacementCommandNotRegistered,
                HostDiagnosticCodes.WorkbenchKeyGestureDuplicate],
            PluginContributionValidator.Validate(builder.CaptureContributions(), Owner).Select(item => item.Code));
    }

    private sealed class Model;
}
