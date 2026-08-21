using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.FileSystem;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
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
        var documentOpenService = new RecordingDocumentOpenService();
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            documentOpenService,
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
    public async Task 文件树只为存在文件调用窄打开服务()
    {
        using var context = new TestHostContext();
        var path = Path.Combine(context.TempDirectory, "open.txt");
        context.Storage.AddFile(path, "content");
        var documentOpenService = new RecordingDocumentOpenService();
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            documentOpenService,
            initializeTree: false);
        viewModel.NodeSelected(new FileSystemNode(path));

        await viewModel.OpenFile();

        Assert.Equal([path], documentOpenService.Paths);
        viewModel.NodeSelected(new FileSystemNode(
            Path.Combine(context.TempDirectory, "missing.txt")));
        await viewModel.OpenFile();
        Assert.Single(documentOpenService.Paths);
    }

    [Fact]
    public async Task 选择自定义文件夹后文件树只显示该目录()
    {
        using var context = new TestHostContext();
        var folder = Path.Combine(context.TempDirectory, "selected");
        Directory.CreateDirectory(folder);
        context.Storage.FolderPath = folder;
        var documentOpenService = new RecordingDocumentOpenService();
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            documentOpenService,
            initializeTree: false);

        await viewModel.SelectFolder();

        Assert.True(viewModel.ShowCustomFolder);
        Assert.Equal(Path.GetFullPath(folder), viewModel.SelectedFolderPath);
        Assert.Equal(Path.GetFullPath(folder),
            Assert.Single(viewModel.RootNodes).Path);
    }

    [Fact]
    public async Task 插件分组工具创建文档并切换分类展开()
    {
        using var context = DocumentV2TestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var viewModel = context.Provider.GetRequiredService<PlugGroupMenuViewModel>();
        var category = viewModel.CategoryNodes.Single(node =>
            node.CategoryName == "测试");

        viewModel.ToggleCategoryExpand(category);
        await viewModel.CreateDocumentAsync(TestDocumentIds.TypeId.Value);

        Assert.True(category.IsExpanded);
        var dock = Assert.IsType<DocumentDock>(
            context.Factory.GetDockable<IDocumentDock>("Files"));
        Assert.Contains(dock.VisibleDockables!, item =>
            item is ManagedDocumentDockable { Model: TestSavableDocument });
    }

    [Fact]
    public void 工具管理隐藏恢复各提交一次布局变化()
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
        var mainViewModel = context.CreateMainWindowViewModel();
        var manager = GetManagedToolModel<ToolManagementViewModel>(
            context.Factory.CreatedTools[DockNameConstant.ToolManagement]);
        var item = manager.ToolItems.Single(candidate =>
            candidate.ToolId == tool.Id);
        var updateCount = 0;
        mainViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(mainViewModel.Layout))
            {
                updateCount++;
            }
        };

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
        var manager = GetManagedToolModel<ToolManagementViewModel>(
            context.Factory.CreatedTools[DockNameConstant.ToolManagement]);
        var item = manager.ToolItems.First(candidate => !candidate.CanClose);
        var before = item.IsVisible;

        manager.ToggleToolVisibility(item);

        Assert.Equal(before, item.IsVisible);
        Assert.False(context.Factory.TrySetToolVisibility(item.ToolId, !before));
        Assert.Equal(before, item.IsVisible);
    }

    [Fact]
    public void Dock关闭与ShowTool直接同步管理器并各通知一次()
    {
        var tool = new Tool
        {
            Id = "myavalonia.host.tool.external-visibility",
            Title = "外部显隐工具",
            CanClose = true
        };
        var strategy = new StubToolStrategy(
            tool,
            new ToolMetadata(
                new ToolTypeId(tool.Id),
                tool.Title!,
                ToolDockSide.Right)
            {
                Description = string.Empty,
                IconPath = string.Empty
            });
        using var context = new TestHostContext(toolStrategies: [strategy]);
        var mainViewModel = context.CreateMainWindowViewModel();
        var manager = GetManagedToolModel<ToolManagementViewModel>(
            context.Factory.CreatedTools[DockNameConstant.ToolManagement]);
        var item = manager.ToolItems.Single(candidate => candidate.ToolId == tool.Id);
        var layoutChanges = 0;
        mainViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(mainViewModel.Layout))
            {
                layoutChanges++;
            }
        };

        context.Factory.HideDockable(tool);

        Assert.False(item.IsVisible);
        Assert.Equal(1, layoutChanges);

        Assert.True(context.Factory.ShowTool(tool.Id));

        Assert.True(item.IsVisible);
        Assert.Equal(2, layoutChanges);
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
        var manager = GetManagedToolModel<ToolManagementViewModel>(
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

    private static TModel GetManagedToolModel<TModel>(Tool tool)
        where TModel : class =>
        Assert.IsType<TModel>(Assert.IsType<ManagedToolDockable>(tool).Model);

    /// <summary>只记录文件树提交路径的窄服务替身。</summary>
    private sealed class RecordingDocumentOpenService : IHostDocumentOpenService
    {
        internal List<string> Paths { get; } = [];

        public Task OpenPathAsync(string filePath)
        {
            Paths.Add(filePath);
            return Task.CompletedTask;
        }
    }
}
