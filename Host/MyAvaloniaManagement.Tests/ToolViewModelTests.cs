using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.Models.FileSystem;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证文件树、插件菜单和工具管理三个宿主工具 ViewModel。
/// </summary>
public sealed class ToolViewModelTests
{
    [Fact]
    public void 文件树展开折叠和选择会同步状态()
    {
        using var context = new TestHostContext();
        var node = new FileSystemNode(context.TempDirectory);
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            context.EventBus,
            initializeTree: false);

        FileSystemTreeViewModel.ExpandNode(node);
        viewModel.NodeSelected(node);

        Assert.True(node.IsExpanded);
        Assert.Same(node, viewModel.SelectedNode);
        Assert.Equal(node.Path, viewModel.SelectedPath);
        FileSystemTreeViewModel.CollapseNode(node);
        Assert.False(node.IsExpanded);
    }

    [Fact]
    public void 文件树只为存在文件发送打开消息()
    {
        using var context = new TestHostContext();
        var path = Path.Combine(context.TempDirectory, "open.txt");
        context.Storage.AddFile(path, "content");
        var received = new List<string>();
        using var subscription = context.EventBus.Subscribe<OpenFileMessage>(
            message => received.Add(message.FilePath));
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            context.EventBus,
            initializeTree: false);
        viewModel.NodeSelected(new FileSystemNode(path));

        viewModel.OpenFile();

        Assert.Equal([path], received);
        viewModel.NodeSelected(new FileSystemNode(
            Path.Combine(context.TempDirectory, "missing.txt")));
        viewModel.OpenFile();
        Assert.Single(received);
    }

    [Fact]
    public async Task 选择自定义文件夹后文件树只显示该目录()
    {
        using var context = new TestHostContext();
        var folder = Path.Combine(context.TempDirectory, "selected");
        Directory.CreateDirectory(folder);
        context.Storage.FolderPath = folder;
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            context.EventBus,
            initializeTree: false);

        await viewModel.SelectFolder();

        Assert.True(viewModel.ShowCustomFolder);
        Assert.Equal(Path.GetFullPath(folder), viewModel.SelectedFolderPath);
        Assert.Equal(Path.GetFullPath(folder),
            Assert.Single(viewModel.RootNodes).Path);
    }

    [Fact]
    public void 插件分组工具创建文档并切换分类展开()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        _ = context.CreateMainWindowViewModel();
        var viewModel = new PlugGroupMenuViewModel(
            context.Factory,
            new PluginMenuService(context.Factory));
        var category = viewModel.CategoryNodes.Single(node =>
            node.CategoryName == "测试");

        viewModel.ToggleCategoryExpand(category);
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);

        Assert.True(category.IsExpanded);
        var dock = Assert.IsType<DocumentDock>(
            context.Factory.GetDockable<IDocumentDock>("Files"));
        Assert.Contains(dock.VisibleDockables!, item =>
            item is TestSavableDocument);
    }

    [Fact]
    public void 插件分组工具把创建意图传递给策略()
    {
        var strategy = new CapturingIntentStrategy();
        using var context = new TestHostContext(documentStrategies: [strategy]);
        _ = context.CreateMainWindowViewModel();
        var viewModel = new PlugGroupMenuViewModel(context.Factory, new PluginMenuService(context.Factory));
        var entry = viewModel.CategoryNodes
            .SelectMany(node => node.Documents)
            .Single(item => item.DocumentTypeId == CapturingIntentStrategy.TypeId);

        viewModel.CreateDocumentEntry(entry);

        Assert.Equal("personal-source", strategy.LastIntentId?.Value);
    }

    private sealed class CapturingIntentStrategy : IDocumentCreationStrategy, IDocumentCreationIntentProvider
    {
        public static readonly DocumentTypeId TypeId =
            new("myavalonia.host.document.intent-capture");
        public CreationIntentId? LastIntentId { get; private set; }

        public Document CreateDocument(DocumentCreationParams @params)
        {
            LastIntentId = @params.CreationIntentId;
            return new Document();
        }

        public DocumentMetadata GetMetadata() => new(TypeId, "个人来源") { MenuCategory = "测试" };

        public IReadOnlyList<DocumentCreationIntentMetadata> GetCreationIntents() =>
            [new(new CreationIntentId("personal-source"), "个人来源")];
    }

    [Fact]
    public void 工具管理可以隐藏恢复并发送布局消息()
    {
        var tool = new Tool
        {
            Id = "myavalonia.host.tool.closable",
            Title = "可关闭工具",
            CanClose = true
        };
        var strategy = new StubToolStrategy(
            tool,
            new ToolMetadata(
                new ToolTypeId(tool.Id),
                tool.Title!,
                ToolDockSide.Left)
            {
                Description = string.Empty,
                IconPath = string.Empty
            });
        using var context = new TestHostContext(toolStrategies: [strategy]);
        _ = context.CreateMainWindowViewModel();
        var manager = Assert.IsType<ToolManagementViewModel>(
            context.Factory.CreatedTools[DockNameConstant.ToolManagement]);
        var item = manager.ToolItems.Single(candidate =>
            candidate.ToolId == tool.Id);
        var updateCount = 0;
        using var subscription = context.EventBus.Subscribe<UpdateLayoutMessage>(
            _ => updateCount++);

        manager.ToggleToolVisibility(item);
        Assert.False(item.IsVisible);
        Assert.DoesNotContain(
            EnumerateDockables(
                context.Factory.GetToolManagementData()!.RootDock),
            dockable => ReferenceEquals(dockable, tool));

        manager.ToggleToolVisibility(item);
        Assert.True(item.IsVisible);
        Assert.Equal(2, updateCount);
    }

    [Fact]
    public void 工具管理忽略不可关闭项并同步外部隐藏状态()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var manager = Assert.IsType<ToolManagementViewModel>(
            context.Factory.CreatedTools[DockNameConstant.ToolManagement]);
        var item = manager.ToolItems.First(candidate => !candidate.CanClose);
        var before = item.IsVisible;

        manager.ToggleToolVisibility(item);

        Assert.Equal(before, item.IsVisible);
        context.EventBus.Publish(
            new ToolVisibilityChangedMessage("external-change"));
        manager.SyncToolsVisibility();
    }

    [Fact]
    public void PinnedToolRemainsVisibleInManagementAndCanBeHiddenAndRestored()
    {
        var tool = new Tool
        {
            Id = "myavalonia.host.tool.pinned-closable",
            Title = "Pinned Tool",
            CanClose = true
        };
        var strategy = new StubToolStrategy(
            tool,
            new ToolMetadata(
                new ToolTypeId(tool.Id),
                tool.Title!,
                ToolDockSide.Left)
            {
                Description = string.Empty,
                IconPath = string.Empty
            });
        using var context = new TestHostContext(toolStrategies: [strategy]);
        _ = context.CreateMainWindowViewModel();
        var data = context.Factory.GetToolManagementData()!;
        var manager = Assert.IsType<ToolManagementViewModel>(
            context.Factory.CreatedTools[DockNameConstant.ToolManagement]);
        var item = manager.ToolItems.Single(candidate => candidate.ToolId == tool.Id);

        context.Factory.PinDockable(tool);
        manager.SyncToolsVisibility();

        Assert.True(item.IsVisible);
        var owningRoot = context.Factory.FindRoot(tool, _ => true)!;
        Assert.Contains(tool, owningRoot.LeftPinnedDockables!);

        manager.ToggleToolVisibility(item);

        Assert.False(item.IsVisible);
        Assert.DoesNotContain(tool, owningRoot.LeftPinnedDockables!);
        Assert.Contains(tool, owningRoot.HiddenDockables!);

        manager.ToggleToolVisibility(item);

        Assert.True(item.IsVisible);
        Assert.DoesNotContain(tool, owningRoot.HiddenDockables!);
        Assert.Contains(
            EnumerateDockables(data.RootDock),
            dockable => ReferenceEquals(dockable, tool));
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable root)
    {
        yield return root;
        if (root is not IDock dock || dock.VisibleDockables is null)
        {
            yield break;
        }

        foreach (var child in dock.VisibleDockables)
        {
            foreach (var descendant in EnumerateDockables(child))
            {
                yield return descendant;
            }
        }
    }
}
