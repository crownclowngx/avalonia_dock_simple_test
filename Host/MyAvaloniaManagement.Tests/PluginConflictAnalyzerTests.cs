using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证全局规则与局部校验的差异；只比较事实，不创建或释放插件资源。</summary>
public sealed class PluginConflictAnalyzerTests
{
    private static readonly PluginId First = new("myavalonia.plugin.v12-analyzer-first");
    private static readonly PluginId Second = new("myavalonia.plugin.v12-analyzer-second");

    [Fact]
    public void 图标同Owner重复仍拒绝而普通全局身份只比较跨Owner()
    {
        var builder = new PluginRegistryBuilder();
        var command = new CommandDescriptor(new("shared.command.same"), "操作", "测试");
        var document = new DocumentTypeId("shared.document.same");
        builder.AddDocumentCommand(First, command, document);
        builder.AddDocumentCommand(First, command, document);
        builder.AddIcon(First, "same", new("M0,0 H24 V24 Z", 24, 24));
        builder.AddIcon(First, "same", new("M0,0 H24 V24 Z", 24, 24));
        var conflict = Assert.Single(PluginConflictAnalyzer.Analyze(builder.CaptureContributions()));
        Assert.Equal("ICON_REFERENCE_DUPLICATE", conflict.Code);
        Assert.Equal(First, conflict.OwnerId);
        Assert.Null(conflict.Contributor);
    }

    [Fact]
    public void 跨DocumentTool精确模型冲突返回两个Owner并保留View来源()
    {
        var builder = new PluginRegistryBuilder();
        builder.AddDocument(First, new(new(First.Value + ".document.main"), "页面", "测试", "测试"),
            typeof(Model), typeof(UserControl), () => throw new InvalidOperationException(), false);
        builder.AddTool(Second, new(new(Second.Value + ".tool.main"), "工具", "测试", ToolDockSide.Left, ToolCloseBehavior.Hide),
            typeof(Model), typeof(Border), () => throw new InvalidOperationException());
        var conflicts = PluginConflictAnalyzer.Analyze(builder.CaptureContributions()).ToArray();
        Assert.Equal([First, Second], conflicts.Select(item => item.OwnerId));
        Assert.All(conflicts, item => Assert.Equal("VIEW_MODEL_REGISTRATION_DUPLICATE", item.Code));
        Assert.Equal([typeof(UserControl), typeof(Border)], conflicts.Select(item => item.Contributor));
    }

    [Fact]
    public void 分析固定快照可重复且不受Builder后续追加影响()
    {
        var builder = new PluginRegistryBuilder();
        var command = new CommandDescriptor(new("shared.command.same"), "操作", "测试");
        var document = new DocumentTypeId("shared.document.same");
        builder.AddDocumentCommand(First, command, document);
        var snapshot = builder.CaptureContributions();
        builder.AddDocumentCommand(Second, command, document);
        Assert.Empty(PluginConflictAnalyzer.Analyze(snapshot));
        Assert.Empty(PluginConflictAnalyzer.Analyze(snapshot));
        var conflicts = PluginConflictAnalyzer.Analyze(builder.CaptureContributions()).ToArray();
        Assert.Equal([First, Second], conflicts.Select(item => item.OwnerId));
        Assert.All(conflicts, item => Assert.Equal(typeof(PluginRegistryBuilder), item.Contributor));
    }

    private sealed class Model;
}
