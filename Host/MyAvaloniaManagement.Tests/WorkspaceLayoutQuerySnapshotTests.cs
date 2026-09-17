using System.Diagnostics;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Workspace;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Models.Tools;
using Xunit;
using Xunit.Abstractions;

namespace MyAvaloniaManagement.Tests;

/// <summary>用显式拓扑及未修改的旧遍历入口核对关系，不以快照自身推导期望值。</summary>
public sealed class WorkspaceLayoutQuerySnapshotTests(ITestOutputHelper output)
{
    [Fact]
    public void 空布局没有文档窗口或工具关系()
    {
        var snapshot = WorkspaceLayoutQuerySnapshot.Capture(null);
        var target = new Document();
        Assert.Null(snapshot.FindDocumentDock(target));
        Assert.Null(snapshot.FindWindow(target));
        Assert.False(snapshot.IsHidden(target));
        Assert.False(snapshot.IsPinned(target));
        Assert.False(snapshot.IsDocked(target));
        Assert.False(snapshot.IsActive(target));
    }

    [Fact]
    public void 相同标题和Id不合并实例且重复成员保留首个分组()
    {
        var main = new Document { Id = "same", Title = "同名" };
        var floating = new Document { Id = "same", Title = "同名" };
        var firstGroup = new DocumentDock { Id = "Documents", VisibleDockables = [main, main] };
        var secondGroup = new DocumentDock { Id = "Documents", VisibleDockables = [main] };
        var floatingGroup = new DocumentDock { Id = "Documents", VisibleDockables = [floating] };
        var window = new DockWindow { Layout = new RootDock { VisibleDockables = [floatingGroup] } };
        var root = new RootDock
        {
            VisibleDockables = [new ProportionalDock { VisibleDockables = [firstGroup, secondGroup] }],
            Windows = [window, window]
        };

        var snapshot = WorkspaceLayoutQuerySnapshot.Capture(root);
        Assert.Same(firstGroup, snapshot.FindDocumentDock(main));
        Assert.Same(floatingGroup, snapshot.FindDocumentDock(floating));
        Assert.Null(snapshot.FindWindow(main));
        Assert.Same(window, snapshot.FindWindow(floating));
        Assert.Null(snapshot.FindDocumentDock(new Document { Id = "same", Title = "同名" }));
        Assert.Same(firstGroup, DockTreeNavigator.FindDockById<DocumentDock>(root, "Documents"));
        AssertEquivalent(root, [main, floating, firstGroup, secondGroup, floatingGroup]);
    }

    [Fact]
    public void 浮窗嵌套与窗口回边仍按原窗口顺序寻找承载者()
    {
        var main = new Document();
        var page = new Document();
        var tool = new Tool();
        var root = new RootDock { VisibleDockables = [new DocumentDock { VisibleDockables = [main] }] };
        var firstRoot = new RootDock { VisibleDockables = [new DocumentDock { VisibleDockables = [page] }] };
        var secondRoot = new RootDock { VisibleDockables = [new ToolDock { VisibleDockables = [tool] }] };
        var first = new DockWindow { Layout = firstRoot };
        var second = new DockWindow { Layout = secondRoot };
        var back = new DockWindow { Layout = root };
        root.Windows = [first];
        firstRoot.Windows = [second];
        secondRoot.Windows = [back];

        var snapshot = WorkspaceLayoutQuerySnapshot.Capture(root);
        Assert.Same(first, snapshot.FindWindow(page));
        Assert.Same(second, snapshot.FindWindow(tool));
        // 此人工回边使主树也被一个窗口声明引用；等价重构必须保留旧查找结果，不能自行修复拓扑。
        Assert.Same(back, snapshot.FindWindow(main));
        AssertEquivalent(root, [main, page, tool, root, firstRoot, secondRoot]);
    }

    [Fact]
    public void 窗口声明顺序优先于工作区DFS首次到达的窗口()
    {
        var shared = new Document();
        var group = new DocumentDock { VisibleDockables = [shared] };
        var sharedLayout = new RootDock { VisibleDockables = [group] };
        var first = new DockWindow { Layout = sharedLayout };
        var visitedEarlier = new DockWindow { Layout = sharedLayout };
        var nested = new RootDock { Windows = [visitedEarlier] };
        var root = new RootDock { VisibleDockables = [nested], Windows = [first] };

        var snapshot = WorkspaceLayoutQuerySnapshot.Capture(root);
        Assert.Same(first, snapshot.FindWindow(shared));
        Assert.Same(group, snapshot.FindDocumentDock(shared));
        AssertEquivalent(root, [shared, sharedLayout, nested]);
    }

    [Fact]
    public void 隐藏四向固定可见和活动关系分别捕获且查询不修复重叠()
    {
        var tools = Enumerable.Range(0, 5).Select(_ => new Tool()).ToArray();
        var dock = new ToolDock { VisibleDockables = [.. tools], ActiveDockable = tools[4] };
        var root = new RootDock
        {
            VisibleDockables = [dock], HiddenDockables = [tools[0]],
            LeftPinnedDockables = [tools[0]], RightPinnedDockables = [tools[1]],
            TopPinnedDockables = [tools[2]], BottomPinnedDockables = [tools[3]]
        };
        var changes = 0;
        root.PropertyChanged += (_, _) => changes++;
        dock.PropertyChanged += (_, _) => changes++;

        var snapshot = WorkspaceLayoutQuerySnapshot.Capture(root);
        Assert.True(snapshot.IsHidden(tools[0]));
        Assert.All(tools.Take(4), item => Assert.True(snapshot.IsPinned(item)));
        Assert.All(tools, item => Assert.True(snapshot.IsDocked(item)));
        Assert.True(snapshot.IsActive(tools[4]));
        Assert.False(snapshot.IsPinned(tools[4]));
        Assert.False(snapshot.IsActive(tools[0]));
        AssertEquivalent(root, tools);
        Assert.Equal(0, changes);
        Assert.Equal(tools, dock.VisibleDockables);
        Assert.Same(tools[0], Assert.Single(root.HiddenDockables));
    }

    [Fact]
    public void 下一次捕获看到移动隐藏和移除而旧关系保持不变()
    {
        var page = new Document();
        var tool = new Tool();
        var documents = new DocumentDock { VisibleDockables = [page] };
        var tools = new ToolDock { VisibleDockables = [tool], ActiveDockable = tool };
        var root = new RootDock { VisibleDockables = [documents, tools] };
        var before = WorkspaceLayoutQuerySnapshot.Capture(root);

        root.VisibleDockables.Remove(documents);
        var window = new DockWindow { Layout = new RootDock { VisibleDockables = [documents] } };
        root.Windows = [window];
        tools.VisibleDockables.Clear();
        tools.ActiveDockable = null;
        root.HiddenDockables = [tool];
        var after = WorkspaceLayoutQuerySnapshot.Capture(root);
        Assert.Null(before.FindWindow(page));
        Assert.True(before.IsDocked(tool));
        Assert.True(before.IsActive(tool));
        Assert.False(before.IsHidden(tool));
        Assert.Same(window, after.FindWindow(page));
        Assert.Same(documents, after.FindDocumentDock(page));
        Assert.True(after.IsHidden(tool));
        Assert.False(after.IsDocked(tool));
        Assert.False(after.IsActive(tool));

        documents.VisibleDockables.Clear();
        root.HiddenDockables.Clear();
        var removed = WorkspaceLayoutQuerySnapshot.Capture(root);
        Assert.Null(removed.FindDocumentDock(page));
        Assert.Null(removed.FindWindow(page));
        Assert.False(removed.IsHidden(tool));
        Assert.Same(documents, after.FindDocumentDock(page));
    }

    [Fact]
    public void 工具读取保留隐藏优先级并在下一次查询看到恢复与固定()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var root = context.Workspace.RootDock!;
        var tool = context.Workspace.CreatedTools.First().Value;
        // 生产骨架包含外层根和内层窗口根；先清除初始化时的隐藏关系再构造本例的重叠输入。
        DockTreeNavigator.RemoveFromHiddenDockables(root, tool);
        var dock = new ToolDock { VisibleDockables = [tool], ActiveDockable = tool };
        root.VisibleDockables!.Add(dock);
        root.HiddenDockables = [tool];
        root.LeftPinnedDockables = [tool];
        var query = context.Provider.GetRequiredService<ToolWorkspaceReadModel>();
        ToolWorkspaceState Read() => query.Capture().Single(item => item.ToolId == tool.Id);

        Assert.Equal(ToolLayoutState.Hidden, Read().LayoutState);
        Assert.False(Read().IsVisible);
        root.HiddenDockables.Clear();
        Assert.Equal(ToolLayoutState.AutoHidden, Read().LayoutState);
        root.LeftPinnedDockables.Clear();
        Assert.Equal(ToolLayoutState.Docked, Read().LayoutState);
        Assert.True(Read().IsActive);
        root.VisibleDockables.Remove(dock);
        root.Windows = [new DockWindow { Layout = new RootDock { VisibleDockables = [dock] } }];
        Assert.Equal(ToolLayoutState.Floating, Read().LayoutState);
        Assert.Same(tool, context.Workspace.CreatedTools[tool.Id]);
    }

    [Fact]
    public void 有限大布局与原查询一致并记录关系查询采样()
    {
        var pages = new List<Document>();
        var tools = new List<Tool>();
        RootDock MakeRoot()
        {
            var localPages = Enumerable.Range(0, 30).Select(_ => new Document { Id = "same" }).ToArray();
            var localTools = Enumerable.Range(0, 10).Select(_ => new Tool { Id = "same" }).ToArray();
            pages.AddRange(localPages);
            tools.AddRange(localTools);
            return new RootDock { VisibleDockables = [new ProportionalDock { VisibleDockables =
                [new DocumentDock { VisibleDockables = [.. localPages] },
                 new ToolDock { VisibleDockables = [.. localTools], ActiveDockable = localTools[0] }] }] };
        }
        var root = MakeRoot();
        root.Windows = [.. Enumerable.Range(0, 8).Select(_ => new DockWindow { Layout = MakeRoot() })];
        AssertEquivalent(root, [.. pages, .. tools]);

        // 只采样本次改造的关系查找，不把模型创建、UI 渲染或机器毫秒作为通过门槛。
        // 旧入口每页查两次成员、每工具查一次窗口；新入口一次建索引再做相同次数的读取。
        int Original()
        {
            var found = 0;
            foreach (var page in pages)
            {
                if (DockTreeNavigator.FindDocumentDock(root, page) is not null) found++;
                if (DockTreeNavigator.FindDocumentDock(root, page) is not null) found++;
            }
            foreach (var tool in tools) if (DockTreeNavigator.FindWindow(root, tool) is not null) found++;
            return found;
        }
        int Indexed()
        {
            var snapshot = WorkspaceLayoutQuerySnapshot.Capture(root);
            var found = 0;
            foreach (var page in pages)
            {
                if (snapshot.FindDocumentDock(page) is not null) found++;
                if (snapshot.FindDocumentDock(page) is not null) found++;
            }
            foreach (var tool in tools) if (snapshot.FindWindow(tool) is not null) found++;
            return found;
        }
        const int expected = 270 * 2 + 80;
        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(expected, Original());
            Assert.Equal(expected, Indexed());
        }
        var original = new List<double>();
        var indexed = new List<double>();
        for (var index = 0; index < 7; index++)
        {
            // 交替先后顺序降低固定执行次序的影响；结果逐轮断言，时间仅作为开发观察。
            if (index % 2 == 0) { Sample(Original, original); Sample(Indexed, indexed); }
            else { Sample(Indexed, indexed); Sample(Original, original); }
        }
        output.WriteLine($"关系查询采样：270 页面、90 工具、8 浮窗、396 节点；预热 3 轮、采样 7 轮。");
        output.WriteLine($"旧遍历毫秒：{string.Join(", ", original.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        output.WriteLine($"快照毫秒：{string.Join(", ", indexed.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        void Sample(Func<int> query, List<double> times)
        {
            var timer = Stopwatch.StartNew();
            var actual = query();
            timer.Stop();
            times.Add(timer.Elapsed.TotalMilliseconds);
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>参照直接沿用未修改的旧查找及集合扫描；不读取新快照内部状态。</summary>
    private static void AssertEquivalent(RootDock root, IEnumerable<IDockable> targets)
    {
        var snapshot = WorkspaceLayoutQuerySnapshot.Capture(root);
        var nodes = DockTreeNavigator.EnumerateWorkspace(root).ToArray();
        var roots = nodes.OfType<IRootDock>().ToArray();
        foreach (var target in targets)
        {
            Assert.Same(DockTreeNavigator.FindDocumentDock(root, target), snapshot.FindDocumentDock(target));
            Assert.Same(DockTreeNavigator.FindWindow(root, target), snapshot.FindWindow(target));
            Assert.Equal(roots.Any(item => item.HiddenDockables?.Contains(target) == true), snapshot.IsHidden(target));
            Assert.Equal(DockTreeNavigator.IsToolPinned(root, target), snapshot.IsPinned(target));
            Assert.Equal(nodes.OfType<IDock>().Any(item => item.VisibleDockables?.Contains(target) == true), snapshot.IsDocked(target));
            Assert.Equal(nodes.OfType<IDock>().Any(item => ReferenceEquals(item.ActiveDockable, target)), snapshot.IsActive(target));
        }
    }
}
