using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using BiliDownloader.Converters;
using BiliDownloader.Models;
using BiliDownloader.Plugin;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Views;
using BiliDownloader.Views.BiliDownloader;
using BiliDownloader.Views.BiliScheduler;
using BiliDownloader.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Plugin;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class BiliDownloaderDocumentVisualTests
{
    [AvaloniaFact]
    public void 下载文档在宽窄尺寸与双主题下均可布局()
    {
        using var context = new UiTestContext();
        var application = Assert.IsType<App>(Application.Current);
        var originalTheme = application.RequestedThemeVariant;

        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                application.RequestedThemeVariant = theme;
                var view = new BiliDownloaderView();

                Measure(view, new Size(1240, 760));
                Measure(view, new Size(760, 620));
                Measure(view, new Size(520, 520));

                Assert.Equal(520, view.Bounds.Width);
                Assert.Equal(520, view.Bounds.Height);
                Assert.IsType<Border>(view.Content);
                Assert.NotEmpty(view.GetLogicalDescendants().OfType<PathIcon>());
                Assert.False(Assert.IsType<Expander>(
                    view.FindControl<Expander>("DownloadSettingsExpander")).IsExpanded);
            }
        }
        finally
        {
            application.RequestedThemeVariant = originalTheme;
        }
    }

    [AvaloniaFact]
    public void 下载列表保持显式虚拟化并共享无状态转换器()
    {
        using var context = new UiTestContext();
        var view = new VideoListView();
        var list = view.FindControl<ListBox>("VideoItemsList");

        Assert.NotNull(list);
        Assert.NotNull(list.ItemsPanel);
        Assert.True(double.IsPositiveInfinity(list.MaxHeight));
        Assert.IsType<RenameDisplayConverter>(view.Resources["RenameDisplayConverter"]);
    }

    [AvaloniaFact]
    public void 下载列表占据剩余高度且底部操作栏保持可见()
    {
        using var context = new UiTestContext();
        foreach (var size in new[] { new Size(760, 420), new Size(480, 320) })
        {
            var view = new VideoListView();
            var window = new Window
            {
                Width = size.Width,
                Height = size.Height,
                Content = view,
            };
            try
            {
                window.Show();
                Measure(view, size);
                var list = Assert.IsType<ListBox>(view.FindControl<ListBox>("VideoItemsList"));
                var actionBar = Assert.IsType<Border>(view.FindControl<Border>("DownloadActionBar"));
                Assert.True(list.Bounds.Height >= 64,
                    $"列表高度应至少为 64，实际为 {list.Bounds.Height}，视图高度为 {view.Bounds.Height}。");
                Assert.True(actionBar.Bounds.Height > 0);
                Assert.True(actionBar.Bounds.Bottom <= view.Bounds.Bottom,
                    $"操作栏底部 {actionBar.Bounds.Bottom} 超出视图底部 {view.Bounds.Bottom}；"
                    + $"列表高度 {list.Bounds.Height}、MinHeight {list.MinHeight}、视图期望高度 {view.DesiredSize.Height}。");
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void 任务中心在关键断点与双主题下保持响应式和虚拟化()
    {
        using var context = new UiTestContext();
        var application = Assert.IsType<App>(Application.Current);
        var originalTheme = application.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var width in new[] { 320d, 479d, 480d, 640d, 700d })
            {
                application.RequestedThemeVariant = theme;
                var view = new SchedulerTaskListView();
                Measure(view, new Size(width, 700));
                Assert.Equal(width < 480, view.Classes.Contains("compact"));
                Assert.NotNull(view.FindControl<ListBox>("TaskList")?.ItemsPanel);
            }
        }
        finally
        {
            application.RequestedThemeVariant = originalTheme;
        }
    }

    [AvaloniaFact]
    public void 运行日志默认折叠且文档样式不影响调度工具()
    {
        using var context = new UiTestContext();
        var document = new BiliDownloaderView();
        var schedulerTool = new BiliSchedulerToolView();

        var log = document.FindControl<Expander>("DownloadLogExpander");
        Assert.NotNull(log);
        Assert.False(log.IsExpanded);

        var toolControls = schedulerTool.GetLogicalDescendants()
            .OfType<Control>()
            .Append(schedulerTool);
        Assert.DoesNotContain(
            toolControls,
            control => control.Classes.Any(item => item.StartsWith("bili-doc-", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task 三个Document与隐藏重建Tool共享同一百条SQLite事实()
    {
        using var ui = new UiTestContext();
        var services = new ServiceCollection();
        new BiliDownloaderPluginModule().ConfigureServices(services);
        services.AddSingleton<IMessengerService>(ui.Messenger);
        services.AddSingleton<IBiliDataPaths>(new UiBiliDataPaths(
            Path.Combine(ui.TempDirectory, "BiliDownloader")));
        services.AddSingleton<PluginLifecycleManager>();
        using var provider = services.BuildServiceProvider();

        var documents = Enumerable.Range(0, 3)
            .Select(_ => provider.GetRequiredService<BiliDownloaderViewModel>())
            .ToArray();
        var repository = provider.GetRequiredService<IDownloadTaskRepository>();
        await repository.InitAsync();
        var tasks = Enumerable.Range(0, 100)
            .Select(index => new DownloadTaskRecord
            {
                TaskId = $"g8-ui-{index:D3}",
                DocumentId = documents[index % documents.Length].DocumentId,
                SourceDocumentTitle = $"方案 {index % documents.Length + 1}",
                SeriesTitle = "G8 UI",
                ItemTitle = $"任务 {index:D3}",
                Status = index % 10 == 0 ? "waiting_for_login" : "pending",
                CreatedAt = DateTime.Today.AddMinutes(index),
                LastUpdatedAt = DateTime.Today.AddMinutes(index),
            })
            .ToList();
        await repository.InsertBatchAsync(tasks);

        foreach (var document in documents)
            await document.RecoverTasksFromStoreAsync();
        Assert.Equal([34, 33, 33], documents.Select(document => document.VideoList.Count));

        var tool = provider.GetRequiredService<BiliSchedulerToolViewModel>();
        await tool.ActivateAsync();
        Assert.Equal(100, tool.TaskList.Tasks.Count);
        Assert.Equal(10, tool.TaskList.Tasks.Count(task => task.Status == "waiting_for_login"));

        tool.TaskList.StatusFilter = "waiting_login";
        Assert.Equal(10, tool.TaskList.FilteredTasks.Count);
        tool.TaskList.SelectAllFilteredCommand.Execute(null);
        Assert.Equal(10, tool.TaskList.SelectedCount);

        // Headless 环境没有用户确认对话框，安全默认实现必须拒绝破坏性批量删除；
        // 同一批等待登录任务的恢复入口可执行，但没有凭据时仍不得改变 SQLite 状态。
        await tool.TaskList.BatchDeleteCommand.ExecuteAsync(null);
        Assert.Equal(100, (await repository.GetAllAsync()).Count);
        var waiting = tool.TaskList.Tasks.First(task => task.Status == "waiting_for_login");
        Assert.True(tool.TaskList.ResumeTaskCommand.CanExecute(waiting));
        await tool.TaskList.ResumeTaskCommand.ExecuteAsync(waiting);
        Assert.Equal("waiting_for_login", (await repository.GetAllAsync())
            .Single(task => task.TaskId == waiting.TaskId).Status);
        tool.TaskList.ClearSelectionCommand.Execute(null);
        tool.TaskList.StatusFilter = "all";

        // 先后创建两个 Tool 视图模拟隐藏与重新附加视觉树；共享 ViewModel 和 Coordinator
        // 不被视图拥有，因此第二次激活只重建 SQLite 投影，不会创建第二份任务事实。
        var firstView = new BiliSchedulerToolView { DataContext = tool };
        Measure(firstView, new Size(700, 700));
        var restoredView = new BiliSchedulerToolView { DataContext = tool };
        await tool.ActivateAsync();
        Measure(restoredView, new Size(480, 620));

        Assert.Equal(100, tool.TaskList.Tasks.Count);
        Assert.NotNull(restoredView.GetLogicalDescendants()
            .OfType<ListBox>()
            .Single(control => control.Name == "TaskList")
            .ItemsPanel);

        // Microsoft.Data.Sqlite 默认连接池会在仓储方法返回后保留文件句柄；测试结束前清池，
        // 让测试沙箱可以立即删除，同时不改变生产环境的连接池策略。
        SqliteConnection.ClearAllPools();
    }

    private static void Measure(Control control, Size size)
    {
        control.Measure(size);
        control.Arrange(new Rect(size));
    }

    /// <summary>将插件持久化完全限制在当前 Headless 测试目录。</summary>
    private sealed class UiBiliDataPaths : IBiliDataPaths
    {
        public UiBiliDataPaths(string root)
        {
            DataDirectory = root;
            LogDirectory = Path.Combine(root, "logs");
            TempDirectory = Path.Combine(root, "temp");
            FfmpegDependencyDirectory = Path.Combine(root, "dependencies", "ffmpeg");
            FfmpegCurrentPointerPath = Path.Combine(FfmpegDependencyDirectory, "current.json");
            DownloadTaskDatabasePath = Path.Combine(root, "tasks.db");
            CredentialDatabasePath = Path.Combine(root, "credentials.db");
            CredentialKeyPath = Path.Combine(root, "credential.key");
            StorageEpochMarkerPath = Path.Combine(root, "storage_epoch_v2");
            ResetDirectories = [root];
        }

        public string DataDirectory { get; }
        public string LogDirectory { get; }
        public string TempDirectory { get; }
        public string FfmpegDependencyDirectory { get; }
        public string FfmpegCurrentPointerPath { get; }
        public string DownloadTaskDatabasePath { get; }
        public string CredentialDatabasePath { get; }
        public string CredentialKeyPath { get; }
        public string StorageEpochMarkerPath { get; }
        public IReadOnlyList<string> ResetDirectories { get; }
    }
}
