using System.Text.Json;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>验证 Document 内容所有权和不可变贡献描述符的输入边界。</summary>
public sealed class DocumentAndDescriptorTests
{
    [Fact]
    public void DocumentContent克隆JsonElement且不依赖原始Document生命周期()
    {
        DocumentContent content;
        using (var document = JsonDocument.Parse("{\"value\":42}"))
        {
            content = new DocumentContent(3, document.RootElement);
        }

        Assert.Equal(3, content.SchemaVersion);
        Assert.Equal(42, content.Payload.GetProperty("value").GetInt32());
    }

    [Fact]
    public void DocumentContent拒绝非法Schema和Undefined()
    {
        using var document = JsonDocument.Parse("null");
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentContent(0, document.RootElement));
        Assert.Throws<ArgumentException>(() => new DocumentContent(1, default));
        Assert.Equal(JsonValueKind.Null, new DocumentContent(1, document.RootElement).Payload.ValueKind);
    }

    [Fact]
    public void Activation和Presentation拒绝Null但允许由插件决定的空标题()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentActivationContext(null!));
        Assert.Throws<ArgumentNullException>(() => new DocumentPresentationState(null!));
        Assert.Equal(string.Empty, new DocumentActivationContext(string.Empty).Title);
        Assert.Equal(string.Empty, new DocumentPresentationState(string.Empty).Title);
        var defaults = new DocumentActivationContext("默认");
        Assert.Null(defaults.CreationIntentId);
        Assert.Null(defaults.RestoredContent);
    }

    [Fact]
    public void DocumentDescriptor防御复制创建意图集合()
    {
        var intents = new List<DocumentCreationIntentDescriptor>
        {
            new(new CreationIntentId("default"), "默认入口"),
        };
        var descriptor = new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"),
            "示例文档",
            "说明",
            "示例",
            creationIntents: intents);

        intents.Add(new(new CreationIntentId("another"), "第二入口"));

        Assert.Single(descriptor.CreationIntents);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DocumentCreationIntentDescriptor>)descriptor.CreationIntents).Add(intents[1]));
    }

    [Fact]
    public void DocumentDescriptor拒绝重复意图与空白显示字段()
    {
        var intent = new DocumentCreationIntentDescriptor(new CreationIntentId("default"), "默认入口");
        Assert.Throws<ArgumentException>(() => new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"),
            "示例",
            "说明",
            "菜单",
            creationIntents: [intent, intent]));
        Assert.Throws<ArgumentException>(() => new DocumentCreationIntentDescriptor(
            new CreationIntentId("default"), " "));
        Assert.Throws<ArgumentException>(() => new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"), " ", "说明", "菜单"));
        Assert.Throws<ArgumentNullException>(() => new DocumentDescriptor(
            null!, "示例", "说明", "菜单"));
        Assert.Throws<ArgumentNullException>(() => new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"), "示例", null!, "菜单"));
        Assert.Throws<ArgumentException>(() => new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"), "示例", "说明", " "));
        Assert.Throws<ArgumentException>(() => new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"), "示例", "说明", "菜单",
            creationIntents: [null!]));
    }

    [Fact]
    public void Descriptor可选字段具有稳定默认值()
    {
        var intent = new DocumentCreationIntentDescriptor(new CreationIntentId("default"), "默认入口");
        var document = new DocumentDescriptor(
            new DocumentTypeId("myavalonia.plugin.sample.document.main"), "示例", "说明", "菜单");

        Assert.Equal(string.Empty, intent.Description);
        Assert.Equal(string.Empty, intent.IconPath);
        Assert.Equal(string.Empty, document.IconPath);
        Assert.Empty(document.CreationIntents);
    }

    [Fact]
    public void ToolDescriptor拒绝非法枚举并保存明确关闭语义()
    {
        var descriptor = new ToolDescriptor(
            new ToolTypeId("myavalonia.plugin.sample.tool.status"),
            "状态",
            "显示状态",
            ToolDockSide.Right,
            ToolCloseBehavior.Hide);

        Assert.Equal(ToolDockSide.Right, descriptor.DockSide);
        Assert.Equal(ToolCloseBehavior.Hide, descriptor.CloseBehavior);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolDescriptor(
            descriptor.ToolTypeId, "状态", "说明", (ToolDockSide)99, ToolCloseBehavior.Hide));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolDescriptor(
            descriptor.ToolTypeId, "状态", "说明", ToolDockSide.Left, (ToolCloseBehavior)99));
    }
}
