using System.Text;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证布局线格式和迁移的用户数据边界，不启动桌面或插件。</summary>
public sealed class DockLayoutV3Tests
{
    [Fact]
    public void V3往返保留隐藏浮窗结构和负坐标但不包含文档内容()
    {
        var snapshot = Sample();
        var json = Write(snapshot);
        var restored = Read(json);
        Assert.Equal(json, Write(restored));
        Assert.Equal(-1200, restored.FloatingWindows[0].Bounds.X);
        Assert.Equal("hidden", restored.Tools[0].State);
        Assert.DoesNotContain("payload", json);
        Assert.DoesNotContain("filePath", json);
        Assert.DoesNotContain("pageId", json);
    }

    [Theory]
    [InlineData("\"schemaVersion\": 3", "\"schemaVersion\": 3, \"schemaVersion\": 3")]
    [InlineData("\"schemaVersion\": 3", "\"schemaVersion\": 3, \"extra\": true")]
    [InlineData("\"schemaVersion\": 3,", "")]
    [InlineData("\"schemaVersion\": 3", "\"SchemaVersion\": 3")]
    [InlineData("\"schemaVersion\": 3", "\"schemaVersion\": \"3\"")]
    [InlineData("\"maximized\": false", "\"maximized\": false, \"maximized\": true")]
    [InlineData("\"screen\": null", "\"otherScreen\": null")]
    [InlineData("\"state\": \"hidden\"", "\"state\": \"floating\"")]
    public void 严格拒绝重复未知缺失字段与错误类型(string from, string to) =>
        Assert.Throws<DockLayoutFormatException>(() => Read(Write(Sample()).Replace(from, to)));

    [Fact]
    public void 未来版本与坏Json和超大文件有稳定错误()
    {
        Assert.Equal("LAYOUT_SCHEMA_FUTURE", Assert.Throws<DockLayoutFormatException>(() => Read("{\"schemaVersion\":4}")).Code);
        Assert.Equal("LAYOUT_JSON_INVALID", Assert.Throws<DockLayoutFormatException>(() => Read("{bad-json")).Code);
        Assert.Equal("LAYOUT_SIZE_EXCEEDED", Assert.Throws<DockLayoutFormatException>(() => Read(new string(' ', 1048577))).Code);
    }

    [Fact]
    public void 结构拒绝重复占位和浮窗文档或自动隐藏()
    {
        var source = Sample();
        var duplicate = source.FloatingWindows[0] with { Id = "second" };
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(source with { FloatingWindows = [.. source.FloatingWindows, duplicate] }));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(source with
            { FloatingWindows = [source.FloatingWindows[0] with { Root = DockLayoutNode.Documents() }] }));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(source with
            { Tools = [source.Tools[0] with { State = "autoHidden" }] }));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(source with
            { MainWindow = source.MainWindow with { Bounds = DockWindowBounds.Default with { Width = double.NaN } } }));
    }

    [Fact]
    public void 内存循环与非法活动工具在序列化之前拒绝()
    {
        var children = new List<DockLayoutNode>();
        var cycle = DockLayoutNode.Split("cycle", "horizontal", [] ) with { Children = children };
        children.Add(cycle with { Proportion = 0.5 });
        children.Add(DockLayoutNode.Documents() with { Proportion = 0.5 });
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(Sample() with
            { MainWindow = Sample().MainWindow with { Root = cycle } }));
        var source = Sample();
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(source with
            { FloatingWindows = [source.FloatingWindows[0] with { Root = source.FloatingWindows[0].Root with { ActiveToolId = "sample.tool" } }] }));
    }

    [Fact]
    public void V2转换保留比例顺序隐藏与自动隐藏且不修改输入()
    {
        var source = new DockLayoutSnapshotV2
        {
            Panes = [new() { Id = DockLayoutIds.LeftPane, Proportion = 0.25 }],
            Tools = [new() { Id = "tool.hidden", DockId = DockLayoutIds.LeftTools, Order = 1, IsVisible = false },
                new() { Id = "tool.pinned", DockId = DockLayoutIds.LeftTools, Order = 0, IsVisible = true, IsPinned = true }]
        };
        var result = DockLayoutV2Migration.Convert(source);
        var group = result.MainWindow.Root.Children[0];
        Assert.Equal(0.25, group.Proportion);
        Assert.Equal(new[] { "tool.pinned", "tool.hidden" }, group.ToolIds);
        Assert.Equal("autoHidden", result.Tools.Single(t => t.Id == "tool.pinned").State);
        Assert.Equal(2, source.SchemaVersion);
        Assert.Empty(result.FloatingWindows);
        Assert.Equal(Write(result), Write(Read(Write(result))));
    }

    internal static DockLayoutSnapshotV3 Sample() => new(3, new("main", DockWindowBounds.Default, DockLayoutNode.Documents()),
        [new("float-1", new(-1200, 20, 640, 480, false, null), DockLayoutNode.Group("group-1", ["sample.tool"]))],
        [new("sample.tool", "hidden", DockLayoutIds.LeftTools, 0)]);
    internal static string Write(DockLayoutSnapshotV3 snapshot)
    {
        using var stream = new MemoryStream();
        DockLayoutV3Json.Write(stream, snapshot);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    internal static DockLayoutSnapshotV3 Read(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return DockLayoutV3Json.Read(stream);
    }
}
