using System.Globalization;
using Avalonia.Media;
using BiliDownloader.Converters;
using BiliDownloader.Create;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.ViewModels;
using BiliDownloader.ViewModels.BiliDownloader;
using BiliDownloader.ViewModels.BiliScheduler;
using Flurl.Http.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Save;

namespace BiliDownloader.Tests;

public sealed class PresentationLogicTests
{
    public static IEnumerable<object[]> StatusCases()
        => Enum.GetValues<DownloadTaskStatus>()
            .Select(status => new object[]
            {
                status,
                DownloadTaskStatusMapper.ToStorageString(status),
                DownloadTaskStatusMapper.ToDisplayText(status),
            });

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void 状态枚举可往返并提供统一显示文本(
        DownloadTaskStatus status,
        string storage,
        string display)
    {
        Assert.Equal(status, DownloadTaskStatusMapper.FromStorageString(storage));
        Assert.Equal(display, TaskStatusDisplay.ToDisplayText(storage));
        Assert.Equal(display, TaskStatusDisplay.ToStageText(storage));
    }

    [Fact]
    public void 未知存储状态回退到Ready且运行状态集合准确()
    {
        Assert.Equal(
            DownloadTaskStatus.Ready,
            DownloadTaskStatusMapper.FromStorageString("future_state"));
        Assert.True(DownloadTaskStatusMapper.IsRunning(DownloadTaskStatus.FetchingMetadata));
        Assert.True(DownloadTaskStatusMapper.IsRunning(DownloadTaskStatus.Merging));
        Assert.False(DownloadTaskStatusMapper.IsRunning(DownloadTaskStatus.Ready));
        Assert.False(DownloadTaskStatusMapper.IsRunning(DownloadTaskStatus.Failed));
    }

    [Fact]
    public void 转换器覆盖状态颜色完成失败与重命名显示()
    {
        var culture = CultureInfo.InvariantCulture;
        var statusConverter = new TaskStatusDisplayConverter();
        Assert.Equal("完成", statusConverter.Convert("done", typeof(string), null, culture));
        Assert.Null(statusConverter.Convert(null, typeof(string), null, culture));
        Assert.Throws<NotSupportedException>(() =>
            statusConverter.ConvertBack("完成", typeof(string), null, culture));

        var done = new IsDoneStatusConverter();
        Assert.True((bool)done.Convert("done", typeof(bool), null, culture));
        Assert.False((bool)done.Convert("failed", typeof(bool), null, culture));
        Assert.False((bool)done.Convert(null, typeof(bool), null, culture));
        Assert.Throws<NotImplementedException>(() =>
            done.ConvertBack(true, typeof(string), null, culture));

        var failed = new IsFailedStatusConverter();
        Assert.True((bool)failed.Convert("failed", typeof(bool), null, culture));
        Assert.False((bool)failed.Convert("done", typeof(bool), null, culture));
        Assert.False((bool)failed.Convert(null, typeof(bool), null, culture));

        var rename = new RenameDisplayConverter();
        Assert.Equal("原标题 → 新标题", rename.Convert(
            ["原标题", "新标题"], typeof(string), null, culture));
        Assert.Equal("标题", rename.Convert(
            ["标题", "标题"], typeof(string), null, culture));
        Assert.Equal("42", rename.Convert(
            [new object(), 42], typeof(string), null, culture));
        Assert.Equal("", rename.Convert(
            ["only-one"], typeof(string), null, culture));
        Assert.Equal("", rename.Convert([], typeof(string), null, culture));

        var colors = new StatusToColorConverter();
        Assert.Equal(
            Color.Parse("#4CAF50"),
            Assert.IsType<SolidColorBrush>(
                colors.Convert("完成", typeof(IBrush), null, culture)).Color);
        Assert.Equal(
            Color.Parse("#F44336"),
            Assert.IsType<SolidColorBrush>(
                colors.Convert("已中断", typeof(IBrush), null, culture)).Color);
        Assert.Equal(
            Color.Parse("#F44336"),
            Assert.IsType<SolidColorBrush>(
                colors.Convert("失败", typeof(IBrush), null, culture)).Color);
        foreach (var status in new[] { "下载视频", "下载音频", "合并中", "获取信息" })
        {
            Assert.Equal(
                Color.Parse("#00A1D6"),
                Assert.IsType<SolidColorBrush>(
                    colors.Convert(status, typeof(IBrush), null, culture)).Color);
        }
        foreach (var status in new[] { "排队中", "等待中" })
        {
            Assert.Equal(
                Color.Parse("#9E9E9E"),
                Assert.IsType<SolidColorBrush>(
                    colors.Convert(status, typeof(IBrush), null, culture)).Color);
        }
        Assert.Same(
            Brushes.Transparent,
            colors.Convert("未知", typeof(IBrush), null, culture));
        Assert.Same(
            Brushes.Transparent,
            colors.Convert(null, typeof(IBrush), null, culture));
        Assert.Throws<NotImplementedException>(() =>
            colors.ConvertBack(null, typeof(string), null, culture));
    }

    [Fact]
    public void 批量重命名验证行数裁剪空白并切换面板()
    {
        List<string>? applied = null;
        var count = 2;
        var vm = new RenamePanelViewModel(
            values => applied = values,
            () => count);
        vm.InitTitles(
        [
            new BiliVideoItem { Title = "A" },
            new BiliVideoItem { Title = "B" },
        ]);

        Assert.Equal($"A{Environment.NewLine}B", vm.OriginalTitlesText);
        vm.ToggleRenamePanelCommand.Execute(null);
        Assert.True(vm.ShowRenamePanel);

        vm.NewTitlesText = "only one";
        vm.ApplyRenameCommand.Execute(null);
        Assert.Null(applied);
        Assert.Contains("行数", vm.StatusMessage, StringComparison.Ordinal);

        vm.NewTitlesText = "  新A  \n   ";
        vm.ApplyRenameCommand.Execute(null);
        Assert.Equal(["新A", ""], applied);
        Assert.Contains("2 个视频", vm.StatusMessage, StringComparison.Ordinal);

        count = 0;
        applied = null;
        vm.ApplyRenameCommand.Execute(null);
        Assert.Null(applied);
    }

    [Fact]
    public void 视频列表设置恢复选择进度与删除保持一致()
    {
        var statuses = new List<string>();
        var vm = new VideoListViewModel(
            () => new SubmitContext(),
            new RecordingMessengerService(),
            statuses.Add);
        var first = new BiliVideoItem { ItemId = "one", Title = "A", IsSelected = false };
        var second = new BiliVideoItem { ItemId = "two", Title = "B", IsSelected = false };
        vm.SetItems([first, second]);

        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.VideoItems, x => Assert.True(x.IsSelected));
        vm.DeselectAllCommand.Execute(null);
        Assert.All(vm.VideoItems, x => Assert.False(x.IsSelected));

        vm.UpdateItemProgress(new DownloadTaskProgressMessage(
            "doc", "one", "A", 40, "downloading_video",
            videoProgress: 80, speedText: "2 MB/s"));
        vm.UpdateItemProgress(new DownloadTaskProgressMessage(
            "doc", "two", "B", 20, "failed", "network"));
        Assert.Equal(30, vm.TotalProgress);
        Assert.Equal("下载视频", first.Status);
        Assert.Equal("失败: network", second.Status);
        Assert.Equal("失败", second.StageText);

        vm.AddRecoveredItem(new BiliVideoItem
        {
            ItemId = "one",
            Status = "完成",
            Progress = 100,
        });
        Assert.Equal(2, vm.Count);
        Assert.Equal(100, first.Progress);

        vm.UpdateItemStatus(new DownloadTaskStatusChangedMessage(
            "doc", "one", "interrupted", 55));
        Assert.Equal("已中断", first.Status);
        vm.RemoveItem("two");
        vm.RemoveItem("missing");
        Assert.Equal(["one"], vm.VideoItems.Select(x => x.ItemId));
    }

    [Fact]
    public void 视频提交前置校验提供明确消息()
    {
        var status = "";
        var context = new SubmitContext();
        var vm = new VideoListViewModel(
            () => context,
            new RecordingMessengerService(),
            value => status = value);

        vm.SubmitDownloadCommand.Execute(null);
        Assert.Equal("请先解析视频", status);

        vm.SetItems(
        [
            new BiliVideoItem
            {
                ItemId = "one",
                Title = "A",
                IsSelected = false,
            },
        ]);
        vm.SubmitDownloadCommand.Execute(null);
        Assert.Equal("请至少勾选一个视频", status);

        vm.VideoItems[0].IsSelected = true;
        vm.SubmitDownloadCommand.Execute(null);
        Assert.Equal("请选择清晰度", status);
    }

    [Fact]
    public async Task 视频提交构造完整消息并清除选择()
    {
        using var state = new StaticStateScope();
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var fakeFfmpeg = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        await File.WriteAllTextAsync(fakeFfmpeg, "test marker");
        FfmpegService.CustomPath = fakeFfmpeg;
        var messenger = new RecordingMessengerService();
        var status = "";
        var context = new SubmitContext
        {
            DocumentId = "doc-submit",
            QualityId = 120,
            AudioQualityId = 30280,
            OutputDirectory = paths.RootDirectory,
            UseGroupFolder = true,
            AddIndexToTitle = true,
            SeriesTitle = "系列",
            DownloadDanmaku = true,
            DownloadSubtitle = true,
            DownloadCover = true,
            CoverUrl = "https://cover.test/a.jpg",
        };
        var vm = new VideoListViewModel(
            () => context,
            messenger,
            value => status = value);
        vm.SetItems(
        [
            new BiliVideoItem
            {
                Index = 2,
                ItemId = "item",
                Title = "标题",
                Aid = 1,
                Bvid = "BV1abcDEF123",
                Cid = 2,
                Duration = 3,
                IsSelected = true,
            },
        ]);

        vm.SubmitDownloadCommand.Execute(null);

        var message = Assert.IsType<SubmitDownloadTaskMessage>(
            Assert.Single(messenger.SentMessages));
        Assert.Equal("doc-submit", message.SourceDocumentId);
        Assert.Equal("系列", message.SeriesTitle);
        Assert.Equal(120, message.QualityId);
        Assert.Equal(30280, message.AudioQualityId);
        Assert.True(message.UseGroupFolder);
        Assert.Equal(
            ExtrasType.Danmaku | ExtrasType.Subtitle | ExtrasType.Cover,
            message.ExtrasConfig);
        var item = Assert.Single(message.Items);
        Assert.Equal("2.标题", item.Title);
        Assert.Equal("https://cover.test/a.jpg", item.CoverUrl);
        Assert.False(vm.VideoItems[0].IsSelected);
        Assert.Equal("排队中", vm.VideoItems[0].Status);
        Assert.Contains("已提交 1 个", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 下载配置加载默认目录并填充画质()
    {
        var settings = new InMemorySettingsRepository();
        settings.Seed("default_output_dir", "saved-output");
        var vm = new DownloadConfigViewModel(settings);
        await AsyncTest.EventuallyAsync(() => vm.OutputDirectory == "saved-output");
        var video = new BiliQualityOption { QualityId = 80, DisplayName = "1080P" };
        var audio = new BiliQualityOption { QualityId = 30232, DisplayName = "192kbps" };

        vm.PopulateQualities([video], video, [audio], audio, isMultiVideo: true);

        Assert.Same(video, Assert.Single(vm.QualityOptions));
        Assert.Same(audio, Assert.Single(vm.AudioQualityOptions));
        Assert.Same(video, vm.SelectedQuality);
        Assert.Same(audio, vm.SelectedAudioQuality);
        Assert.True(vm.IsMultiVideo);
        Assert.True(vm.UseGroupFolder);
    }

    [Fact]
    public async Task 下载配置读取失败回退默认值且无窗口时选择命令安全返回()
    {
        var settings = new InMemorySettingsRepository
        {
            InitializeException = new InvalidOperationException("broken"),
        };
        var vm = new DownloadConfigViewModel(settings);
        await AsyncTest.EventuallyAsync(() => !string.IsNullOrEmpty(vm.OutputDirectory));

        Assert.EndsWith("视频下载", vm.OutputDirectory, StringComparison.Ordinal);
        vm.SelectFolderCommand.Execute(null);
    }

    [Fact]
    public async Task 调度器设置加载合法值忽略非法值并持久化后续变更()
    {
        using var state = new StaticStateScope();
        var settings = new InMemorySettingsRepository();
        settings.Seed("default_output_dir", "saved");
        settings.Seed("ffmpeg_custom_path", "missing-ffmpeg");
        settings.Seed("max_concurrent_downloads", "3");
        var vm = new SchedulerSettingsViewModel(settings);
        var changed = 0;
        vm.MaxConcurrentDownloadsChanged += value => changed = value;

        await vm.LoadSettingsAsync();
        Assert.Equal("saved", vm.DefaultOutputDirectory);
        Assert.Equal("missing-ffmpeg", FfmpegService.CustomPath);
        Assert.Equal(3, vm.MaxConcurrentDownloads);

        vm.DefaultOutputDirectory = "new-output";
        vm.MaxConcurrentDownloads = 5;
        await AsyncTest.EventuallyAsync(() => settings.Writes.Count >= 2);
        Assert.Contains(("default_output_dir", "new-output"), settings.Writes);
        Assert.Contains(("max_concurrent_downloads", "5"), settings.Writes);
        Assert.Equal(5, changed);
    }

    [Fact]
    public async Task 调度器设置忽略非法并发且无窗口浏览命令安全返回()
    {
        using var state = new StaticStateScope();
        var settings = new InMemorySettingsRepository();
        settings.Seed("max_concurrent_downloads", "99");
        var vm = new SchedulerSettingsViewModel(settings);

        await vm.LoadSettingsAsync();
        Assert.Equal(1, vm.MaxConcurrentDownloads);
        await vm.BrowseFfmpegCommand.ExecuteAsync(null);
        await vm.BrowseOutputDirCommand.ExecuteAsync(null);

        Environment.SetEnvironmentVariable("PATH", "");
        FfmpegService.CustomPath = null;
        await vm.CheckFfmpegAsync();
        Assert.False(vm.FfmpegReady);
        Assert.Contains("未找到", vm.FfmpegStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginBar从本地快照初始化并执行退出()
    {
        var store = new InMemoryBiliCredentialStore(new BiliCredentialSession(
        [
            new("SESSDATA", "session"),
            new("bili_jct", "csrf"),
        ]));
        var api = new StubBiliSessionApi();
        var state = new BiliLoginStateService(
            store,
            api,
            new IsolatedMessengerService());
        await state.RestoreSavedSessionAsync();
        var vm = new LoginBarViewModel(state, new BiliLoginService());

        Assert.True(vm.IsLoggedIn);
        Assert.Equal("已保存账号", vm.UserName);
        await vm.EnsureLoggedInAsync();
        Assert.Equal(0, api.ValidationCount);

        await vm.LogoutCommand.ExecuteAsync(null);
        Assert.Null(store.Session);
        Assert.Equal(1, store.DeleteCount);
        Assert.Equal("ignored", LoginBarViewModel.GetDisplayName(false, "ignored"));
        Assert.Equal("name", LoginBarViewModel.GetDisplayName(true, "name"));
    }

    [Fact]
    public async Task 视频列表覆盖Ffmpeg未就绪消息发送失败与不存在目录()
    {
        using var state = new StaticStateScope();
        using var paths = new TestDataPaths();
        Environment.SetEnvironmentVariable("PATH", "");
        var context = new SubmitContext
        {
            QualityId = 80,
            OutputDirectory = Path.Combine(paths.RootDirectory, "missing"),
        };
        var messenger = new RecordingMessengerService();
        var status = "";
        var vm = new VideoListViewModel(
            () => context,
            messenger,
            value => status = value);
        vm.SetItems(
        [
            new BiliVideoItem
            {
                ItemId = "one",
                Title = "A",
                IsSelected = true,
            },
        ]);

        vm.SubmitDownloadCommand.Execute(null);
        Assert.Contains("ffmpeg 未就绪", status, StringComparison.Ordinal);
        vm.OpenOutputDirCommand.Execute(null);
        Assert.Contains("目录不存在", status, StringComparison.Ordinal);

        Directory.CreateDirectory(paths.RootDirectory);
        var fake = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        await File.WriteAllTextAsync(fake, "marker");
        FfmpegService.CustomPath = fake;
        messenger.ThrowOnSend = true;
        vm.VideoItems[0].IsSelected = true;
        vm.SubmitDownloadCommand.Execute(null);
        Assert.Contains("提交任务失败", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 视频解析ViewModel覆盖输入登录成功与异常状态()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        var credential = new FakeCredentialProviderWithLogin("SESSDATA=test");
        VideoParseResult? parsed = null;
        var vm = new VideoParseViewModel(
            new BiliApiService(),
            credential,
            result => parsed = result,
            () => true);

        await vm.ParseCommand.ExecuteAsync(null);
        Assert.Equal("请输入有效的B站视频链接", vm.DownloadInfo);
        http.ShouldNotHaveMadeACall();

        http.ForCallsTo("*x/web-interface/view*")
            .RespondWith("""
                {"code":0,"data":{"title":"视频","aid":1,"bvid":"BV1abcDEF123","cid":2,"duration":3}}
                """);
        ConfigureWbiNav(http);
        http.ForCallsTo("*x/player/wbi/playurl*")
            .RespondWith("""
                {"code":0,"data":{
                  "accept_quality":[80],
                  "accept_description":["1080P"],
                  "dash":{
                    "video":[{"id":80,"base_url":"https://v.test","codecid":7,"bandwidth":1}],
                    "audio":[
                      {"id":30232,"base_url":"https://a1.test","bandwidth":100000},
                      {"id":30232,"base_url":"https://a2.test","bandwidth":200000}
                    ]
                  }
                }}
                """);
        vm.Url = "BV1abcDEF123";
        await vm.ParseCommand.ExecuteAsync(null);

        Assert.True(vm.IsParsed);
        Assert.False(vm.IsLoading);
        Assert.NotNull(parsed);
        Assert.Single(parsed.VideoItems);
        Assert.Equal("视频", parsed.VideoItems[0].OriginalTitle);
        Assert.Single(parsed.AudioQualityOptions);
        Assert.Equal(30232, parsed.SelectedAudioQuality?.QualityId);
    }

    [Fact]
    public async Task Document保存恢复任务及消息按Document隔离()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var messenger = new RecordingMessengerService();
        var settings = new InMemorySettingsRepository();
        var loginState = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(),
            new StubBiliSessionApi(),
            messenger);
        var vm = new BiliDownloaderViewModel(
            messenger,
            repository,
            settings,
            loginState,
            new BiliLoginService(),
            new BiliApiService(),
            new FakeCredentialProvider());
        vm.Title = "测试文档";
        vm.VideoParse.Url = "BV1abcDEF123";
        vm.DownloadInfo = "日志";
        vm.DownloadConfig.OutputDirectory = "output";
        vm.DownloadConfig.UseGroupFolder = true;
        vm.DownloadConfig.AddIndexToTitle = false;

        var saved = vm.CreateSaveDocumentMetaData("unused");
        var restored = new BiliDownloaderViewModel(
            messenger,
            repository,
            settings,
            loginState,
            new BiliLoginService(),
            new BiliApiService(),
            new FakeCredentialProvider());
        restored.LoadDocumentByMetaData(saved);

        Assert.Equal(vm.DocumentId, restored.DocumentId);
        Assert.Equal("BV1abcDEF123", restored.VideoParse.Url);
        Assert.Equal("日志", restored.DownloadInfo);
        Assert.Equal("output", restored.DownloadConfig.OutputDirectory);
        Assert.True(restored.DownloadConfig.UseGroupFolder);
        Assert.False(restored.DownloadConfig.AddIndexToTitle);
        repository.Seed(new DownloadTaskRecord
        {
            TaskId = "recover",
            DocumentId = restored.DocumentId,
            ItemTitle = "恢复任务",
            Status = "interrupted",
            Progress = 45,
        });
        await restored.RecoverTasksFromStoreAsync();
        var item = Assert.Single(restored.VideoList.VideoItems);
        Assert.Equal("已中断", item.Status);
        Assert.Equal(45, item.Progress);

        messenger.Send(new DownloadTaskProgressMessage(
            "other-doc", "recover", "ignored", 99, "done"));
        Assert.Equal(45, item.Progress);
        messenger.Send(new DownloadTaskProgressMessage(
            restored.DocumentId, "recover", "target", 50, "downloading_audio"));
        Assert.Equal(50, item.Progress);
        Assert.Equal("下载音频", item.Status);
        messenger.Send(new LoginStateChangedMessage(
            true, null, null, true, "已恢复"));
        Assert.True(restored.LoginBar.IsLoggedIn);
        Assert.Equal("已保存账号", restored.LoginBar.UserName);
        Assert.Equal("已恢复", restored.LoginBar.StatusMessage);
    }

    [Fact]
    public void Document创建策略创建独立文档并提供元数据()
    {
        var services = new ServiceCollection();
        var repository = new InMemoryDownloadTaskRepository();
        var messenger = new IsolatedMessengerService();
        var loginState = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(),
            new StubBiliSessionApi(),
            messenger);
        services.AddTransient(_ => new BiliDownloaderViewModel(
            messenger,
            repository,
            new InMemorySettingsRepository(),
            loginState,
            new BiliLoginService(),
            new BiliApiService(),
            new FakeCredentialProvider()));
        using var provider = services.BuildServiceProvider();
        var strategy = new BiliDownloaderDocumentStrategy(provider);

        var custom = strategy.CreateDocument(
            new DocumentCreationParams("BiliDownloader") { Title = "自定义" });
        var fallback = strategy.CreateDocument(
            new DocumentCreationParams("BiliDownloader"));

        Assert.NotSame(custom, fallback);
        Assert.Equal("自定义", custom.Title);
        Assert.Equal("Bilibili下载", fallback.Title);
        var metadata = strategy.GetMetadata();
        Assert.Equal("下载", metadata.DisplayName);
        Assert.Equal("Bilibili下载器", metadata.MenuCategory);
    }

    private static void ConfigureWbiNav(HttpTest http)
    {
        http.ForCallsTo("https://api.bilibili.com/x/web-interface/nav")
            .RespondWith("""
                {"data":{"wbi_img":{
                  "img_url":"https://i.test/abcdefghijklmnopqrstuvwxyz123456.png",
                  "sub_url":"https://i.test/654321zyxwvutsrqponmlkjihgfedcba.png"
                }}}
                """);
    }

    private sealed class FakeCredentialProviderWithLogin(string cookie)
        : IBiliCredentialProvider
    {
        public string GetCookieHeader() => cookie;
        public bool IsLoggedIn => true;
    }
}
