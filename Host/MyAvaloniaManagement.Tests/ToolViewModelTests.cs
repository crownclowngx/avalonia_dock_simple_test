using MyAvaloniaManagement.Business.Presentation.Icons;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Models.FileSystem;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Tools;

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
    public async Task UNC共享根不访问真实网络且作为唯一自定义根显示()
    {
        using var context = new TestHostContext();
        const string selected = @"\\Server\Share\";
        const string normalized = @"\\Server\Share";
        context.Storage.FolderPath = selected;
        context.Storage.Directories.Add(normalized);
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            new RecordingDocumentOpenService(),
            initializeTree: false);

        await viewModel.SelectFolder();

        Assert.True(viewModel.ShowCustomFolder);
        Assert.Equal(normalized, viewModel.SelectedFolderPath);
        Assert.Equal(normalized, Assert.Single(viewModel.RootNodes).Path);

        viewModel.RefreshAll();

        Assert.False(viewModel.ShowCustomFolder);
        Assert.Equal(string.Empty, viewModel.SelectedFolderPath);
        Assert.NotEmpty(viewModel.RootNodes);
    }

    [Fact]
    public async Task 裸盘符规范化为驱动器根并保持本地驱动器模式()
    {
        using var context = new TestHostContext();
        context.Storage.FolderPath = "C:";
        context.Storage.Directories.Add(@"C:\");
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            new RecordingDocumentOpenService(),
            initializeTree: false);

        await viewModel.SelectFolder();

        Assert.False(viewModel.ShowCustomFolder);
        Assert.Equal(string.Empty, viewModel.SelectedFolderPath);
        Assert.Equal(@"C:\", Assert.Single(viewModel.RootNodes).Path);
    }

    [Fact]
    public async Task 选择过程中消失或非法的路径不提交半成品状态()
    {
        using var context = new TestHostContext();
        var existing = Path.Combine(context.TempDirectory, "existing");
        Directory.CreateDirectory(existing);
        context.Storage.FolderPath = existing;
        var viewModel = new FileSystemTreeViewModel(
            context.Storage,
            new RecordingDocumentOpenService(),
            initializeTree: false);
        await viewModel.SelectFolder();
        var originalRoot = Assert.Single(viewModel.RootNodes);

        context.Storage.FolderPath = Path.Combine(context.TempDirectory, "disappeared");
        await viewModel.SelectFolder();
        Assert.Same(originalRoot, Assert.Single(viewModel.RootNodes));
        Assert.Equal(Path.GetFullPath(existing), viewModel.SelectedFolderPath);
        Assert.True(viewModel.ShowCustomFolder);

        context.Storage.FolderPath = "relative";
        await viewModel.SelectFolder();
        Assert.Same(originalRoot, Assert.Single(viewModel.RootNodes));
    }

    [Fact]
    public async Task 插件分组工具创建文档并切换分类展开()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var viewModel = context.Provider.GetRequiredService<PlugGroupMenuViewModel>();
        var category = viewModel.CategoryNodes.Single(node =>
            node.CategoryName == "测试");

        viewModel.ToggleCategoryExpand(category);
        await viewModel.CreateDocumentEntryAsync(
            Assert.Single(category.Documents));

        Assert.True(category.IsExpanded);
        var dock = Assert.IsType<DocumentDock>(
            context.Workspace.DockFactory.GetDockable<IDocumentDock>(DockLayoutIds.Documents));
        Assert.Contains(dock.VisibleDockables!, item =>
            item is ManagedDocumentDockable { Model: TestSavableDocument });
    }

    [Fact]
    public void 分类名和Document集合是构造期只读快照()
    {
        var source = new List<DocumentCreationMenuEntry>
        {
            CreateMenuEntry("first"),
        };
        var node = new CategoryNode("测试", source);

        source.Add(CreateMenuEntry("second"));

        Assert.Equal("测试", node.CategoryName);
        Assert.Single(node.Documents);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DocumentCreationMenuEntry>)node.Documents).Add(
                CreateMenuEntry("third")));
        Assert.Null(typeof(CategoryNode).GetProperty(nameof(node.CategoryName))!.SetMethod);
        Assert.Null(typeof(CategoryNode).GetProperty(nameof(node.Documents))!.SetMethod);
        Assert.NotNull(typeof(CategoryNode).GetProperty(nameof(node.IsExpanded))!.SetMethod);
        Assert.Throws<ArgumentException>(() => new CategoryNode(" ", source));
        Assert.Throws<ArgumentNullException>(() => new CategoryNode("测试", null!));
    }

    [Fact]
    public async Task 插件分组工具对空构造依赖和空命令参数明确失败()
    {
        using var context = DocumentTestContext.Create();
        var query = context.Provider.GetRequiredService<DocumentCreationMenuQuery>();
        var documents = context.Provider.GetRequiredService<DocumentPersistenceCoordinator>();
        var state = context.Provider.GetRequiredService<DocumentOperationState>();

        Assert.Throws<ArgumentNullException>(() =>
            new PlugGroupMenuViewModel(null!, documents, state, new(Path.Combine(context.TempDirectory, "navigation.json")), context.Provider.GetRequiredService<HostIconRenderer>()));
        Assert.Throws<ArgumentNullException>(() =>
            new PlugGroupMenuViewModel(query, null!, state, new(Path.Combine(context.TempDirectory, "navigation.json")), context.Provider.GetRequiredService<HostIconRenderer>()));
        Assert.Throws<ArgumentNullException>(() =>
            new PlugGroupMenuViewModel(query, documents, null!, new(Path.Combine(context.TempDirectory, "navigation.json")), context.Provider.GetRequiredService<HostIconRenderer>()));
        using var viewModel = new PlugGroupMenuViewModel(query, documents, state, new(Path.Combine(context.TempDirectory, "navigation.json")), context.Provider.GetRequiredService<HostIconRenderer>());
        Assert.Throws<ArgumentNullException>(() => viewModel.ToggleCategoryExpand(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            viewModel.CreateDocumentEntryAsync(null!));
    }

    [Fact]
    public void 工具中心隐藏恢复各提交一次布局变化且复用实例()
    {
        using var context = new TestHostContext();
        var main = context.CreateMainWindowViewModel();
        var id = HostExtensionIds.FileSystemTree;
        Assert.True(context.Workspace.ShowTool(id));
        var original = context.Workspace.CreatedTools[id.Value];
        var states = context.Provider.GetRequiredService<MyAvaloniaManagement.Business.Workspace.ToolWorkspaceReadModel>();
        var changes = 0;
        main.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(main.Layout)) changes++; };
        Assert.True(context.Workspace.SetToolVisibility(id.Value, false).Succeeded);
        Assert.False(states.Capture().Single(item => item.ToolId == id.Value).IsVisible);
        Assert.True(context.Workspace.OpenTool(id.Value).Succeeded);
        Assert.True(states.Capture().Single(item => item.ToolId == id.Value).IsVisible);
        Assert.Same(original, context.Workspace.CreatedTools[id.Value]);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void 所有内建工具均可隐藏且无管理Tool占位()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        Assert.DoesNotContain(RetiredToolLayoutMigration.ToolManagementId, context.Workspace.CreatedTools.Keys);
        Assert.All(context.Workspace.CreatedTools.Values, tool => Assert.True(tool.CanClose));
        foreach (var id in context.Workspace.CreatedTools.Keys)
        {
            Assert.True(context.Workspace.OpenTool(id).Succeeded);
            Assert.True(context.Workspace.SetToolVisibility(id, false).Succeeded);
        }
        Assert.All(context.Provider.GetRequiredService<MyAvaloniaManagement.Business.Workspace.ToolWorkspaceReadModel>().Capture(),
            item => Assert.False(item.IsVisible));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dock隐藏或关闭与ShowTool直接同步快照并各通知一次(bool close)
    {
        using var context = new TestHostContext();
        var main = context.CreateMainWindowViewModel();
        var id = HostExtensionIds.PluginStatus;
        context.Workspace.ShowTool(id);
        var states = context.Provider.GetRequiredService<MyAvaloniaManagement.Business.Workspace.ToolWorkspaceReadModel>();
        var changes = 0;
        main.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(main.Layout)) changes++; };
        var tool = context.Workspace.CreatedTools[id.Value];
        if (close)
            context.Workspace.DockFactory.CloseDockable(tool);
        else
            context.Workspace.DockFactory.HideDockable(tool);
        Assert.False(states.Capture().Single(item => item.ToolId == id.Value).IsVisible);
        Assert.Equal(1, changes);
        Assert.True(context.Workspace.ShowTool(id));
        Assert.True(states.Capture().Single(item => item.ToolId == id.Value).IsVisible);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void 自动收起工具可以预览定位再隐藏恢复()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var id = HostExtensionIds.FileSystemTree;
        context.Workspace.ShowTool(id);
        var tool = context.Workspace.CreatedTools[id.Value];
        var factory = context.Workspace.DockFactory;
        factory.PinDockable(tool);
        Assert.True(context.Workspace.OpenTool(id.Value).Succeeded);
        var root = factory.FindRoot(tool, _ => true)!;
        var states = context.Provider.GetRequiredService<MyAvaloniaManagement.Business.Workspace.ToolWorkspaceReadModel>();
        Assert.Equal(ToolLayoutState.AutoHidden, states.Capture().Single(item => item.ToolId == id.Value).LayoutState);
        Assert.Contains(tool, root.LeftPinnedDockables!);
        Assert.Same(tool, root.PinnedDock?.ActiveDockable);
        Assert.True(context.Workspace.SetToolVisibility(id.Value, false).Succeeded);
        Assert.DoesNotContain(tool, root.LeftPinnedDockables!);
        Assert.Contains(tool, root.HiddenDockables!);
        Assert.True(context.Workspace.OpenTool(id.Value).Succeeded);
        Assert.DoesNotContain(tool, root.HiddenDockables!);
        Assert.True(states.Capture().Single(item => item.ToolId == id.Value).IsVisible);
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

    private static DocumentCreationMenuEntry CreateMenuEntry(string suffix) =>
        new(
            new DocumentTypeId($"myavalonia.plugin.host-tests.document.{suffix}"),
            null,
            suffix,
            suffix,
            string.Empty,
            "测试");

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
