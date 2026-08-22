using System.Security;
using System.Text;
using System.Text.Json;
using BiliDownloader.Models;
using BiliDownloader.Services.History;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Naming;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels.BiliScheduler;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Tests;

/// <summary>
/// P1-G6 历史中心核心测试。测试按职责边界覆盖 SQL 查询、文件异常分类、重新下载映射和安全导出，
/// 不通过真实网络、系统文件选择器或桌面 Shell 获取偶然成功。
/// </summary>
public sealed class TaskHistoryG6Tests
{
    [Fact]
    public async Task 历史查询只返回终态并支持组合筛选和稳定分页()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var records = Enumerable.Range(0, 205).Select(index => CreateRecord(
            $"history-{index:D3}",
            index % 2 == 0 ? DownloadTaskStatus.Completed : DownloadTaskStatus.Failed,
            DateTime.Today.AddSeconds(index),
            title: index % 3 == 0 ? $"100%_专题-{index}" : $"普通-{index}",
            documentId: index % 2 == 0 ? "doc-a" : "doc-b")).ToList();
        records.Add(CreateRecord("active", DownloadTaskStatus.Paused, DateTime.Now));
        await store.InsertBatchAsync(records);

        var service = new TaskHistoryQueryService(store);
        var query = new TaskHistoryQuery(
            Title: "100%_",
            DocumentId: "doc-a",
            Statuses: new HashSet<DownloadTaskStatus> { DownloadTaskStatus.Completed });
        var all = new List<TaskHistoryEntry>();
        string? cursor = null;
        do
        {
            var page = await service.QueryPageAsync(query, new TaskHistoryPageRequest(7, cursor));
            all.AddRange(page.Items);
            cursor = page.NextCursor;
            if (!page.HasMore) break;
        } while (true);

        Assert.NotEmpty(all);
        Assert.All(all, item =>
        {
            Assert.Contains("100%_", item.ItemTitle);
            Assert.Equal("doc-a", item.DocumentId);
            Assert.Equal("done", item.Status);
        });
        Assert.Equal(all.Count, all.Select(item => item.TaskId).Distinct().Count());
        Assert.Equal(all.OrderByDescending(item => item.CreatedAt).Select(item => item.TaskId), all.Select(item => item.TaskId));

        var entireHistory = new List<TaskHistoryEntry>();
        await foreach (var entry in service.StreamAsync(new TaskHistoryQuery())) entireHistory.Add(entry);
        Assert.Equal(205, entireHistory.Count);
        Assert.DoesNotContain(entireHistory, item => item.TaskId == "active");
    }

    [Fact]
    public async Task 快照字段完整往返且旧记录保持未知并创建历史索引()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var exact = CreateRecord("exact", DownloadTaskStatus.Completed, DateTime.Now);
        exact.SubmissionSnapshotVersion = 1;
        exact.DurationSeconds = 321;
        exact.UseGroupFolder = true;
        exact.AddIndexToTitle = true;
        exact.NamingTemplate = "{index}-{title}";
        exact.PresetId = "preset-1";
        exact.SelectedVideoCodec = VideoCodecPreference.Hevc;
        exact.ActualVideoCodec = "hevc";
        exact.SelectedOutputContainer = OutputContainer.Mkv;
        exact.SelectedOutputMediaMode = OutputMediaMode.VideoOnly;
        exact.RedownloadedFromTaskId = "old-task";
        var legacy = CreateRecord("legacy", DownloadTaskStatus.Canceled, DateTime.Now.AddSeconds(-1));
        await store.InsertBatchAsync([exact, legacy]);

        var loaded = await store.GetAllAsync();
        var actual = loaded.Single(item => item.TaskId == "exact");
        Assert.Equal(1, actual.SubmissionSnapshotVersion);
        Assert.Equal(321, actual.DurationSeconds);
        Assert.True(actual.UseGroupFolder);
        Assert.True(actual.AddIndexToTitle);
        Assert.Equal("{index}-{title}", actual.NamingTemplate);
        Assert.Equal("preset-1", actual.PresetId);
        Assert.Equal(VideoCodecPreference.Hevc, actual.SelectedVideoCodec);
        Assert.Equal(OutputContainer.Mkv, actual.SelectedOutputContainer);
        Assert.Equal(OutputMediaMode.VideoOnly, actual.SelectedOutputMediaMode);
        Assert.Equal("old-task", actual.RedownloadedFromTaskId);
        var old = loaded.Single(item => item.TaskId == "legacy");
        Assert.Equal(0, old.SubmissionSnapshotVersion);
        Assert.Null(old.SelectedVideoCodec);
        Assert.Null(old.SelectedOutputContainer);
        Assert.Null(old.SelectedOutputMediaMode);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DownloadTaskDatabasePath,
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA index_list(download_tasks);";
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new List<string>();
        while (await reader.ReadAsync()) indexes.Add(reader.GetString(1));
        Assert.Contains("ix_download_tasks_history_status_created", indexes);
        Assert.Contains("ix_download_tasks_history_output", indexes);

        var filtered = await new TaskHistoryQueryService(store).QueryPageAsync(
            new TaskHistoryQuery(
                SelectedVideoCodec: VideoCodecPreference.Hevc,
                OutputContainer: OutputContainer.Mkv,
                OutputMediaMode: OutputMediaMode.VideoOnly),
            new TaskHistoryPageRequest());
        Assert.Equal("exact", Assert.Single(filtered.Items).TaskId);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TaskHistoryQueryService(store).QueryPageAsync(
                new TaskHistoryQuery(), new TaskHistoryPageRequest(), canceled.Token));
    }

    [Fact]
    public async Task 文件检查区分存在缺失不可访问并保留取消语义()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "done.mp4");
        await File.WriteAllTextAsync(file, "ok");
        var service = new OutputFileStatusService();

        Assert.Equal(FilePresenceStatus.Exists, await service.CheckAsync(file));
        Assert.Equal(FilePresenceStatus.Missing, await service.CheckAsync(paths.DataDirectory));
        Assert.Equal(FilePresenceStatus.Missing, await service.CheckAsync(Path.Combine(paths.DataDirectory, "missing.mp4")));
        Assert.Equal(FilePresenceStatus.Missing, await service.CheckAsync(""));
        Assert.Equal(FilePresenceStatus.Inaccessible,
            await new OutputFileStatusService(new ThrowingFileProbe(new UnauthorizedAccessException())).CheckAsync(file));
        Assert.Equal(FilePresenceStatus.Inaccessible,
            await new OutputFileStatusService(new ThrowingFileProbe(new IOException("offline share"))).CheckAsync(file));
        Assert.Equal(FilePresenceStatus.Inaccessible,
            await new OutputFileStatusService(new ThrowingFileProbe(new SecurityException())).CheckAsync(file));
        Assert.Equal(FilePresenceStatus.Inaccessible,
            await new OutputFileStatusService(new ThrowingFileProbe(new ArgumentException("非法路径"))).CheckAsync(file));
        Assert.Equal(FilePresenceStatus.Inaccessible,
            await new OutputFileStatusService(new ThrowingFileProbe(new NotSupportedException("不支持的路径格式"))).CheckAsync(file));

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CheckManyAsync([], null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CheckManyAsync([], _ => Task.CompletedTask, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CheckManyAsync([], _ => Task.CompletedTask, 33));
        await service.CheckManyAsync([], _ => Task.CompletedTask, 4);

        using var cancellation = new CancellationTokenSource();
        var completed = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckManyAsync(
            Enumerable.Range(0, 20).Select(index => new OutputFileReference(index.ToString(), file)).ToArray(),
            _ =>
            {
                completed++;
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            maxConcurrency: 1,
            cancellation.Token));
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task 重新下载完整快照生成新任务标识且旧记录不变()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var source = CreateRecord("source-task", DownloadTaskStatus.Canceled, DateTime.Now);
        source.SubmissionSnapshotVersion = 3;
        source.DurationSeconds = 99;
        source.UseGroupFolder = true;
        source.AddIndexToTitle = true;
        source.NamingTemplate = "{title}-{quality}";
        source.PresetId = "preset";
        source.SelectedVideoCodec = VideoCodecPreference.Av1;
        source.SelectedOutputContainer = OutputContainer.Mkv;
        source.SelectedOutputMediaMode = OutputMediaMode.AudioOnly;
        source.SelectedVideoDynamicRangePreference = VideoDynamicRangePreference.Auto;
        source.SelectedAudioFeaturePreference = AudioFeaturePreference.Auto;
        source.ConflictPolicy = FileConflictPolicy.AutoNumber;
        await store.InsertBatchAsync([source]);
        var service = new TaskHistoryRedownloadService(new TaskHistoryQueryService(store));

        var plan = await service.CreatePlanAsync(source.TaskId);

        Assert.False(plan.RequiresCompatibilityConfirmation);
        Assert.NotEqual(source.TaskId, Assert.Single(plan.Submission.Items).ItemId);
        Assert.Equal(source.TaskId, plan.Submission.RedownloadedFromTaskId);
        Assert.Equal(source.Aid, plan.Submission.Items[0].Aid);
        Assert.Equal(VideoCodecPreference.Av1, plan.Submission.Profile.VideoCodecPreference);
        Assert.Equal(OutputContainer.Mkv, plan.Submission.Profile.OutputContainer);
        Assert.Equal(OutputMediaMode.AudioOnly, plan.Submission.Profile.OutputMediaMode);
        Assert.Equal("preset", plan.Submission.Profile.PresetId);
        Assert.Single(await store.GetAllAsync());
        Assert.Equal("canceled", (await store.GetAllAsync())[0].Status);
    }

    [Fact]
    public async Task 旧任务兼容重建给出警告且不完整身份被拒绝()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var legacy = CreateRecord("legacy", DownloadTaskStatus.Canceled, DateTime.Now);
        legacy.SubFolder = "系列";
        var invalid = CreateRecord("invalid", DownloadTaskStatus.Canceled, DateTime.Now.AddSeconds(-1));
        invalid.Aid = invalid.Cid = invalid.EpId = 0;
        invalid.Bvid = string.Empty;
        var bvidOnly = CreateRecord("bvid-only", DownloadTaskStatus.Canceled, DateTime.Now.AddSeconds(-2));
        bvidOnly.Aid = bvidOnly.Cid = bvidOnly.EpId = 0;
        var active = CreateRecord("active", DownloadTaskStatus.Paused, DateTime.Now.AddSeconds(-3));
        await store.InsertBatchAsync([legacy, invalid, bvidOnly, active]);
        var service = new TaskHistoryRedownloadService(new TaskHistoryQueryService(store));

        var plan = await service.CreatePlanAsync("legacy");

        Assert.True(plan.RequiresCompatibilityConfirmation);
        Assert.Equal(VideoCodecPreference.AutoCompatibility, plan.Submission.Profile.VideoCodecPreference);
        Assert.Equal(OutputContainer.Mp4, plan.Submission.Profile.OutputContainer);
        Assert.Equal(OutputMediaMode.AudioVideo, plan.Submission.Profile.OutputMediaMode);
        Assert.Equal(NamingTemplateEngine.DefaultTemplate, plan.Submission.Profile.NamingTemplate);
        Assert.True(plan.Submission.Profile.UseGroupFolder);
        Assert.Equal("BV1HISTORY", (await service.CreatePlanAsync("bvid-only")).Submission.Items[0].Bvid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePlanAsync("invalid"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePlanAsync("active"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePlanAsync("missing"));
    }

    [Fact]
    public async Task 历史重下经过预检和Coordinator并保留旧任务事实()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var source = CreateRecord("completed-missing", DownloadTaskStatus.Completed, DateTime.Now);
        source.SubmissionSnapshotVersion = 1;
        source.SelectedVideoCodec = VideoCodecPreference.AutoCompatibility;
        source.SelectedOutputContainer = OutputContainer.Mp4;
        source.SelectedOutputMediaMode = OutputMediaMode.AudioVideo;
        source.NamingTemplate = NamingTemplateEngine.DefaultTemplate;
        source.OutputDirectory = paths.DataDirectory;
        source.OutputFilePath = Path.Combine(paths.DataDirectory, "missing.mp4");
        await store.InsertBatchAsync([source]);
        var query = new TaskHistoryQueryService(store);
        var plan = await new TaskHistoryRedownloadService(query).CreatePlanAsync(source.TaskId);
        var preflight = new SubmissionPreflightService(
            new FakeCredentialProvider { IsLoggedIn = true },
            new FakeFfmpegService { ReadyOverride = true },
            store,
            new HistoryFixedEstimator(),
            new HistoryFixedCapacity());
        var coordinator = new BiliDownloadCoordinator(
            store,
            new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths,
            new FakeCredentialProvider { IsLoggedIn = true },
            new DownloadRecoveryService(store));
        var submissions = new DownloadSubmissionService(preflight, coordinator);

        var report = await submissions.PreflightAsync(plan.Submission);
        Assert.False(report.IsBlocked);
        Assert.Equal(1, report.ReadyCount);
        var committed = await submissions.CommitAsync(new PreparedSubmission(report, UserConfirmed: true));

        Assert.Equal(SubmissionCommitStatus.Committed, committed.Status);
        var rows = await store.GetAllAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("done", rows.Single(row => row.TaskId == source.TaskId).Status);
        var created = rows.Single(row => row.TaskId != source.TaskId);
        Assert.NotEqual(source.TaskId, created.TaskId);
        Assert.Equal(source.TaskId, created.RedownloadedFromTaskId);
        Assert.Equal(4, created.SubmissionSnapshotVersion);
        Assert.Equal(source.TaskRateLimitBytesPerSecond, created.TaskRateLimitBytesPerSecond);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 历史ViewModel加载和筛选不会主动检查文件()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        await store.InsertBatchAsync(
        [
            CreateRecord("done", DownloadTaskStatus.Completed, DateTime.Now),
            CreateRecord("failed", DownloadTaskStatus.Failed, DateTime.Now.AddSeconds(-1)),
            CreateRecord("active", DownloadTaskStatus.Paused, DateTime.Now.AddSeconds(-2)),
        ]);
        var query = new TaskHistoryQueryService(store);
        var fileStatus = new CountingOutputStatusService();
        var vm = new TaskHistoryViewModel(
            query,
            fileStatus,
            new TaskHistoryExporter(query),
            new TaskHistoryRedownloadService(query),
            new NoOpSubmissionService(),
            new NoOpFailureActionService(),
            new NoOpPromptService(),
            new CancelExportPicker(),
            new NoOpFileRevealService(),
            new InlineUiDispatcher());

        await vm.ReloadAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(0, fileStatus.CheckCount);
        Assert.All(vm.Items, item => Assert.Equal(FilePresenceStatus.Unknown, item.FilePresenceStatus));
        await vm.CheckItemCommand.ExecuteAsync(vm.Items[0]);
        Assert.Equal(1, fileStatus.CheckCount);
        Assert.Equal(FilePresenceStatus.Missing, vm.Items[0].FilePresenceStatus);

        await vm.CheckItemCommand.ExecuteAsync(null);
        await vm.CheckSelectedCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);
        await vm.CheckSelectedCommand.ExecuteAsync(null);
        await vm.CheckFilteredCommand.ExecuteAsync(null);
        vm.CancelCheckCommand.Execute(null);
        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
        await vm.ExportSelectedCsvCommand.ExecuteAsync(null);
        await vm.ExportFilteredJsonCommand.ExecuteAsync(null);

        vm.StatusFilter = "done";
        Assert.Contains(DownloadTaskStatus.Completed, vm.BuildQuery().Statuses!);
        vm.StatusFilter = "failed";
        Assert.Contains(DownloadTaskStatus.Failed, vm.BuildQuery().Statuses!);
        vm.StatusFilter = "canceled";
        Assert.Contains(DownloadTaskStatus.Canceled, vm.BuildQuery().Statuses!);
        vm.StatusFilter = "all";
        Assert.Null(vm.BuildQuery().Statuses);
        vm.DateFilter = "today";
        Assert.NotNull(vm.BuildQuery().CreatedFrom);
        vm.DateFilter = "7d";
        Assert.NotNull(vm.BuildQuery().CreatedFrom);
        vm.DateFilter = "30d";
        Assert.NotNull(vm.BuildQuery().CreatedFrom);
        vm.DateFilter = "all";
        Assert.Null(vm.BuildQuery().CreatedFrom);
        vm.CodecFilter = "Av1";
        vm.ContainerFilter = "Mkv";
        vm.OutputModeFilter = "VideoOnly";
        var exactQuery = vm.BuildQuery();
        Assert.Equal(VideoCodecPreference.Av1, exactQuery.SelectedVideoCodec);
        Assert.Equal(OutputContainer.Mkv, exactQuery.OutputContainer);
        Assert.Equal(OutputMediaMode.VideoOnly, exactQuery.OutputMediaMode);
        vm.CodecFilter = vm.ContainerFilter = vm.OutputModeFilter = "unknown";
        var unknownQuery = vm.BuildQuery();
        Assert.True(unknownQuery.IncludeUnknownVideoCodec);
        Assert.True(unknownQuery.IncludeUnknownOutputContainer);
        Assert.True(unknownQuery.IncludeUnknownOutputMode);
        await vm.ReloadAsync();
    }

    [Fact]
    public void 历史条目投影覆盖未知状态文件状态和重下资格分支()
    {
        var completed = TaskHistoryEntry.FromRecord(CreateRecord("done", DownloadTaskStatus.Completed, DateTime.Now));
        var doneItem = new TaskHistoryItemViewModel(completed, false, FilePresenceStatus.Unknown);
        Assert.Equal("未检查", doneItem.FilePresenceText);
        Assert.False(doneItem.CanReveal);
        Assert.False(doneItem.CanRedownload);
        Assert.Contains("未知编码", doneItem.OutputSpecificationText);

        doneItem.FilePresenceStatus = FilePresenceStatus.Exists;
        Assert.Equal("文件存在", doneItem.FilePresenceText);
        Assert.True(doneItem.CanReveal);
        Assert.False(doneItem.CanRedownload);
        doneItem.FilePresenceStatus = FilePresenceStatus.Missing;
        Assert.Equal("文件缺失", doneItem.FilePresenceText);
        Assert.True(doneItem.CanRedownload);
        doneItem.FilePresenceStatus = FilePresenceStatus.Inaccessible;
        Assert.Equal("无法访问", doneItem.FilePresenceText);
        Assert.False(doneItem.CanRedownload);

        var retryableRecord = CreateRecord("retry", DownloadTaskStatus.Failed, DateTime.Now);
        retryableRecord.IsRetryable = true;
        retryableRecord.SourceDocumentTitle = string.Empty;
        retryableRecord.ErrorMessage = "第一行\n第二行";
        retryableRecord.SelectedVideoCodec = VideoCodecPreference.Av1;
        retryableRecord.SelectedOutputContainer = OutputContainer.Mkv;
        retryableRecord.SelectedOutputMediaMode = OutputMediaMode.VideoOnly;
        var retryable = new TaskHistoryItemViewModel(
            TaskHistoryEntry.FromRecord(retryableRecord), false, FilePresenceStatus.Unknown);
        Assert.True(retryable.CanRetryOriginal);
        Assert.False(retryable.CanRedownload);
        Assert.StartsWith("工作台 ", retryable.SourceDocumentDisplay);
        Assert.Equal("第一行", retryable.ErrorSummary);
        Assert.Contains("Av1", retryable.OutputSpecificationText);

        retryableRecord.IsRetryable = false;
        retryableRecord.DocumentId = string.Empty;
        retryableRecord.ErrorMessage = null;
        var failed = new TaskHistoryItemViewModel(
            TaskHistoryEntry.FromRecord(retryableRecord), false, FilePresenceStatus.Unknown);
        Assert.False(failed.CanRetryOriginal);
        Assert.True(failed.CanRedownload);
        Assert.Equal("未知工作台", failed.SourceDocumentDisplay);
        Assert.Empty(failed.ErrorSummary);

        var canceled = new TaskHistoryItemViewModel(
            TaskHistoryEntry.FromRecord(CreateRecord("cancel", DownloadTaskStatus.Canceled, DateTime.Now)),
            false,
            FilePresenceStatus.Unknown);
        Assert.True(canceled.CanRedownload);
        Assert.False(canceled.CanRetryOriginal);

        var active = new TaskHistoryItemViewModel(
            TaskHistoryEntry.FromRecord(CreateRecord("active", DownloadTaskStatus.Paused, DateTime.Now)),
            false,
            FilePresenceStatus.Unknown);
        Assert.False(active.CanRedownload);
        Assert.False(active.CanRetryOriginal);
        Assert.Equal("来源工作台", active.SourceDocumentDisplay);
    }

    [Fact]
    public async Task CSV和JSON流式导出使用白名单脱敏并阻止公式注入()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var records = Enumerable.Range(0, 1000).Select(index =>
        {
            var record = CreateRecord($"export-{index:D4}", DownloadTaskStatus.Completed, DateTime.Today.AddSeconds(index));
            record.ItemTitle = index == 0
                ? "  =HYPERLINK(\"https://bad.test/?token=title-secret\")，逗号\n换行"
                : $"标题-{index}";
            record.ErrorMessage = index == 0
                ? "Authorization: Bearer bearer-secret\n   at Secret.Stack(token-secret)"
                : null;
            record.CoverUrl = "https://cover.test/a.jpg?token=cover-secret";
            record.TempDirectory = "C:\\Cookie\\SESSDATA=temp-secret";
            record.OutputPathKey = index == 0 ? "authorization=path-key-secret" : string.Empty;
            return record;
        }).ToList();
        await store.InsertBatchAsync(records);
        var exporter = new TaskHistoryExporter(new TaskHistoryQueryService(store));
        var csv = Path.Combine(paths.DataDirectory, "history.csv");
        var json = Path.Combine(paths.DataDirectory, "history.json");

        var csvResult = await exporter.ExportAsync(new TaskHistoryExportRequest(
            csv, TaskHistoryExportFormat.Csv, new TaskHistoryQuery()));
        var jsonResult = await exporter.ExportAsync(new TaskHistoryExportRequest(
            json, TaskHistoryExportFormat.Json, new TaskHistoryQuery()));
        var selected = Path.Combine(paths.DataDirectory, "selected.json");
        var selectedResult = await exporter.ExportAsync(new TaskHistoryExportRequest(
            selected,
            TaskHistoryExportFormat.Json,
            new TaskHistoryQuery(),
            ["export-0001", "export-0999"]));

        Assert.Equal(1000, csvResult.ExportedCount);
        Assert.Equal(1000, jsonResult.ExportedCount);
        Assert.Equal(2, selectedResult.ExportedCount);
        var csvText = await File.ReadAllTextAsync(csv);
        var jsonText = await File.ReadAllTextAsync(json);
        Assert.StartsWith("taskId,redownloadedFromTaskId", csvText.TrimStart('\uFEFF'));
        Assert.Contains("'  =HYPERLINK", csvText);
        Assert.Contains("?<redacted>", csvText);
        using var document = JsonDocument.Parse(jsonText);
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1000, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("items")[0].TryGetProperty("taskId", out _));

        foreach (var text in new[] { csvText, jsonText })
        {
            Assert.DoesNotContain("bearer-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("token-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("cover-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("temp-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("path-key-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret.Stack", text, StringComparison.Ordinal);
            Assert.DoesNotContain("coverUrl", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tempDirectory", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("outputPathKey", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task 导出取消时保留旧目标且清理同目录临时文件()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        await store.InsertBatchAsync([CreateRecord("one", DownloadTaskStatus.Completed, DateTime.Now)]);
        var exporter = new TaskHistoryExporter(new TaskHistoryQueryService(store));
        var destination = Path.Combine(paths.DataDirectory, "keep.json");
        await File.WriteAllTextAsync(destination, "old-content");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(
            new TaskHistoryExportRequest(destination, TaskHistoryExportFormat.Json, new TaskHistoryQuery()),
            cancellation.Token));

        Assert.Equal("old-content", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(paths.DataDirectory, ".keep.json.*.tmp"));
    }

    [Fact]
    public void 活动分区排除完成失败取消并保留可恢复状态()
    {
        var tasks = Enum.GetValues<DownloadTaskStatus>()
            .Select(status => CreateRecord(status.ToString(), status, DateTime.Now))
            .ToList();
        var result = TaskFilterSortEngine.Apply(
            tasks,
            new TaskFilterCriteria(null, "all", "all", ActiveOnly: true),
            TaskSortField.CreatedAt,
            false);

        Assert.DoesNotContain(result, item => item.Status is "done" or "failed" or "canceled");
        Assert.Contains(result, item => item.Status == "paused");
        Assert.Contains(result, item => item.Status == "interrupted");
        Assert.Contains(result, item => item.Status == "waiting_for_login");
    }

    private static DownloadTaskRecord CreateRecord(
        string taskId,
        DownloadTaskStatus status,
        DateTime createdAt,
        string title = "历史标题",
        string documentId = "doc") => new()
    {
        TaskId = taskId,
        DocumentId = documentId,
        SourceDocumentTitle = "来源工作台",
        SeriesTitle = "系列",
        ItemTitle = title,
        Aid = 101,
        Bvid = "BV1HISTORY",
        Cid = 202,
        MediaUnitKey = "mu1:101:202",
        QualityId = 80,
        AudioQualityId = 30280,
        OutputDirectory = Path.GetTempPath(),
        OutputFilePath = Path.Combine(Path.GetTempPath(), taskId + ".mp4"),
        Status = DownloadTaskStatusMapper.ToStorageString(status),
        CreatedAt = createdAt,
        LastUpdatedAt = createdAt,
        MediaType = "video",
    };

    private sealed class ThrowingFileProbe(Exception exception) : IFileAttributeProbe
    {
        public FileAttributes GetAttributes(string fullPath) => throw exception;
    }

    private sealed class HistoryFixedEstimator : IMediaSizeEstimator
    {
        public Task<long?> EstimatePeakBytesAsync(
            DownloadSubmissionItem item,
            DownloadProfileSnapshot profile,
            CancellationToken cancellationToken) => Task.FromResult<long?>(100);
    }

    private sealed class HistoryFixedCapacity : IStorageCapacityProvider
    {
        public long? GetAvailableBytes(string path) => 1_000_000;
    }

    private sealed class CountingOutputStatusService : IOutputFileStatusService
    {
        public int CheckCount { get; private set; }

        public Task<FilePresenceStatus> CheckAsync(string? path, CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return Task.FromResult(FilePresenceStatus.Missing);
        }

        public async Task CheckManyAsync(
            IReadOnlyCollection<OutputFileReference> files,
            Func<OutputFileStatusResult, Task> onResult,
            int maxConcurrency = 4,
            CancellationToken cancellationToken = default)
        {
            foreach (var file in files)
            {
                var status = await CheckAsync(file.Path, cancellationToken);
                await onResult(new OutputFileStatusResult(file.TaskId, status));
            }
        }
    }

    private sealed class NoOpSubmissionService : IDownloadSubmissionService
    {
        public Task<SubmissionPreflightReport> PreflightAsync(
            DownloadSubmission submission,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SubmissionCommitResult> CommitAsync(
            PreparedSubmission prepared,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpFailureActionService : IDownloadFailureActionService
    {
        public Task<DownloadFailureActionResult> ExecuteAsync(
            DownloadTaskRecord task,
            DownloadFailureActionKind action,
            CancellationToken cancellationToken = default) => Task.FromResult(new DownloadFailureActionResult(false, "未执行"));
    }

    private sealed class NoOpPromptService : IUserPromptService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<DeleteTaskPromptResult> ConfirmDeleteAsync(int taskCount, bool hasOutputFiles)
            => Task.FromResult(DeleteTaskPromptResult.Cancelled);
        public Task<bool> ConfirmSubmissionAsync(SubmissionPreflightReport report) => Task.FromResult(false);
    }

    private sealed class CancelExportPicker : IHistoryExportDestinationPicker
    {
        public Task<HistoryExportDestination?> PickAsync(TaskHistoryExportFormat format)
            => Task.FromResult<HistoryExportDestination?>(null);
    }

    private sealed class NoOpFileRevealService : IFileRevealService
    {
        public Task RevealAsync(string path) => Task.CompletedTask;
    }
}
