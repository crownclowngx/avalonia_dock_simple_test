using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Services.Persistence;

/// <summary>
/// 下载任务 SQLite 持久化存储。路径由 <see cref="IBiliDataPaths"/> 统一决定。
/// </summary>
public class DownloadTaskStore : IDownloadTaskRepository
{
    private readonly string _connectionString;

    static DownloadTaskStore()
    {
        SqliteNativeLoader.EnsureLoaded();
    }

    public DownloadTaskStore(IBiliDataPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DownloadTaskDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    /// <summary>
    /// 初始化数据库，创建 download_tasks 表（若不存在）
    /// </summary>
    public async Task InitAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 启用 WAL 模式以提升并发写入性能
        await using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await pragmaCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS download_tasks (
                task_id             TEXT NOT NULL PRIMARY KEY,
                document_id         TEXT NOT NULL,
                source_document_title TEXT NOT NULL DEFAULT '',
                series_title        TEXT NOT NULL DEFAULT '',
                item_title          TEXT NOT NULL DEFAULT '',
                aid                 INTEGER NOT NULL DEFAULT 0,
                bvid                TEXT NOT NULL DEFAULT '',
                cid                 INTEGER NOT NULL DEFAULT 0,
                quality_id          INTEGER NOT NULL DEFAULT 80,
                audio_quality_id    INTEGER NOT NULL DEFAULT 0,
                output_directory    TEXT NOT NULL DEFAULT '',
                sub_folder          TEXT NOT NULL DEFAULT '',
                progress            REAL NOT NULL DEFAULT 0,
                status              TEXT NOT NULL DEFAULT 'pending',
                error_message       TEXT,
                temp_directory      TEXT NOT NULL DEFAULT '',
                video_bytes         INTEGER NOT NULL DEFAULT 0,
                audio_bytes         INTEGER NOT NULL DEFAULT 0,
                video_progress      REAL NOT NULL DEFAULT 0,
                audio_progress      REAL NOT NULL DEFAULT 0,
                merge_progress      REAL NOT NULL DEFAULT 0,
                speed_text          TEXT NOT NULL DEFAULT '',
                bytes_per_second    INTEGER NOT NULL DEFAULT 0,
                created_at          TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                expected_video_bytes INTEGER NOT NULL DEFAULT 0,
                expected_audio_bytes INTEGER NOT NULL DEFAULT 0,
                video_integrity_passed INTEGER NOT NULL DEFAULT 0,
                audio_integrity_passed INTEGER NOT NULL DEFAULT 0,
                output_file_path    TEXT NOT NULL DEFAULT '',
                last_updated_at     TEXT NOT NULL DEFAULT '',
                error_type          TEXT,
                is_retryable        INTEGER NOT NULL DEFAULT 0,
                media_type          TEXT NOT NULL DEFAULT 'video',
                ep_id               INTEGER NOT NULL DEFAULT 0,
                season_id           INTEGER NOT NULL DEFAULT 0,
                extras_config       INTEGER NOT NULL DEFAULT 0,
                cover_url           TEXT NOT NULL DEFAULT '',
                extras_result_summary TEXT
            );
            """;
        await cmd.ExecuteNonQueryAsync();

        // 升级旧表：添加分段进度列和新字段（如果不存在）
        string[] alterSqls =
        {
            "ALTER TABLE download_tasks ADD COLUMN video_progress REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN audio_progress REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN merge_progress REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN speed_text TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN bytes_per_second INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN source_document_title TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN audio_quality_id INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN sub_folder TEXT NOT NULL DEFAULT '';",
            // 架构改进：完整性验证和错误分类字段
            "ALTER TABLE download_tasks ADD COLUMN expected_video_bytes INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN expected_audio_bytes INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN video_integrity_passed INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN audio_integrity_passed INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN output_file_path TEXT NOT NULL DEFAULT '';",
            // SQLite 不允许 ADD COLUMN 使用非常量默认表达式；空值由读取兼容层解释为未知时间。
            "ALTER TABLE download_tasks ADD COLUMN last_updated_at TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN error_type TEXT;",
            "ALTER TABLE download_tasks ADD COLUMN is_retryable INTEGER NOT NULL DEFAULT 0;",
            // 番剧支持字段
            "ALTER TABLE download_tasks ADD COLUMN media_type TEXT NOT NULL DEFAULT 'video';",
            "ALTER TABLE download_tasks ADD COLUMN ep_id INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN season_id INTEGER NOT NULL DEFAULT 0;",
            // 附加资源（extras）字段
            "ALTER TABLE download_tasks ADD COLUMN extras_config INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN cover_url TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN extras_result_summary TEXT;",
        };
        foreach (var sql in alterSqls)
        {
            try
            {
                await using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = sql;
                await alterCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // 列已存在，忽略
            }
        }
    }

    /// <summary>
    /// 批量插入任务记录（事务保护，避免部分插入）
    /// </summary>
    public async Task InsertBatchAsync(List<DownloadTaskRecord> records)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var r in records)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO download_tasks
                    (task_id, document_id, source_document_title, series_title, item_title, aid, bvid, cid,
                     quality_id, audio_quality_id, output_directory, sub_folder,
                     progress, status, error_message,
                     temp_directory, video_bytes, audio_bytes,
                     video_progress, audio_progress, merge_progress, speed_text, bytes_per_second,
                     created_at, media_type, ep_id, season_id,
                     extras_config, cover_url, extras_result_summary,
                     expected_video_bytes, expected_audio_bytes,
                     video_integrity_passed, audio_integrity_passed,
                     output_file_path, last_updated_at, error_type, is_retryable)
                VALUES
                    ($task_id, $document_id, $source_document_title, $series_title, $item_title, $aid, $bvid, $cid,
                     $quality_id, $audio_quality_id, $output_directory, $sub_folder,
                     $progress, $status, $error_message,
                     $temp_directory, $video_bytes, $audio_bytes,
                     $video_progress, $audio_progress, $merge_progress, $speed_text, $bytes_per_second,
                     $created_at, $media_type, $ep_id, $season_id,
                     $extras_config, $cover_url, $extras_result_summary,
                     $expected_video_bytes, $expected_audio_bytes,
                     $video_integrity_passed, $audio_integrity_passed,
                     $output_file_path, $last_updated_at, $error_type, $is_retryable);
                """;
            cmd.Parameters.AddWithValue("$task_id", r.TaskId);
            cmd.Parameters.AddWithValue("$document_id", r.DocumentId);
            cmd.Parameters.AddWithValue("$source_document_title", r.SourceDocumentTitle);
            cmd.Parameters.AddWithValue("$series_title", r.SeriesTitle);
            cmd.Parameters.AddWithValue("$item_title", r.ItemTitle);
            cmd.Parameters.AddWithValue("$aid", r.Aid);
            cmd.Parameters.AddWithValue("$bvid", r.Bvid);
            cmd.Parameters.AddWithValue("$cid", r.Cid);
            cmd.Parameters.AddWithValue("$quality_id", r.QualityId);
            cmd.Parameters.AddWithValue("$audio_quality_id", r.AudioQualityId);
            cmd.Parameters.AddWithValue("$output_directory", r.OutputDirectory);
            cmd.Parameters.AddWithValue("$sub_folder", r.SubFolder);
            cmd.Parameters.AddWithValue("$progress", r.Progress);
            cmd.Parameters.AddWithValue("$status", r.Status);
            cmd.Parameters.AddWithValue(
                "$error_message",
                ToDatabaseValue(SensitiveDataSanitizer.Sanitize(r.ErrorMessage)));
            cmd.Parameters.AddWithValue("$temp_directory", r.TempDirectory);
            cmd.Parameters.AddWithValue("$video_bytes", r.VideoBytesDownloaded);
            cmd.Parameters.AddWithValue("$audio_bytes", r.AudioBytesDownloaded);
            cmd.Parameters.AddWithValue("$video_progress", r.VideoProgress);
            cmd.Parameters.AddWithValue("$audio_progress", r.AudioProgress);
            cmd.Parameters.AddWithValue("$merge_progress", r.MergeProgress);
            cmd.Parameters.AddWithValue("$speed_text", r.SpeedText);
            cmd.Parameters.AddWithValue("$bytes_per_second", r.BytesPerSecond);
            cmd.Parameters.AddWithValue("$created_at", r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$media_type", r.MediaType);
            cmd.Parameters.AddWithValue("$ep_id", r.EpId);
            cmd.Parameters.AddWithValue("$season_id", r.SeasonId);
            cmd.Parameters.AddWithValue("$extras_config", r.ExtrasConfig);
            cmd.Parameters.AddWithValue(
                "$cover_url",
                SensitiveDataSanitizer.SanitizeUrlForStorage(r.CoverUrl));
            cmd.Parameters.AddWithValue(
                "$extras_result_summary",
                ToDatabaseValue(SensitiveDataSanitizer.Sanitize(r.ExtrasResultSummary)));
            cmd.Parameters.AddWithValue("$expected_video_bytes", r.ExpectedVideoBytes);
            cmd.Parameters.AddWithValue("$expected_audio_bytes", r.ExpectedAudioBytes);
            cmd.Parameters.AddWithValue("$video_integrity_passed", r.VideoIntegrityPassed ? 1 : 0);
            cmd.Parameters.AddWithValue("$audio_integrity_passed", r.AudioIntegrityPassed ? 1 : 0);
            cmd.Parameters.AddWithValue("$output_file_path", r.OutputFilePath);
            cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(r.LastUpdatedAt));
            cmd.Parameters.AddWithValue("$error_type", ToDatabaseValue(r.ErrorType));
            cmd.Parameters.AddWithValue("$is_retryable", r.IsRetryable ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// 更新任务进度和状态
    /// </summary>
    public async Task UpdateProgressAsync(string taskId, double progress, string status, string? errorMessage = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET progress = $progress, status = $status, error_message = $error_message,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$progress", progress);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue(
            "$error_message",
            ToDatabaseValue(SensitiveDataSanitizer.Sanitize(errorMessage)));
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(DateTime.Now));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 更新断点续传字节数
    /// </summary>
    public async Task UpdateBytesAsync(string taskId, long videoBytes, long audioBytes)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET video_bytes = $video_bytes, audio_bytes = $audio_bytes,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$video_bytes", videoBytes);
        cmd.Parameters.AddWithValue("$audio_bytes", audioBytes);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(DateTime.Now));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateRuntimeSnapshotAsync(TaskRuntimeSnapshot snapshot)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET progress = $progress,
                status = $status,
                video_progress = $video_progress,
                audio_progress = $audio_progress,
                merge_progress = $merge_progress,
                speed_text = $speed_text,
                bytes_per_second = $bytes_per_second,
                video_bytes = $video_bytes,
                audio_bytes = $audio_bytes,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", snapshot.TaskId);
        cmd.Parameters.AddWithValue("$progress", snapshot.Progress);
        cmd.Parameters.AddWithValue("$status", snapshot.Status);
        cmd.Parameters.AddWithValue("$video_progress", snapshot.VideoProgress);
        cmd.Parameters.AddWithValue("$audio_progress", snapshot.AudioProgress);
        cmd.Parameters.AddWithValue("$merge_progress", snapshot.MergeProgress);
        cmd.Parameters.AddWithValue("$speed_text", snapshot.SpeedText);
        cmd.Parameters.AddWithValue("$bytes_per_second", snapshot.BytesPerSecond);
        cmd.Parameters.AddWithValue("$video_bytes", snapshot.VideoBytes);
        cmd.Parameters.AddWithValue("$audio_bytes", snapshot.AudioBytes);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(snapshot.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateIntegrityAsync(
        string taskId,
        long expectedVideoBytes,
        long expectedAudioBytes,
        bool videoIntegrityPassed,
        bool audioIntegrityPassed,
        DateTime lastUpdatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET expected_video_bytes = $expected_video_bytes,
                expected_audio_bytes = $expected_audio_bytes,
                video_integrity_passed = $video_integrity_passed,
                audio_integrity_passed = $audio_integrity_passed,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$expected_video_bytes", expectedVideoBytes);
        cmd.Parameters.AddWithValue("$expected_audio_bytes", expectedAudioBytes);
        cmd.Parameters.AddWithValue("$video_integrity_passed", videoIntegrityPassed ? 1 : 0);
        cmd.Parameters.AddWithValue("$audio_integrity_passed", audioIntegrityPassed ? 1 : 0);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(lastUpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkCompletedAsync(
        string taskId,
        string outputFilePath,
        string? extrasResultSummary,
        DateTime lastUpdatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET progress = 100,
                status = $status,
                video_progress = 100,
                audio_progress = 100,
                merge_progress = 100,
                speed_text = '',
                output_file_path = $output_file_path,
                extras_result_summary = $extras_result_summary,
                error_message = NULL,
                error_type = NULL,
                is_retryable = 0,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue(
            "$status",
            DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Completed));
        cmd.Parameters.AddWithValue("$output_file_path", outputFilePath);
        cmd.Parameters.AddWithValue(
            "$extras_result_summary",
            ToDatabaseValue(SensitiveDataSanitizer.Sanitize(extrasResultSummary)));
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(lastUpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkFailedAsync(
        string taskId,
        double progress,
        string? errorMessage,
        string? errorType,
        bool isRetryable,
        DateTime lastUpdatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET progress = $progress,
                status = $status,
                error_message = $error_message,
                error_type = $error_type,
                is_retryable = $is_retryable,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$progress", progress);
        cmd.Parameters.AddWithValue(
            "$status",
            DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Failed));
        cmd.Parameters.AddWithValue(
            "$error_message",
            ToDatabaseValue(SensitiveDataSanitizer.Sanitize(errorMessage)));
        cmd.Parameters.AddWithValue("$error_type", ToDatabaseValue(errorType));
        cmd.Parameters.AddWithValue("$is_retryable", isRetryable ? 1 : 0);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(lastUpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 更新临时目录路径
    /// </summary>
    public async Task UpdateTempDirectoryAsync(string taskId, string tempDirectory)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET temp_directory = $temp_directory,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$temp_directory", tempDirectory);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(DateTime.Now));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 按 Document ID 查询任务列表
    /// </summary>
    public async Task<List<DownloadTaskRecord>> GetByDocumentIdAsync(string documentId)
    {
        var records = new List<DownloadTaskRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM download_tasks WHERE document_id = $document_id ORDER BY created_at;";
        cmd.Parameters.AddWithValue("$document_id", documentId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadRecord(reader));
        }
        return records;
    }

    /// <summary>
    /// 查询所有未完成的任务（pending 或 downloading 状态），用于重启恢复
    /// </summary>
    public async Task<List<DownloadTaskRecord>> GetIncompleteAsync()
    {
        var records = new List<DownloadTaskRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT * FROM download_tasks
            WHERE status IN ({string.Join(", ",
                new[] { DownloadTaskStatus.Ready, DownloadTaskStatus.DownloadingVideo,
                        DownloadTaskStatus.DownloadingAudio, DownloadTaskStatus.Merging,
                        DownloadTaskStatus.FetchingMetadata }
                    .Select(s => $"'{DownloadTaskStatusMapper.ToStorageString(s)}'"))})
            ORDER BY created_at;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadRecord(reader));
        }
        return records;
    }

    /// <summary>
    /// 查询所有任务
    /// </summary>
    public async Task<List<DownloadTaskRecord>> GetAllAsync()
    {
        var records = new List<DownloadTaskRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM download_tasks ORDER BY created_at;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadRecord(reader));
        }
        return records;
    }

    /// <summary>
    /// 删除已完成的任务（可选清理）
    /// </summary>
    public async Task DeleteDoneAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM download_tasks WHERE status = '{DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Completed)}';";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 更新附加资源执行结果
    /// </summary>
    public async Task UpdateExtrasResultAsync(string taskId, string? extrasResultSummary)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET extras_result_summary = $extras_result_summary,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue(
            "$extras_result_summary",
            ToDatabaseValue(SensitiveDataSanitizer.Sanitize(extrasResultSummary)));
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(DateTime.Now));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 按 task_id 删除单条记录
    /// </summary>
    public async Task DeleteByIdAsync(string taskId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM download_tasks WHERE task_id = $task_id;";
        cmd.Parameters.AddWithValue("$task_id", taskId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 按 task_id 列表批量删除记录
    /// </summary>
    public async Task DeleteByIdsAsync(IEnumerable<string> taskIds)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        foreach (var taskId in taskIds)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM download_tasks WHERE task_id = $task_id;";
            cmd.Parameters.AddWithValue("$task_id", taskId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 更新任务分段进度和速度
    /// </summary>
    public async Task UpdateStageProgressAsync(
        string taskId,
        double progress,
        string status,
        double videoProgress,
        double audioProgress,
        double mergeProgress,
        string speedText)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET progress = $progress, status = $status,
                video_progress = $video_progress, audio_progress = $audio_progress,
                merge_progress = $merge_progress, speed_text = $speed_text,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$progress", progress);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$video_progress", videoProgress);
        cmd.Parameters.AddWithValue("$audio_progress", audioProgress);
        cmd.Parameters.AddWithValue("$merge_progress", mergeProgress);
        cmd.Parameters.AddWithValue("$speed_text", speedText);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(DateTime.Now));
        await cmd.ExecuteNonQueryAsync();
    }

    private static DownloadTaskRecord ReadRecord(SqliteDataReader reader)
    {
        return new DownloadTaskRecord
        {
            TaskId = reader.GetString(reader.GetOrdinal("task_id")),
            DocumentId = reader.GetString(reader.GetOrdinal("document_id")),
            SourceDocumentTitle = TryGetString(reader, "source_document_title"),
            SeriesTitle = reader.GetString(reader.GetOrdinal("series_title")),
            ItemTitle = reader.GetString(reader.GetOrdinal("item_title")),
            Aid = reader.GetInt64(reader.GetOrdinal("aid")),
            Bvid = reader.GetString(reader.GetOrdinal("bvid")),
            Cid = reader.GetInt64(reader.GetOrdinal("cid")),
            QualityId = reader.GetInt32(reader.GetOrdinal("quality_id")),
            AudioQualityId = TryGetInt(reader, "audio_quality_id"),
            OutputDirectory = reader.GetString(reader.GetOrdinal("output_directory")),
            SubFolder = TryGetString(reader, "sub_folder"),
            Progress = reader.GetDouble(reader.GetOrdinal("progress")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message"))
                ? null
                : reader.GetString(reader.GetOrdinal("error_message")),
            TempDirectory = reader.GetString(reader.GetOrdinal("temp_directory")),
            VideoBytesDownloaded = reader.GetInt64(reader.GetOrdinal("video_bytes")),
            AudioBytesDownloaded = reader.GetInt64(reader.GetOrdinal("audio_bytes")),
            VideoProgress = TryGetDouble(reader, "video_progress"),
            AudioProgress = TryGetDouble(reader, "audio_progress"),
            MergeProgress = TryGetDouble(reader, "merge_progress"),
            SpeedText = TryGetString(reader, "speed_text"),
            BytesPerSecond = TryGetLong(reader, "bytes_per_second"),
            CreatedAt = DateTime.TryParse(
                reader.GetString(reader.GetOrdinal("created_at")),
                out var dt) ? dt : DateTime.Now,
            // 架构改进：新字段
            ExpectedVideoBytes = TryGetLong(reader, "expected_video_bytes"),
            ExpectedAudioBytes = TryGetLong(reader, "expected_audio_bytes"),
            VideoIntegrityPassed = TryGetBool(reader, "video_integrity_passed"),
            AudioIntegrityPassed = TryGetBool(reader, "audio_integrity_passed"),
            OutputFilePath = TryGetString(reader, "output_file_path"),
            LastUpdatedAt = TryGetDateTime(reader, "last_updated_at"),
            ErrorType = TryGetNullableString(reader, "error_type"),
            IsRetryable = TryGetBool(reader, "is_retryable"),
            MediaType = TryGetString(reader, "media_type"),
            EpId = TryGetLong(reader, "ep_id"),
            SeasonId = TryGetLong(reader, "season_id"),
            ExtrasConfig = TryGetInt(reader, "extras_config"),
            CoverUrl = TryGetString(reader, "cover_url"),
            ExtrasResultSummary = TryGetNullableString(reader, "extras_result_summary"),
        };
    }

    private static object ToDatabaseValue(string? value)
        => string.IsNullOrEmpty(value) ? DBNull.Value : value;

    private static string ToStorageTime(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.fffffff");

    private static double TryGetDouble(SqliteDataReader reader, string column)
    {
        try { return reader.GetDouble(reader.GetOrdinal(column)); }
        catch { return 0; }
    }

    private static int TryGetInt(SqliteDataReader reader, string column)
    {
        try { return reader.GetInt32(reader.GetOrdinal(column)); }
        catch { return 0; }
    }

    private static string TryGetString(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        }
        catch { return ""; }
    }

    private static string? TryGetNullableString(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
        catch { return null; }
    }

    private static long TryGetLong(SqliteDataReader reader, string column)
    {
        try { return reader.GetInt64(reader.GetOrdinal(column)); }
        catch { return 0; }
    }

    private static bool TryGetBool(SqliteDataReader reader, string column)
    {
        try { return reader.GetInt32(reader.GetOrdinal(column)) != 0; }
        catch { return false; }
    }

    private static DateTime TryGetDateTime(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? DateTime.MinValue
                : DateTime.TryParse(reader.GetString(ordinal), out var dt) ? dt : DateTime.MinValue;
        }
        catch { return DateTime.MinValue; }
    }
}
