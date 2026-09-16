using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

public sealed class DockLayoutTreeTests
{
    [Fact]
    public void 隐藏整窗保留原位置而可见投影不凭空创建窗口()
    {
        var previous = DockLayoutV3Tests.Sample();
        var live = Empty() with { Tools = previous.Tools };
        var merged = DockLayoutTree.Merge(live, previous);
        Assert.Single(merged.FloatingWindows);
        Assert.Equal(DockLayoutV3Tests.Write(previous), DockLayoutV3Tests.Write(merged));
        Assert.Equal("hidden", Assert.Single(merged.Tools).State);
    }

    [Fact]
    public void 同一标签组保留隐藏项相对次序而活动项以当前可见工具为准()
    {
        var previous = Empty() with
        {
            FloatingWindows = [new("float", DockWindowBounds.Default, DockLayoutNode.Group("group", ["a", "b"]))],
            Tools = [Tool("a", "hidden"), Tool("b", "visible")],
        };
        var live = previous with
        {
            FloatingWindows = [previous.FloatingWindows[0] with { Root = DockLayoutNode.Group("group", ["b"], active: "b") }],
        };
        var merged = DockLayoutTree.Merge(live, previous);
        Assert.Equal(new[] { "a", "b" }, merged.FloatingWindows[0].Root.ToolIds);
        Assert.Equal("b", merged.FloatingWindows[0].Root.ActiveToolId);
    }

    [Fact]
    public void 缺失插件分支在可见树收缩后保留比例和显示意图()
    {
        var previous = Empty() with
        {
            MainWindow = new("main", DockWindowBounds.Default, DockLayoutNode.Split("split", "horizontal",
                [DockLayoutNode.Group("group", ["absent"], 0.3), DockLayoutNode.Documents() with { Proportion = 0.7 }])),
            Tools = [Tool("absent", "visible")],
        };
        var merged = DockLayoutTree.Merge(Empty(), previous);
        Assert.Equal("split", merged.MainWindow.Root.Id);
        Assert.Equal(0.3, merged.MainWindow.Root.Children[0].Proportion);
        Assert.Equal("visible", Assert.Single(merged.Tools).State);
    }

    [Fact]
    public void 同一父分割新增可见组时仍能合并仅剩一个隐藏分支()
    {
        var previous = Empty() with
        {
            MainWindow = new("main", DockWindowBounds.Default, DockLayoutNode.Split("split", "horizontal",
                [DockLayoutNode.Group("a-group", ["a"], 0.3), DockLayoutNode.Documents() with { Proportion = 0.7 }])),
            Tools = [Tool("a", "hidden")],
        };
        var live = Empty() with
        {
            MainWindow = new("main", DockWindowBounds.Default, DockLayoutNode.Split("split", "horizontal",
                [DockLayoutNode.Documents() with { Proportion = 0.7 }, DockLayoutNode.Group("b-group", ["b"], 0.3, "b")])),
            Tools = [Tool("a", "hidden"), Tool("b", "visible")],
        };
        var merged = DockLayoutTree.Merge(live, previous);
        Assert.Equal(new[] { "a-group", "Documents", "b-group" }, merged.MainWindow.Root.Children.Select(node => node.Id));
        Assert.Equal(1, merged.MainWindow.Root.Children.Sum(node => node.Proportion), 6);
    }

    [Fact]
    public void 新隐藏工具的恢复组在连续捕获时保持稳定身份()
    {
        var live = Empty() with { Tools = [Tool("new", "hidden")] };
        var first = DockLayoutTree.Merge(live, DockLayoutV3Tests.Sample());
        var second = DockLayoutTree.Merge(live, first);
        Assert.Equal(DockLayoutV3Tests.Write(first), DockLayoutV3Tests.Write(second));
        Assert.Equal(2, second.Tools.Count);
        Assert.Single(second.FloatingWindows);
    }

    [Fact]
    public void 过滤文档与空分割后只留下工具且继承原父比例()
    {
        var source = DockLayoutNode.Split("outer", "vertical",
        [
            DockLayoutNode.Documents() with { Proportion = 0.4 },
            DockLayoutNode.Split("inner", "horizontal",
                [DockLayoutNode.Group("keep", ["a"], 0.2), DockLayoutNode.Group("remove", ["b"], 0.8)], 0.6),
        ]);
        var filtered = DockLayoutTree.Filter(source, new HashSet<string> { "a" }, keepDocuments: false);
        Assert.Equal("keep", filtered!.Id);
        Assert.Equal(1, filtered.Proportion);
        Assert.Empty(filtered.Children);
        Assert.Null(DockLayoutTree.Filter(source, new HashSet<string>(), keepDocuments: false));
    }

    private static DockLayoutSnapshotV3 Empty() => new(3, new("main", DockWindowBounds.Default, DockLayoutNode.Documents()), [], []);
    private static DockLayoutTool Tool(string id, string state) => new(id, state, DockLayoutIds.LeftTools, 0);
}
