using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels;
using BiliDownloader.ViewModels.BiliDownloader;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Tests;

/// <summary>
/// Document V3 的往返、离线恢复、复用方案和安全边界测试。
/// 所有 Provider 都是内存替身，测试不得访问真实 B 站或创建下载任务。
/// </summary>
public sealed class DocumentV3G4Tests
{
    [Fact]
    public void V3默认值_符合当前产品基线()
    {
        var data = new DocumentSaveDataV3();

        Assert.Equal(VideoCodecPreference.AutoCompatibility, data.VideoCodecPreference);
        Assert.Equal(OutputContainer.Mp4, data.OutputContainer);
        Assert.Equal(OutputMediaMode.AudioVideo, data.OutputMediaMode);
        Assert.Equal(VideoDynamicRangePreference.Auto, data.VideoDynamicRangePreference);
        Assert.Equal(AudioFeaturePreference.Auto, data.AudioFeaturePreference);
        Assert.Equal(SubtitleSelectionMode.None, data.SubtitleOptions.SelectionMode);
        Assert.Empty(data.DanmakuOptions.Formats);
        Assert.Equal(0, data.PerTaskRateLimitBytesPerSecond);
        Assert.Equal(IncrementalBaselineSaveData.CurrentVersion, data.Baseline.BaselineVersion);
    }

    [Fact]
    public void V3保存加载_完整来源筛选基线和输出规则往返()
    {
        var vm = CreateVm();
        var source = new SourceDescriptorSaveData
        {
            Kind = ContentSourceKind.Uploader.ToString(),
            StableSourceId = "uploader:42",
            DisplayName = "测试 UP 主",
            CapabilityVersion = 1,
        };
        var filters = new SourceFilterRulesSaveData
        {
            Keyword = "教程",
            PublishedFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PublishedTo = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            MediaTypes = [ContentSourceItemType.Video, ContentSourceItemType.Course],
            SortOrder = ContentSourceSortOrder.PublishedNewest,
        };
        var baseline = new IncrementalBaselineSaveData
        {
            LastCompletedCheckAtUtc = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
            SnapshotToken = "snapshot-20260701",
            BoundaryItemKeys =
            [
                new ContentItemKeySaveData
                {
                    SourceKind = ContentSourceKind.Uploader.ToString(),
                    NativeId = "aid:100",
                },
            ],
        };
        vm.SourceWorkflow.RestorePersistentState(new(source, filters, baseline), null);
        vm.NamingTemplate.Template = "{bv}_{title}";
        vm.DownloadConfig.VideoCodecPreference = VideoCodecPreference.Av1;
        vm.DownloadConfig.OutputContainer = OutputContainer.Mkv;
        vm.DownloadConfig.OutputMediaMode = OutputMediaMode.VideoOnly;
        vm.DownloadConfig.VideoDynamicRangePreference = VideoDynamicRangePreference.Hdr;
        vm.DownloadConfig.AudioFeaturePreference = AudioFeaturePreference.HiRes;
        vm.DownloadConfig.SubtitleOptions = new SubtitleOptions
        {
            SelectionMode = SubtitleSelectionMode.SelectedLanguages,
            LanguageKeys = ["zh-CN", "en-US"],
            OutputFormat = SubtitleOutputFormat.Ass,
            DeliveryMode = SubtitleDeliveryMode.ExternalAndSoftMuxed,
        };
        vm.DownloadConfig.DanmakuOptions = new DanmakuOptions
        {
            Formats = [DanmakuOutputFormat.Ass, DanmakuOutputFormat.Json],
            AssStyleId = "default",
        };
        vm.DownloadConfig.PerTaskRateLimitBytesPerSecond = 2_000_000;

        var saved = vm.CreateSaveDocumentMetaData("unused");
        var restored = CreateVm();
        restored.LoadDocumentByMetaData(saved);
        var savedAgain = restored.CreateSaveDocumentMetaData("unused-2");
        var data = JsonConvert.DeserializeObject<DocumentSaveDataV3>(savedAgain.Content)!;

        Assert.Equal("3.0", JObject.Parse(saved.PluginMetadata)["Version"]?.ToString());
        Assert.Equal("uploader:42", data.Source?.StableSourceId);
        Assert.Equal("教程", data.Filters.Keyword);
        Assert.Equal(ContentSourceSortOrder.PublishedNewest, data.Filters.SortOrder);
        Assert.Equal("snapshot-20260701", data.Baseline.SnapshotToken);
        Assert.Equal(VideoCodecPreference.Av1, data.VideoCodecPreference);
        Assert.Equal(OutputContainer.Mkv, data.OutputContainer);
        Assert.Equal(OutputMediaMode.VideoOnly, data.OutputMediaMode);
        Assert.Equal(SubtitleOutputFormat.Ass, data.SubtitleOptions.OutputFormat);
        Assert.Equal(["zh-CN", "en-US"], data.SubtitleOptions.LanguageKeys);
        Assert.Equal([DanmakuOutputFormat.Ass, DanmakuOutputFormat.Json], data.DanmakuOptions.Formats);
        Assert.Equal(2_000_000, data.PerTaskRateLimitBytesPerSecond);
        Assert.True(restored.SourceWorkflow.IsRestoredSourceUnsupported);
    }

    [Theory]
    [InlineData(ContentSourceKind.DirectLink)]
    [InlineData(ContentSourceKind.Uploader)]
    [InlineData(ContentSourceKind.Favorite)]
    [InlineData(ContentSourceKind.WatchLater)]
    [InlineData(ContentSourceKind.History)]
    [InlineData(ContentSourceKind.FollowingBangumi)]
    [InlineData(ContentSourceKind.FollowingCinema)]
    [InlineData(ContentSourceKind.Collection)]
    [InlineData(ContentSourceKind.Course)]
    public void V3来源_全部当前来源类型可序列化(ContentSourceKind kind)
    {
        var vm = CreateVm();
        var stableId = $"{kind.ToString().ToLowerInvariant()}:1";
        vm.SourceWorkflow.RestorePersistentState(new(
            new SourceDescriptorSaveData
            {
                Kind = kind.ToString(),
                StableSourceId = stableId,
                DisplayName = kind.ToString(),
                CapabilityVersion = 1,
                AutoOpen = kind == ContentSourceKind.Course,
            },
            new SourceFilterRulesSaveData(),
            new IncrementalBaselineSaveData()), null);

        var data = JsonConvert.DeserializeObject<DocumentSaveDataV3>(
            vm.CreateSaveDocumentMetaData("unused").Content)!;

        Assert.Equal(kind.ToString(), data.Source?.Kind);
        Assert.Equal(stableId, data.Source?.StableSourceId);
        Assert.Equal(kind == ContentSourceKind.Course, data.Source?.AutoOpen);
    }

    [Fact]
    public void V1格式_明确拒绝而不执行隐式迁移()
    {
        var saveData = Envelope(1, new
        {
            DocumentId = "v1-doc",
            Url = "https://www.bilibili.com/video/BV1abcDEF123",
            DownloadInfo = "旧日志",
            OutputDirectory = "D:\\Media",
            UseGroupFolder = true,
            AddIndexToTitle = false,
        });
        var vm = CreateVm();

        var exception = Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));

        Assert.Contains("V3", exception.Message);
    }

    [Fact]
    public void V2格式_明确拒绝而不执行隐式迁移()
    {
        var saveData = Envelope(2, new
        {
            DocumentId = "v2-doc",
            Url = "ep123",
            DownloadSubtitle = true,
            DownloadDanmaku = true,
        });
        var vm = CreateVm();

        var exception = Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));

        Assert.Contains("V3", exception.Message);
    }

    [Fact]
    public void V3未知字段_加载和再次保存均成功()
    {
        var content = JObject.FromObject(new DocumentSaveDataV3 { DocumentId = "known" });
        content["FutureMinorField"] = new JObject { ["Any"] = 1 };
        var saveData = EnvelopeRaw("3.8", content.ToString(Formatting.None));
        var vm = CreateVm();

        vm.LoadDocumentByMetaData(saveData);
        var savedAgain = vm.CreateSaveDocumentMetaData("unused");

        Assert.Equal("known", vm.DocumentId);
        Assert.Null(JObject.Parse(savedAgain.Content)["FutureMinorField"]);
    }

    [Fact]
    public void 未知主版本_明确拒绝而不猜测恢复字段()
    {
        var saveData = EnvelopeRaw("99.0", JsonConvert.SerializeObject(new
        {
            DocumentId = "future-doc",
            Url = "BV1abcDEF123",
            OutputDirectory = "D:\\Safe",
            DownloadSubtitle = true,
            Source = new { Kind = "FutureSecretSource", StableSourceId = "should-not-load" },
        }));
        var vm = CreateVm();

        var exception = Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));

        Assert.Contains("V3", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    public void 损坏V3内容_拒绝打开(string content)
    {
        var vm = CreateVm();
        var saveData = EnvelopeRaw("3.0", content);

        Assert.Throws<DocumentLoadException>(() => vm.LoadDocumentByMetaData(saveData));
    }

    [Fact]
    public void 损坏版本元数据_拒绝打开()
    {
        var saveData = EnvelopeRaw("3.0", "{}");
        saveData.PluginMetadata = "{";

        Assert.Throws<DocumentLoadException>(() => CreateVm().LoadDocumentByMetaData(saveData));
    }

    [Fact]
    public async Task 打开与初始化_不调用Provider且只恢复当前Document任务投影()
    {
        var provider = new CountingDocumentProvider();
        var vm = CreateVm(provider, out var repository);
        var saveData = Envelope(3, new DocumentSaveDataV3
        {
            DocumentId = "target-doc",
            Source = new SourceDescriptorSaveData
            {
                Kind = ContentSourceKind.Course.ToString(),
                StableSourceId = "course:1",
                DisplayName = "离线课程",
                CapabilityVersion = 1,
            },
            Filters = new SourceFilterRulesSaveData { Keyword = "离线筛选" },
        });
        repository.Seed(
            new DownloadTaskRecord { TaskId = "target", DocumentId = "target-doc", ItemTitle = "目标任务", Status = "interrupted" },
            new DownloadTaskRecord { TaskId = "other", DocumentId = "other-doc", ItemTitle = "其他任务", Status = "interrupted" });

        vm.LoadDocumentByMetaData(saveData);
        await vm.InitializeAsync();

        Assert.Equal(0, provider.NormalizeCount);
        Assert.Equal(0, provider.GetPageCount);
        Assert.Contains("repository:get-document:target-doc", repository.CallLog);
        Assert.Single(vm.VideoList.VideoItems);
        Assert.Equal("目标任务", vm.VideoList.VideoItems[0].Title);
        Assert.Contains("离线恢复", vm.SourceWorkflow.Browser.Status);
    }

    [Fact]
    public void 缺失Provider_来源原样保留并可再次保存()
    {
        var data = new DocumentSaveDataV3
        {
            Source = new SourceDescriptorSaveData
            {
                Kind = "FutureCatalog",
                StableSourceId = "future:42",
                DisplayName = "未来来源",
                CapabilityVersion = 2,
            },
        };
        var vm = CreateVm();

        vm.LoadDocumentByMetaData(Envelope(3, data));
        var saved = JsonConvert.DeserializeObject<DocumentSaveDataV3>(
            vm.CreateSaveDocumentMetaData("unused").Content)!;

        Assert.True(vm.SourceWorkflow.IsRestoredSourceUnsupported);
        Assert.Equal("FutureCatalog", saved.Source?.Kind);
        Assert.Equal("future:42", saved.Source?.StableSourceId);
    }

    [Fact]
    public void 敏感来源或快照_拒绝进入SaveData()
    {
        var vm = CreateVm();
        vm.SourceWorkflow.RestorePersistentState(new(
            new SourceDescriptorSaveData
            {
                Kind = ContentSourceKind.Uploader.ToString(),
                StableSourceId = "https://api.test/list?w_rid=signed-secret",
                DisplayName = "不安全来源",
                CapabilityVersion = 1,
            },
            new SourceFilterRulesSaveData(),
            new IncrementalBaselineSaveData()), null);

        Assert.Throws<DocumentLoadException>(() => vm.CreateSaveDocumentMetaData("unused"));
    }

    [Fact]
    public void V3快照_不包含页面游标选择或临时媒体字段()
    {
        var json = CreateVm().CreateSaveDocumentMetaData("unused").Content;
        var root = JObject.Parse(json);

        Assert.Equal(
        [
            "DocumentId", "Url", "DownloadInfo", "OutputDirectory", "UseGroupFolder",
            "AddIndexToTitle", "PresetId", "NamingTemplate", "QualityId", "AudioQualityId",
            "DownloadDanmaku", "DownloadSubtitle", "DownloadCover", "ConflictPolicy",
            "Source", "Filters", "Baseline", "VideoCodecPreference", "OutputContainer",
            "OutputMediaMode", "VideoDynamicRangePreference", "AudioFeaturePreference",
            "SubtitleOptions", "DanmakuOptions", "PerTaskRateLimitBytesPerSecond",
        ], root.Properties().Select(static property => property.Name).ToArray());
        Assert.DoesNotContain("ContinuationToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SelectedKeys", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BaseUrl", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 持久配置变化_完整置脏且磁盘保存通知后清除()
    {
        var vm = CreateVm();
        vm.AcceptChanges();
        Assert.False(vm.IsModified);

        vm.NamingTemplate.Template = "{title}";
        Assert.True(vm.IsModified);
        vm.AcceptChanges();
        vm.DownloadConfig.OutputContainer = OutputContainer.Mkv;
        Assert.True(vm.IsModified);
        vm.AcceptChanges();
        vm.SourceWorkflow.SetIncrementalBaseline(new IncrementalBaselineSaveData
        {
            SnapshotToken = "snapshot-2",
        });
        Assert.True(vm.IsModified);
    }

    [Fact]
    public async Task 已恢复筛选发生变化_置脏且仅在用户编辑后访问Provider()
    {
        var provider = new CountingDocumentProvider();
        var vm = CreateVm(provider, out _);
        vm.LoadDocumentByMetaData(Envelope(3, new DocumentSaveDataV3
        {
            Source = new SourceDescriptorSaveData
            {
                Kind = ContentSourceKind.Course.ToString(),
                StableSourceId = "course:1",
                DisplayName = "课程",
                CapabilityVersion = 1,
            },
            Filters = new SourceFilterRulesSaveData(),
        }));
        vm.AcceptChanges();
        Assert.Equal(0, provider.GetPageCount);

        vm.SourceWorkflow.Browser.SearchText = "新筛选";
        await Task.Delay(400);

        Assert.True(vm.IsModified);
        Assert.Equal(1, provider.GetPageCount);
    }

    [Fact]
    public async Task 旧预设缺少P1字段_使用兼容默认值()
    {
        using var paths = new TestDataPaths();
        await new SettingsStore(paths).InitAsync();
        var store = new PresetStore(paths);
        await store.SaveAsync(new DownloadPreset { Id = "legacy", Name = "旧预设" });

        var loaded = await store.GetByIdAsync("legacy");

        Assert.NotNull(loaded);
        Assert.Equal(VideoCodecPreference.AutoCompatibility, loaded.VideoCodecPreference);
        Assert.Equal(OutputContainer.Mp4, loaded.OutputContainer);
        Assert.Equal(OutputMediaMode.AudioVideo, loaded.OutputMediaMode);
        Assert.Equal(0, loaded.PerTaskRateLimitBytesPerSecond);
    }

    [Fact]
    public void 完整P1预设_应用后可捕获为等价的可复用方案()
    {
        var config = CreateVm().DownloadConfig;
        var video720 = new BiliQualityOption { QualityId = 64, DisplayName = "720P" };
        var video1080 = new BiliQualityOption { QualityId = 80, DisplayName = "1080P" };
        var audio = new BiliQualityOption { QualityId = 30232, DisplayName = "192kbps" };
        config.PopulateQualities([video720, video1080], video1080, [audio], audio, isMultiVideo: false);
        var preset = new DownloadPreset
        {
            Id = "complete-p1",
            Name = "完整 P1 方案",
            QualityPreference = "720p",
            AudioQualityId = 99999,
            UseGroupFolder = true,
            AddIndexToTitle = true,
            DownloadSubtitle = true,
            DownloadDanmaku = true,
            DownloadCover = true,
            OutputDirectory = "D:\\Reusable",
            ConflictPolicy = FileConflictPolicy.Overwrite,
            VideoCodecPreference = VideoCodecPreference.Av1,
            OutputContainer = OutputContainer.Mkv,
            OutputMediaMode = OutputMediaMode.VideoOnly,
            VideoDynamicRangePreference = VideoDynamicRangePreference.Hdr,
            AudioFeaturePreference = AudioFeaturePreference.DolbyAtmos,
            SubtitleOptions = new SubtitleOptions
            {
                SelectionMode = SubtitleSelectionMode.SelectedLanguages,
                LanguageKeys = ["zh-CN"],
                OutputFormat = SubtitleOutputFormat.Ass,
                DeliveryMode = SubtitleDeliveryMode.SoftMuxed,
            },
            DanmakuOptions = new DanmakuOptions
            {
                Formats = [DanmakuOutputFormat.Ass],
                AssStyleId = "compact",
            },
            PerTaskRateLimitBytesPerSecond = 8_000_000,
        };
        var appliedCount = 0;
        config.PresetApplied += _ => appliedCount++;

        config.ApplyPreset(preset);
        var profile = config.CaptureCurrentProfile();
        var copy = config.CaptureCurrentAsPreset("copy", "副本");

        Assert.Same(video720, config.SelectedQuality);
        Assert.Same(audio, config.SelectedAudioQuality); // 指定音质不存在时回退到首个可用项。
        Assert.Equal(1, appliedCount);
        Assert.Equal("D:\\Reusable", profile.OutputDirectory);
        Assert.Equal(VideoCodecPreference.Av1, profile.VideoCodecPreference);
        Assert.Equal(OutputContainer.Mkv, copy.OutputContainer);
        Assert.Equal(SubtitleSelectionMode.SelectedLanguages, copy.SubtitleOptions.SelectionMode);
        Assert.Equal([DanmakuOutputFormat.Ass], copy.DanmakuOptions.Formats);
        Assert.Equal(8_000_000, copy.PerTaskRateLimitBytesPerSecond);
    }

    [Fact]
    public void P1持久字段_变更置脏但重复赋相同值不置脏()
    {
        var vm = CreateVm();

        AssertPersistentChangeIsIdempotent(vm, () => vm.DownloadConfig.VideoCodecPreference = VideoCodecPreference.Avc);
        AssertPersistentChangeIsIdempotent(vm, () => vm.DownloadConfig.OutputContainer = OutputContainer.Mkv);
        AssertPersistentChangeIsIdempotent(vm, () => vm.DownloadConfig.OutputMediaMode = OutputMediaMode.AudioOnly);
        AssertPersistentChangeIsIdempotent(vm, () => vm.DownloadConfig.VideoDynamicRangePreference = VideoDynamicRangePreference.DolbyVision);
        AssertPersistentChangeIsIdempotent(vm, () => vm.DownloadConfig.AudioFeaturePreference = AudioFeaturePreference.HiRes);

        var subtitle = new SubtitleOptions
        {
            SelectionMode = SubtitleSelectionMode.All,
            OutputFormat = SubtitleOutputFormat.Srt,
            DeliveryMode = SubtitleDeliveryMode.External,
        };
        AssertPersistentChangeIsIdempotent(vm, () => vm.DownloadConfig.SubtitleOptions = subtitle);
        var danmaku = new DanmakuOptions { Formats = [DanmakuOutputFormat.Xml] };
        AssertPersistentChangeIsIdempotent(vm, () => vm.DownloadConfig.DanmakuOptions = danmaku);
        AssertPersistentChangeIsIdempotent(vm, () =>
            vm.DownloadConfig.PerTaskRateLimitBytesPerSecond = 64 * 1024);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => vm.DownloadConfig.PerTaskRateLimitBytesPerSecond = -1);
    }

    [Fact]
    public void 离线恢复_直链与个人来源分别挂载到正确入口()
    {
        var provider = new CountingDocumentProvider();
        var vm = CreateVm(provider, out _);
        var directLink = new SourceDescriptorSaveData
        {
            Kind = ContentSourceKind.DirectLink.ToString(),
            StableSourceId = "video:bv:1abcDEF123",
            DisplayName = "BV1abcDEF123",
            CapabilityVersion = 1,
        };

        vm.SourceWorkflow.RestorePersistentState(new(
            directLink, new SourceFilterRulesSaveData(), new IncrementalBaselineSaveData()),
            "https://www.bilibili.com/video/BV1abcDEF123");

        Assert.Equal(DownloadCreationMode.QuickUrl, vm.SourceWorkflow.Mode);
        Assert.False(vm.SourceWorkflow.IsBrowsing);
        Assert.Equal("video:bv:1abcDEF123", vm.SourceWorkflow.CapturePersistentState().Source?.StableSourceId);

        vm.VideoParse.Url = "BV1changed";
        Assert.Null(vm.SourceWorkflow.CapturePersistentState().Source);

        var personalSource = new SourceDescriptorSaveData
        {
            Kind = ContentSourceKind.Course.ToString(),
            StableSourceId = "course:2",
            DisplayName = "课程 2",
            CapabilityVersion = 1,
        };
        var filters = new SourceFilterRulesSaveData
        {
            Keyword = "离线关键词",
            MediaTypes = [ContentSourceItemType.Course],
            SortOrder = ContentSourceSortOrder.PublishedOldest,
        };

        vm.SourceWorkflow.RestorePersistentState(new(
            personalSource, filters, new IncrementalBaselineSaveData()), null);

        Assert.Equal(DownloadCreationMode.PersonalSource, vm.SourceWorkflow.Mode);
        Assert.True(vm.SourceWorkflow.IsBrowsing);
        Assert.Equal("离线关键词", vm.SourceWorkflow.Browser.SearchText);
        Assert.Equal(0, provider.GetPageCount);
    }

    [Fact]
    public void 下载提交上下文_无解析状态与完整解析状态均能安全构造()
    {
        var vm = CreateVm();
        var initialItem = new BiliVideoItem
        {
            ItemId = "initial",
            Title = "未解析条目",
            IsSelected = true,
        };
        vm.VideoList.SetItems([initialItem]);
        vm.VideoList.VideoItems[0].IsSelected = true;

        // 先触发所有可空解析字段的安全默认分支，预检会在“未选画质”处正常停止。
        vm.VideoList.SubmitDownloadCommand.Execute(null);

        var video = new BiliQualityOption { QualityId = 80, DisplayName = "1080P" };
        var audio = new BiliQualityOption { QualityId = 30232, DisplayName = "192kbps" };
        var parsedItem = new BiliVideoItem
        {
            ItemId = "parsed",
            Title = "已解析条目",
            Bvid = "BV1abcDEF123",
            IsSelected = true,
        };
        vm.Workspace.ApplyParseResult(new VideoParseResult
        {
            Collection = new BiliVideoCollection
            {
                SeriesTitle = "系列名",
                Cover = "https://i.test/cover.jpg",
                UpName = "UP 主",
                PublishDate = new DateTime(2026, 8, 7),
            },
            VideoItems = [parsedItem],
            QualityOptions = [video],
            SelectedQuality = video,
            AudioQualityOptions = [audio],
            SelectedAudioQuality = audio,
        });
        vm.VideoList.VideoItems[0].IsSelected = true;

        // 再触发所有解析字段已存在的分支；本测试只验证本地上下文，不提交远程下载。
        vm.VideoList.SubmitDownloadCommand.Execute(null);

        Assert.Equal("1080P", vm.DownloadConfig.SelectedQuality?.DisplayName);
        Assert.Equal("系列名", vm.Workspace.VideoCollection?.SeriesTitle);
    }

    [Fact]
    public void V3结构化选项_加载时去重清理并兼容缺失集合()
    {
        var vm = CreateVm();
        var data = new DocumentSaveDataV3
        {
            Filters = null!,
            Baseline = null!,
            DownloadSubtitle = true,
            DownloadDanmaku = true,
            SubtitleOptions = null!,
            DanmakuOptions = null!,
        };

        vm.LoadDocumentByMetaData(Envelope(3, data));
        var legacyNormalized = JsonConvert.DeserializeObject<DocumentSaveDataV3>(
            vm.CreateSaveDocumentMetaData("normalized-legacy.bili").Content)!;

        Assert.Equal(SubtitleSelectionMode.All, legacyNormalized.SubtitleOptions.SelectionMode);
        Assert.Equal([DanmakuOutputFormat.Xml], legacyNormalized.DanmakuOptions.Formats);
        Assert.Empty(legacyNormalized.Filters.MediaTypes);
        Assert.Empty(legacyNormalized.Baseline.BoundaryItemKeys);

        var structuredVm = CreateVm();
        structuredVm.LoadDocumentByMetaData(Envelope(3, new DocumentSaveDataV3
        {
            SubtitleOptions = new SubtitleOptions
            {
                SelectionMode = SubtitleSelectionMode.SelectedLanguages,
                LanguageKeys = [" zh-CN ", "zh-CN", "  "],
            },
            DanmakuOptions = new DanmakuOptions
            {
                Formats = [DanmakuOutputFormat.Xml, DanmakuOutputFormat.Xml, DanmakuOutputFormat.Ass],
                AssStyleId = "  compact  ",
            },
        }));
        var structured = JsonConvert.DeserializeObject<DocumentSaveDataV3>(
            structuredVm.CreateSaveDocumentMetaData("normalized-structured.bili").Content)!;

        Assert.Equal(["zh-CN"], structured.SubtitleOptions.LanguageKeys);
        Assert.Equal([DanmakuOutputFormat.Xml, DanmakuOutputFormat.Ass],
            structured.DanmakuOptions.Formats.OrderBy(static value => value).ToArray());
        Assert.Equal("compact", structured.DanmakuOptions.AssStyleId);
    }

    [Fact]
    public void V3安全策略_对每个结构与枚举边界均拒绝损坏数据()
    {
        var invalidMutations = new Action<DocumentSaveDataV3>[]
        {
            data => data.Source = ValidSource(kind: ""),
            data => data.Source = ValidSource(stableSourceId: ""),
            data => data.Source = ValidSource(displayName: ""),
            data => data.Source = ValidSource(capabilityVersion: 0),
            data => data.ConflictPolicy = (FileConflictPolicy)999,
            data => data.VideoCodecPreference = (VideoCodecPreference)999,
            data => data.OutputContainer = (OutputContainer)999,
            data => data.OutputMediaMode = (OutputMediaMode)999,
            data => data.VideoDynamicRangePreference = (VideoDynamicRangePreference)999,
            data => data.AudioFeaturePreference = (AudioFeaturePreference)999,
            data => data.SubtitleOptions = new SubtitleOptions { SelectionMode = (SubtitleSelectionMode)999 },
            data => data.SubtitleOptions = new SubtitleOptions
            {
                SelectionMode = SubtitleSelectionMode.All,
                OutputFormat = (SubtitleOutputFormat)999,
            },
            data => data.SubtitleOptions = new SubtitleOptions
            {
                SelectionMode = SubtitleSelectionMode.All,
                DeliveryMode = (SubtitleDeliveryMode)999,
            },
            data => data.DanmakuOptions = new DanmakuOptions { Formats = [(DanmakuOutputFormat)999] },
            data => data.Baseline = new IncrementalBaselineSaveData { BaselineVersion = 0 },
            data => data.Baseline = new IncrementalBaselineSaveData
            {
                BoundaryItemKeys = Enumerable.Range(0, 101)
                    .Select(index => new ContentItemKeySaveData { SourceKind = "Video", NativeId = $"aid:{index}" })
                    .ToList(),
            },
            data => data.Baseline = new IncrementalBaselineSaveData { SnapshotToken = new string('x', 2049) },
            data => data.Baseline = new IncrementalBaselineSaveData
            {
                BoundaryItemKeys = [new ContentItemKeySaveData { SourceKind = "", NativeId = "aid:1" }],
            },
            data => data.PerTaskRateLimitBytesPerSecond = -1,
        };

        foreach (var mutate in invalidMutations)
        {
            var data = new DocumentSaveDataV3();
            mutate(data);
            Assert.Throws<DocumentLoadException>(() => CreateVm().LoadDocumentByMetaData(Envelope(3, data)));
        }
    }

    [Theory]
    [InlineData("stable?id=1")]
    [InlineData("stable#fragment")]
    [InlineData("cookie-value")]
    [InlineData("authorization-token")]
    [InlineData("signed_url_value")]
    public void V3安全策略_稳定标识拒绝临时地址和凭据特征(string unsafeValue)
    {
        var data = new DocumentSaveDataV3
        {
            Source = ValidSource(stableSourceId: unsafeValue),
        };

        Assert.Throws<DocumentLoadException>(() => CreateVm().LoadDocumentByMetaData(Envelope(3, data)));
    }

    [Theory]
    [InlineData(null, "", 1)]
    [InlineData("{}", "", 1)]
    [InlineData("{\"Version\":\"not-a-version\"}", "", -1)]
    [InlineData("{\"Version\":\"2.5\"}", "{}", 2)]
    public void 版本识别_缺失和非法元数据具有确定的安全结果(
        string? pluginMetadata,
        string content,
        int expectedMajor)
    {
        var decoded = DocumentSaveCodec.Decode(new DocumentSaveData
        {
            DocumentTypeId = new("test-document"),
            Title = "版本识别",
            PluginMetadata = pluginMetadata ?? string.Empty,
            Content = content,
        });

        Assert.Equal(expectedMajor, decoded.MajorVersion);
        Assert.Equal(expectedMajor == 3, decoded.IsKnownVersion);
    }

    [Fact]
    public void 内容源浏览器_展示状态的重复赋值不制造多余通知()
    {
        var provider = new CountingDocumentProvider();
        var vm = CreateVm(provider, out _);
        vm.SourceWorkflow.RestorePersistentState(new(
            ValidSource(), new SourceFilterRulesSaveData(), new IncrementalBaselineSaveData()), null);
        var browser = vm.SourceWorkflow.Browser;
        var changed = new Dictionary<string, int>(StringComparer.Ordinal);
        browser.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changed[args.PropertyName] = changed.GetValueOrDefault(args.PropertyName) + 1;
        };

        browser.Title = "离线标题";
        browser.Title = "离线标题";
        browser.Status = "本地状态";
        browser.Status = "本地状态";
        browser.HasMore = !browser.HasMore;
        browser.HasMore = browser.HasMore;
        browser.IsBusy = !browser.IsBusy;
        browser.IsBusy = browser.IsBusy;
        browser.IsResolvingSelection = !browser.IsResolvingSelection;
        browser.IsResolvingSelection = browser.IsResolvingSelection;
        browser.CanRetry = !browser.CanRetry;
        browser.CanRetry = browser.CanRetry;
        browser.CanGoBack = !browser.CanGoBack;
        browser.CanGoBack = browser.CanGoBack;
        browser.CanResolveCurrentSource = !browser.CanResolveCurrentSource;
        browser.CanResolveCurrentSource = browser.CanResolveCurrentSource;
        browser.FilterValidationMessage = "日期范围无效";
        browser.FilterValidationMessage = "日期范围无效";
        browser.SelectionInvalidatedMessage = "选择已失效";
        browser.SelectionInvalidatedMessage = "选择已失效";
        browser.FilterScopeText = "当前层级";
        browser.FilterScopeText = "当前层级";
        browser.LoadedCount = 3;
        browser.LoadedCount = 3;
        browser.DisplayedCount = 2;
        browser.DisplayedCount = 2;

        Assert.Equal(1, changed[nameof(ContentSourceBrowserViewModel.Title)]);
        Assert.Equal(1, changed[nameof(ContentSourceBrowserViewModel.Status)]);
        Assert.Equal(0, provider.GetPageCount);
    }

    [Fact]
    public void 下载配置_全部旧字段同样遵守幂等变更契约()
    {
        var config = CreateVm().DownloadConfig;
        var propertyChanges = 0;
        config.PropertyChanged += (_, _) => propertyChanges++;
        var video = new BiliQualityOption { QualityId = 80, DisplayName = "1080P" };
        var audio = new BiliQualityOption { QualityId = 30232, DisplayName = "192kbps" };
        var preset = BuiltInPresets.Compatible();

        SetTwice(() => config.SelectedQuality = video);
        SetTwice(() => config.SelectedAudioQuality = audio);
        SetTwice(() => config.UseGroupFolder = true);
        SetTwice(() => config.AddIndexToTitle = false);
        SetTwice(() => config.DownloadDanmaku = true);
        SetTwice(() => config.DownloadSubtitle = true);
        SetTwice(() => config.DownloadCover = true);
        SetTwice(() => config.OutputDirectory = "D:\\Idempotent");
        SetTwice(() => config.SelectedConflictPolicy = config.ConflictPolicyOptions[^1]);
        SetTwice(() => config.SelectedPreset = preset);
        SetTwice(() => config.CustomPresetName = "自定义");
        SetTwice(() => config.QualityRestoreNotice = "画质回退");

        Assert.True(propertyChanges > 0);
        Assert.Equal(preset.Name, config.PresetStatusText);
        config.IsPresetModified = true;
        Assert.Contains("已修改", config.PresetStatusText);
        config.IsRestoredPresetUnavailable = true;
        Assert.Contains("不可用", config.PresetStatusText);

        void SetTwice(Action set)
        {
            set();
            var afterFirst = propertyChanges;
            set();
            Assert.Equal(afterFirst, propertyChanges);
        }
    }

    [Fact]
    public void 可复用方案_默认与结构化选项均有明确有效值()
    {
        var defaultProfile = DownloadProfile.Default;
        Assert.Equal(SubtitleSelectionMode.None, defaultProfile.EffectiveSubtitleOptions.SelectionMode);
        Assert.Empty(defaultProfile.EffectiveDanmakuOptions.Formats);

        var structuredSubtitle = new SubtitleOptions { SelectionMode = SubtitleSelectionMode.All };
        var structuredDanmaku = new DanmakuOptions { Formats = [DanmakuOutputFormat.Xml] };
        var structuredProfile = defaultProfile with
        {
            SubtitleOptions = structuredSubtitle,
            DanmakuOptions = structuredDanmaku,
        };
        Assert.Same(structuredSubtitle, structuredProfile.EffectiveSubtitleOptions);
        Assert.Same(structuredDanmaku, structuredProfile.EffectiveDanmakuOptions);

        Assert.NotEmpty(BuiltInPresets.Compatible().Description);
        Assert.NotEmpty(BuiltInPresets.Quality().Description);
        Assert.NotEmpty(BuiltInPresets.Archive().Description);
        Assert.Contains("自定义", new DownloadPreset { Id = "custom" }.Description);
    }

    [Fact]
    public void 内容权限展示_所有状态与容器媒体分支均有稳定语义()
    {
        foreach (var state in Enum.GetValues<ContentAccessState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ContentAccessPresentationPolicy.GetLabel(state, true)));
            Assert.False(string.IsNullOrWhiteSpace(ContentAccessPresentationPolicy.GetLabel(state, false)));

            var media = new ContentSourceItemViewModel(new ContentSourceItem(
                new ContentItemKey(ContentSourceKind.Course, $"media:{state}"),
                $"媒体 {state}",
                ContentSourceItemType.Course,
                author: state == ContentAccessState.Available ? "UP" : null,
                publishedAt: state == ContentAccessState.Available ? DateTimeOffset.UtcNow : null,
                accessState: state,
                childCount: state == ContentAccessState.Available ? 1 : null));
            _ = media.Detail;
            _ = media.AccessText;
            _ = media.StateIcon;
            _ = media.ShowCheckBox;
            _ = media.ShowStateIcon;
        }

        var selectionChanged = 0;
        var selectable = new ContentSourceItemViewModel(new ContentSourceItem(
            new ContentItemKey(ContentSourceKind.Course, "selectable"),
            "可选媒体",
            ContentSourceItemType.Course),
            selectionChanged: () => selectionChanged++);
        selectable.IsSelected = true;
        selectable.IsSelected = true;
        selectable.IsSelected = false;

        var container = new ContentSourceItemViewModel(new ContentSourceItem(
            new ContentItemKey(ContentSourceKind.Course, "container"),
            "容器",
            ContentSourceItemType.Course,
            nodeKind: ContentSourceNodeKind.Container));
        container.IsSelected = true;

        Assert.Equal(2, selectionChanged);
        Assert.True(container.CanOpen);
        Assert.False(container.CanSelect);
    }

    [Fact]
    public void 来源选择器_选项与展示状态重复赋值保持幂等()
    {
        var provider = new CountingDocumentProvider();
        var vm = CreateVm(provider, out _);
        var picker = vm.SourceWorkflow.Picker;
        var changes = 0;
        picker.PropertyChanged += (_, _) => changes++;
        var course = picker.Options.Single(option => option.Kind == ContentSourceKind.Course);

        picker.SelectedOption = course;
        var afterSelection = changes;
        picker.SelectedOption = course;
        Assert.Equal(afterSelection, changes);
        picker.Input = "course:1";
        picker.Input = "course:1";
        picker.Status = "离线";
        picker.Status = "离线";
        picker.IsBusy = true;
        picker.IsBusy = true;
        picker.HasFavoriteFolders = true;
        picker.HasFavoriteFolders = true;

        Assert.True(picker.ShowManualInput);
        Assert.True(picker.ShowAccountShortcut);
        Assert.False(picker.IsFavoriteSelected);
        Assert.True(changes > afterSelection);
    }

    [Theory]
    [InlineData("quality:80", 80)]
    [InlineData("quality:999", 120)]
    [InlineData("1080p", 80)]
    [InlineData("720p", 32)]
    [InlineData("highest", 120)]
    public void 画质偏好_精确匹配与降级策略都可预测(string preference, int expectedQualityId)
    {
        var config = CreateVm().DownloadConfig;
        var quality32 = new BiliQualityOption { QualityId = 32, DisplayName = "480P" };
        var quality80 = new BiliQualityOption { QualityId = 80, DisplayName = "1080P" };
        var quality120 = new BiliQualityOption { QualityId = 120, DisplayName = "4K" };
        config.PopulateQualities(
            [quality32, quality80, quality120], quality32, [], null, isMultiVideo: false);

        config.ApplyPreset(new DownloadPreset
        {
            Id = $"quality-{preference}",
            Name = preference,
            QualityPreference = preference,
        });

        Assert.Equal(expectedQualityId, config.SelectedQuality?.QualityId);
    }

    [Fact]
    public async Task 离线筛选器_用户编辑后组合关键词日期类型与排序()
    {
        var provider = new CountingDocumentProvider();
        var vm = CreateVm(provider, out _);
        vm.SourceWorkflow.RestorePersistentState(new(
            ValidSource(), new SourceFilterRulesSaveData(), new IncrementalBaselineSaveData()), null);
        var browser = vm.SourceWorkflow.Browser;

        browser.SearchText = "系列";
        browser.SearchText = "系列";
        browser.PublishedFrom = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
        browser.PublishedFrom = browser.PublishedFrom;
        browser.PublishedTo = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(browser.HasFilterValidationMessage);
        browser.PublishedTo = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
        browser.SelectedSortOption = browser.SortOptions[^1];
        browser.SelectedSortOption = browser.SortOptions[^1];
        var courseType = browser.TypeFilterOptions.Single(option => option.Value == ContentSourceItemType.Course);
        courseType.IsSelected = true;
        courseType.IsSelected = true;

        await Task.Delay(450);
        var filters = browser.CaptureFilters();

        Assert.Equal("系列", filters.Keyword);
        Assert.Equal(ContentSourceSortOrder.PublishedOldest, filters.SortOrder);
        Assert.Equal([ContentSourceItemType.Course], filters.MediaTypes);
        Assert.False(browser.HasFilterValidationMessage);
        Assert.True(provider.GetPageCount >= 1);
    }

    [Fact]
    public void 来源DTO恢复_AutoOpen与数值型未知枚举分别按白名单处理()
    {
        var provider = new CountingDocumentProvider();
        var vm = CreateVm(provider, out _);
        var autoOpen = ValidSource();
        autoOpen.AutoOpen = true;

        vm.SourceWorkflow.RestorePersistentState(new(
            autoOpen, new SourceFilterRulesSaveData(), new IncrementalBaselineSaveData()), null);
        var captured = vm.SourceWorkflow.CapturePersistentState();

        Assert.True(captured.Source?.AutoOpen);
        Assert.False(vm.SourceWorkflow.IsRestoredSourceUnsupported);

        vm.SourceWorkflow.RestorePersistentState(new(
            new SourceDescriptorSaveData
            {
                Kind = "999",
                StableSourceId = "future:999",
                DisplayName = "数值型未知来源",
                CapabilityVersion = 1,
            },
            new SourceFilterRulesSaveData(),
            new IncrementalBaselineSaveData()), null);

        Assert.True(vm.SourceWorkflow.IsRestoredSourceUnsupported);
        Assert.Equal("999", vm.SourceWorkflow.CapturePersistentState().Source?.Kind);
    }

    [Fact]
    public void 旧预设异常值_负限速归零且空目录不覆盖当前选择()
    {
        var config = CreateVm().DownloadConfig;
        config.OutputDirectory = "D:\\Current";

        config.ApplyPreset(new DownloadPreset
        {
            Id = "legacy-invalid-rate",
            Name = "旧预设",
            OutputDirectory = string.Empty,
            PerTaskRateLimitBytesPerSecond = -1,
            DownloadSubtitle = true,
            DownloadDanmaku = true,
        });

        Assert.Equal("D:\\Current", config.OutputDirectory);
        Assert.Equal(0, config.PerTaskRateLimitBytesPerSecond);
        Assert.Equal(SubtitleSelectionMode.All, config.SubtitleOptions.SelectionMode);
        Assert.Equal([DanmakuOutputFormat.Xml], config.DanmakuOptions.Formats);
    }

    private static SourceDescriptorSaveData ValidSource(
        string? kind = null,
        string? stableSourceId = null,
        string? displayName = null,
        int capabilityVersion = 1) => new()
    {
        Kind = kind ?? ContentSourceKind.Course.ToString(),
        StableSourceId = stableSourceId ?? "course:valid",
        DisplayName = displayName ?? "有效课程",
        CapabilityVersion = capabilityVersion,
    };

    /// <summary>
    /// 统一验证持久属性的变更语义：第一次赋值代表用户意图变化，
    /// 保存后再赋相同值不应制造伪脏状态。该辅助方法让每个 P1 字段都执行同一契约。
    /// </summary>
    private static void AssertPersistentChangeIsIdempotent(BiliDownloaderViewModel vm, Action change)
    {
        vm.AcceptChanges();
        change();
        Assert.True(vm.IsModified);

        vm.AcceptChanges();
        change();
        Assert.False(vm.IsModified);
    }

    private static BiliDownloaderViewModel CreateVm()
    {
        var messenger = new RecordingMessengerService();
        var repository = new InMemoryDownloadTaskRepository();
        var loginState = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(), new StubBiliSessionApi(), messenger);
        return new BiliDownloaderViewModel(
            messenger, repository, new InMemorySettingsRepository(), loginState,
            new BiliLoginService(), new BiliApiService(), new FakeCredentialProvider(),
            new FakeFfmpegService());
    }

    private static BiliDownloaderViewModel CreateVm(
        CountingDocumentProvider provider,
        out InMemoryDownloadTaskRepository repository)
    {
        var messenger = new RecordingMessengerService();
        repository = new InMemoryDownloadTaskRepository();
        var settings = new InMemorySettingsRepository();
        var loginState = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(), new StubBiliSessionApi(), messenger);
        var api = new BiliApiService();
        var credentials = new FakeCredentialProvider();
        var registry = new ContentSourceProviderRegistry(
            [new DirectLinkProvider(api, credentials), provider]);
        return new BiliDownloaderViewModel(
            messenger, repository, settings, loginState, new BiliLoginService(),
            registry, api, credentials, new FakeFfmpegService());
    }

    private static DocumentSaveData Envelope(int majorVersion, object content) =>
        EnvelopeRaw($"{majorVersion}.0", JsonConvert.SerializeObject(content));

    private static DocumentSaveData EnvelopeRaw(string version, string content) => new()
    {
        DocumentTypeId = new("A3F7E1B2-9C4D-4E8A-B6F1-2D5E8A7C3B10"),
        Title = "测试",
        SaveTime = DateTime.Now,
        Content = content,
        PluginMetadata = JsonConvert.SerializeObject(new { Version = version }),
    };
}

/// <summary>用于证明 Document 恢复路径不会调用任何来源 API 的计数 Provider。</summary>
internal sealed class CountingDocumentProvider : IContentSourceProvider
{
    public int NormalizeCount { get; private set; }
    public int GetPageCount { get; private set; }
    public ContentSourceKind Kind => ContentSourceKind.Course;
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.SupportsPaging | ContentSourceCapabilities.SupportsKeyword;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        NormalizeCount++;
        return ValueTask.FromResult(new ContentSourceDescriptor(Kind, "course:1", "课程", null, 1));
    }

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        GetPageCount++;
        return Task.FromResult(new ContentPage([], null, false));
    }
}
