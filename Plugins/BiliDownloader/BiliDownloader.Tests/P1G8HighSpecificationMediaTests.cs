using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.ViewModels.BiliDownloader;

namespace BiliDownloader.Tests;

/// <summary>
/// P1-G8 高规格媒体核心验收矩阵。所有测试均使用离线 DASH/ffprobe JSON，确保失败可稳定复现，
/// 并把平台网络波动留给独立发布验收而不是污染业务单元测试。
/// </summary>
public sealed class P1G8HighSpecificationMediaTests
{
    private readonly MediaStreamSelectionPolicy _policy = new(new OutputArtifactPolicy());

    [Fact]
    public void 自动模式按杜比视界和Atmos优先且记录预期事实()
    {
        var dash = Dash(
            [Video(80, VideoCodec.Avc), Video(125, VideoCodec.Hevc, MediaFeatureFlags.Hdr),
             Video(126, VideoCodec.Hevc, MediaFeatureFlags.DolbyVision)],
            [Audio(30232, AudioCodec.Aac), Audio(30280, AudioCodec.Flac, MediaFeatureFlags.HiResAudio),
             Audio(30250, AudioCodec.DolbyDigitalPlus, MediaFeatureFlags.DolbyAtmos)]);

        var result = _policy.Select(dash, Request(OutputContainer.Mkv));

        Assert.True(result.Success, result.Message);
        Assert.Equal(126, result.SelectedVideo!.Id);
        Assert.Equal(30250, result.SelectedAudio!.Id);
        Assert.Equal(MediaFeatureFlags.DolbyVision | MediaFeatureFlags.DolbyAtmos,
            result.OutputPlan!.ExpectedMediaFeatures);
    }

    [Fact]
    public void 自动模式在Mp4跳过不兼容HiRes并回退标准音频()
    {
        var dash = Dash(
            [Video(125, VideoCodec.Hevc, MediaFeatureFlags.Hdr)],
            [Audio(30232, AudioCodec.Aac), Audio(30280, AudioCodec.Flac, MediaFeatureFlags.HiResAudio)]);

        var result = _policy.Select(dash, Request(OutputContainer.Mp4));

        Assert.True(result.Success, result.Message);
        Assert.Equal(30232, result.SelectedAudio!.Id);
        Assert.Equal(MediaFeatureFlags.Hdr, result.OutputPlan!.ExpectedMediaFeatures);
    }

    [Fact]
    public void 显式HiRes与Mp4不兼容时阻止且绝不降级()
    {
        var dash = Dash(
            [Video(80, VideoCodec.Avc)],
            [Audio(30232, AudioCodec.Aac), Audio(30280, AudioCodec.Flac, MediaFeatureFlags.HiResAudio)]);
        var request = Request(OutputContainer.Mp4) with { AudioFeaturePreference = AudioFeaturePreference.HiRes };

        var result = _policy.Select(dash, request);

        Assert.False(result.Success);
        Assert.Equal(MediaSelectionFailureCode.MediaFeatureIncompatibleWithContainer, result.FailureCode);
        Assert.Null(result.OutputPlan);
    }

    [Fact]
    public void 显式受限规格返回结构化失败而不选择标准流()
    {
        var dash = Dash([Video(80, VideoCodec.Avc)], [Audio(30232, AudioCodec.Aac)]);
        dash.Capabilities = new MediaCapabilitySnapshot(
        [
            new(MediaFeatureFlags.DolbyVision, MediaCapabilityAvailability.RequiresPremium, "need_vip"),
        ]);
        var request = Request(OutputContainer.Mkv) with
        {
            VideoDynamicRangePreference = VideoDynamicRangePreference.DolbyVision,
        };

        var result = _policy.Select(dash, request);

        Assert.False(result.Success);
        Assert.Equal(MediaSelectionFailureCode.ExplicitMediaFeatureUnavailable, result.FailureCode);
        Assert.Contains("大会员", result.Message, StringComparison.Ordinal);
        Assert.Null(result.SelectedVideo);
    }

    [Fact]
    public void 原生音频扩展名由实际编码决定()
    {
        var policy = new OutputArtifactPolicy();

        Assert.Equal(".m4a", policy.GetFileExtension(OutputMediaMode.AudioOnly, OutputContainer.NativeAudio,
            AudioCodec.DolbyDigitalPlus));
        Assert.Equal(".flac", policy.GetFileExtension(OutputMediaMode.AudioOnly, OutputContainer.NativeAudio,
            AudioCodec.Flac));
    }

    [Fact]
    public void 显式Hdr和标准音频只选择各自层级()
    {
        var dash = Dash(
            [Video(80, VideoCodec.Avc), Video(125, VideoCodec.Hevc, MediaFeatureFlags.Hdr),
             Video(126, VideoCodec.Hevc, MediaFeatureFlags.DolbyVision)],
            [Audio(30232, AudioCodec.Aac), Audio(30250, AudioCodec.DolbyDigitalPlus, MediaFeatureFlags.DolbyAtmos)]);
        var request = Request(OutputContainer.Mkv) with
        {
            VideoDynamicRangePreference = VideoDynamicRangePreference.Hdr,
            AudioFeaturePreference = AudioFeaturePreference.Standard,
        };

        var result = _policy.Select(dash, request);

        Assert.True(result.Success, result.Message);
        Assert.Equal(125, result.SelectedVideo!.Id);
        Assert.Equal(30232, result.SelectedAudio!.Id);
        Assert.Equal(MediaFeatureFlags.Hdr, result.OutputPlan!.ExpectedMediaFeatures);
    }

    [Fact]
    public void 仅音频自动选择HiRes并生成Flac而仅视频清除音频特征()
    {
        var dash = Dash(
            [Video(126, VideoCodec.Hevc, MediaFeatureFlags.DolbyVision)],
            [Audio(30280, AudioCodec.Flac, MediaFeatureFlags.HiResAudio)]);
        var audio = _policy.Select(dash, Request(OutputContainer.NativeAudio) with
        {
            OutputMediaMode = OutputMediaMode.AudioOnly,
        });
        var video = _policy.Select(dash, Request(OutputContainer.Mkv) with
        {
            OutputMediaMode = OutputMediaMode.VideoOnly,
        });

        Assert.True(audio.Success, audio.Message);
        Assert.Equal(".flac", audio.OutputPlan!.FileExtension);
        Assert.Equal(MediaFeatureFlags.HiResAudio, audio.OutputPlan.ExpectedMediaFeatures);
        Assert.True(video.Success, video.Message);
        Assert.Equal(MediaFeatureFlags.DolbyVision, video.OutputPlan!.ExpectedMediaFeatures);
        Assert.Null(video.SelectedAudio);
    }

    [Fact]
    public void 输出策略在不消费的维度忽略特征并拒绝非法组合和未知音频编码()
    {
        var policy = new OutputArtifactPolicy();

        Assert.True(policy.SupportsFeatures(OutputMediaMode.VideoOnly, OutputContainer.Mp4,
            MediaFeatureFlags.HiResAudio));
        Assert.True(policy.SupportsFeatures(OutputMediaMode.AudioOnly, OutputContainer.NativeAudio,
            MediaFeatureFlags.Hdr));
        Assert.False(policy.SupportsFeatures(OutputMediaMode.AudioVideo, OutputContainer.NativeAudio,
            MediaFeatureFlags.Hdr));
        Assert.Throws<ArgumentException>(() => policy.GetFileExtension(
            OutputMediaMode.AudioOnly, OutputContainer.NativeAudio, AudioCodec.Unknown));
    }

    [Fact]
    public void 指纹V2包含高规格偏好且仍能读取旧V1()
    {
        var media = new BiliDownloader.Models.ContentSources.MediaUnitKey(1, 2);
        var standard = new RenditionSpecification(80, 30232, VideoCodecPreference.Avc,
            OutputContainer.Mp4, OutputMediaMode.AudioVideo,
            VideoDynamicRangePreference.Standard, AudioFeaturePreference.Standard);
        var hdr = standard with { VideoDynamicRangePreference = VideoDynamicRangePreference.Hdr };

        var first = RenditionFingerprint.Create(media, standard);
        var second = RenditionFingerprint.Create(media, hdr);

        Assert.StartsWith("rf2:", first.Value, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(RenditionFingerprint.TryParse("rf1:" + new string('a', 64), out _));
        Assert.False(RenditionFingerprint.TryParse("rx2:" + new string('a', 64), out _));
        Assert.False(RenditionFingerprint.TryParse("rf2:short", out _));
        Assert.False(RenditionFingerprint.TryParse("rf2:" + new string('z', 64), out _));
    }

    [Theory]
    [MemberData(nameof(FfprobeCases))]
    public void Ffprobe结构化字段识别高规格且不根据码率猜测(string json, MediaFeatureFlags expected)
        => Assert.Equal(expected, FfprobeMediaOutputVerifier.ParseFeatures(json));

    public static TheoryData<string, MediaFeatureFlags> FfprobeCases => new()
    {
        {
            """{"streams":[{"codec_type":"video","color_primaries":"bt2020","color_transfer":"smpte2084"}]}""",
            MediaFeatureFlags.Hdr
        },
        {
            """{"streams":[{"codec_type":"video","color_primaries":"bt2020","color_transfer":"smpte2084","side_data_list":[{"side_data_type":"DOVI configuration record","dv_profile":8}]}]}""",
            MediaFeatureFlags.DolbyVision
        },
        {
            """{"streams":[{"codec_type":"audio","codec_name":"flac","bit_rate":"1000000"}]}""",
            MediaFeatureFlags.HiResAudio
        },
        {
            """{"streams":[{"codec_type":"audio","codec_name":"eac3","profile":"Dolby Digital Plus + Dolby Atmos"}]}""",
            MediaFeatureFlags.DolbyAtmos
        },
        {
            """{"streams":[{"codec_type":"audio","codec_name":"eac3","profile":"E-AC-3","bit_rate":"768000"},{"codec_type":"video","codec_name":"hevc","bit_rate":"20000000"}]}""",
            MediaFeatureFlags.None
        },
    };

    [Fact]
    public async Task 验证器成功时返回实际特征且参数不经过Shell拼接()
    {
        using var environment = new FfprobeEnvironment();
        var factory = new FakeFfmpegProcessFactory();
        factory.Process.StandardOutput =
            """{"streams":[{"codec_type":"video","color_primaries":"bt2020","color_transfer":"smpte2084"}]}""";
        var verifier = new FfprobeMediaOutputVerifier(environment.Locator, factory);

        var actual = await verifier.VerifyAsync(environment.MediaPath, MediaFeatureFlags.Hdr);

        Assert.Equal(MediaFeatureFlags.Hdr, actual);
        Assert.Equal(environment.FfprobePath, factory.StartInfo!.FileName);
        Assert.Contains(environment.MediaPath, factory.StartInfo.ArgumentList);
        Assert.False(factory.StartInfo.UseShellExecute);
    }

    [Fact]
    public async Task 验证器对冲突和损坏Json给出媒体校验错误()
    {
        using var environment = new FfprobeEnvironment();
        var factory = new FakeFfmpegProcessFactory();
        factory.Process.StandardOutput = """{"streams":[]}""";
        var verifier = new FfprobeMediaOutputVerifier(environment.Locator, factory);

        var conflict = await Assert.ThrowsAsync<MediaValidationException>(() =>
            verifier.VerifyAsync(environment.MediaPath, MediaFeatureFlags.DolbyVision));
        Assert.Contains("特征冲突", conflict.Message, StringComparison.Ordinal);

        factory.Process.StandardOutput = "not-json";
        await Assert.ThrowsAsync<MediaValidationException>(() =>
            verifier.VerifyAsync(environment.MediaPath, MediaFeatureFlags.Hdr));
    }

    [Fact]
    public async Task 验证器超时会终止进程而外部取消保持取消语义()
    {
        using var environment = new FfprobeEnvironment();
        var timeoutFactory = new FakeFfmpegProcessFactory();
        timeoutFactory.Process.BlockUntilCancelled = true;
        var verifier = new FfprobeMediaOutputVerifier(
            environment.Locator, timeoutFactory, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<MediaValidationException>(() =>
            verifier.VerifyAsync(environment.MediaPath, MediaFeatureFlags.Hdr));
        Assert.True(timeoutFactory.Process.KillCalled);

        var cancelFactory = new FakeFfmpegProcessFactory();
        cancelFactory.Process.BlockUntilCancelled = true;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelVerifier = new FfprobeMediaOutputVerifier(environment.Locator, cancelFactory);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelVerifier.VerifyAsync(environment.MediaPath, MediaFeatureFlags.Hdr, cts.Token));
    }

    [Fact]
    public async Task Sqlite区分旧任务未知与新任务明确标准规格()
    {
        using var paths = new TestDataPaths();
        var store = new BiliDownloader.Services.Persistence.DownloadTaskStore(paths);
        await store.InitAsync();
        var legacy = Record("legacy");
        var current = Record("current");
        current.SubmissionSnapshotVersion = 2;
        current.SelectedVideoDynamicRangePreference = VideoDynamicRangePreference.Standard;
        current.SelectedAudioFeaturePreference = AudioFeaturePreference.Standard;
        current.RequestedMediaFeatures = MediaFeatureFlags.None;
        current.ExpectedMediaFeatures = MediaFeatureFlags.None;
        current.ActualMediaFeatures = MediaFeatureFlags.None;
        await store.InsertBatchAsync([legacy, current]);

        var rows = (await store.GetAllAsync()).ToDictionary(row => row.TaskId);

        Assert.Null(rows["legacy"].ActualMediaFeatures);
        Assert.Equal(MediaFeatureFlags.None, rows["current"].ActualMediaFeatures);
        Assert.Equal(VideoDynamicRangePreference.Standard,
            rows["current"].SelectedVideoDynamicRangePreference);
    }

    [Fact]
    public async Task 批量能力探测计算交集复用脱敏缓存并可显式清空()
    {
        var probe = new CapabilityProbe(new Dictionary<long, MediaCapabilitySnapshot>
        {
            [1] = Capabilities(
                (MediaFeatureFlags.Hdr, MediaCapabilityAvailability.Available),
                (MediaFeatureFlags.DolbyVision, MediaCapabilityAvailability.Available),
                (MediaFeatureFlags.HiResAudio, MediaCapabilityAvailability.Available),
                (MediaFeatureFlags.DolbyAtmos, MediaCapabilityAvailability.RequiresLogin)),
            [2] = Capabilities(
                (MediaFeatureFlags.Hdr, MediaCapabilityAvailability.Available),
                (MediaFeatureFlags.DolbyVision, MediaCapabilityAvailability.Unavailable),
                (MediaFeatureFlags.HiResAudio, MediaCapabilityAvailability.RequiresPremium),
                (MediaFeatureFlags.DolbyAtmos, MediaCapabilityAvailability.Unknown)),
        });
        var service = new MediaCapabilityInspectionService(probe, new AnonymousCredentials());
        BiliVideoItem[] items = [new() { Aid = 1, Cid = 1 }, new() { Aid = 2, Cid = 2 }];

        var first = await service.InspectAsync(items, 80);
        var cached = await service.InspectAsync(items, 80);

        Assert.Equal(MediaCapabilityAvailability.Available, first.GetAvailability(MediaFeatureFlags.Hdr));
        Assert.Equal(MediaCapabilityAvailability.Unavailable, first.GetAvailability(MediaFeatureFlags.DolbyVision));
        Assert.Equal(MediaCapabilityAvailability.RequiresPremium, first.GetAvailability(MediaFeatureFlags.HiResAudio));
        Assert.Equal(MediaCapabilityAvailability.RequiresLogin, first.GetAvailability(MediaFeatureFlags.DolbyAtmos));
        Assert.Equal(2, first.AvailableCounts[MediaFeatureFlags.Hdr]);
        Assert.Equal(2, probe.CallCount);
        Assert.Equal(first.GetAvailability(MediaFeatureFlags.Hdr),
            cached.GetAvailability(MediaFeatureFlags.Hdr));

        service.Clear();
        await service.InspectAsync(items, 80);
        Assert.Equal(4, probe.CallCount);

        var empty = await service.InspectAsync([], 80);
        Assert.Equal(0, empty.ItemCount);
        Assert.Equal(MediaCapabilityAvailability.Unknown, empty.GetAvailability(MediaFeatureFlags.Hdr));
    }

    [Fact]
    public async Task 失败探测不会污染会话缓存()
    {
        var probe = new CapabilityProbe(new Dictionary<long, MediaCapabilitySnapshot>
        {
            [1] = Capabilities((MediaFeatureFlags.Hdr, MediaCapabilityAvailability.Available)),
        }) { FailNext = true };
        var service = new MediaCapabilityInspectionService(probe, new AnonymousCredentials());
        BiliVideoItem[] items = [new() { Aid = 1, Cid = 1 }];

        await Assert.ThrowsAsync<HttpRequestException>(() => service.InspectAsync(items, 80));
        var recovered = await service.InspectAsync(items, 80);

        Assert.Equal(MediaCapabilityAvailability.Available, recovered.GetAvailability(MediaFeatureFlags.Hdr));
        Assert.Equal(2, probe.CallCount);
    }

    [Fact]
    public async Task 工作区能力探测失败会稳定收口检查状态()
    {
        // 这里直接组合工作区的三个子 ViewModel，避免通过整个 Document
        // 间接触发能力探测。这样每个依赖的责任边界都是可见的：配置只保存
        // UI 状态，视频列表只发出选择变化，可控检查器只提供失败事实。
        var config = new DownloadConfigViewModel(new InMemorySettingsRepository());
        var naming = new NamingTemplateViewModel();
        var videoList = new VideoListViewModel(
            () => null!,
            eventBus: null,
            onStatusMessage: _ => { },
            new FakeFfmpegService());
        var inspector = new ThrowingCapabilityInspector();
        using var workspace = new DownloadWorkspaceViewModel(config, naming, videoList, inspector);
        videoList.SetItems([new BiliVideoItem { Aid = 1, Cid = 2, Title = "test" }]);

        videoList.VideoItems[0].IsSelected = true;

        // 生产逻辑有 250 ms 防抖，因此测试等待可观测状态，不使用猜测性的
        // 固定长延时。同时等待错误文本和 finally 收口标志，确保后台任务
        // 在用例结束前真正完成，从根本上消除覆盖率采集的调度漂移。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (config.MediaCapabilityStatusText != "高规格能力探测失败；提交时仍会重新预检。"
               || config.IsMediaCapabilityInspecting)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.Equal(1, inspector.CallCount);
        Assert.False(config.IsMediaCapabilityInspecting);
    }

    [Fact]
    public async Task 安全默认验证器只允许已知标准规格()
    {
        var verifier = new UnavailableMediaOutputVerifier();

        Assert.Equal(MediaFeatureFlags.None,
            await verifier.VerifyAsync("not-needed", MediaFeatureFlags.None));
        await Assert.ThrowsAsync<MediaValidationException>(() =>
            verifier.VerifyAsync("missing", MediaFeatureFlags.Hdr));
    }

    [Fact]
    public void 能力快照按可用会员登录不可用未知的可信顺序归并()
    {
        var feature = MediaFeatureFlags.Hdr;
        Assert.Equal(MediaCapabilityAvailability.Available,
            Capabilities((feature, MediaCapabilityAvailability.Unavailable),
                (feature, MediaCapabilityAvailability.Available)).GetAvailability(feature));
        Assert.Equal(MediaCapabilityAvailability.RequiresPremium,
            Capabilities((feature, MediaCapabilityAvailability.RequiresLogin),
                (feature, MediaCapabilityAvailability.RequiresPremium)).GetAvailability(feature));
        Assert.Equal(MediaCapabilityAvailability.RequiresLogin,
            Capabilities((feature, MediaCapabilityAvailability.RequiresLogin)).GetAvailability(feature));
        Assert.Equal(MediaCapabilityAvailability.Unavailable,
            Capabilities((feature, MediaCapabilityAvailability.Unavailable)).GetAvailability(feature));
        Assert.Equal(MediaCapabilityAvailability.Unknown,
            MediaCapabilitySnapshot.Unknown.GetAvailability(feature));
    }

    private static MediaCapabilitySnapshot Capabilities(
        params (MediaFeatureFlags Feature, MediaCapabilityAvailability Availability)[] items)
        => new(items.Select((item, index) => new MediaFeatureEvidence(
            item.Feature, item.Availability, $"test_{index}")).ToArray());

    private static DownloadTaskRecord Record(string id) => new()
    {
        TaskId = id,
        DocumentId = "doc",
        ItemTitle = id,
        OutputDirectory = Path.GetTempPath(),
        CreatedAt = DateTime.Now,
    };

    private static MediaSelectionRequest Request(OutputContainer container) => new(
        80, 30232, VideoCodecPreference.AutoCompatibility, container, OutputMediaMode.AudioVideo,
        VideoDynamicRangePreference.Auto, AudioFeaturePreference.Auto);

    private static BiliDashResult Dash(BiliDashStream[] video, BiliDashStream[] audio) => new()
    {
        VideoStreams = video.ToList(),
        AudioStreams = audio.ToList(),
    };

    private static BiliDashStream Video(int id, VideoCodec codec, MediaFeatureFlags features = MediaFeatureFlags.None)
        => new()
        {
            Id = id,
            Codecid = codec switch { VideoCodec.Avc => 7, VideoCodec.Hevc => 12, VideoCodec.Av1 => 13, _ => 0 },
            Codecs = codec switch { VideoCodec.Avc => "avc1.640028", VideoCodec.Hevc => "hev1.1.6.L120", VideoCodec.Av1 => "av01.0.08M.08", _ => "" },
            Bandwidth = 1_000,
            Features = features,
        };

    private static BiliDashStream Audio(int id, AudioCodec codec, MediaFeatureFlags features = MediaFeatureFlags.None)
        => new()
        {
            Id = id,
            Codecs = codec switch { AudioCodec.Aac => "mp4a.40.2", AudioCodec.Flac => "flac", AudioCodec.DolbyDigitalPlus => "ec-3", _ => "" },
            MimeType = codec == AudioCodec.Flac ? "audio/flac" : "audio/mp4",
            Bandwidth = codec == AudioCodec.Aac ? 192_000 : 1_000_000,
            AudioFeature = features switch
            {
                MediaFeatureFlags.HiResAudio => BiliAudioFeature.HiRes,
                MediaFeatureFlags.DolbyAtmos => BiliAudioFeature.DolbyAtmos,
                _ => BiliAudioFeature.Standard,
            },
            Features = features,
        };

    private sealed class FfprobeEnvironment : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "BiliDownloader.G8", Guid.NewGuid().ToString("N"));

        public FfprobeEnvironment()
        {
            Directory.CreateDirectory(_root);
            var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            FfmpegPath = Path.Combine(_root, "ffmpeg" + extension);
            FfprobePath = Path.Combine(_root, "ffprobe" + extension);
            MediaPath = Path.Combine(_root, "staging.mkv");
            File.WriteAllText(FfmpegPath, "test");
            File.WriteAllText(FfprobePath, "test");
            File.WriteAllText(MediaPath, "test");
            Locator = new FixedLocator(FfmpegPath);
        }

        public string FfmpegPath { get; }
        public string FfprobePath { get; }
        public string MediaPath { get; }
        public IFfmpegRuntimeLocator Locator { get; }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }
    }

    private sealed class FixedLocator(string path) : IFfmpegRuntimeLocator
    {
        public string? CustomPath { get; set; }
        public string? ResolvedPath => path;
        public bool IsReady => true;
        public string? ResolveFfmpegPath() => path;
        public Task<bool> ValidatePathAsync(string candidate, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FfmpegRuntimeStatus> DetectAsync(CancellationToken ct = default)
            => Task.FromResult(new FfmpegRuntimeStatus(true, path, "test", FfmpegRuntimeSource.Custom, "ok"));
    }

    private sealed class AnonymousCredentials : IBiliCredentialProvider
    {
        public string GetCookieHeader() => string.Empty;
        public bool IsLoggedIn => false;
    }

    private sealed class CapabilityProbe(IReadOnlyDictionary<long, MediaCapabilitySnapshot> snapshots)
        : IBiliMediaProbe
    {
        public int CallCount { get; private set; }
        public bool FailNext { get; set; }

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
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNext)
            {
                FailNext = false;
                throw new HttpRequestException("offline test");
            }
            return Task.FromResult(new BiliDashResult { Capabilities = snapshots[aid] });
        }
    }

    /// <summary>
    /// 只表达“检查失败”的最小测试依赖。工作区依赖接口而不是真实 API，
    /// 使异常分支可以离线、确定性地验证，也体现依赖倒置的设计边界。
    /// </summary>
    private sealed class ThrowingCapabilityInspector : IMediaCapabilityInspectionService
    {
        public int CallCount { get; private set; }

        public Task<BatchMediaCapabilitySnapshot> InspectAsync(
            IReadOnlyCollection<BiliVideoItem> items,
            int qualityId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            throw new HttpRequestException("offline test");
        }

        public void Clear()
        {
        }
    }
}
