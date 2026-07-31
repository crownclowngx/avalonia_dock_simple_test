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
using MyAvaloniaManagementCommon.Message;
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
            context.Messenger,
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
        var receiver = new object();
        context.Messenger.Register<object, OpenFileMessage>(
            receiver,
            (_, message) => received.Add(message.FilePath));
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            context.Messenger,
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
            context.Messenger,
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
        using var context = new TestHostContext();
        context.Factory.RegisterStrategy(new TestSavableStrategy());
        _ = context.CreateMainWindowViewModel();
        var viewModel = new PlugGroupMenuViewModel(
            context.Factory,
            new PluginMenuService(context.Factory));
        var category = viewModel.CategoryNodes.Single(node =>
            node.CategoryName == "测试");

        viewModel.ToggleCategoryExpand(category);
        viewModel.CreateDocument(TestSavableStrategy.TypeId);

        Assert.True(category.IsExpanded);
        var dock = Assert.IsType<DocumentDock>(
            context.Factory.GetDockable<IDocumentDock>("Files"));
        Assert.Contains(dock.VisibleDockables!, item =>
            item is TestSavableDocument);
    }

    [Fact]
    public void 工具管理可以隐藏恢复并发送布局消息()
    {
        using var context = new TestHostContext();
        var tool = new Tool
        {
            Id = "closable-tool",
            Title = "可关闭工具",
            CanClose = true
        };
        context.Factory.RegisterToolStrategy(new StubToolStrategy(
            tool,
            new ToolMetadata
            {
                ToolTypeId = tool.Id,
                DisplayName = tool.Title!,
                Description = string.Empty,
                IconPath = string.Empty,
                Alignment = "Left"
            }));
        _ = context.CreateMainWindowViewModel();
        var manager = Assert.IsType<ToolManagementViewModel>(
            context.Factory.CreatedTools[DockNameConstant.ToolManagement]);
        var item = manager.ToolItems.Single(candidate =>
            candidate.ToolId == tool.Id);
        var updateCount = 0;
        var receiver = new object();
        context.Messenger.Register<object, UpdateLayoutMessage>(
            receiver,
            (_, _) => updateCount++);

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
        context.Messenger.Send(
            new ToolVisibilityChangedMessage("external-change"));
        manager.SyncToolsVisibility();
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
