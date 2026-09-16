using System.Text;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

/// <summary>覆盖可接受边界与刚越界的成对输入，确保恶意尺寸不会先修改 UI 或无界分配。</summary>
public sealed class DockLayoutV3BoundaryTests
{
    [Fact]
    public void 文件字节上限包含合法空白且超过一个字节拒绝()
    {
        using var content = new MemoryStream();
        DockLayoutV3Json.Write(content, Empty());
        var bytes = Encoding.UTF8.GetString(content.ToArray()).PadRight(DockLayoutV3Validator.MaximumFileBytes);
        Assert.NotNull(DockLayoutV3Json.Read(new MemoryStream(Encoding.UTF8.GetBytes(bytes))));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Json.Read(new MemoryStream(Encoding.UTF8.GetBytes(bytes + " "))));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999999999999999999999")]
    public void 未来版本超出Int32也只读保护(string schema) => Assert.Equal("LAYOUT_SCHEMA_FUTURE",
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Json.Read(new MemoryStream(Encoding.UTF8.GetBytes("{\"schemaVersion\":" + schema + "}")))).Code);

    [Fact]
    public void 工具和浮窗数量分别检查边界()
    {
        DockLayoutV3Validator.Validate(WithTools(512));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(WithTools(513)));
        DockLayoutSnapshotV3 Windows(int count) => new(3, Empty().MainWindow,
            Enumerable.Range(0, count).Select(i => new DockLayoutWindow("w-" + i, DockWindowBounds.Default,
                DockLayoutNode.Group("g-" + i, ["tool-" + i]))).ToArray(),
            Enumerable.Range(0, count).Select(Tool).ToArray());
        DockLayoutV3Validator.Validate(Windows(32));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(Windows(33)));
    }

    [Fact]
    public void 树深度及节点总量分别受限()
    {
        DockLayoutSnapshotV3 Deep(int depth)
        {
            var node = DockLayoutNode.Documents();
            for (var i = 0; i < depth; i++) node = DockLayoutNode.Split("s-" + i, "horizontal",
                [node with { Proportion = 0.5 }, DockLayoutNode.Group("g-" + i, ["tool-" + i], 0.5)]);
            return Empty() with { MainWindow = Empty().MainWindow with { Root = node }, Tools = Enumerable.Range(0, depth).Select(Tool).ToArray() };
        }
        DockLayoutV3Validator.Validate(Deep(32));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(Deep(33)));
        DockLayoutNode Balanced(DockLayoutNode[] nodes, string id) => nodes.Length == 1 ? nodes[0] :
            DockLayoutNode.Split(id, "horizontal", [Balanced(nodes[..(nodes.Length / 2)], id + "l") with { Proportion = 0.5 },
                Balanced(nodes[(nodes.Length / 2)..], id + "r") with { Proportion = 0.5 }]);
        DockLayoutSnapshotV3 Wide(int count) => Empty() with
        {
            MainWindow = Empty().MainWindow with { Root = Balanced(Enumerable.Range(0, count).Select(i => DockLayoutNode.Group("g-" + i, ["tool-" + i])).Append(DockLayoutNode.Documents()).ToArray(), "s") },
            Tools = Enumerable.Range(0, count).Select(Tool).ToArray(),
        };
        DockLayoutV3Validator.Validate(Wide(511));
        Assert.Throws<DockLayoutFormatException>(() => DockLayoutV3Validator.Validate(Wide(512)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void 非有限或无效窗口尺寸拒绝(double width) => Assert.Throws<DockLayoutFormatException>(() =>
        DockLayoutV3Validator.Validate(Empty() with { MainWindow = Empty().MainWindow with { Bounds = DockWindowBounds.Default with { Width = width } } }));

    private static DockLayoutTool Tool(int index) => new("tool-" + index, "hidden", DockLayoutIds.LeftTools, index);
    private static DockLayoutSnapshotV3 Empty() => new(3, new("main", DockWindowBounds.Default, DockLayoutNode.Documents()), [], []);
    private static DockLayoutSnapshotV3 WithTools(int count) => Empty() with
    {
        MainWindow = Empty().MainWindow with { Root = DockLayoutNode.Split("s", "horizontal", [DockLayoutNode.Documents() with { Proportion = 0.5 },
            DockLayoutNode.Group("g", Enumerable.Range(0, count).Select(i => Tool(i).Id), 0.5)]) },
        Tools = Enumerable.Range(0, count).Select(Tool).ToArray(),
    };
}
