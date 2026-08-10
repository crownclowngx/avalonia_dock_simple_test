using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BiliDownloader.Services.Persistence;

/// <summary>
/// 下载任务 SQLite 持久化存储。路径由 <see cref="IBiliDataPaths"/> 统一决定。
/// </summary>
public class DownloadTaskStore : IDownloadTaskRepository, ITaskHistoryReadRepository
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
                media_unit_key      TEXT NOT NULL DEFAULT '',
                rendition_fingerprint TEXT NOT NULL DEFAULT '',
                quality_id          INTEGER NOT NULL DEFAULT 80,
                audio_quality_id    INTEGER NOT NULL DEFAULT 0,
                submission_snapshot_version INTEGER NOT NULL DEFAULT 0,
                task_rate_limit_bytes_per_second INTEGER NOT NULL DEFAULT 0,
                duration_seconds    INTEGER NOT NULL DEFAULT 0,
                use_group_folder    INTEGER NOT NULL DEFAULT 0,
                add_index_to_title  INTEGER NOT NULL DEFAULT 0,
                naming_template     TEXT NOT NULL DEFAULT '',
                preset_id           TEXT,
                selected_video_codec TEXT NOT NULL DEFAULT '',
                actual_video_codec  TEXT NOT NULL DEFAULT '',
                output_container    TEXT NOT NULL DEFAULT '',
                output_media_mode   TEXT NOT NULL DEFAULT '',
                video_dynamic_range_preference TEXT NOT NULL DEFAULT '',
                audio_feature_preference TEXT NOT NULL DEFAULT '',
                requested_media_features TEXT NOT NULL DEFAULT '',
                expected_media_features TEXT NOT NULL DEFAULT '',
                actual_media_features TEXT NOT NULL DEFAULT '',
                redownloaded_from_task_id TEXT NOT NULL DEFAULT '',
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
                output_path_key     TEXT NOT NULL DEFAULT '',
                conflict_policy     TEXT NOT NULL DEFAULT 'AutoNumber',
                estimated_required_bytes INTEGER NOT NULL DEFAULT 0,
                overwrite_confirmed INTEGER NOT NULL DEFAULT 0,
                last_updated_at     TEXT NOT NULL DEFAULT '',
                error_type          TEXT,
                is_retryable        INTEGER NOT NULL DEFAULT 0,
                media_type          TEXT NOT NULL DEFAULT 'video',
                ep_id               INTEGER NOT NULL DEFAULT 0,
                season_id           INTEGER NOT NULL DEFAULT 0,
                extras_config       INTEGER NOT NULL DEFAULT 0,
                cover_url           TEXT NOT NULL DEFAULT '',
                extras_result_summary TEXT,
                subtitle_options_json TEXT NOT NULL DEFAULT '',
                danmaku_options_json TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS output_path_reservations (
                output_path_key TEXT NOT NULL PRIMARY KEY,
                task_id         TEXT NOT NULL UNIQUE,
                reserved_at     TEXT NOT NULL
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
            "ALTER TABLE download_tasks ADD COLUMN output_path_key TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN conflict_policy TEXT NOT NULL DEFAULT 'AutoNumber';",
            "ALTER TABLE download_tasks ADD COLUMN estimated_required_bytes INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN overwrite_confirmed INTEGER NOT NULL DEFAULT 0;",
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
            // P1-G9：结构化附加资源意图。空字符串表示旧任务未知，不能冒充新快照。
            "ALTER TABLE download_tasks ADD COLUMN subtitle_options_json TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN danmaku_options_json TEXT NOT NULL DEFAULT '';",
            // P1-G5：身份列只使用常量空默认值，避免旧数据在无法确认编码/容器时被伪装为完整指纹。
            "ALTER TABLE download_tasks ADD COLUMN media_unit_key TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN rendition_fingerprint TEXT NOT NULL DEFAULT '';",
            // P1-G6：旧任务保持 snapshot_version=0 和未知枚举，不把兼容默认值伪装成历史事实。
            "ALTER TABLE download_tasks ADD COLUMN submission_snapshot_version INTEGER NOT NULL DEFAULT 0;",
            // P1-G10：0 表示不限速；旧库迁移后保持原有行为，不凭空施加带宽限制。
            "ALTER TABLE download_tasks ADD COLUMN task_rate_limit_bytes_per_second INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN duration_seconds INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN use_group_folder INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN add_index_to_title INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE download_tasks ADD COLUMN naming_template TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN preset_id TEXT;",
            "ALTER TABLE download_tasks ADD COLUMN selected_video_codec TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN actual_video_codec TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN output_container TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN output_media_mode TEXT NOT NULL DEFAULT '';",
            // P1-G8：空字符串专门表示旧任务“未知”；新任务的标准规格显式写入 None。
            "ALTER TABLE download_tasks ADD COLUMN video_dynamic_range_preference TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN audio_feature_preference TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN requested_media_features TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN expected_media_features TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN actual_media_features TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE download_tasks ADD COLUMN redownloaded_from_task_id TEXT NOT NULL DEFAULT '';",
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

        await using var identityIndex = connection.CreateCommand();
        identityIndex.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_download_tasks_media_unit_key
                ON download_tasks(media_unit_key);
            CREATE INDEX IF NOT EXISTS ix_download_tasks_rendition_fingerprint
                ON download_tasks(rendition_fingerprint);
            CREATE INDEX IF NOT EXISTS ix_download_tasks_history_status_created
                ON download_tasks(status, created_at DESC, task_id DESC);
            CREATE INDEX IF NOT EXISTS ix_download_tasks_history_document
                ON download_tasks(document_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_download_tasks_history_output
                ON download_tasks(selected_video_codec, output_container, output_media_mode);
            """;
        await identityIndex.ExecuteNonQueryAsync();
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
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = """
                INSERT INTO download_tasks
                    (task_id, document_id, source_document_title, series_title, item_title, aid, bvid, cid,
                     media_unit_key, rendition_fingerprint,
                     quality_id, audio_quality_id,
                     submission_snapshot_version, task_rate_limit_bytes_per_second,
                     duration_seconds, use_group_folder, add_index_to_title,
                     naming_template, preset_id, selected_video_codec, actual_video_codec,
                     output_container, output_media_mode,
                     video_dynamic_range_preference, audio_feature_preference,
                     requested_media_features, expected_media_features, actual_media_features,
                     redownloaded_from_task_id,
                     output_directory, sub_folder,
                     progress, status, error_message,
                     temp_directory, video_bytes, audio_bytes,
                     video_progress, audio_progress, merge_progress, speed_text, bytes_per_second,
                     created_at, media_type, ep_id, season_id,
                     extras_config, cover_url, extras_result_summary, subtitle_options_json, danmaku_options_json,
                     expected_video_bytes, expected_audio_bytes,
                     video_integrity_passed, audio_integrity_passed,
                     output_file_path, output_path_key, conflict_policy, estimated_required_bytes, overwrite_confirmed,
                     last_updated_at, error_type, is_retryable)
                VALUES
                    ($task_id, $document_id, $source_document_title, $series_title, $item_title, $aid, $bvid, $cid,
                     $media_unit_key, $rendition_fingerprint,
                     $quality_id, $audio_quality_id,
                     $submission_snapshot_version, $task_rate_limit_bytes_per_second,
                     $duration_seconds, $use_group_folder, $add_index_to_title,
                     $naming_template, $preset_id, $selected_video_codec, $actual_video_codec,
                     $output_container, $output_media_mode,
                     $video_dynamic_range_preference, $audio_feature_preference,
                     $requested_media_features, $expected_media_features, $actual_media_features,
                     $redownloaded_from_task_id,
                     $output_directory, $sub_folder,
                     $progress, $status, $error_message,
                     $temp_directory, $video_bytes, $audio_bytes,
                     $video_progress, $audio_progress, $merge_progress, $speed_text, $bytes_per_second,
                     $created_at, $media_type, $ep_id, $season_id,
                     $extras_config, $cover_url, $extras_result_summary, $subtitle_options_json, $danmaku_options_json,
                     $expected_video_bytes, $expected_audio_bytes,
                     $video_integrity_passed, $audio_integrity_passed,
                     $output_file_path, $output_path_key, $conflict_policy, $estimated_required_bytes, $overwrite_confirmed,
                     $last_updated_at, $error_type, $is_retryable);
                """;
            cmd.Parameters.AddWithValue("$task_id", r.TaskId);
            cmd.Parameters.AddWithValue("$document_id", r.DocumentId);
            cmd.Parameters.AddWithValue("$source_document_title", r.SourceDocumentTitle);
            cmd.Parameters.AddWithValue("$series_title", r.SeriesTitle);
            cmd.Parameters.AddWithValue("$item_title", r.ItemTitle);
            cmd.Parameters.AddWithValue("$aid", r.Aid);
            cmd.Parameters.AddWithValue("$bvid", r.Bvid);
            cmd.Parameters.AddWithValue("$cid", r.Cid);
            cmd.Parameters.AddWithValue("$media_unit_key", r.MediaUnitKey);
            cmd.Parameters.AddWithValue("$rendition_fingerprint", r.RenditionFingerprint);
            cmd.Parameters.AddWithValue("$quality_id", r.QualityId);
            cmd.Parameters.AddWithValue("$audio_quality_id", r.AudioQualityId);
            cmd.Parameters.AddWithValue("$submission_snapshot_version", r.SubmissionSnapshotVersion);
            cmd.Parameters.AddWithValue("$task_rate_limit_bytes_per_second", r.TaskRateLimitBytesPerSecond);
            cmd.Parameters.AddWithValue("$duration_seconds", r.DurationSeconds);
            cmd.Parameters.AddWithValue("$use_group_folder", r.UseGroupFolder ? 1 : 0);
            cmd.Parameters.AddWithValue("$add_index_to_title", r.AddIndexToTitle ? 1 : 0);
            cmd.Parameters.AddWithValue("$naming_template", r.NamingTemplate);
            cmd.Parameters.AddWithValue("$preset_id", ToDatabaseValue(r.PresetId));
            cmd.Parameters.AddWithValue("$selected_video_codec", r.SelectedVideoCodec?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$actual_video_codec", r.ActualVideoCodec);
            cmd.Parameters.AddWithValue("$output_container", r.SelectedOutputContainer?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$output_media_mode", r.SelectedOutputMediaMode?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$video_dynamic_range_preference", r.SelectedVideoDynamicRangePreference?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$audio_feature_preference", r.SelectedAudioFeaturePreference?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$requested_media_features", r.RequestedMediaFeatures?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$expected_media_features", r.ExpectedMediaFeatures?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$actual_media_features", r.ActualMediaFeatures?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$redownloaded_from_task_id", r.RedownloadedFromTaskId);
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
            cmd.Parameters.AddWithValue("$subtitle_options_json", SerializeOptions(r.SubtitleOptions.Canonicalize()));
            cmd.Parameters.AddWithValue("$danmaku_options_json", SerializeOptions(r.DanmakuOptions.Canonicalize()));
            cmd.Parameters.AddWithValue("$expected_video_bytes", r.ExpectedVideoBytes);
            cmd.Parameters.AddWithValue("$expected_audio_bytes", r.ExpectedAudioBytes);
            cmd.Parameters.AddWithValue("$video_integrity_passed", r.VideoIntegrityPassed ? 1 : 0);
            cmd.Parameters.AddWithValue("$audio_integrity_passed", r.AudioIntegrityPassed ? 1 : 0);
            cmd.Parameters.AddWithValue("$output_file_path", r.OutputFilePath);
            cmd.Parameters.AddWithValue("$output_path_key", r.OutputPathKey);
            cmd.Parameters.AddWithValue("$conflict_policy", r.ConflictPolicy.ToString());
            cmd.Parameters.AddWithValue("$estimated_required_bytes", r.EstimatedRequiredBytes);
            cmd.Parameters.AddWithValue("$overwrite_confirmed", r.OverwriteConfirmed ? 1 : 0);
            cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(r.LastUpdatedAt));
            cmd.Parameters.AddWithValue("$error_type", ToDatabaseValue(r.ErrorType));
            cmd.Parameters.AddWithValue("$is_retryable", r.IsRetryable ? 1 : 0);

            if (!string.IsNullOrWhiteSpace(r.OutputPathKey))
            {
                await using var reserve = connection.CreateCommand();
                reserve.Transaction = (SqliteTransaction)transaction;
                reserve.CommandText = """
                    INSERT INTO output_path_reservations(output_path_key, task_id, reserved_at)
                    VALUES($output_path_key, $task_id, $reserved_at);
                    """;
                reserve.Parameters.AddWithValue("$output_path_key", r.OutputPathKey);
                reserve.Parameters.AddWithValue("$task_id", r.TaskId);
                reserve.Parameters.AddWithValue("$reserved_at", ToStorageTime(DateTime.Now));
                await reserve.ExecuteNonQueryAsync();
            }
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
        if (status is "done" or "canceled")
            await ReleaseReservationAsync(connection, taskId);
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

    /// <inheritdoc />
    public async Task UpdateTaskRateLimitAsync(string taskId, long bytesPerSecond, DateTime lastUpdatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET task_rate_limit_bytes_per_second = $bytes_per_second,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id AND status <> 'done';
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$bytes_per_second", bytesPerSecond);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(lastUpdatedAt));
        if (await cmd.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"任务 {taskId} 不存在或已完成，不能修改限速历史快照。");
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

    public async Task UpdateActualVideoCodecAsync(
        string taskId,
        string actualVideoCodec,
        DateTime lastUpdatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET actual_video_codec = $actual_video_codec,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$actual_video_codec", actualVideoCodec);
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(lastUpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task UpdateExpectedMediaFeaturesAsync(
        string taskId,
        MediaFeatureFlags expectedFeatures,
        DateTime lastUpdatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET expected_media_features = $expected_media_features,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$expected_media_features", expectedFeatures.ToString());
        cmd.Parameters.AddWithValue("$last_updated_at", ToStorageTime(lastUpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task UpdateActualMediaFeaturesAsync(
        string taskId,
        MediaFeatureFlags actualFeatures,
        DateTime lastUpdatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE download_tasks
            SET actual_media_features = $actual_media_features,
                last_updated_at = $last_updated_at
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$actual_media_features", actualFeatures.ToString());
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
                video_progress = CASE WHEN output_media_mode = 'AudioOnly' THEN 0 ELSE 100 END,
                audio_progress = CASE WHEN output_media_mode = 'VideoOnly' THEN 0 ELSE 100 END,
                merge_progress = CASE WHEN output_media_mode = 'AudioOnly' THEN 0 ELSE 100 END,
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
        await ReleaseReservationAsync(connection, taskId);
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

    /// <inheritdoc />
    public async Task<TaskHistoryPage> QueryHistoryPageAsync(
        TaskHistoryQuery query,
        TaskHistoryPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(request), "历史分页大小必须在 1～500 之间。");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = BuildHistoryWhere(command, query);
        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            var (createdAt, taskId) = DecodeHistoryCursor(request.Cursor);
            where.Add("(created_at < $cursor_created OR (created_at = $cursor_created AND task_id < $cursor_task))");
            command.Parameters.AddWithValue("$cursor_created", createdAt);
            command.Parameters.AddWithValue("$cursor_task", taskId);
        }

        command.CommandText = $"""
            SELECT * FROM download_tasks
            WHERE {string.Join(" AND ", where)}
            ORDER BY created_at DESC, task_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", request.PageSize + 1);

        var rows = new List<(TaskHistoryEntry Entry, string RawCreatedAt)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rawCreatedAt = reader.GetString(reader.GetOrdinal("created_at"));
            rows.Add((TaskHistoryEntry.FromRecord(ReadRecord(reader)), rawCreatedAt));
        }

        var hasMore = rows.Count > request.PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var nextCursor = hasMore && rows.Count > 0
            ? EncodeHistoryCursor(rows[^1].RawCreatedAt, rows[^1].Entry.TaskId)
            : null;
        return new TaskHistoryPage(rows.Select(static row => row.Entry).ToArray(), nextCursor, hasMore);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TaskHistoryEntry> StreamHistoryAsync(
        TaskHistoryQuery query,
        IReadOnlyCollection<string>? taskIds = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var idSet = taskIds is { Count: > 0 }
            ? taskIds.ToHashSet(StringComparer.Ordinal)
            : null;
        if (taskIds is { Count: 0 }) yield break;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // 导出期间保持一个 WAL 只读快照。这样任务状态即使在后台变化，导出文件内部仍然自洽，
        // 同时不会阻塞 Coordinator 的正常写入。
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        var where = BuildHistoryWhere(command, query);
        command.CommandText = $"""
            SELECT * FROM download_tasks
            WHERE {string.Join(" AND ", where)}
            ORDER BY created_at DESC, task_id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = TaskHistoryEntry.FromRecord(ReadRecord(reader));
            if (idSet is null || idSet.Contains(entry.TaskId)) yield return entry;
        }
    }

    /// <inheritdoc />
    public async Task<DownloadTaskRecord?> GetTaskByIdAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return null;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM download_tasks WHERE task_id = $task_id LIMIT 1;";
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskHistoryDocumentOption>> GetHistoryDocumentOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_id, MAX(source_document_title) AS source_document_title
            FROM download_tasks
            WHERE status IN ('done', 'failed', 'canceled') AND document_id <> ''
            GROUP BY document_id
            ORDER BY source_document_title COLLATE NOCASE, document_id;
            """;
        var result = new List<TaskHistoryDocumentOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            result.Add(new TaskHistoryDocumentOption(
                id,
                string.IsNullOrWhiteSpace(title) ? $"工作台 {id[..Math.Min(8, id.Length)]}" : title));
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<List<DownloadTaskRecord>> GetByIdentityAsync(
        IReadOnlyCollection<MediaUnitKey> mediaUnitKeys,
        IReadOnlyCollection<string> renditionFingerprints,
        CancellationToken cancellationToken = default)
    {
        if (mediaUnitKeys.Count == 0 && renditionFingerprints.Count == 0) return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var records = new Dictionary<string, DownloadTaskRecord>(StringComparer.Ordinal);
        var media = mediaUnitKeys.Distinct().ToArray();
        var fingerprints = renditionFingerprints
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // SQLite 默认参数上限有限。分块查询而不是截断输入，确保大型来源不会漏掉第 151 个媒体单元，
        // 同时每个媒体单元兼容匹配新列和旧 Aid/Cid 行。
        foreach (var mediaChunk in media.Chunk(150))
        {
            await using var cmd = connection.CreateCommand();
            var predicates = new List<string>(mediaChunk.Length);
            for (var index = 0; index < mediaChunk.Length; index++)
            {
                predicates.Add($"(media_unit_key = $mk{index} OR (aid = $aid{index} AND cid = $cid{index}))");
                cmd.Parameters.AddWithValue($"$mk{index}", mediaChunk[index].ToStorageKey());
                cmd.Parameters.AddWithValue($"$aid{index}", mediaChunk[index].Aid);
                cmd.Parameters.AddWithValue($"$cid{index}", mediaChunk[index].Cid);
            }
            cmd.CommandText = $"SELECT * FROM download_tasks WHERE {string.Join(" OR ", predicates)} ORDER BY created_at;";
            await ReadIntoAsync(cmd);
        }

        foreach (var fingerprintChunk in fingerprints.Chunk(400))
        {
            await using var cmd = connection.CreateCommand();
            var names = new List<string>(fingerprintChunk.Length);
            for (var index = 0; index < fingerprintChunk.Length; index++)
            {
                names.Add($"$rf{index}");
                cmd.Parameters.AddWithValue($"$rf{index}", fingerprintChunk[index]);
            }
            cmd.CommandText = $"SELECT * FROM download_tasks WHERE rendition_fingerprint IN ({string.Join(',', names)}) ORDER BY created_at;";
            await ReadIntoAsync(cmd);
        }

        return records.Values.OrderBy(record => record.CreatedAt).ToList();

        async Task ReadIntoAsync(SqliteCommand command)
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var record = ReadRecord(reader);
                records[record.TaskId] = record;
            }
        }
    }

    /// <summary>
    /// 删除已完成的任务（可选清理）
    /// </summary>
    public async Task DeleteDoneAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM output_path_reservations
            WHERE task_id IN (SELECT task_id FROM download_tasks WHERE status = '{DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Completed)}');
            DELETE FROM download_tasks WHERE status = '{DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Completed)}';
            """;
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

    public async Task PrepareVerifiedResumeAsync(
        string taskId,
        string outputFilePath,
        string outputPathKey,
        FileConflictPolicy conflictPolicy,
        long estimatedRequiredBytes)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var release = connection.CreateCommand())
        {
            release.Transaction = (SqliteTransaction)transaction;
            release.CommandText = "DELETE FROM output_path_reservations WHERE task_id = $task_id;";
            release.Parameters.AddWithValue("$task_id", taskId);
            await release.ExecuteNonQueryAsync();
        }
        await using (var reserve = connection.CreateCommand())
        {
            reserve.Transaction = (SqliteTransaction)transaction;
            reserve.CommandText = """
                INSERT INTO output_path_reservations(output_path_key, task_id, reserved_at)
                VALUES($output_path_key, $task_id, $reserved_at);
                """;
            reserve.Parameters.AddWithValue("$output_path_key", outputPathKey);
            reserve.Parameters.AddWithValue("$task_id", taskId);
            reserve.Parameters.AddWithValue("$reserved_at", ToStorageTime(DateTime.Now));
            await reserve.ExecuteNonQueryAsync();
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE download_tasks
                SET output_file_path = $output_file_path,
                    output_path_key = $output_path_key,
                    conflict_policy = $conflict_policy,
                    estimated_required_bytes = $estimated_required_bytes,
                    overwrite_confirmed = 0,
                    status = $status,
                    error_message = NULL,
                    error_type = NULL,
                    last_updated_at = $last_updated_at
                WHERE task_id = $task_id;
                """;
            update.Parameters.AddWithValue("$task_id", taskId);
            update.Parameters.AddWithValue("$output_file_path", outputFilePath);
            update.Parameters.AddWithValue("$output_path_key", outputPathKey);
            update.Parameters.AddWithValue("$conflict_policy", conflictPolicy.ToString());
            update.Parameters.AddWithValue("$estimated_required_bytes", estimatedRequiredBytes);
            update.Parameters.AddWithValue("$status", DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Ready));
            update.Parameters.AddWithValue("$last_updated_at", ToStorageTime(DateTime.Now));
            if (await update.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("待续传任务不存在，无法建立输出路径保留。");
        }
        await transaction.CommitAsync();
    }

    public async Task<bool> OwnsOutputPathReservationAsync(string taskId, string outputPathKey)
    {
        if (string.IsNullOrWhiteSpace(outputPathKey)) return false;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM output_path_reservations
            WHERE task_id = $task_id AND output_path_key = $output_path_key;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$output_path_key", outputPathKey);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    public async Task RelocateOutputAsync(
        string taskId,
        string outputDirectory,
        string outputFilePath,
        string outputPathKey)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // 先删除旧保留、再插入新保留都位于同一事务；新路径冲突时事务回滚，旧保留仍然存在。
        await using (var release = connection.CreateCommand())
        {
            release.Transaction = (SqliteTransaction)transaction;
            release.CommandText = "DELETE FROM output_path_reservations WHERE task_id = $task_id;";
            release.Parameters.AddWithValue("$task_id", taskId);
            await release.ExecuteNonQueryAsync();
        }
        await using (var reserve = connection.CreateCommand())
        {
            reserve.Transaction = (SqliteTransaction)transaction;
            reserve.CommandText = """
                INSERT INTO output_path_reservations(output_path_key, task_id, reserved_at)
                VALUES($output_path_key, $task_id, $reserved_at);
                """;
            reserve.Parameters.AddWithValue("$output_path_key", outputPathKey);
            reserve.Parameters.AddWithValue("$task_id", taskId);
            reserve.Parameters.AddWithValue("$reserved_at", ToStorageTime(DateTime.Now));
            await reserve.ExecuteNonQueryAsync();
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE download_tasks
                SET output_directory = $output_directory,
                    sub_folder = '',
                    output_file_path = $output_file_path,
                    output_path_key = $output_path_key,
                    conflict_policy = $conflict_policy,
                    overwrite_confirmed = 0,
                    status = $status,
                    error_message = NULL,
                    error_type = NULL,
                    is_retryable = 0,
                    last_updated_at = $last_updated_at
                WHERE task_id = $task_id;
                """;
            update.Parameters.AddWithValue("$task_id", taskId);
            update.Parameters.AddWithValue("$output_directory", outputDirectory);
            update.Parameters.AddWithValue("$output_file_path", outputFilePath);
            update.Parameters.AddWithValue("$output_path_key", outputPathKey);
            update.Parameters.AddWithValue("$conflict_policy", FileConflictPolicy.AutoNumber.ToString());
            update.Parameters.AddWithValue("$status", DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Ready));
            update.Parameters.AddWithValue("$last_updated_at", ToStorageTime(DateTime.Now));
            if (await update.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("待迁移任务不存在。");
        }
        await transaction.CommitAsync();
    }

    /// <summary>
    /// 按 task_id 删除单条记录
    /// </summary>
    public async Task DeleteByIdAsync(string taskId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM output_path_reservations WHERE task_id = $task_id; DELETE FROM download_tasks WHERE task_id = $task_id;";
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
            cmd.CommandText = "DELETE FROM output_path_reservations WHERE task_id = $task_id; DELETE FROM download_tasks WHERE task_id = $task_id;";
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
        var snapshotVersion = TryGetInt(reader, "submission_snapshot_version");
        var extrasConfig = TryGetInt(reader, "extras_config");
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
            MediaUnitKey = GetCompatibleMediaUnitKey(reader),
            RenditionFingerprint = TryGetString(reader, "rendition_fingerprint"),
            QualityId = reader.GetInt32(reader.GetOrdinal("quality_id")),
            AudioQualityId = TryGetInt(reader, "audio_quality_id"),
            SubmissionSnapshotVersion = snapshotVersion,
            TaskRateLimitBytesPerSecond = TryGetLong(reader, "task_rate_limit_bytes_per_second"),
            DurationSeconds = TryGetInt(reader, "duration_seconds"),
            UseGroupFolder = TryGetBool(reader, "use_group_folder"),
            AddIndexToTitle = TryGetBool(reader, "add_index_to_title"),
            NamingTemplate = TryGetString(reader, "naming_template"),
            PresetId = TryGetNullableString(reader, "preset_id"),
            SelectedVideoCodec = TryGetEnum<VideoCodecPreference>(reader, "selected_video_codec"),
            ActualVideoCodec = TryGetString(reader, "actual_video_codec"),
            SelectedOutputContainer = TryGetEnum<OutputContainer>(reader, "output_container"),
            SelectedOutputMediaMode = TryGetEnum<OutputMediaMode>(reader, "output_media_mode"),
            SelectedVideoDynamicRangePreference = TryGetEnum<VideoDynamicRangePreference>(reader, "video_dynamic_range_preference"),
            SelectedAudioFeaturePreference = TryGetEnum<AudioFeaturePreference>(reader, "audio_feature_preference"),
            RequestedMediaFeatures = TryGetEnum<MediaFeatureFlags>(reader, "requested_media_features"),
            ExpectedMediaFeatures = TryGetEnum<MediaFeatureFlags>(reader, "expected_media_features"),
            ActualMediaFeatures = TryGetEnum<MediaFeatureFlags>(reader, "actual_media_features"),
            RedownloadedFromTaskId = TryGetString(reader, "redownloaded_from_task_id"),
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
            OutputPathKey = TryGetString(reader, "output_path_key"),
            ConflictPolicy = TryGetConflictPolicy(reader),
            EstimatedRequiredBytes = TryGetLong(reader, "estimated_required_bytes"),
            OverwriteConfirmed = TryGetBool(reader, "overwrite_confirmed"),
            LastUpdatedAt = TryGetDateTime(reader, "last_updated_at"),
            ErrorType = TryGetNullableString(reader, "error_type"),
            IsRetryable = TryGetBool(reader, "is_retryable"),
            MediaType = TryGetString(reader, "media_type"),
            EpId = TryGetLong(reader, "ep_id"),
            SeasonId = TryGetLong(reader, "season_id"),
            ExtrasConfig = extrasConfig,
            CoverUrl = TryGetString(reader, "cover_url"),
            ExtrasResultSummary = TryGetNullableString(reader, "extras_result_summary"),
            SubtitleOptions = ReadOptions(
                reader, "subtitle_options_json",
                (extrasConfig & 2) != 0 ? SubtitleOptions.LegacyEnabled : SubtitleOptions.None),
            DanmakuOptions = ReadOptions(
                reader, "danmaku_options_json",
                (extrasConfig & 1) != 0 ? DanmakuOptions.LegacyEnabled : DanmakuOptions.None),
        };
    }

    private static string SerializeOptions<T>(T value) => JsonSerializer.Serialize(value);

    /// <summary>
    /// 读取结构化配置；空列和损坏 JSON 只在旧任务上使用布尔兼容值。
    /// 新任务若发生损坏也选择安全兼容值，并由测试/日志诊断，而不让任务库初始化崩溃。
    /// </summary>
    private static T ReadOptions<T>(SqliteDataReader reader, string column, T compatibilityValue)
    {
        var json = TryGetString(reader, column);
        if (string.IsNullOrWhiteSpace(json)) return compatibilityValue;
        try { return JsonSerializer.Deserialize<T>(json) ?? compatibilityValue; }
        catch (JsonException) { return compatibilityValue; }
    }

    private static object ToDatabaseValue(string? value)
        => string.IsNullOrEmpty(value) ? DBNull.Value : value;

    private static string ToStorageTime(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.fffffff");

    /// <summary>
    /// 构造历史查询 WHERE 子句。所有用户输入都使用参数，标题中的 LIKE 元字符先转义，
    /// 因而搜索“100%”或“a_b”不会意外扩大结果集。
    /// </summary>
    private static List<string> BuildHistoryWhere(SqliteCommand command, TaskHistoryQuery query)
    {
        var terminal = new[]
        {
            DownloadTaskStatus.Completed,
            DownloadTaskStatus.Failed,
            DownloadTaskStatus.Canceled,
        };
        var requested = query.Statuses is null
            ? terminal
            : terminal.Where(query.Statuses.Contains).ToArray();
        var where = new List<string>();
        if (requested.Length == 0)
        {
            where.Add("1 = 0");
        }
        else
        {
            var names = new List<string>();
            for (var index = 0; index < requested.Length; index++)
            {
                var name = $"$history_status_{index}";
                names.Add(name);
                command.Parameters.AddWithValue(name, DownloadTaskStatusMapper.ToStorageString(requested[index]));
            }
            where.Add($"status IN ({string.Join(',', names)})");
        }

        if (!string.IsNullOrWhiteSpace(query.Title))
        {
            where.Add("(item_title LIKE $history_title ESCAPE '\\' OR series_title LIKE $history_title ESCAPE '\\')");
            command.Parameters.AddWithValue("$history_title", $"%{EscapeLike(query.Title.Trim())}%");
        }
        if (!string.IsNullOrWhiteSpace(query.DocumentId) && query.DocumentId != "all")
        {
            where.Add("document_id = $history_document");
            command.Parameters.AddWithValue("$history_document", query.DocumentId);
        }
        if (query.CreatedFrom.HasValue)
        {
            where.Add("created_at >= $history_created_from");
            command.Parameters.AddWithValue("$history_created_from", ToStorageTime(query.CreatedFrom.Value));
        }

        AddEnumFilter(command, where, "selected_video_codec", "$history_codec",
            query.SelectedVideoCodec, query.IncludeUnknownVideoCodec);
        AddEnumFilter(command, where, "output_container", "$history_container",
            query.OutputContainer, query.IncludeUnknownOutputContainer);
        AddEnumFilter(command, where, "output_media_mode", "$history_mode",
            query.OutputMediaMode, query.IncludeUnknownOutputMode);
        return where;
    }

    private static void AddEnumFilter<TEnum>(
        SqliteCommand command,
        ICollection<string> where,
        string column,
        string parameterName,
        TEnum? value,
        bool includeUnknown)
        where TEnum : struct, Enum
    {
        if (value.HasValue)
        {
            where.Add($"{column} = {parameterName}");
            command.Parameters.AddWithValue(parameterName, value.Value.ToString());
        }
        else if (includeUnknown)
        {
            where.Add($"{column} = ''");
        }
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static string EncodeHistoryCursor(string rawCreatedAt, string taskId)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCreatedAt + "\n" + taskId));

    private static (string CreatedAt, string TaskId) DecodeHistoryCursor(string cursor)
    {
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var separator = value.LastIndexOf('\n');
            if (separator <= 0 || separator == value.Length - 1) throw new FormatException();
            return (value[..separator], value[(separator + 1)..]);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ArgumentException("历史分页游标无效。", nameof(cursor), ex);
        }
    }

    /// <summary>
    /// 旧行没有 media_unit_key 时可以由 Aid/Cid 无损恢复媒体身份；输出指纹还缺少编码和容器，
    /// 因此只补媒体键，绝不在读取层伪造 rendition_fingerprint。
    /// </summary>
    private static string GetCompatibleMediaUnitKey(SqliteDataReader reader)
    {
        var stored = TryGetString(reader, "media_unit_key");
        if (!string.IsNullOrWhiteSpace(stored)) return stored;
        try
        {
            var aid = reader.GetInt64(reader.GetOrdinal("aid"));
            var cid = reader.GetInt64(reader.GetOrdinal("cid"));
            return aid > 0 && cid > 0 ? new MediaUnitKey(aid, cid).ToStorageKey() : string.Empty;
        }
        catch { return string.Empty; }
    }

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

    private static TEnum? TryGetEnum<TEnum>(SqliteDataReader reader, string column)
        where TEnum : struct, Enum
    {
        var value = TryGetString(reader, column);
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : null;
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

    private static FileConflictPolicy TryGetConflictPolicy(SqliteDataReader reader)
        => Enum.TryParse<FileConflictPolicy>(TryGetString(reader, "conflict_policy"), out var value)
            ? value
            : FileConflictPolicy.AutoNumber;

    /// <summary>
    /// 终态任务不再需要占用路径键。释放动作与状态写入使用同一连接顺序执行，
    /// 即使进程在两条语句之间退出，下一次预检仍会以任务状态过滤陈旧保留，不会允许静默覆盖。
    /// </summary>
    private static async Task ReleaseReservationAsync(SqliteConnection connection, string taskId)
    {
        await using var release = connection.CreateCommand();
        release.CommandText = "DELETE FROM output_path_reservations WHERE task_id = $task_id;";
        release.Parameters.AddWithValue("$task_id", taskId);
        await release.ExecuteNonQueryAsync();
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
