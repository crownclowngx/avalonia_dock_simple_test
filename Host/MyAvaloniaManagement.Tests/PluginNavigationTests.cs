using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Navigation;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels.FunctionCenter;
using MyAvaloniaManagement.ViewModels.Tools;

namespace MyAvaloniaManagement.Tests;

/// <summary>以可观察的分类、身份和创建结果验证 V6；不按实现细节模拟字典或绑定代码。</summary>
public sealed class PluginNavigationTests
{
    [Theory]
    [InlineData("图像分析", true, 1, "图像分析")]
    [InlineData("大唐-会计", true, 1, "大唐-会计")]
    [InlineData("闲才 / 文本检测", true, 2, "闲才/文本检测")]
    [InlineData("闲才/文本检测/批量", true, 3, "闲才/文本检测/批量")]
    [InlineData("闲才//文本检测", false, 1, "闲才//文本检测")]
    [InlineData("/闲才", false, 1, "/闲才")]
    [InlineData("闲才/", false, 1, "闲才/")]
    [InlineData("闲才/ /文本检测", false, 1, "闲才/ /文本检测")]
    public void 分类路径保留单层兼容并整体回退非法空段(string value, bool valid, int depth, string canonical)
    {
        var path = DocumentCategoryPath.Parse(value);
        Assert.Equal(valid, path.IsValid);
        Assert.Equal(depth, path.Segments.Count);
        Assert.Equal(canonical, path.CanonicalPath);
    }

    [Fact]
    public void 分类树合并前缀保留直属叶子并统计创建意图()
    {
        var entries = new[]
        {
            Entry("闲才/文本检测", "文本审核", "first"),
            Entry("闲才 / 文本检测", "快速审核", "second"),
            Entry("闲才/图片检测", "图片审核"),
            Entry("闲才", "业务首页"),
            Entry("其他/文本检测", "其他审核"),
        };
        var directory = new DocumentCreationDirectory(entries);
        var root = Assert.Single(directory.Categories, item => item.Name == "闲才");
        Assert.Equal(4, root.EntryCount);
        Assert.Equal("业务首页", Assert.Single(root.Items).DisplayName);
        var text = Assert.Single(root.Children, item => item.Name == "文本检测");
        Assert.Equal(new[] { "first", "second" }, text.Items.Select(item => item.Entry.CreationIntentId!.Value));
        Assert.Equal(entries, directory.Items.Select(item => item.Entry));
        Assert.NotEqual(text.Path.Key, directory.Categories.Single(item => item.Name == "其他").Children.Single().Path.Key);
        Assert.Equal(4, directory.Filter(root.Path, "").Count);
        Assert.Equal(3, directory.Filter(null, "文本检测").Count);
        Assert.Equal(5, directory.Filter(null, "说明").Count);
        Assert.Empty(directory.Filter(null, "不存在的功能"));
    }

    [Fact]
    public void 后代筛选使用分段边界而非字符串前缀()
    {
        var directory = new DocumentCreationDirectory([Entry("业务/A", "A"), Entry("业务/AB", "AB"), Entry("业务/A/子组", "子组")]);
        Assert.Equal(2, directory.Filter(DocumentCategoryPath.Parse("业务/A"), "").Count);
        Assert.Single(directory.Filter(DocumentCategoryPath.Parse("业务/A"), "AB"));
        Assert.Single(directory.Filter(null, "a/子组"));
    }

    [Fact]
    public void 非法路径不会丢失入口且回退节点与合法层级身份独立()
    {
        var errors = new List<string>();
        var directory = new DocumentCreationDirectory([Entry("A//B", "错误路径"), Entry("A/B", "正常路径")], errors.Add);
        Assert.Equal("A//B", Assert.Single(errors));
        Assert.Equal(2, directory.Categories.Count);
        Assert.Equal(2, directory.Categories.Sum(item => item.EntryCount));
        Assert.Single(directory.Filter(directory.Categories.Single(item => item.Name == "A//B").Path, ""));
    }

    [Fact]
    public void 六十四级路径可展示且两个视图的展开状态独立()
    {
        var directory = new DocumentCreationDirectory([Entry(string.Join('/', Enumerable.Range(0, 64)), "深层入口")]);
        var tool = NavigationTreeNode.Build(directory.Categories, true);
        var center = NavigationTreeNode.Build(directory.Categories, false);
        tool[0].IsExpanded = true;
        Assert.False(center[0].IsExpanded);
        Assert.Equal(65, NavigationTreeNode.Flatten(tool).Count());
        Assert.Equal(64, NavigationTreeNode.Flatten(center).Count());
        var refreshed = NavigationTreeNode.Build(directory.Categories, true, tool);
        Assert.True(refreshed[0].IsExpanded);
        Assert.NotSame(tool[0], refreshed[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("builtin:unknown")]
    [InlineData("https://example.com/icon.png")]
    [InlineData("C:/image.png")]
    public void 未声明或不支持的图标统一回退默认(string? value)
    {
        using var context = new TestHostContext();
        Assert.Equal(HostIconCatalog.DefaultKey,
            context.Provider.GetRequiredService<HostIconCatalog>().Resolve(new(null, value)).Reference);
    }

    [Fact]
    public void 双模式切换保留展开身份与用户显示名称且不会创建文档()
    {
        using var context = DocumentTestContext.Create();
        var store = context.Provider.GetRequiredService<PluginNavigationSettingsStore>();
        store.Save(new(PluginMenuMode.Legacy, "经典目录", "业务树"));
        var tool = context.Provider.GetRequiredService<PlugGroupMenuViewModel>();
        var before = tool.CategoryNodes.SelectMany(group => group.Documents).ToArray();
        tool.CategoryNodes.Single(group => group.CategoryName == "测试").IsExpanded = true;
        Assert.True(tool.IsLegacy);
        Assert.Equal("经典目录", tool.SelectedMode.DisplayName);
        tool.SelectedMode = tool.Modes.Single(mode => mode.Mode == PluginMenuMode.Tree);
        tool.TreeNodes.Single(group => group.DisplayName == "测试").IsExpanded = true;
        Assert.True(tool.IsTree);
        Assert.Equal(before, tool.CategoryNodes.SelectMany(group => group.Documents));
        tool.SelectedMode = tool.Modes.Single(mode => mode.Mode == PluginMenuMode.Legacy);
        Assert.True(tool.CategoryNodes.Single(group => group.CategoryName == "测试").IsExpanded);
        Assert.True(tool.TreeNodes.Single(group => group.DisplayName == "测试").IsExpanded);
        Assert.Equal("业务树", store.Load().TreeName);
        Assert.Equal(PluginMenuMode.Legacy, store.Load().Mode);
        Assert.Empty(context.Provider.GetRequiredService<DocumentTestProbe>().ActivationContexts);
    }

    [Fact]
    public async Task 功能中心创建意图正确并屏蔽重复请求和旧错误()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var probe = context.Provider.GetRequiredService<DocumentTestProbe>();
        probe.InitializeBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = context.Provider.GetRequiredService<DocumentOperationState>();
        state.Apply(DocumentOperationResult.Failure("其他操作的历史错误"));
        using var center = CreateCenter(context);
        Assert.False(center.CanCreate);
        center.SearchText = "示例";
        center.SelectedItem = Assert.Single(center.VisibleItems);
        Assert.Empty(probe.ActivationContexts);
        var created = 0;
        center.Created += (_, _) => created++;
        var first = center.CreateAsync();
        Assert.True(center.IsBusy);
        await center.CreateAsync();
        center.SearchText = "创建中不改变目标";
        Assert.Equal("示例", center.SearchText);
        probe.InitializeBlocker.SetResult();
        await first;
        Assert.Single(probe.ActivationContexts);
        Assert.Equal("sample-intent", Assert.IsType<NewDocumentActivation>(probe.ActivationContexts[0]).CreationIntentId!.Value);
        Assert.Equal(1, created);
        Assert.False(center.HasError);
        Assert.False(state.HasError);
        Assert.False(center.CanCreate);
    }

    [Fact]
    public async Task 创建失败保留筛选和选择并允许原地重试()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var probe = context.Provider.GetRequiredService<DocumentTestProbe>();
        probe.InitializeException = new InvalidOperationException("测试插件失败");
        using var center = CreateCenter(context);
        center.SearchText = "示例";
        center.SelectedItem = Assert.Single(center.VisibleItems);
        var selected = center.SelectedItem;
        await center.CreateAsync();
        Assert.True(center.HasError);
        Assert.True(center.CanCreate);
        Assert.Equal("示例", center.SearchText);
        Assert.Same(selected, center.SelectedItem);
        probe.InitializeException = null;
        await center.CreateAsync();
        Assert.False(center.HasError);
        Assert.Equal(2, probe.ActivationContexts.Count);
    }

    [Fact]
    public void 全局搜索清空恢复分类并清理不可见选择()
    {
        using var context = DocumentTestContext.Create();
        using var center = CreateCenter(context);
        center.SelectedCategory = center.Categories.Single(category => category.DisplayName == "测试");
        center.SelectedItem = Assert.Single(center.VisibleItems);
        center.SearchText = "欢迎";
        Assert.Equal("全部分类中的搜索结果", center.ResultsTitle);
        Assert.Null(center.SelectedItem);
        center.SearchText = "";
        Assert.Equal("测试", center.ResultsTitle);
        Assert.Single(center.VisibleItems);
        center.SearchText = "没有结果";
        Assert.True(center.HasNoResults);
        center.SelectedCategory = center.SelectedCategory;
        Assert.Equal("", center.SearchText);
        Assert.Single(center.VisibleItems);
    }

    private static FunctionCenterViewModel CreateCenter(TestHostContext context) => new(
        context.Provider.GetRequiredService<DocumentCreationMenuQuery>(),
        context.Provider.GetRequiredService<DocumentPersistenceCoordinator>(),
        context.Provider.GetRequiredService<DocumentOperationState>(), context.Provider.GetRequiredService<HostIconRenderer>());

    [Fact]
    public async Task 关闭期间三个创建入口均收到失败结果且不启动插件()
    {
        using var context = DocumentTestContext.Create();
        var tool = context.Provider.GetRequiredService<PlugGroupMenuViewModel>();
        var entry = tool.CategoryNodes.Single(group => group.CategoryName == "测试").Documents.First();
        using var center = CreateCenter(context);
        center.SearchText = "示例";
        center.SelectedItem = Assert.Single(center.VisibleItems);
        context.Provider.GetRequiredService<DocumentOperationGate>().BeginShutdown();
        await tool.CreateDocumentEntryAsync(entry);
        Assert.True(context.Provider.GetRequiredService<DocumentOperationState>().HasError);
        var leaf = NavigationTreeNode.Flatten(tool.TreeNodes).First(node => node.Item?.Entry == entry);
        await tool.ActivateTreeNodeCommand.ExecuteAsync(leaf);
        await center.CreateAsync();
        Assert.True(center.HasError);
        Assert.Empty(context.Provider.GetRequiredService<DocumentTestProbe>().ActivationContexts);
    }

    private static DocumentCreationMenuEntry Entry(string category, string name, string? intent = null) =>
        new(new DocumentTypeId("myavalonia.plugin.navigation-tests.document.main"),
            intent is null ? null : new CreationIntentId(intent), name, "功能说明", "", category);
}
