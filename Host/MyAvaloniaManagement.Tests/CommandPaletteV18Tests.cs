using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.PluginSdk;
using System.ComponentModel;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.Commands.Catalog;

namespace MyAvaloniaManagement.Tests;

/// <summary>通过真实工作区验证分组改变阅读顺序，但不合并真实页面或创建入口。</summary>
public sealed class CommandPaletteV18Tests
{
    [Fact]
    public void 组相关性优先且同组保持连续并以身份稳定打破同名平局()
    {
        var entries = new[] { Item(0, "page", 3), Item(1, "new", 1), Item(2, "tool", 2),
            Item(3, "save", 0), Item(3, "other", 3) };
        var first = WorkbenchPaletteOrdering.Sort(entries);
        Assert.Equal(["save", "other", "new", "tool", "page"], first.Select(item => item.DisplayName));
        Assert.Equal(first, WorkbenchPaletteOrdering.Sort(entries.Reverse()));
        Assert.Equal(4, first.Count(item => item.StartsGroup));
        var equal = WorkbenchPaletteOrdering.Sort(entries.Select(item => item with { MatchRank = 0 }));
        Assert.Equal([0, 1, 2, 3, 3], equal.Select(item => item.Identity.Order));
    }

    [Fact]
    public void 同等级可用项优先但状态刷新保留原顺序而新查询应用新顺序()
    {
        var a = Item(3, "a", 0) with { IsEnabled = false };
        var b = Item(3, "b", 0);
        var previous = WorkbenchPaletteOrdering.Sort([a, b]);
        Assert.Equal("b", previous[0].DisplayName);
        var current = WorkbenchPaletteOrdering.Sort([a with { IsEnabled = true }, b with { IsEnabled = false }]);
        Assert.Equal("a", current[0].DisplayName);
        var refreshed = WorkbenchPaletteOrdering.PreserveOrder(previous, current);
        Assert.Equal("b", refreshed[0].DisplayName);
        Assert.False(refreshed[0].IsEnabled);
        Assert.Single(refreshed, item => item.StartsGroup);
        Assert.Equal(current.Take(1), WorkbenchPaletteOrdering.PreserveOrder(previous, current.Take(1).ToArray()));
    }

    [Fact]
    public void 禁用原因无事实时使用通用说明且命令名称不被推断为完成结果()
    {
        var entry = Item(3, "预览小说正文导出", 0) with { IsEnabled = false };
        Assert.Equal("当前状态下不可用", entry.EnterHint);
        Assert.Equal("没有可用连接", (entry with { UnavailableReason = "没有可用连接" }).EnterHint);
        Assert.Equal("Enter 执行：预览小说正文导出", (entry with { IsEnabled = true }).EnterHint);
        Assert.Equal(string.Empty, entry.LeadingAction);
    }

    [Fact]
    public void 四类实际投影包含身份动作和真实原因且重复查询不创建页面()
    {
        var probe = new DocumentTestProbe();
        using var context = DocumentTestContext.Create(services => services.AddSingleton(probe));
        _ = context.CreateMainWindowViewModel();
        var palette = context.Provider.GetRequiredService<WorkbenchCommandPresentation>().Palette;
        context.Workspace.TryActivatePage(context.Workspace.GetDocuments().Single().PageId);
        var original = palette.GetItems(null);
        Assert.Equal(4, original.Count(item => item.StartsGroup));
        Assert.Equal(original.Count, original.Select(item => item.StableKey).Distinct().Count());
        Assert.All(original, item => Assert.NotEmpty(item.SourceText));
        Assert.Contains(original, item => item.Identity is PagePaletteIdentity && item.InstanceText.Contains("当前") && item.EnterHint.Contains("回到"));
        Assert.Contains(original, item => item.Identity is FunctionPaletteIdentity && item.EnterHint.Contains("新开"));
        Assert.Contains(original, item => item.Identity is ToolPaletteIdentity && item.LeadingAction == "显示");
        Assert.Equal("当前页面不支持保存", original.Single(item => item.CommandId == HostWorkbenchCommandIds.SaveDocument).DisabledText);
        Assert.Equal(original.Select(item => item.StableKey), palette.GetItems("").Select(item => item.StableKey));
        Assert.Empty(probe.ActivationContexts);
        Assert.Single(context.Workspace.GetDocuments());
    }

    [Fact]
    public async Task 同组弱匹配页面仍连续排列而不会被同名创建入口穿插()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var exact = await context.Workspace.CreateAndPublishDocumentAsync(
            TestDocumentIds.TypeId, new NewDocumentActivation("示例入口"));
        var prefix = await context.Workspace.CreateAndPublishDocumentAsync(
            TestDocumentIds.TypeId, new NewDocumentActivation("示例入口第二页"));
        var items = context.Provider.GetRequiredService<WorkbenchCommandPresentation>().Palette.GetItems("示例入口");

        var pages = items.TakeWhile(item => item.Identity is PagePaletteIdentity).ToArray();
        Assert.Contains(pages, item => item.Identity == new PagePaletteIdentity(exact.PageId));
        Assert.Contains(pages, item => item.Identity == new PagePaletteIdentity(prefix.PageId));
        Assert.Contains(items.Skip(pages.Length), item => item.Identity is FunctionPaletteIdentity);
        Assert.DoesNotContain(items.Skip(pages.Length), item => item.Identity is PagePaletteIdentity);
        Assert.Equal(items.Count, items.Select(item => item.StableKey).Distinct().Count());
    }

    private static WorkbenchCommandPaletteProjectionEntry Item(int kind, string name, int rank)
    {
        WorkbenchPaletteIdentity identity = kind switch
        {
            0 => new PagePaletteIdentity(new WorkspacePageId(Guid.Parse("00000000-0000-0000-0000-000000000001"))),
            1 => new FunctionPaletteIdentity(new("myavalonia.test.document.new"), null),
            2 => new ToolPaletteIdentity(new("myavalonia.test.tool.panel")),
            _ => new CommandPaletteIdentity(new("myavalonia.test.command." + (name == "other" ? "other" : name == "b" ? "b" : "a")))
        };
        return new(identity, name, "", "", true, new NoOperation()) { MatchRank = rank };
    }

    private sealed class NoOperation : IWorkbenchPresentationCommandBinding
    {
        public bool IsEnabled => true;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => throw new InvalidOperationException("排序不得执行命令。");
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }
}
