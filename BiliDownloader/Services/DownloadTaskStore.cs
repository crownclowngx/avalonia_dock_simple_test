using System.Runtime.InteropServices;
using BiliDownloader.Models;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Services;

/// <summary>
/// 下载任务 SQLite 持久化存储
/// 数据库路径: %AppData%/BiliDownloader/bili_download_tasks.db
/// </summary>
public class DownloadTaskStore
{
    private readonly string _connectionString;

    /// <summary>
    /// 静态构造函数：预加载 e_sqlite3 原生库，解决插件子目录中无法被发现的问题
    /// </summary>
    static DownloadTaskStore()
    {
        try
        {
            var assemblyDir = Path.GetDirectoryName(
                typeof(DownloadTaskStore).Assembly.Location) ?? "";

            var rid = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                Architecture.Arm => "win-arm",
                _ => "win-x64"
            };

            var nativeLibPath = Path.Combine(assemblyDir, "runtimes", rid, "native", "e_sqlite3.dll");
            if (File.Exists(nativeLibPath))
            {
                NativeLibrary.Load(nativeLibPath);
            }
        }
        catch
        {
            // 若预加载失败则忽略
        }
    }

    public DownloadTaskStore()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BiliDownloader");
        Directory.CreateDirectory(appDataDir);
        var dbPath = Path.Combine(appDataDir, "bili_download_tasks.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
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
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS download_tasks (
                task_id             TEXT NOT NULL PRIMARY KEY,
                document_id         TEXT NOT NULL,
                series_title        TEXT NOT NULL DEFAULT '',
                item_title          TEXT NOT NULL DEFAULT '',
                aid                 INTEGER NOT NULL DEFAULT 0,
                bvid                TEXT NOT NULL DEFAULT '',
                cid                 INTEGER NOT NULL DEFAULT 0,
                quality_id          INTEGER NOT NULL DEFAULT 80,
                output_directory    TEXT NOT NULL DEFAULT '',
                cookie              TEXT NOT NULL DEFAULT '',
                progress            REAL NOT NULL DEFAULT 0,
                status              TEXT NOT NULL DEFAULT 'pending',
                error_message       TEXT,
                temp_directory      TEXT NOT NULL DEFAULT '',
                video_bytes         INTEGER NOT NULL DEFAULT 0,
                audio_bytes         INTEGER NOT NULL DEFAULT 0,
                created_at          TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 批量插入任务记录
    /// </summary>
    public async Task InsertBatchAsync(List<DownloadTaskRecord> records)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var r in records)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO download_tasks
                    (task_id, document_id, series_title, item_title, aid, bvid, cid,
                     quality_id, output_directory, cookie, progress, status, error_message,
                     temp_directory, video_bytes, audio_bytes, created_at)
                VALUES
                    ($task_id, $document_id, $series_title, $item_title, $aid, $bvid, $cid,
                     $quality_id, $output_directory, $cookie, $progress, $status, $error_message,
                     $temp_directory, $video_bytes, $audio_bytes, $created_at);
                """;
            cmd.Parameters.AddWithValue("$task_id", r.TaskId);
            cmd.Parameters.AddWithValue("$document_id", r.DocumentId);
            cmd.Parameters.AddWithValue("$series_title", r.SeriesTitle);
            cmd.Parameters.AddWithValue("$item_title", r.ItemTitle);
            cmd.Parameters.AddWithValue("$aid", r.Aid);
            cmd.Parameters.AddWithValue("$bvid", r.Bvid);
            cmd.Parameters.AddWithValue("$cid", r.Cid);
            cmd.Parameters.AddWithValue("$quality_id", r.QualityId);
            cmd.Parameters.AddWithValue("$output_directory", r.OutputDirectory);
            cmd.Parameters.AddWithValue("$cookie", r.Cookie);
            cmd.Parameters.AddWithValue("$progress", r.Progress);
            cmd.Parameters.AddWithValue("$status", r.Status);
            cmd.Parameters.AddWithValue("$error_message", (object?)r.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$temp_directory", r.TempDirectory);
            cmd.Parameters.AddWithValue("$video_bytes", r.VideoBytesDownloaded);
            cmd.Parameters.AddWithValue("$audio_bytes", r.AudioBytesDownloaded);
            cmd.Parameters.AddWithValue("$created_at", r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            await cmd.ExecuteNonQueryAsync();
        }
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
            SET progress = $progress, status = $status, error_message = $error_message
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$progress", progress);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$error_message", (object?)errorMessage ?? DBNull.Value);
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
            SET video_bytes = $video_bytes, audio_bytes = $audio_bytes
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$video_bytes", videoBytes);
        cmd.Parameters.AddWithValue("$audio_bytes", audioBytes);
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
            SET temp_directory = $temp_directory
            WHERE task_id = $task_id;
            """;
        cmd.Parameters.AddWithValue("$task_id", taskId);
        cmd.Parameters.AddWithValue("$temp_directory", tempDirectory);
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
        cmd.CommandText = """
            SELECT * FROM download_tasks
            WHERE status IN ('pending', 'downloading_video', 'downloading_audio', 'merging')
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
        cmd.CommandText = "DELETE FROM download_tasks WHERE status = 'done';";
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

    private static DownloadTaskRecord ReadRecord(SqliteDataReader reader)
    {
        return new DownloadTaskRecord
        {
            TaskId = reader.GetString(reader.GetOrdinal("task_id")),
            DocumentId = reader.GetString(reader.GetOrdinal("document_id")),
            SeriesTitle = reader.GetString(reader.GetOrdinal("series_title")),
            ItemTitle = reader.GetString(reader.GetOrdinal("item_title")),
            Aid = reader.GetInt64(reader.GetOrdinal("aid")),
            Bvid = reader.GetString(reader.GetOrdinal("bvid")),
            Cid = reader.GetInt64(reader.GetOrdinal("cid")),
            QualityId = reader.GetInt32(reader.GetOrdinal("quality_id")),
            OutputDirectory = reader.GetString(reader.GetOrdinal("output_directory")),
            Cookie = reader.GetString(reader.GetOrdinal("cookie")),
            Progress = reader.GetDouble(reader.GetOrdinal("progress")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message"))
                ? null
                : reader.GetString(reader.GetOrdinal("error_message")),
            TempDirectory = reader.GetString(reader.GetOrdinal("temp_directory")),
            VideoBytesDownloaded = reader.GetInt64(reader.GetOrdinal("video_bytes")),
            AudioBytesDownloaded = reader.GetInt64(reader.GetOrdinal("audio_bytes")),
            CreatedAt = DateTime.TryParse(
                reader.GetString(reader.GetOrdinal("created_at")),
                out var dt) ? dt : DateTime.Now,
        };
    }
}
