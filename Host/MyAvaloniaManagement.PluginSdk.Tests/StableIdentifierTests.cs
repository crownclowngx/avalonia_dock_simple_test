using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>验证 V2 的四类稳定身份只接受单一规范线格式。</summary>
public sealed class StableIdentifierTests
{
    [Theory]
    [InlineData("myavalonia.plugin.sample")]
    [InlineData("document-2")]
    [InlineData("a.b-c.d4")]
    public void 全局身份接受小写点分KebabCase(string value)
    {
        Assert.Equal(value, new PluginId(value).Value);
        Assert.Equal(value, new DocumentTypeId(value).Value);
        Assert.Equal(value, new ToolTypeId(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData("a_b")]
    [InlineData(".leading")]
    [InlineData("trailing.")]
    [InlineData("double..segment")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("white space")]
    public void 全局身份拒绝非规范输入(string value)
    {
        Assert.Throws<ArgumentException>(() => new PluginId(value));
        Assert.Throws<ArgumentException>(() => new DocumentTypeId(value));
        Assert.Throws<ArgumentException>(() => new ToolTypeId(value));
        Assert.False(PluginId.TryParse(value, out _));
    }

    [Fact]
    public void 全局身份接受最大长度并拒绝超长输入()
    {
        var maximum = new string('a', 128);
        Assert.Equal(maximum, PluginId.Parse(maximum).Value);
        Assert.Equal(maximum, DocumentTypeId.Parse(maximum).Value);
        Assert.Equal(maximum, ToolTypeId.Parse(maximum).Value);
        Assert.Throws<ArgumentException>(() => PluginId.Parse(maximum + "a"));
        Assert.Throws<ArgumentException>(() => DocumentTypeId.Parse(maximum + "a"));
        Assert.Throws<ArgumentException>(() => ToolTypeId.Parse(maximum + "a"));
        Assert.False(PluginId.TryParse(maximum + "a", out _));
        Assert.False(DocumentTypeId.TryParse(maximum + "a", out _));
        Assert.False(ToolTypeId.TryParse(maximum + "a", out _));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("personal-source-2")]
    public void 创建意图只接受单段KebabCase(string value)
    {
        var intent = CreationIntentId.Parse(value);
        Assert.Equal(value, intent.Value);
        Assert.True(CreationIntentId.TryParse(value, out var parsed));
        Assert.Equal(intent, parsed);
    }

    [Theory]
    [InlineData("document.default")]
    [InlineData("Default")]
    [InlineData("bad_intent")]
    public void 创建意图拒绝点号和非规范字符(string value)
    {
        Assert.Throws<ArgumentException>(() => new CreationIntentId(value));
        Assert.False(CreationIntentId.TryParse(value, out _));
    }

    [Fact]
    public void 创建意图验证边界长度且四类TryParse对Null返回失败()
    {
        var maximum = new string('a', 128);
        Assert.Equal(maximum, CreationIntentId.Parse(maximum).Value);
        Assert.Throws<ArgumentException>(() => CreationIntentId.Parse(maximum + "a"));
        Assert.False(CreationIntentId.TryParse(maximum + "a", out _));

        Assert.False(PluginId.TryParse(null, out var pluginId));
        Assert.False(DocumentTypeId.TryParse(null, out var documentTypeId));
        Assert.False(ToolTypeId.TryParse(null, out var toolTypeId));
        Assert.False(CreationIntentId.TryParse(null, out var creationIntentId));
        Assert.Null(pluginId);
        Assert.Null(documentTypeId);
        Assert.Null(toolTypeId);
        Assert.Null(creationIntentId);
    }

    [Fact]
    public void 值对象按类型和值比较且输出原始稳定字符串()
    {
        var first = new PluginId("myavalonia.plugin.sample");
        var second = new PluginId("myavalonia.plugin.sample");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("myavalonia.plugin.sample", first.ToString());
        Assert.NotEqual<object>(first, new DocumentTypeId(first.Value));
    }
}
