using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证操作前捕获、布局改变后提交的可观察归属；不依赖原生窗口或全局焦点。</summary>
public sealed class DocumentCreationTargetTests
{
    [Fact]
    public void N02N03最近文档组按窗口隔离且工具激活不覆盖记录()
    {
        var main = Root(out var primary, out var secondary);
        var floating = Root(out var first, out var second);
        main.Windows = [new DockWindow { Layout = floating }];
        var resolver = new DocumentCreationTargetResolver();
        resolver.Remember(main, Page(secondary));
        resolver.Remember(main, Page(second));
        resolver.Remember(main, new Tool());
        Assert.Same(secondary, resolver.Capture(main).PreferredDock);
        Assert.Same(second, resolver.Capture(floating).PreferredDock);
        Assert.Same(primary, resolver.Resolve(main, primary, null, _ => true));
        Assert.NotSame(first, resolver.Capture(floating).PreferredDock);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("move")]
    [InlineData("drop")]
    [InlineData("fill")]
    public void N06原组失效或迁往别窗时优先同来源其他组(string change)
    {
        var main = Root(out var primary, out _);
        var source = Root(out var first, out var retained);
        var other = Root(out _, out _);
        main.Windows = [new DockWindow { Layout = source }, new DockWindow { Layout = other }];
        var resolver = new DocumentCreationTargetResolver();
        var target = resolver.Capture(source);
        if (change is "remove" or "move") source.VisibleDockables!.Remove(first);
        if (change == "move") { other.VisibleDockables!.Add(first); first.Owner = other; }
        if (change == "drop") first.CanDrop = false;
        if (change == "fill") first.AllowedDropOperations = DockOperationMask.Left;
        Assert.Same(retained, resolver.Resolve(main, primary, target, _ => true));
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("closing")]
    [InlineData("empty")]
    public void N04N06来源失效只回退主窗有效组而不选其他浮窗(string change)
    {
        var main = Root(out var primary, out var secondary);
        var source = Root(out _, out _);
        var other = Root(out var unrelated, out _);
        var sourceWindow = new DockWindow { Layout = source };
        main.Windows = [sourceWindow, new DockWindow { Layout = other }];
        var resolver = new DocumentCreationTargetResolver();
        resolver.Remember(main, Page(secondary));
        var target = resolver.Capture(source);
        resolver.Remember(main, Page(unrelated));
        if (change == "removed") main.Windows.Remove(sourceWindow);
        if (change == "empty") source.VisibleDockables!.Clear();
        Assert.Same(secondary, resolver.Resolve(main, primary, target,
            root => change != "closing" || !ReferenceEquals(root, source)));
        secondary.CanDrop = false;
        Assert.Same(primary, resolver.Resolve(main, primary, target,
            root => change != "closing" || !ReferenceEquals(root, source)));
    }

    [Fact]
    public void N07没有安全目标时拒绝且相同标识不能替代来源身份()
    {
        var main = Root(out var primary, out _);
        var old = Root(out _, out _);
        var replacement = Root(out _, out _);
        old.Id = replacement.Id = "same-id";
        main.Windows = [new DockWindow { Layout = replacement }];
        var resolver = new DocumentCreationTargetResolver();
        var target = resolver.Capture(old);
        Assert.Same(primary, resolver.Resolve(main, primary, target, _ => true));
        Assert.Null(resolver.Resolve(main, primary, target, _ => false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task N07N08初始化失败或等待期间退出均不发布并仅释放一次(bool shutdown)
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var probe = context.Provider.GetRequiredService<DocumentTestProbe>();
        probe.InitializeBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!shutdown) probe.InitializeException = new InvalidOperationException("初始化失败");
        var service = context.Provider.GetRequiredService<DocumentPersistenceCoordinator>();
        var request = service.CreateDocumentAsync(TestDocumentIds.TypeId, target:
            context.Workspace.CaptureDocumentCreationTarget(context.Workspace.RootDock!));
        Assert.False(request.IsCompleted);
        if (shutdown) context.Workspace.BeginShutdown();
        probe.InitializeBlocker.SetResult();
        var result = await request;
        Assert.NotEmpty(result.Error);
        Assert.Null(result.CreatedPageId);
        Assert.Single(context.Workspace.GetDocuments());
        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public async Task N08目标插入后失败撤销部分写入且重复发布不移动原页面()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var root = context.Workspace.RootDock!;
        var factory = context.Workspace.DockFactory;
        var failed = new FailingDock { VisibleDockables = factory.CreateList<IDockable>() };
        root.VisibleDockables!.Add(failed);
        factory.InitDockable(failed, root);
        var result = await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId, target: new(root, failed));
        Assert.NotEmpty(result.Error);
        Assert.Empty(failed.VisibleDockables!);
        Assert.Single(context.Workspace.GetDocuments());
        Assert.Single(context.Workspace.GetOpenPages());
        Assert.Equal(1, context.Provider.GetRequiredService<DocumentTestProbe>().DisposeCount);
        var original = context.Workspace.GetDocuments().Single();
        var owner = original.Owner;
        Assert.Throws<InvalidOperationException>(() => context.Workspace.PublishDocument(original, new(root, failed)));
        Assert.Same(owner, original.Owner);
        Assert.Single(context.Workspace.GetOpenPages());
    }

    [Fact]
    public async Task N12无显式目标的旧创建入口保持主默认组()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var root = context.Workspace.RootDock!;
        var original = context.Workspace.GetDocuments().Single();
        var primary = original.Owner;
        var other = new DocumentDock { VisibleDockables = context.Workspace.DockFactory.CreateList<IDockable>() };
        root.VisibleDockables!.Add(other);
        context.Workspace.DockFactory.InitDockable(other, root);
        var local = await context.Workspace.CreateAndPublishDocumentAsync(TestDocumentIds.TypeId, new NewDocumentActivation("分组"), new(root, other));
        Assert.Same(other, local.Owner);
        var defaultPage = await context.Workspace.CreateAndPublishDocumentAsync(TestDocumentIds.TypeId, new NewDocumentActivation("默认"));
        Assert.Same(primary, defaultPage.Owner);
    }

    private static RootDock Root(out DocumentDock first, out DocumentDock second)
    {
        first = new DocumentDock { VisibleDockables = [] };
        second = new DocumentDock { VisibleDockables = [] };
        var root = new RootDock { VisibleDockables = [first, second] };
        first.Owner = root; second.Owner = root;
        return root;
    }

    private static Document Page(DocumentDock group)
    {
        var page = new Document { Owner = group };
        group.VisibleDockables!.Add(page);
        return page;
    }

    /// <summary>模拟 Dock 接受项后外部通知失败，验证提交回滚，而非模拟初始化失败。</summary>
    private sealed class FailingDock : DocumentDock
    {
        public override void AddDocument(IDockable document)
        {
            base.AddDocument(document);
            throw new InvalidOperationException("插入后失败");
        }
    }
}
