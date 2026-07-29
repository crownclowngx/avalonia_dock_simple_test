using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task 任务记录可完整往返且按创建时间排序()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        await store.InitAsync();
        var first = CreateRecord("first", "doc-a", DateTime.Parse("2026-01-01 10:00:00"));
        var second = CreateRecord("second", "doc-a", DateTime.Parse("2026-01-01 09:00:00"));
        second.Status = "downloading_audio";

        await store.InsertBatchAsync([first, second]);

        var records = await store.GetAllAsync();
        Assert.Equal(["second", "first"], records.Select(x => x.TaskId));
        var actual = records.Single(x => x.TaskId == "first");
        Assert.Equal("系列", actual.SeriesTitle);
        Assert.Equal("标题", actual.ItemTitle);
        Assert.Equal(101, actual.Aid);
        Assert.Equal("BV1TEST0001", actual.Bvid);
        Assert.Equal(202, actual.Cid);
        Assert.Equal(120, actual.QualityId);
        Assert.Equal(30280, actual.AudioQualityId);
        Assert.Equal("输出", actual.OutputDirectory);
        Assert.Equal("子目录", actual.SubFolder);
        Assert.Equal(12.5, actual.Progress);
        Assert.Equal("failed", actual.Status);
        Assert.Equal("safe error", actual.ErrorMessage);
        Assert.Equal("temp", actual.TempDirectory);
        Assert.Equal(11, actual.VideoBytesDownloaded);
        Assert.Equal(22, actual.AudioBytesDownloaded);
        Assert.Equal(33, actual.VideoProgress);
        Assert.Equal(44, actual.AudioProgress);
        Assert.Equal(55, actual.MergeProgress);
        Assert.Equal("1 MB/s", actual.SpeedText);
        Assert.Equal("bangumi", actual.MediaType);
        Assert.Equal(303, actual.EpId);
        Assert.Equal(404, actual.SeasonId);
        Assert.Equal(7, actual.ExtrasConfig);
        Assert.Equal("https://example.invalid/cover.jpg", actual.CoverUrl);
    }

    [Fact]
    public async Task 同任务ID会替换且Document查询彼此隔离()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var original = CreateRecord("same", "doc-a", DateTime.UtcNow);
        var other = CreateRecord("other", "doc-b", DateTime.UtcNow);
        await store.InsertBatchAsync([original, other]);
        original.ItemTitle = "替换后";
        original.Progress = 80;

        await store.InsertBatchAsync([original]);

        var docA = Assert.Single(await store.GetByDocumentIdAsync("doc-a"));
        Assert.Equal("替换后", docA.ItemTitle);
        Assert.Equal(80, docA.Progress);
        Assert.Single(await store.GetByDocumentIdAsync("doc-b"));
        Assert.Equal(2, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task 更新与删除方法保持任务事实一致()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var active = CreateRecord("active", "doc", DateTime.UtcNow);
        active.Status = "pending";
        var done = CreateRecord("done", "doc", DateTime.UtcNow.AddSeconds(1));
        done.Status = "done";
        var delete = CreateRecord("delete", "doc", DateTime.UtcNow.AddSeconds(2));
        await store.InsertBatchAsync([active, done, delete]);

        await store.UpdateProgressAsync("active", 10, "downloading_video", "safe");
        await store.UpdateBytesAsync("active", 123, 456);
        await store.UpdateTempDirectoryAsync("active", "new-temp");
        await store.UpdateStageProgressAsync(
            "active", 60, "merging", 100, 100, 20, "2 MB/s");
        await store.UpdateExtrasResultAsync("active", "cover: OK");

        var updated = (await store.GetAllAsync()).Single(x => x.TaskId == "active");
        Assert.Equal(60, updated.Progress);
        Assert.Equal("merging", updated.Status);
        Assert.Equal("safe", updated.ErrorMessage);
        Assert.Equal(123, updated.VideoBytesDownloaded);
        Assert.Equal(456, updated.AudioBytesDownloaded);
        Assert.Equal("new-temp", updated.TempDirectory);
        Assert.Equal(100, updated.VideoProgress);
        Assert.Equal(100, updated.AudioProgress);
        Assert.Equal(20, updated.MergeProgress);
        Assert.Equal("2 MB/s", updated.SpeedText);
        Assert.Equal("cover: OK", updated.ExtrasResultSummary);
        Assert.Equal(["active"], (await store.GetIncompleteAsync()).Select(x => x.TaskId));

        await store.DeleteByIdAsync("delete");
        await store.DeleteDoneAsync();
        Assert.Equal(["active"], (await store.GetAllAsync()).Select(x => x.TaskId));

        await store.InsertBatchAsync(
        [
            CreateRecord("batch-1", "doc", DateTime.UtcNow),
            CreateRecord("batch-2", "doc", DateTime.UtcNow),
        ]);
        await store.DeleteByIdsAsync(["active", "batch-2"]);
        Assert.Equal(["batch-1"], (await store.GetAllAsync()).Select(x => x.TaskId));
    }

    [Fact]
    public async Task 旧表初始化会补齐扩展列并保留已有任务()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DownloadTaskDatabasePath,
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE download_tasks (
                    task_id TEXT NOT NULL PRIMARY KEY,
                    document_id TEXT NOT NULL,
                    series_title TEXT NOT NULL DEFAULT '',
                    item_title TEXT NOT NULL DEFAULT '',
                    aid INTEGER NOT NULL DEFAULT 0,
                    bvid TEXT NOT NULL DEFAULT '',
                    cid INTEGER NOT NULL DEFAULT 0,
                    quality_id INTEGER NOT NULL DEFAULT 80,
                    output_directory TEXT NOT NULL DEFAULT '',
                    progress REAL NOT NULL DEFAULT 0,
                    status TEXT NOT NULL DEFAULT 'pending',
                    error_message TEXT,
                    temp_directory TEXT NOT NULL DEFAULT '',
                    video_bytes INTEGER NOT NULL DEFAULT 0,
                    audio_bytes INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT ''
                );
                INSERT INTO download_tasks(task_id, document_id, item_title)
                VALUES ('legacy', 'doc-old', '旧任务');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new DownloadTaskStore(paths);
        await store.InitAsync();

        var legacy = Assert.Single(await store.GetAllAsync());
        Assert.Equal("legacy", legacy.TaskId);
        Assert.Equal("旧任务", legacy.ItemTitle);
        Assert.Equal(0, legacy.AudioQualityId);
        Assert.Equal("video", legacy.MediaType);

        await using var verify = new SqliteConnection(connectionString);
        await verify.OpenAsync();
        await using var pragma = verify.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(download_tasks);";
        await using var reader = await pragma.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        Assert.Contains("extras_result_summary", columns);
        Assert.Contains("expected_video_bytes", columns);
        Assert.DoesNotContain("cookie", columns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 设置支持缺失读取覆盖Unicode并跨实例恢复()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new SettingsStore(paths);
        await store.InitAsync();
        await store.InitAsync();

        Assert.Null(await store.GetSettingAsync("missing"));
        await store.SetSettingAsync("路径 key", "D:\\视频 & 字幕");
        await store.SetSettingAsync("路径 key", "第二个值=✓");

        var reloaded = new SettingsStore(paths);
        await reloaded.InitAsync();
        Assert.Equal("第二个值=✓", await reloaded.GetSettingAsync("路径 key"));
    }

    private static DownloadTaskRecord CreateRecord(
        string taskId,
        string documentId,
        DateTime createdAt)
        => new()
        {
            TaskId = taskId,
            DocumentId = documentId,
            SeriesTitle = "系列",
            ItemTitle = "标题",
            Aid = 101,
            Bvid = "BV1TEST0001",
            Cid = 202,
            QualityId = 120,
            AudioQualityId = 30280,
            OutputDirectory = "输出",
            SubFolder = "子目录",
            Progress = 12.5,
            Status = "failed",
            ErrorMessage = "safe error",
            TempDirectory = "temp",
            VideoBytesDownloaded = 11,
            AudioBytesDownloaded = 22,
            VideoProgress = 33,
            AudioProgress = 44,
            MergeProgress = 55,
            SpeedText = "1 MB/s",
            CreatedAt = createdAt,
            MediaType = "bangumi",
            EpId = 303,
            SeasonId = 404,
            ExtrasConfig = 7,
            CoverUrl = "https://example.invalid/cover.jpg",
        };
}
