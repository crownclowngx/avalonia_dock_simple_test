using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Services;

/// <summary>
/// B站 Cookie SQLite 持久化存储
/// </summary>
public class BiliCookieStore
{
    private readonly string _connectionString;

    /// <summary>
    /// 静态构造函数：注册原生库解析器，解决插件子目录中 e_sqlite3 无法被 .NET 运行时自动发现的问题
    /// </summary>
    static BiliCookieStore()
    {
        try
        {
            // 获取插件 DLL 所在目录
            var assemblyDir = Path.GetDirectoryName(
                typeof(BiliCookieStore).Assembly.Location) ?? "";

            // 构造原生库路径，如 runtimes/win-x64/native/e_sqlite3.dll
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
                // 预加载原生库，后续 P/Invoke 会复用已加载的句柄
                NativeLibrary.Load(nativeLibPath);
            }
        }
        catch
        {
            // 若预加载失败则忽略，让运行时走默认解析路径
        }
    }

    public BiliCookieStore()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BiliDownloader");
        Directory.CreateDirectory(appDataDir);
        var dbPath = Path.Combine(appDataDir, "bili_cookies.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    /// <summary>
    /// 初始化数据库，创建 cookies 表（若不存在）
    /// </summary>
    public async Task InitAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS cookies (
                name    TEXT NOT NULL PRIMARY KEY,
                value   TEXT NOT NULL,
                domain  TEXT,
                path    TEXT,
                expires INTEGER
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Upsert 单条 Cookie
    /// </summary>
    public async Task SaveCookieAsync(string name, string value,
        string? domain = null, string? path = null, long? expires = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cookies (name, value, domain, path, expires)
            VALUES (@name, @value, @domain, @path, @expires)
            ON CONFLICT(name) DO UPDATE SET
                value   = @value,
                domain  = @domain,
                path    = @path,
                expires = @expires;
            """;
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@domain", (object?)domain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@path", (object?)path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@expires", (object?)expires ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 批量保存 Cookie（登录成功后一次性写入）
    /// </summary>
    public async Task SaveCookiesAsync(IEnumerable<(string Name, string Value)> cookies)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        foreach (var (name, value) in cookies)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO cookies (name, value) VALUES (@name, @value)
                ON CONFLICT(name) DO UPDATE SET value = @value;
                """;
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@value", value);
            await cmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// 加载全部 Cookie
    /// </summary>
    public async Task<Dictionary<string, string>> LoadAllCookiesAsync()
    {
        var result = new Dictionary<string, string>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, value FROM cookies;";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var value = reader.GetString(1);
            result[name] = value;
        }

        return result;
    }

    /// <summary>
    /// 清空所有 Cookie（退出登录时调用）
    /// </summary>
    public async Task DeleteAllCookiesAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM cookies;";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 拼接为 HTTP Header 用的 Cookie 字符串
    /// </summary>
    public async Task<string> GetCookieStringAsync()
    {
        var cookies = await LoadAllCookiesAsync();
        return string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
