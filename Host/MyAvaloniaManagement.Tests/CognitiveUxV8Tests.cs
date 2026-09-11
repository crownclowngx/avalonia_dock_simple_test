using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Search;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>用真实工作区和插件初始化探针验证 V8 身份、发现与执行边界。</summary>
public sealed class CognitiveUxV8Tests
{
    [Theory]
    [InlineData("Excel", " excel ", "", 0)]
    [InlineData("Excel 审核", "excel", "", 1)]
    [InlineData("批量 Excel 审核", "excel", "", 2)]
    [InlineData("报表", "excel", "支持 EXCEL 数据", 3)]
    [InlineData("报表", "excle", "支持 Excel 数据", int.MaxValue)]
    [InlineData("报表", "　", "", 0)]
    public void 查询按名称精确前缀包含说明匹配排序且不做模糊猜测(string name, string query, string details, int rank) =>
        Assert.Equal(rank, WorkbenchTextMatch.Rank(name, query, details));

    [Fact]
    public void 功能目录与工具目录共用匹配优先级且同名不同来源和意图不合并()
    {
        var ownerA = new PluginId("myavalonia.plugin.v8-a");
        var ownerB = new PluginId("myavalonia.plugin.v8-b");
        DocumentCreationMenuEntry Entry(string suffix, string name, string description, PluginId owner, CreationIntentId? intent = null) =>
            new(new($"{owner.Value}.document.{suffix}"), intent, name, description, "", "审核", owner);
        var directory = new DocumentCreationDirectory([
            Entry("details", "其他", "Excel 支持", ownerA), Entry("contains", "批量 Excel", "", ownerA),
            Entry("prefix", "Excel 审核", "", ownerA), Entry("exact", "Excel", "", ownerA),
            Entry("exact", "Excel", "", ownerB), Entry("exact", "Excel", "", ownerA, new("fast"))]);
        Assert.Equal(["Excel", "Excel", "Excel", "Excel 审核", "批量 Excel", "其他"], directory.Filter(null, " eXcEl ").Select(item => item.DisplayName));
        Assert.Equal(6, directory.Items.Select(item => new FunctionPaletteIdentity(item.Entry.DocumentTypeId, item.Entry.CreationIntentId).StableKey).Distinct().Count());
        using var context = new TestHostContext();
        var query = context.Provider.GetRequiredService<MyAvaloniaManagement.Business.ToolCenter.ToolCenterQuery>();
        var tools = directory.Items.Select((item, index) => new MyAvaloniaManagement.Business.ToolCenter.ToolCenterItem(
            new MyAvaloniaManagement.Models.Tools.ToolWorkspaceState($"{item.Entry.OwnerId!.Value}.tool.item-{index}", item.DisplayName, false, false)
            { Description = item.Description, OwnerId = item.Entry.OwnerId }, "审核", "审核", false)).ToArray();
        Assert.Equal(["Excel", "Excel", "Excel 审核", "批量 Excel", "其他"], query.Filter(tools, "favorites", "Excel", ownerA.Value).Select(item => item.DisplayName));
    }

    [Fact]
    public void 功能发现使用创建目录无需Command并且重复查询不初始化页面()
    {
        var probe = new DocumentTestProbe();
        using var context = DocumentTestContext.Create(services => services.AddSingleton(probe));
        _ = context.CreateMainWindowViewModel();
        var palette = context.Provider.GetRequiredService<WorkbenchCommandPresentation>().Palette;
        var directory = context.Provider.GetRequiredService<DocumentCreationMenuQuery>().ReadDirectory();
        var expected = directory.Items.Select(item => new FunctionPaletteIdentity(item.Entry.DocumentTypeId, item.Entry.CreationIntentId)).ToHashSet();
        for (var count = 0; count < 5; count++)
        {
            var items = palette.GetItems(null);
            Assert.True(expected.SetEquals(items.Select(item => item.Identity).OfType<FunctionPaletteIdentity>()));
            Assert.Equal(items.Count, items.Select(item => item.StableKey).Distinct().Count());
            Assert.Contains(items, item => item.Identity is PagePaletteIdentity);
            Assert.Contains(items, item => item.Identity is ToolPaletteIdentity);
            Assert.Contains(items, item => item.Identity is CommandPaletteIdentity);
        }
        Assert.Empty(probe.ActivationContexts);
        Assert.Single(context.Workspace.GetDocuments());
    }

    [Fact]
    public async Task 异步功能只提交一次且失败回滚后可携带原Intent重试()
    {
        var blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new DocumentTestProbe { InitializeBlocker = blocker, InitializeException = new InvalidOperationException("内部异常不得直接显示") };
        using var context = DocumentTestContext.Create(services => services.AddSingleton(probe));
        _ = context.CreateMainWindowViewModel();
        var palette = context.Provider.GetRequiredService<WorkbenchCommandPresentation>().Palette;
        var item = Assert.Single(palette.GetItems("示例入口"));
        var command = Assert.IsType<WorkspacePaletteCommand>(item.Command);
        var first = command.ExecuteAsync().AsTask();
        Assert.False(first.IsCompleted);
        Assert.False(command.CanExecute(null));
        Assert.Contains("等待", await command.ExecuteAsync());
        Assert.Single(probe.ActivationContexts);
        blocker.SetResult();
        Assert.Contains("未新增页面", await first);
        Assert.Single(context.Workspace.GetDocuments());
        Assert.Equal(1, probe.DisposeCount);
        probe.InitializeException = null;
        Assert.Equal(string.Empty, await command.ExecuteAsync());
        Assert.Equal(2, context.Workspace.GetDocuments().Count);
        Assert.All(probe.ActivationContexts, activation => Assert.Equal(new CreationIntentId("sample-intent"), Assert.IsType<NewDocumentActivation>(activation).CreationIntentId));
    }

    [Fact]
    public async Task 同名页面按运行时身份切换且关闭旧项绝不替换成同名新页面()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var first = await context.Workspace.CreateAndPublishDocumentAsync(TestDocumentIds.TypeId, new NewDocumentActivation("同名"));
        var second = await context.Workspace.CreateAndPublishDocumentAsync(TestDocumentIds.TypeId, new NewDocumentActivation("同名"));
        var firstSnapshot = context.Workspace.GetOpenPages().Single(page => page.Id == first.PageId);
        Assert.True(context.Workspace.TryActivatePage(first.PageId));
        Assert.Same(first, context.Workspace.GetActiveDocument());
        var actions = context.Provider.GetRequiredService<WorkspacePaletteActions>();
        context.Workspace.DockFactory.CloseDockable(first);
        Assert.False(context.Workspace.TryActivatePage(first.PageId));
        Assert.NotEmpty(await actions.ExecuteAsync(new PagePaletteIdentity(first.PageId)));
        Assert.DoesNotContain(context.Workspace.GetOpenPages(), page => page.Id == first.PageId);
        var third = await context.Workspace.CreateAndPublishDocumentAsync(TestDocumentIds.TypeId, new NewDocumentActivation("同名"));
        Assert.NotEqual(first.PageId, third.PageId);
        Assert.True(context.Workspace.GetOpenPages().Single(page => page.Id == third.PageId).Sequence > firstSnapshot.Sequence);
        Assert.True(context.Workspace.TryActivatePage(second.PageId));
        Assert.Same(second, context.Workspace.GetActiveDocument());
    }

    [Fact]
    public async Task 关闭等待和工作区退出均阻止旧搜索项定位或创建()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var page = await context.Workspace.CreateAndPublishDocumentAsync(TestDocumentIds.TypeId, new NewDocumentActivation("等待关闭"));
        var leases = context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
        Assert.True(leases.TryAcquire(page, out var lease));
        context.Workspace.DockFactory.CloseDockable(page);
        Assert.False(context.Workspace.GetOpenPages().Single(item => item.Id == page.PageId).CanActivate);
        Assert.False(context.Workspace.TryActivatePage(page.PageId));
        lease!.Dispose();
        context.Workspace.BeginShutdown();
        var actions = context.Provider.GetRequiredService<WorkspacePaletteActions>();
        Assert.False(actions.CanExecute(new FunctionPaletteIdentity(TestDocumentIds.TypeId, new("sample-intent"))));
        Assert.All(context.Workspace.GetOpenPages(), item => Assert.False(item.CanActivate));
    }

    [Fact]
    public async Task 页面标题快照刷新但身份序号不变且观察者异常不影响发布释放()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var notifications = 0;
        context.Workspace.PagesChanged += (_, _) => throw new InvalidOperationException("展示观察者故障");
        context.Workspace.PagesChanged += (_, _) => notifications++;
        var page = await context.Workspace.CreateAndPublishDocumentAsync(TestDocumentIds.TypeId, new NewDocumentActivation("初始"));
        var old = context.Workspace.GetOpenPages().Single(item => item.Id == page.PageId);
        page.CommitHostTitle("重命名后");
        page.SetHostRequiresSave(true);
        var updated = context.Workspace.GetOpenPages().Single(item => item.Id == page.PageId);
        Assert.Equal("重命名后", updated.Title);
        Assert.True(updated.IsModified);
        Assert.Contains("未保存修改", updated.Description);
        page.SetHostRequiresSave(false);
        Assert.Equal(old.Sequence, updated.Sequence);
        Assert.True(notifications >= 2);
        context.Workspace.DockFactory.CloseDockable(page);
        var afterClose = notifications;
        page.CommitHostTitle("关闭后迟到的标题");
        Assert.Equal(afterClose, notifications);
        Assert.DoesNotContain(context.Workspace.GetOpenPages(), item => item.Id == page.PageId);
    }
}
