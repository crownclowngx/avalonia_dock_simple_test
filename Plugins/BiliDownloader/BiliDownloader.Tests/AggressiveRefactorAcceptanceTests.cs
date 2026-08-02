using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Naming;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels.BiliDownloader;
using BiliDownloader.ViewModels.BiliScheduler;
using MyAvaloniaManagementCommon.Save;

namespace BiliDownloader.Tests;

public sealed class AggressiveRefactorAcceptanceTests
{
    [Fact]
    public async Task 高频快照与并发Flush_最终完整快照不丢失()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(Record("flood"));
        var channel = new ProgressWriteChannel(repository);
        for (var i = 1; i <= 200; i++)
        {
            channel.Enqueue(new TaskRuntimeSnapshot(
                "flood", i / 2d, "downloading_video", i / 2d, 0, 0,
                $"{i} KB/s", i * 1024L, i * 10L, i * 20L, DateTime.Now));
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => channel.FlushAsync("flood")));
        var firstShutdown = channel.ShutdownAsync();
        var secondShutdown = channel.ShutdownAsync();
        Assert.Same(firstShutdown, secondShutdown);
        await firstShutdown;

        var result = Assert.Single(repository.Tasks);
        Assert.Equal(100, result.Progress);
        Assert.Equal(2000, result.VideoBytesDownloaded);
        Assert.Equal(4000, result.AudioBytesDownloaded);
    }

    [Fact]
    public async Task 分块恢复_汇总chunk并按预期长度截断()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("chunks");
        task.TempDirectory = Path.Combine(paths.TempDirectory, "chunks");
        task.ExpectedVideoBytes = 15;
        task.ExpectedAudioBytes = 0;
        Directory.CreateDirectory(task.TempDirectory);
        await File.WriteAllBytesAsync(Path.Combine(task.TempDirectory, "video.tmp.chunk0"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(task.TempDirectory, "video.tmp.chunk1"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(task.TempDirectory, "audio.tmp.chunk0"), new byte[7]);
        repository.Seed(task);

        await new DownloadRecoveryService(repository).ReconcileAsync(task);

        Assert.Equal(15, task.VideoBytesDownloaded);
        Assert.Equal(7, task.AudioBytesDownloaded);
    }

    [Fact]
    public void 时间范围筛选_今天七天三十天边界正确()
    {
        var now = DateTime.Now;
        var records = new[]
        {
            RecordAt("today", now),
            RecordAt("week", now.AddDays(-3)),
            RecordAt("month", now.AddDays(-20)),
            RecordAt("old", now.AddDays(-40)),
        };

        Assert.Single(Filter(records, TaskDateRange.Today));
        Assert.Equal(2, Filter(records, TaskDateRange.Last7Days).Count);
        Assert.Equal(3, Filter(records, TaskDateRange.Last30Days).Count);
    }

    [Fact]
    public void 命名预览_同批重复文件名直接阻止提交()
    {
        var vm = new NamingTemplateViewModel { Template = "{title}" };
        vm.UpdatePreview([
            new NamingContext { Title = "相同" },
            new NamingContext { Title = "相同" },
        ]);

        Assert.False(vm.IsValid);
        Assert.True(vm.HasOutputConflicts);
        Assert.Contains("相同文件名", vm.ValidationError);
    }

    [Fact]
    public async Task 预设服务_完整聚合复制重命名删除往返()
    {
        using var paths = new TestDataPaths();
        await new SettingsStore(paths).InitAsync();
        var service = new DownloadPresetService(new PresetStore(paths));
        var profile = new DownloadProfile(
            "quality:120", 30280, true, false, true, true, true,
            "{bv}_{title}", "D:\\Media");

        var saved = await service.SaveCopyAsync(profile, "个人归档");
        Assert.Equal(profile, saved.ToProfile());
        var renamed = await service.RenameAsync(saved.Id, "长期归档");
        Assert.Equal("长期归档", renamed?.Name);
        await service.DeleteAsync(saved.Id);
        Assert.Null(await service.GetByIdAsync(saved.Id));
    }

    [Fact]
    public void DownloadSubmission_消息边界保留不可变配置与Document标题()
    {
        var submission = new DownloadSubmission(
            "doc", "我的工作台", "系列",
            new DownloadProfileSnapshot(120, 30280, "out", true, true, true, false, true, "{title}"),
            [new DownloadSubmissionItem("id", "标题", 1, "BV1", 2, 3, BiliMediaType.Video, 0, 0, "cover")]);
        var message = new SubmitDownloadTaskMessage(submission);

        Assert.Equal("我的工作台", message.SourceDocumentTitle);
        Assert.Equal(120, message.QualityId);
        Assert.True(message.ExtrasConfig.HasFlag(global::BiliDownloader.Services.Download.Extras.ExtrasType.Danmaku));
        Assert.Equal(submission.Profile, message.ToSubmission().Profile);
    }

    [Fact]
    public void 文件名安全器_跨平台非法字符与过长目录均给出确定结果()
    {
        Assert.Equal("a_b_c_d_e_f_g_h_i", FileNameSanitizer.Sanitize("a<b>c:d/e\\f|g?h*i"));
        Assert.Throws<PathTooLongException>(() =>
            FileNameSanitizer.EnsurePathLength(new string('x', 255), "name", ".mp4"));
    }

    [Fact]
    public void DocumentCodec_未知主版本只标记安全读取而不伪装V1()
    {
        var decoded = DocumentSaveCodec.Decode(new DocumentSaveData
        {
            DocumentTypeId = "bili",
            Title = "test",
            Content = "{}",
            PluginMetadata = "{\"Version\":\"9.0\"}",
        });

        Assert.Equal(9, decoded.MajorVersion);
        Assert.False(decoded.IsKnownVersion);
    }

    [Fact]
    public async Task 显式初始化_并发调用只执行一次()
    {
        var settings = new InMemorySettingsRepository();
        var vm = new DownloadConfigViewModel(settings);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => vm.InitializeAsync()));

        Assert.Equal(1, settings.InitializeCount);
    }

    [Fact]
    public async Task Document缺失预设和画质_保留配置并向用户说明回退()
    {
        var vm = new DownloadConfigViewModel(new InMemorySettingsRepository());
        vm.RestoreDocumentConfiguration(new DocumentSaveDataV2
        {
            PresetId = "deleted-preset",
            OutputDirectory = "document-output",
            QualityId = 999,
        });
        await vm.InitializeAsync();
        var available = new BiliQualityOption { QualityId = 80, DisplayName = "1080P" };
        vm.PopulateQualities([available], available, [], null, false);

        Assert.Equal("document-output", vm.OutputDirectory);
        Assert.Contains("原预设不可用", vm.PresetStatusText);
        Assert.Contains("999", vm.QualityRestoreNotice);
        Assert.Same(available, vm.SelectedQuality);
    }

    private static List<DownloadTaskRecord> Filter(
        IReadOnlyList<DownloadTaskRecord> source,
        TaskDateRange range) => TaskFilterSortEngine.Apply(
            source,
            new TaskFilterCriteria(null, "all", "all", range),
            TaskSortField.CreatedAt,
            sortDescending: false);

    private static DownloadTaskRecord Record(string id) => new()
    {
        TaskId = id,
        DocumentId = "doc",
        ItemTitle = id,
        Status = "ready",
        CreatedAt = DateTime.Now,
        LastUpdatedAt = DateTime.Now,
    };

    private static DownloadTaskRecord RecordAt(string id, DateTime createdAt)
    {
        var record = Record(id);
        record.CreatedAt = createdAt;
        return record;
    }
}
