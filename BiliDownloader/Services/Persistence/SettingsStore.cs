using Microsoft.Data.Sqlite;

namespace BiliDownloader.Services.Persistence;

/// <summary>
/// 应用设置 SQLite 持久化存储
/// </summary>
public class SettingsStore : ISettingsRepository
{
    private readonly string _connectionString;

    public SettingsStore()
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
    /// 初始化数据库，创建 settings 表（若不存在）
    /// </summary>
    public async Task InitAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL DEFAULT ''
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 获取配置项
    /// </summary>
    public async Task<string?> GetSettingAsync(string key)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    /// <summary>
    /// 设置配置项
    /// </summary>
    public async Task SetSettingAsync(string key, string value)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($key, $value);";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync();
    }
}
