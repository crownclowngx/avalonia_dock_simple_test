using System.Net;
using System.Text;
using System.Text.Json;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels.BiliDownloader;
using Flurl.Http.Testing;

namespace BiliDownloader.Tests;

/// <summary>
/// P1-G7 的核心业务验收矩阵。测试刻意只使用固定 DASH 与临时文件，既验证业务不变量，
/// 又避免普通单元测试依赖账号、签名 URL 或本机 ffmpeg。
/// </summary>
public sealed class P1G7OutputModesTests
{
    private readonly OutputArtifactPolicy _outputPolicy = new();

    [Fact]
    public void 自动兼容只在指定画质内按Avc到Hevc到Av1选择且同编码取最高带宽()
    {
        var dash = Dash(
            [Video(80, 12, "hev1.1.6.L120", 900), Video(80, 7, "avc1.640028", 800),
             Video(80, 7, "avc1.640028", 1_000), Video(64, 7, "avc1.64001f", 9_999),
             Video(80, 13, "av01.0.08M.08", 1_100)],
            [Audio(30232, 192_000)]);
        var policy = Policy();

        var result = policy.Select(dash, Request(VideoCodecPreference.AutoCompatibility));

        Assert.True(result.Success, result.Message);
        Assert.Equal(VideoCodec.Avc, result.OutputPlan!.ActualVideoCodec);
        Assert.Equal(1_000, result.SelectedVideo!.Bandwidth);
        Assert.Equal([VideoCodec.Avc, VideoCodec.Hevc, VideoCodec.Av1], result.AvailableVideoCodecs);
    }

    [Fact]
    public void 显式编码不可用时返回该画质实际可用集合且绝不跨画质降级()
    {
        var dash = Dash(
            [Video(80, 12, "hev1.1.6.L120", 900), Video(64, 13, "av01.0.08M.08", 2_000)],
            [Audio(30232, 192_000)]);

        var result = Policy().Select(dash, Request(VideoCodecPreference.Av1));

        Assert.False(result.Success);
        Assert.Equal(MediaSelectionFailureCode.ExplicitVideoCodecUnavailable, result.FailureCode);
        Assert.Equal([VideoCodec.Hevc], result.AvailableVideoCodecs);
        Assert.Null(result.SelectedVideo);
    }

    [Fact]
    public void Codecid与Codecs矛盾时按未知编码处理而不是伪造实际值()
    {
        var dash = Dash([Video(80, 7, "hev1.1.6.L120", 1_000)], [Audio(30232, 192_000)]);

        var result = Policy().Select(dash, Request(VideoCodecPreference.AutoCompatibility));

        Assert.False(result.Success);
        Assert.Equal(MediaSelectionFailureCode.VideoStreamUnavailable, result.FailureCode);
        Assert.Empty(result.AvailableVideoCodecs);
    }

    [Theory]
    [InlineData(0, "avc1.640028", VideoCodec.Avc)]
    [InlineData(12, "", VideoCodec.Hevc)]
    [InlineData(0, "av01.0.08M.08", VideoCodec.Av1)]
    public void 编码字段允许单一可信来源但绝不要求伪造缺失字段(
        int codecid, string codecs, VideoCodec expected)
    {
        var dash = Dash([Video(80, codecid, codecs, 1_000)], [Audio(30232, 192_000)]);

        var result = Policy().Select(dash, Request(VideoCodecPreference.AutoCompatibility));

        Assert.True(result.Success, result.Message);
        Assert.Equal(expected, result.OutputPlan!.ActualVideoCodec);
    }

    [Fact]
    public void 三种模式只选择自己需要的流且G7过滤杜比与HiRes音频()
    {
        var dash = Dash(
            [Video(80, 7, "avc1.640028", 1_000)],
            [Audio(30232, 192_000), Audio(30250, 384_000, BiliAudioFeature.Dolby, "ec-3"),
             Audio(30280, 1_000_000, BiliAudioFeature.HiRes, "flac")]);
        var policy = Policy();

        var av = policy.Select(dash, Request(VideoCodecPreference.Avc));
        var video = policy.Select(dash, Request(
            VideoCodecPreference.Avc, OutputMediaMode.VideoOnly, OutputContainer.Mkv));
        var audio = policy.Select(dash, Request(
            VideoCodecPreference.Av1, OutputMediaMode.AudioOnly, OutputContainer.NativeAudio));

        Assert.NotNull(av.SelectedVideo);
        Assert.Equal(30232, av.SelectedAudio!.Id);
        Assert.NotNull(video.SelectedVideo);
        Assert.Null(video.SelectedAudio);
        Assert.Null(audio.SelectedVideo);
        Assert.Equal(30232, audio.SelectedAudio!.Id);
        Assert.Equal(".m4a", audio.OutputPlan!.FileExtension);
        Assert.Equal(VideoCodec.Unknown, audio.OutputPlan.ActualVideoCodec);
    }

    [Fact]
    public void 音频质量为自动或旧质量已消失时都选择最高带宽普通音频()
    {
        var dash = Dash(
            [Video(80, 7, "avc1.640028", 1_000)],
            [Audio(30216, 64_000), Audio(30232, 192_000)]);
        var policy = Policy();
        var automatic = policy.Select(dash, new MediaSelectionRequest(
            80, 0, VideoCodecPreference.Avc, OutputContainer.Mp4, OutputMediaMode.AudioVideo));
        var disappeared = policy.Select(dash, new MediaSelectionRequest(
            80, 99999, VideoCodecPreference.Avc, OutputContainer.Mp4, OutputMediaMode.AudioVideo));

        Assert.Equal(30232, automatic.SelectedAudio!.Id);
        Assert.Equal(30232, disappeared.SelectedAudio!.Id);
    }

    [Theory]
    [InlineData(OutputMediaMode.AudioVideo, OutputContainer.Mp4, ".mp4")]
    [InlineData(OutputMediaMode.AudioVideo, OutputContainer.Mkv, ".mkv")]
    [InlineData(OutputMediaMode.VideoOnly, OutputContainer.Mp4, ".mp4")]
    [InlineData(OutputMediaMode.VideoOnly, OutputContainer.Mkv, ".mkv")]
    [InlineData(OutputMediaMode.AudioOnly, OutputContainer.NativeAudio, ".m4a")]
    public void 输出策略集中维护合法组合与实际扩展名(
        OutputMediaMode mode, OutputContainer container, string extension)
    {
        Assert.True(_outputPolicy.IsValidCombination(mode, container));
        Assert.Equal(extension, _outputPolicy.GetFileExtension(mode, container, AudioCodec.Aac));
    }

    [Theory]
    [InlineData(OutputMediaMode.AudioOnly, OutputContainer.Mp4)]
    [InlineData(OutputMediaMode.AudioOnly, OutputContainer.Mkv)]
    [InlineData(OutputMediaMode.AudioVideo, OutputContainer.NativeAudio)]
    [InlineData(OutputMediaMode.VideoOnly, OutputContainer.NativeAudio)]
    public void 输出策略拒绝非法模式容器组合(OutputMediaMode mode, OutputContainer container)
    {
        Assert.False(_outputPolicy.IsValidCombination(mode, container));
        Assert.Throws<ArgumentException>(() =>
            _outputPolicy.GetFileExtension(mode, container, AudioCodec.Aac));
    }

    [Fact]
    public void 输出计划属性与容器能力对所有模式和未知容器都有确定结果()
    {
        var av = new MediaOutputPlan(VideoCodec.Avc, AudioCodec.Aac,
            OutputContainer.Mp4, OutputMediaMode.AudioVideo, ".mp4", 1, 1);
        var video = av with { OutputMediaMode = OutputMediaMode.VideoOnly, ActualAudioCodec = AudioCodec.Unknown };
        var audio = av with { OutputMediaMode = OutputMediaMode.AudioOnly, ActualVideoCodec = VideoCodec.Unknown };
        var capabilities = new MediaMuxerCapabilities(true, false);

        Assert.True(av.RequiresVideo && av.RequiresAudio && av.RequiresMuxer);
        Assert.True(video.RequiresVideo && !video.RequiresAudio && video.RequiresMuxer);
        Assert.True(!audio.RequiresVideo && audio.RequiresAudio && !audio.RequiresMuxer);
        Assert.True(capabilities.Supports(OutputContainer.Mp4));
        Assert.False(capabilities.Supports(OutputContainer.Mkv));
        Assert.True(capabilities.Supports(OutputContainer.NativeAudio));
        Assert.False(capabilities.Supports((OutputContainer)999));
        Assert.Equal([OutputContainer.Mp4, OutputContainer.Mkv],
            _outputPolicy.GetAllowedContainers(OutputMediaMode.VideoOnly));
        Assert.Equal([OutputContainer.NativeAudio],
            _outputPolicy.GetAllowedContainers(OutputMediaMode.AudioOnly));
        Assert.Throws<ArgumentException>(() => _outputPolicy.GetFileExtension(
            OutputMediaMode.AudioOnly, OutputContainer.NativeAudio, AudioCodec.Unknown));
    }

    [Fact]
    public void 大小计算器对未知时长或零带宽保持未知而不是伪造空间()
    {
        var calculator = new MediaSizeCalculator();
        var plan = new MediaOutputPlan(VideoCodec.Unknown, AudioCodec.Aac,
            OutputContainer.NativeAudio, OutputMediaMode.AudioOnly, ".m4a", 0, 200_000);

        Assert.Null(calculator.EstimatePeakBytes(plan, 0));
        Assert.Null(calculator.EstimatePeakBytes(plan with { AudioBandwidth = 0 }, 100));
        Assert.Equal(5_500_000, calculator.EstimatePeakBytes(plan, 100));
    }

    [Fact]
    public void 选择策略区分非法组合无音频与未知普通音频格式()
    {
        var policy = Policy();
        var dash = Dash([Video(80, 7, "avc1.640028", 1_000)], []);
        var invalid = policy.Select(dash, Request(
            VideoCodecPreference.Avc, OutputMediaMode.AudioOnly, OutputContainer.Mp4));
        var noAudio = policy.Select(dash, Request(VideoCodecPreference.Avc));
        var unknownAudio = Audio(30232, 10, codecs: "opus");
        unknownAudio.MimeType = "audio/webm";
        dash.AudioStreams.Add(unknownAudio);
        var unsupported = policy.Select(dash, Request(VideoCodecPreference.Avc));

        Assert.Equal(MediaSelectionFailureCode.InvalidOutputCombination, invalid.FailureCode);
        Assert.Equal(MediaSelectionFailureCode.AudioStreamUnavailable, noAudio.FailureCode);
        Assert.Equal(MediaSelectionFailureCode.UnsupportedAudioCodec, unsupported.FailureCode);
    }

    [Theory]
    [InlineData("flac", "audio/flac")]
    [InlineData("ec-3", "audio/mp4")]
    public void 即使高规格音频被错误放进普通集合G7也会按实际编码阻止(
        string codecs, string mimeType)
    {
        var audio = Audio(30232, 500_000, codecs: codecs);
        audio.MimeType = mimeType;
        var result = Policy().Select(
            Dash([Video(80, 7, "avc1.640028", 1_000)], [audio]),
            Request(VideoCodecPreference.Avc));

        Assert.Equal(MediaSelectionFailureCode.UnsupportedAudioCodec, result.FailureCode);
    }

    [Fact]
    public void 输出身份规范化会清除当前模式不会消费的配置维度()
    {
        var key = new MediaUnitKey(1, 2);
        var audioA = new RenditionSpecification(80, 30232, VideoCodecPreference.Av1,
            OutputContainer.NativeAudio, OutputMediaMode.AudioOnly);
        var audioB = audioA with { VideoQualityId = 120, VideoCodecPreference = VideoCodecPreference.Avc };
        var videoA = new RenditionSpecification(80, 30232, VideoCodecPreference.Hevc,
            OutputContainer.Mkv, OutputMediaMode.VideoOnly);
        var videoB = videoA with { AudioQualityId = 30280 };

        Assert.Equal(RenditionFingerprint.Create(key, audioA), RenditionFingerprint.Create(key, audioB));
        Assert.Equal(RenditionFingerprint.Create(key, videoA), RenditionFingerprint.Create(key, videoB));
    }

    [Fact]
    public async Task 生产Muxer对仅视频显式Map并禁用音频且始终StreamCopy()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var executable = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        await File.WriteAllTextAsync(executable, "test");
        var processFactory = new FakeFfmpegProcessFactory();
        processFactory.Process.StandardOutput = "ffmpeg version 8.1.2 test";
        var service = new FfmpegService(processFactory, paths) { CustomPath = executable };
        Assert.True((await service.DetectAsync()).IsReady);

        await service.MuxAsync(new MediaMuxRequest(
            "video.tmp", null, "result.mkv", OutputContainer.Mkv, OutputMediaMode.VideoOnly));

        var args = processFactory.StartInfo!.ArgumentList.ToArray();
        AssertContainsSubsequence(args, "-map", "0:v:0", "-an", "-c", "copy");
        Assert.DoesNotContain("-c:v", args);
        Assert.DoesNotContain("-c:a", args);
        Assert.DoesNotContain("audio.tmp", args);
    }

    [Fact]
    public async Task 生产Muxer拒绝缺失输入原生音频与非法容器并缓存已验证路径的能力()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var executable = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        await File.WriteAllTextAsync(executable, "test");
        var processFactory = new FakeFfmpegProcessFactory();
        processFactory.Process.StandardOutput = "ffmpeg version 8.1.2 test";
        var service = new FfmpegService(processFactory, paths) { CustomPath = executable };
        Assert.True((await service.DetectAsync()).IsReady);

        await Assert.ThrowsAsync<ArgumentException>(() => service.MuxAsync(new MediaMuxRequest(
            null, null, "x.mp4", OutputContainer.Mp4, OutputMediaMode.VideoOnly)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MuxAsync(new MediaMuxRequest(
            "v", null, "x.mp4", OutputContainer.Mp4, OutputMediaMode.AudioVideo)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MuxAsync(new MediaMuxRequest(
            null, "a", "x.m4a", OutputContainer.NativeAudio, OutputMediaMode.AudioOnly)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MuxAsync(new MediaMuxRequest(
            "v", null, "x.bin", (OutputContainer)999, OutputMediaMode.VideoOnly)));

        processFactory.Process.StandardOutput = " E mp4 MP4\n E matroska Matroska";
        var first = await service.GetCapabilitiesAsync();
        processFactory.Process.StandardOutput = "";
        var cached = await service.GetCapabilitiesAsync();
        Assert.True(first.SupportsMp4 && first.SupportsMkv);
        Assert.Equal(first, cached);
    }

    [Fact]
    public async Task 历史Muxer兼容入口只接受完整音视频请求并明确拒绝其余模式()
    {
        var implementation = new LegacyMediaMuxer();
        IMediaMuxer legacy = implementation;
        await legacy.MuxAsync(new MediaMuxRequest(
            "v", "a", "out.mp4", OutputContainer.Mp4, OutputMediaMode.AudioVideo));
        Assert.Equal(("v", "a", "out.mp4"), implementation.LastMerge);

        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.MuxAsync(new MediaMuxRequest(
            "v", null, "out.mkv", OutputContainer.Mkv, OutputMediaMode.VideoOnly)));
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.MuxAsync(new MediaMuxRequest(
            null, "a", "out.mp4", OutputContainer.Mp4, OutputMediaMode.AudioVideo)));
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.MuxAsync(new MediaMuxRequest(
            "v", null, "out.mp4", OutputContainer.Mp4, OutputMediaMode.AudioVideo)));
    }

    [Fact]
    public async Task 仅音频执行链路不请求视频不启动Ffmpeg并在输出盘原子发布M4a()
    {
        using var state = new StaticStateScope();
        using var apiHttp = DashHttp(videoCodec: 7);
        var audioBytes = Encoding.UTF8.GetBytes("audio-only-data");
        var mediaHttp = new StubBiliHttpClientFactory(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(audioBytes),
        });
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var muxer = new FakeFfmpegService { ReadyOverride = false };
        using var service = new BiliDownloadService(
            paths, muxer, mediaHttp, new FakeDownloadRuntime(), chunkCount: 1);
        var task = Task("audio-only", paths, OutputMediaMode.AudioOnly, OutputContainer.NativeAudio);

        var result = await service.DownloadItemAsync(
            task, new BiliApiService(), "", _ => { }, (_, _) => { }, CancellationToken.None);

        Assert.Equal(".m4a", Path.GetExtension(result.OutputFilePath));
        Assert.Equal(audioBytes, await File.ReadAllBytesAsync(result.OutputFilePath));
        Assert.Single(mediaHttp.Requests);
        Assert.Equal("/audio", mediaHttp.Requests.Single().Uri!.AbsolutePath);
        Assert.Empty(muxer.MuxCalls);
        Assert.False(File.Exists(Path.Combine(task.TempDirectory, "video.tmp")));
        Assert.False(Directory.GetFiles(paths.RootDirectory, "*.staging-*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task 仅视频执行链路不请求音频并以模式化Mux请求发布Mkv()
    {
        using var state = new StaticStateScope();
        using var apiHttp = DashHttp(videoCodec: 12);
        var mediaHttp = new StubBiliHttpClientFactory(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("video-only-data")),
        });
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var muxer = new FakeFfmpegService { ReadyOverride = true, CreateOutputFile = true };
        using var service = new BiliDownloadService(
            paths, muxer, mediaHttp, new FakeDownloadRuntime(), chunkCount: 1);
        var task = Task("video-only", paths, OutputMediaMode.VideoOnly, OutputContainer.Mkv);
        task.SelectedVideoCodec = VideoCodecPreference.Hevc;

        var result = await service.DownloadItemAsync(
            task, new BiliApiService(), "", _ => { }, (_, _) => { }, CancellationToken.None);

        Assert.Equal(".mkv", Path.GetExtension(result.OutputFilePath));
        Assert.Single(mediaHttp.Requests);
        Assert.Equal("/video", mediaHttp.Requests.Single().Uri!.AbsolutePath);
        var mux = Assert.Single(muxer.MuxCalls);
        Assert.Equal(OutputMediaMode.VideoOnly, mux.OutputMediaMode);
        Assert.Null(mux.AudioPath);
        Assert.False(File.Exists(Path.Combine(task.TempDirectory, "audio.tmp")));
    }

    [Fact]
    public void 配置界面切换仅音频后只暴露原生音频并在切回时恢复视频容器()
    {
        var vm = new DownloadConfigViewModel(new InMemorySettingsRepository())
        {
            OutputContainer = OutputContainer.Mkv,
        };

        vm.OutputMediaMode = OutputMediaMode.AudioOnly;

        Assert.False(vm.IsVideoOutputEnabled);
        Assert.True(vm.IsAudioOutputEnabled);
        Assert.Equal(OutputContainer.NativeAudio, vm.OutputContainer);
        Assert.Equal([OutputContainer.NativeAudio], vm.AllowedOutputContainerOptions.Select(x => x.Value));
        Assert.Equal(0, vm.CaptureRenditionSpecification()!.VideoQualityId);

        vm.OutputMediaMode = OutputMediaMode.VideoOnly;

        Assert.True(vm.IsVideoOutputEnabled);
        Assert.False(vm.IsAudioOutputEnabled);
        Assert.Equal(OutputContainer.Mkv, vm.OutputContainer);
        Assert.Equal([OutputContainer.Mp4, OutputContainer.Mkv],
            vm.AllowedOutputContainerOptions.Select(x => x.Value));
    }

    [Fact]
    public async Task 媒体预检一次Dash请求同时产出安全输出计划与模式感知空间估算()
    {
        var probe = new CountingMediaProbe(Dash(
            [Video(80, 13, "av01.0.08M.08", 1_000_000)],
            [Audio(30232, 200_000)]));
        var analyzer = new DashMediaPreflightAnalyzer(
            probe,
            new FakeCredentialProvider(),
            Policy(),
            new MediaSizeCalculator());
        var item = new DownloadSubmissionItem(
            "session-item", "title", 1, "BV1TEST0001", 2, 100, BiliMediaType.Video, 0, 0, "");
        var profile = new DownloadProfileSnapshot(
            VideoQualityId: 80,
            AudioQualityId: 30232,
            OutputDirectory: "out",
            UseGroupFolder: false,
            AddIndexToTitle: false,
            DownloadDanmaku: false,
            DownloadSubtitle: false,
            DownloadCover: false,
            NamingTemplate: "{title}",
            ConflictPolicy: FileConflictPolicy.AutoNumber,
            VideoCodecPreference: VideoCodecPreference.Av1,
            OutputContainer: OutputContainer.Mkv,
            OutputMediaMode: OutputMediaMode.AudioVideo);

        var result = await analyzer.AnalyzeAsync(item, profile, CancellationToken.None);

        Assert.Equal(1, probe.CallCount);
        Assert.True(result.Selection.Success, result.Selection.Message);
        Assert.Equal(VideoCodec.Av1, result.Selection.OutputPlan!.ActualVideoCodec);
        Assert.Equal(".mkv", result.Selection.OutputPlan.FileExtension);
        Assert.Equal(33_000_000, result.EstimatedPeakBytes);
        Assert.DoesNotContain("http", result.Selection.OutputPlan.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SESSDATA", result.Selection.OutputPlan.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 实际视频编码可在下载前通过仓储窄方法原子更新并完整往返()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var task = Task("actual-codec", paths, OutputMediaMode.VideoOnly, OutputContainer.Mkv);
        task.ActualVideoCodec = "";
        await store.InsertBatchAsync([task]);
        var observedAt = DateTime.Parse("2026-08-07 12:34:56");

        await store.UpdateActualVideoCodecAsync(task.TaskId, "hevc", observedAt);

        var restored = Assert.Single(await store.GetAllAsync());
        Assert.Equal("hevc", restored.ActualVideoCodec);
        Assert.Equal(observedAt, restored.LastUpdatedAt);
    }

    [Fact]
    public async Task 原生音频发布器拒绝跨目录Staging并支持明确授权覆盖且失败会清理半成品()
    {
        using var paths = new TestDataPaths();
        var first = Path.Combine(paths.RootDirectory, "first");
        var second = Path.Combine(paths.RootDirectory, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var source = Path.Combine(first, "source.m4a");
        var output = Path.Combine(first, "output.m4a");
        var staging = Path.Combine(first, "output.staging.m4a");
        await File.WriteAllTextAsync(source, "new");
        await File.WriteAllTextAsync(output, "old");
        var publisher = new NativeAudioPublisher();

        await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(
            source, Path.Combine(second, "cross.m4a"), output, false));
        await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(
            source, staging, output, false));
        Assert.False(File.Exists(staging));

        await publisher.PublishAsync(source, staging, output, true);
        Assert.Equal("new", await File.ReadAllTextAsync(output));
        Assert.False(File.Exists(staging));
    }

    private MediaStreamSelectionPolicy Policy() => new(_outputPolicy);

    private static MediaSelectionRequest Request(
        VideoCodecPreference codec,
        OutputMediaMode mode = OutputMediaMode.AudioVideo,
        OutputContainer container = OutputContainer.Mp4)
        => new(80, 30232, codec, container, mode);

    private static BiliDashResult Dash(
        IEnumerable<BiliDashStream> video,
        IEnumerable<BiliDashStream> audio)
        => new() { VideoStreams = video.ToList(), AudioStreams = audio.ToList() };

    private static BiliDashStream Video(int id, int codecid, string codecs, long bandwidth) => new()
    {
        Id = id, Codecid = codecid, Codecs = codecs, Bandwidth = bandwidth,
        MimeType = "video/mp4", ContainerHint = DashContainerHint.Mp4, BaseUrl = "https://media.test/video",
    };

    private static BiliDashStream Audio(
        int id, long bandwidth, BiliAudioFeature feature = BiliAudioFeature.Standard, string codecs = "mp4a.40.2") => new()
    {
        Id = id, Codecs = codecs, Bandwidth = bandwidth, AudioFeature = feature,
        MimeType = codecs == "flac" ? "audio/flac" : "audio/mp4",
        ContainerHint = codecs == "flac" ? DashContainerHint.Flac : DashContainerHint.Mp4,
        BaseUrl = "https://media.test/audio",
    };

    private static HttpTest DashHttp(int videoCodec)
    {
        var http = new HttpTest();
        http.ForCallsTo("https://api.bilibili.com/x/web-interface/nav").RespondWith("""
            {"code":0,"data":{"wbi_img":{
              "img_url":"https://i.test/abcdefghijklmnopqrstuvwxyz123456.png",
              "sub_url":"https://i.test/654321zyxwvutsrqponmlkjihgfedcba.png"}}}
            """);
        var codec = videoCodec == 12 ? "hev1.1.6.L120" : "avc1.640028";
        http.ForCallsTo("*x/player/wbi/playurl*").RespondWith(JsonSerializer.Serialize(new
        {
            code = 0,
            data = new
            {
                dash = new
                {
                    video = new[] { new { id = 80, base_url = "https://media.test/video", codecid = videoCodec, codecs = codec, mime_type = "video/mp4", bandwidth = 1_000_000 } },
                    audio = new[] { new { id = 30232, base_url = "https://media.test/audio", codecs = "mp4a.40.2", mime_type = "audio/mp4", bandwidth = 192_000 } },
                },
            },
        }));
        return http;
    }

    private static DownloadTaskRecord Task(
        string id, TestDataPaths paths, OutputMediaMode mode, OutputContainer container) => new()
    {
        TaskId = id,
        ItemTitle = id,
        Aid = 1,
        Cid = 2,
        QualityId = 80,
        AudioQualityId = 30232,
        OutputDirectory = paths.RootDirectory,
        TempDirectory = Path.Combine(paths.TempDirectory, id),
        MediaType = "video",
        SelectedOutputMediaMode = mode,
        SelectedOutputContainer = container,
        SelectedVideoCodec = VideoCodecPreference.AutoCompatibility,
    };

    private static void AssertContainsSubsequence(string[] source, params string[] expected)
    {
        var index = 0;
        foreach (var value in source)
            if (index < expected.Length && value == expected[index]) index++;
        Assert.Equal(expected.Length, index);
    }

    private sealed class CountingMediaProbe(BiliDashResult result) : IBiliMediaProbe
    {
        public int CallCount { get; private set; }

        public Task<BiliDashResult> GetDashResultAsync(
            long aid,
            long cid,
            int qualityId,
            string cookie,
            BiliMediaType mediaType = BiliMediaType.Video,
            long epId = 0,
            long seasonId = 0,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return System.Threading.Tasks.Task.FromResult(result);
        }
    }

    private sealed class LegacyMediaMuxer : IMediaMuxer
    {
        public (string Video, string Audio, string Output)? LastMerge { get; private set; }

        public Task MergeAsync(
            string videoPath,
            string audioPath,
            string outputPath,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastMerge = (videoPath, audioPath, outputPath);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
