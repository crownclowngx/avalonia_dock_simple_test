using System.Net;
using System.Text;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Download.Extras;
using Flurl.Http.Testing;

namespace BiliDownloader.Tests;

public sealed class ExtrasAndProgressTests
{
    private static readonly byte[] DanmakuFixture =
    [
        0x0A, 0x29,
        0x08, 0x96, 0x01,
        0x10, 0xE2, 0x09,
        0x18, 0x01,
        0x20, 0x19,
        0x28, 0xFF, 0xFF, 0xFF, 0x07,
        0x32, 0x03, 0x61, 0x62, 0x63,
        0x3A, 0x06, 0xE6, 0xB5, 0x8B, 0xE8, 0xAF, 0x95,
        0x40, 0xC0, 0x90, 0xEE, 0x86, 0x06,
        0x50, 0x00,
        0x5A, 0x03, 0x31, 0x35, 0x30,
    ];

    [Fact]
    public void Extras注册表按固定顺序解析位枚举()
    {
        var registry = new ExtrasHandlerRegistry();
        registry.Register(new FakeExtrasHandler("danmaku"));
        registry.Register(new FakeExtrasHandler("cover"));
        registry.Register(new FakeExtrasHandler("subtitle"));

        var result = registry.Resolve(
            ExtrasType.Danmaku | ExtrasType.Subtitle | ExtrasType.Cover);

        Assert.Equal(["cover", "subtitle", "danmaku"], result.Select(x => x.Type));
        Assert.Empty(registry.Resolve(ExtrasType.None));
    }

    [Fact]
    public void Extras重复注册会替换且默认注册表包含全部内置处理器()
    {
        var registry = new ExtrasHandlerRegistry();
        var old = new FakeExtrasHandler("cover", "old");
        var replacement = new FakeExtrasHandler("cover", "new");
        registry.Register(old);
        registry.Register(replacement);

        Assert.Same(replacement, Assert.Single(registry.Resolve(ExtrasType.Cover)));
        Assert.Equal(
            ["cover", "subtitle", "danmaku"],
            ExtrasHandlerRegistry.CreateDefault()
                .Resolve(ExtrasType.Cover | ExtrasType.Subtitle | ExtrasType.Danmaku)
                .Select(x => x.Type));
    }

    [Fact]
    public async Task 字幕处理器拒绝空Cid和无字幕()
    {
        using var paths = new TestDataPaths();
        var handler = new SubtitleExtrasHandler();
        var noCid = await handler.ExecuteAsync(
            CreateContext(paths, cid: 0),
            CancellationToken.None);
        Assert.False(noCid.Success);
        Assert.Contains("cid", noCid.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        using var state = new StaticStateScope();
        using var http = new HttpTest();
        ConfigureWbiNav(http);
        http.ForCallsTo("*x/player/wbi/v2*")
            .RespondWith("""{"code":0,"data":{"subtitle":{"subtitles":[]}}}""");
        var empty = await handler.ExecuteAsync(
            CreateContext(paths),
            CancellationToken.None);
        Assert.False(empty.Success);
        Assert.Contains("没有可用字幕", empty.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 字幕处理器允许单项失败并保存其他字幕()
    {
        using var paths = new TestDataPaths();
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        ConfigureWbiNav(http);
        http.ForCallsTo("*x/player/wbi/v2*")
            .RespondWith("""
                {"code":0,"data":{"subtitle":{"subtitles":[
                  {"lan":"bad","lan_doc":"失败","subtitle_url":"https://sub.test/bad"},
                  {"lan":"zh","lan_doc":"中文","subtitle_url":"https://sub.test/zh"}
                ]}}}
                """);
        http.ForCallsTo("https://sub.test/bad")
            .RespondWith("""{"body":[]}""");
        http.ForCallsTo("https://sub.test/zh")
            .RespondWith("""{"body":[{"from":0,"to":1,"content":"字幕"}]}""");
        var context = CreateContext(paths);
        context = Copy(context, subFolder: "group");

        var result = await new SubtitleExtrasHandler().ExecuteAsync(
            context,
            CancellationToken.None);

        Assert.True(result.Success);
        var output = Assert.Single(result.OutputFiles);
        Assert.EndsWith(
            Path.Combine("group", "video.zh.srt"),
            output,
            StringComparison.Ordinal);
        Assert.Contains("字幕", await File.ReadAllTextAsync(output), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 字幕处理器传播取消()
    {
        using var paths = new TestDataPaths();
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        ConfigureWbiNav(http);
        http.ForCallsTo("*x/player/wbi/v2*")
            .RespondWith("""
                {"code":0,"data":{"subtitle":{"subtitles":[
                  {"lan":"zh","lan_doc":"中文","subtitle_url":"https://sub.test/zh"}
                ]}}}
                """);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SubtitleExtrasHandler().ExecuteAsync(
                CreateContext(paths),
                cts.Token));
    }

    [Fact]
    public async Task 弹幕处理器解码并写出XML()
    {
        using var paths = new TestDataPaths();
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        ConfigureWbiNav(http);
        http.ForCallsTo("*x/v2/dm/wbi/web/seg.so*")
            .RespondWith(() => new ByteArrayContent(DanmakuFixture));

        var result = await new DanmakuExtrasHandler().ExecuteAsync(
            CreateContext(paths),
            CancellationToken.None);

        Assert.True(result.Success);
        var output = Assert.Single(result.OutputFiles);
        var xml = await File.ReadAllTextAsync(output);
        Assert.Contains("<i>", xml, StringComparison.Ordinal);
        Assert.Contains("测试", xml, StringComparison.Ordinal);
        http.ShouldHaveCalled("*seg.so*")
            .WithQueryParam("segment_index", "1")
            .Times(1);
    }

    [Fact]
    public async Task 弹幕处理器拒绝空Cid和空结果()
    {
        using var paths = new TestDataPaths();
        var noCid = await new DanmakuExtrasHandler().ExecuteAsync(
            CreateContext(paths, cid: 0),
            CancellationToken.None);
        Assert.False(noCid.Success);

        using var state = new StaticStateScope();
        using var http = new HttpTest();
        ConfigureWbiNav(http);
        http.ForCallsTo("*seg.so*")
            .RespondWith(() => new ByteArrayContent([0x01, 0x02]));
        var empty = await new DanmakuExtrasHandler().ExecuteAsync(
            CreateContext(paths),
            CancellationToken.None);
        Assert.False(empty.Success);
        Assert.Contains("未获取到", empty.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 封面处理器空URL和非法URL返回失败结果()
    {
        using var paths = new TestDataPaths();
        using var handler = new CoverExtrasHandler();

        var empty = await handler.ExecuteAsync(
            Copy(CreateContext(paths), coverUrl: ""),
            CancellationToken.None);
        var invalid = await handler.ExecuteAsync(
            Copy(CreateContext(paths), coverUrl: "not a valid URI"),
            CancellationToken.None);

        Assert.False(empty.Success);
        Assert.False(invalid.Success);
        Assert.Contains("URL", empty.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("封面下载失败", invalid.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 封面处理器通过注入客户端规范化Https并写入文件()
    {
        var expected = Encoding.UTF8.GetBytes("cover-bytes");
        var factory = new StubBiliHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        });
        using var handler = new CoverExtrasHandler(factory);
        using var paths = new TestDataPaths();

        var result = await handler.ExecuteAsync(
            Copy(CreateContext(paths), coverUrl: "http://image.example.test/cover.jpg?q=1"),
            CancellationToken.None);

        Assert.True(result.Success);
        var output = Assert.Single(result.OutputFiles);
        Assert.Equal(expected, await File.ReadAllBytesAsync(output));
        var request = Assert.Single(factory.Requests);
        Assert.Equal("https", request.Uri?.Scheme);
        Assert.Equal("/cover.jpg?q=1", request.Uri?.PathAndQuery);
        Assert.Contains("bilibili.com", request.Headers["Referer"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task 附加资源未确认覆盖时保留旧文件并返回失败()
    {
        var factory = new StubBiliHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("new")),
        });
        using var handler = new CoverExtrasHandler(factory);
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "video_cover.jpg");
        await File.WriteAllTextAsync(output, "old");

        var result = await handler.ExecuteAsync(CreateContext(paths), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("old", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task 附加资源只有覆盖策略已确认时才原子替换()
    {
        var factory = new StubBiliHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("new")),
        });
        using var handler = new CoverExtrasHandler(factory);
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "video_cover.jpg");
        await File.WriteAllTextAsync(output, "old");
        var context = Copy(CreateContext(paths),
            conflictPolicy: FileConflictPolicy.Overwrite,
            overwriteConfirmed: true);

        var result = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("new", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task 进度追踪器映射阶段节流落库但每次广播UI()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var task = new DownloadTaskRecord
        {
            TaskId = "progress",
            DocumentId = "doc",
            ItemTitle = "title",
            Status = "pending",
        };
        repository.Seed(task);
        var messenger = new RecordingHostEventBus();
        var tracker = new DownloadProgressTracker(repository, messenger);

        tracker.OnProgressChanged(task, new DownloadProgressInfo
        {
            Stage = "video",
            OverallProgress = 10,
            VideoProgress = 20,
            SpeedText = "1 MB/s",
        });
        tracker.OnProgressChanged(task, new DownloadProgressInfo
        {
            Stage = "audio",
            OverallProgress = 50,
            VideoProgress = 100,
            AudioProgress = 10,
        });
        tracker.OnProgressChanged(task, new DownloadProgressInfo
        {
            Stage = "merging",
            OverallProgress = 91,
            VideoProgress = 100,
            AudioProgress = 100,
            MergeProgress = 10,
        });

        // 验证每次进度变更都广播了 UI（广播不受节流影响）
        Assert.Equal(3, messenger.SentMessages.OfType<DownloadTaskProgressMessage>().Count());

        // G3: 关闭并等待写入完成，验证至少有写入发生
        await tracker.ShutdownAsync();
        var stageWrites = repository.CallLog.Where(x => x.StartsWith("repository:stage:", StringComparison.Ordinal)).ToList();
        Assert.True(stageWrites.Count >= 1, $"期望至少 1 次 stage 写入，实际 {stageWrites.Count}");
    }

    [Fact]
    public async Task 字节更新节流且状态广播包含定向文档信息()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var task = new DownloadTaskRecord
        {
            TaskId = "bytes",
            DocumentId = "doc-target",
            Status = "failed",
            Progress = 33,
        };
        repository.Seed(task);
        var messenger = new RecordingHostEventBus();
        var tracker = new DownloadProgressTracker(repository, messenger);

        tracker.OnBytesChanged(task, 10, 20);
        tracker.OnBytesChanged(task, 30, 40);
        tracker.BroadcastStatusChanged(task);

        // G3: 关闭 Channel 并等待所有待写入落盘
        await tracker.ShutdownAsync();

        Assert.True(repository.CallLog.Count(x => x == "repository:bytes") >= 1);
        Assert.Equal(30, task.VideoBytesDownloaded);
        Assert.Equal(40, task.AudioBytesDownloaded);
        var status = Assert.IsType<DownloadTaskStatusChangedMessage>(
            Assert.Single(messenger.SentMessages));
        Assert.Equal("doc-target", status.TargetDocumentId);
        Assert.Equal("bytes", status.TaskId);
        Assert.Equal("failed", status.NewStatus);
    }

    private static ExtrasContext CreateContext(TestDataPaths paths, long cid = 2)
        => new()
        {
            TaskId = "task",
            Aid = 1,
            Bvid = "BV1abcDEF123",
            Cid = cid,
            Duration = 1,
            OutputDirectory = paths.RootDirectory,
            BaseFileName = "video",
            Cookie = "SESSDATA=test",
            CoverUrl = "https://cover.test/image.jpg",
            ApiService = new BiliApiService(),
        };

    private static ExtrasContext Copy(
        ExtrasContext source,
        string? subFolder = null,
        string? coverUrl = null,
        FileConflictPolicy? conflictPolicy = null,
        bool? overwriteConfirmed = null)
        => new()
        {
            TaskId = source.TaskId,
            Aid = source.Aid,
            Bvid = source.Bvid,
            Cid = source.Cid,
            Duration = source.Duration,
            OutputDirectory = source.OutputDirectory,
            SubFolder = subFolder ?? source.SubFolder,
            BaseFileName = source.BaseFileName,
            ConflictPolicy = conflictPolicy ?? source.ConflictPolicy,
            OverwriteConfirmed = overwriteConfirmed ?? source.OverwriteConfirmed,
            Cookie = source.Cookie,
            CoverUrl = coverUrl ?? source.CoverUrl,
            ApiService = source.ApiService,
        };

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

    private sealed class FakeExtrasHandler(string type, string? displayName = null) : IExtrasHandler
    {
        public string Type { get; } = type;
        public string DisplayName { get; } = displayName ?? type;

        public Task<ExtrasResult> ExecuteAsync(ExtrasContext context, CancellationToken ct)
            => Task.FromResult(ExtrasResult.Succeeded(Type));
    }
}
