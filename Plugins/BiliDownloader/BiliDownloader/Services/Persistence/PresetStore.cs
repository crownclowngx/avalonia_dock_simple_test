using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BiliDownloader.Services.Persistence;

/// <summary>
/// 预设持久化存储：复用 settings 表的 KV 结构存储自定义预设。
/// <para>
/// 设计思考：
/// - 复用 ISettingsRepository 的 settings 表（key-value），避免数据库迁移脚本。
/// - key 格式："preset:{id}" → value = JSON 序列化的 DownloadPreset。
/// - 索引 key："preset_index" → value = 所有自定义预设 ID 的 JSON 数组。
/// - 内置预设始终从代码获取（BuiltInPresets.GetAll()），不写入数据库，避免污染。
/// - 序列化使用 Newtonsoft.Json（与 Document 保存一致）。
/// - 反序列化使用 NullValueHandling.Ignore，新增字段自动补默认值（向前兼容）。
/// </para>
/// </summary>
public class PresetStore : IPresetRepository
{
    private readonly string _connectionString;

    /// <summary>预设索引的 settings key（存储所有自定义预设 ID 的 JSON 数组）</summary>
    private const string PresetIndexKey = "preset_index";

    /// <summary>预设数据 key 前缀</summary>
    private const string PresetKeyPrefix = "preset:";

    /// <summary>
    /// JSON 序列化选项：忽略 null 值，减少存储空间。
    /// 设计思考：未来新增字段时，旧数据反序列化自动使用 record 定义的默认值。
    /// </summary>
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public PresetStore(IBiliDataPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DownloadTaskDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    /// <inheritdoc />
    public async Task<List<DownloadPreset>> GetAllAsync()
    {
        // 内置预设始终从代码获取，保证不被数据库污染
        var result = BuiltInPresets.GetAll();

        // 从 settings 表加载自定义预设
        var customIds = await GetPresetIndexAsync();
        foreach (var id in customIds)
        {
            var preset = await GetCustomPresetAsync(id);
            if (preset != null)
                result.Add(preset);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<DownloadPreset?> GetByIdAsync(string id)
    {
        // 先查找内置预设
        var builtIn = BuiltInPresets.GetAll().FirstOrDefault(p => p.Id == id);
        if (builtIn != null)
            return builtIn;

        // 再查找自定义预设
        return await GetCustomPresetAsync(id);
    }

    /// <inheritdoc />
    public async Task SaveAsync(DownloadPreset preset)
    {
        // 内置预设不允许通过此方法覆盖
        if (preset.IsBuiltIn)
            return;

        var json = JsonConvert.SerializeObject(preset, JsonSettings);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 写入预设数据
        await SetValueAsync(connection, $"{PresetKeyPrefix}{preset.Id}", json);

        // 更新索引（如果 ID 不在索引中则追加）
        var ids = await GetPresetIndexAsync(connection);
        if (!ids.Contains(preset.Id))
        {
            ids.Add(preset.Id);
            await SetValueAsync(connection, PresetIndexKey, JsonConvert.SerializeObject(ids));
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id)
    {
        // 内置预设拒绝删除
        if (BuiltInPresets.GetAll().Any(p => p.Id == id))
            return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 删除预设数据
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM settings WHERE key = $key;";
            cmd.Parameters.AddWithValue("$key", $"{PresetKeyPrefix}{id}");
            await cmd.ExecuteNonQueryAsync();
        }

        // 更新索引
        var ids = await GetPresetIndexAsync(connection);
        if (ids.Remove(id))
        {
            await SetValueAsync(connection, PresetIndexKey, JsonConvert.SerializeObject(ids));
        }
    }

    /// <summary>
    /// 获取自定义预设 ID 索引列表（内部使用，使用外部连接）。
    /// </summary>
    private async Task<List<string>> GetPresetIndexAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", PresetIndexKey);
        var result = await cmd.ExecuteScalarAsync() as string;

        if (string.IsNullOrEmpty(result))
            return new List<string>();

        try
        {
            return JsonConvert.DeserializeObject<List<string>>(result) ?? new List<string>();
        }
        catch
        {
            // 索引损坏时回退为空列表，不影响内置预设
            return new List<string>();
        }
    }

    /// <summary>
    /// 获取自定义预设 ID 索引列表（新建连接）。
    /// </summary>
    private async Task<List<string>> GetPresetIndexAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await GetPresetIndexAsync(connection);
    }

    /// <summary>
    /// 从 settings 表读取单个自定义预设。
    /// </summary>
    private async Task<DownloadPreset?> GetCustomPresetAsync(string id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", $"{PresetKeyPrefix}{id}");
        var json = await cmd.ExecuteScalarAsync() as string;

        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<DownloadPreset>(json, JsonSettings);
        }
        catch
        {
            // 反序列化失败（格式损坏），跳过此预设
            return null;
        }
    }

    /// <summary>
    /// 向 settings 表写入 KV 值（INSERT OR REPLACE）。
    /// </summary>
    private static async Task SetValueAsync(SqliteConnection connection, string key, string value)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($key, $value);";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync();
    }
}
