using System.Security.Cryptography;
using System.Text.Json;
using BiliDownloader.Services.Infrastructure;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Services.Auth;

public sealed record BiliCredentialCookie(string Name, string Value);

/// <summary>
/// 加密持久化的完整登录会话。账号资料与 Cookie 使用同一个 AES-GCM 信封。
/// </summary>
public sealed record BiliCredentialSession(
    IReadOnlyList<BiliCredentialCookie> Cookies,
    string? UserName = null,
    string? UserAvatar = null);

/// <summary>
/// Bilibili 登录会话密文存储边界。
/// </summary>
public interface IBiliCredentialStore
{
    Task InitAsync(CancellationToken cancellationToken = default);
    Task SaveSessionAsync(
        BiliCredentialSession session,
        CancellationToken cancellationToken = default);
    Task<BiliCredentialSession?> LoadSessionAsync(
        CancellationToken cancellationToken = default);
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// SQLite 只保存 AES-GCM 信封；Cookie 和账号资料都位于密文内部。
/// </summary>
public sealed class BiliCredentialStore : IBiliCredentialStore
{
    private const int CurrentPayloadVersion = 2;
    private readonly string _connectionString;
    private readonly ICredentialProtector _protector;

    static BiliCredentialStore()
    {
        SqliteNativeLoader.EnsureLoaded();
    }

    public BiliCredentialStore(IBiliDataPaths paths, ICredentialProtector protector)
    {
        _protector = protector;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.CredentialDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public async Task InitAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS credential_store (
                id         INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                version    INTEGER NOT NULL,
                nonce      BLOB NOT NULL,
                ciphertext BLOB NOT NULL,
                tag        BLOB NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSessionAsync(
        BiliCredentialSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(session.Cookies);
        await InitAsync(cancellationToken);

        var payload = new CredentialPayload
        {
            Version = CurrentPayloadVersion,
            Cookies = session.Cookies
                .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name))
                .OrderBy(cookie => cookie.Name, StringComparer.Ordinal)
                .ToList(),
            UserName = session.UserName,
            UserAvatar = session.UserAvatar,
        };
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        try
        {
            var envelope = _protector.Protect(plaintext);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO credential_store (id, version, nonce, ciphertext, tag)
                VALUES (1, $version, $nonce, $ciphertext, $tag)
                ON CONFLICT(id) DO UPDATE SET
                    version = excluded.version,
                    nonce = excluded.nonce,
                    ciphertext = excluded.ciphertext,
                    tag = excluded.tag;
                """;
            command.Parameters.AddWithValue("$version", envelope.Version);
            command.Parameters.AddWithValue("$nonce", envelope.Nonce);
            command.Parameters.AddWithValue("$ciphertext", envelope.Ciphertext);
            command.Parameters.AddWithValue("$tag", envelope.Tag);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<BiliCredentialSession?> LoadSessionAsync(
        CancellationToken cancellationToken = default)
    {
        await InitAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, nonce, ciphertext, tag FROM credential_store WHERE id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var envelope = new CredentialEnvelope(
            reader.GetInt32(0),
            (byte[])reader[1],
            (byte[])reader[2],
            (byte[])reader[3]);

        byte[]? plaintext = null;
        try
        {
            plaintext = _protector.Unprotect(envelope);
            var payload = JsonSerializer.Deserialize<CredentialPayload>(plaintext)
                ?? throw new InvalidDataException("凭据载荷为空。");
            if (payload.Version is not 1 and not CurrentPayloadVersion)
            {
                throw new InvalidDataException("凭据载荷版本无效。");
            }

            var cookies = payload.Cookies
                .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name))
                .GroupBy(cookie => cookie.Name, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList();
            return new BiliCredentialSession(
                cookies,
                payload.Version >= 2 ? payload.UserName : null,
                payload.Version >= 2 ? payload.UserAvatar : null);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or JsonException)
        {
            // 不保留不可读凭据：删除密文和 key，下一次登录重新生成。
            await DeleteAllAsync(cancellationToken);
            _protector.ResetKey();
            return null;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await InitAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM credential_store;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class CredentialPayload
    {
        public int Version { get; init; }
        public List<BiliCredentialCookie> Cookies { get; init; } = [];
        public string? UserName { get; init; }
        public string? UserAvatar { get; init; }
    }
}
