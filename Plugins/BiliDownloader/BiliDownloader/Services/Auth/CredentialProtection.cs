using System.Security.Cryptography;
using System.Text;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Auth;

/// <summary>
/// 持久化凭据的 AES-GCM 信封。
/// </summary>
public sealed record CredentialEnvelope(
    int Version,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag);

/// <summary>
/// 凭据明文与密文之间的唯一转换边界。
/// </summary>
public interface ICredentialProtector
{
    CredentialEnvelope Protect(byte[] plaintext);
    byte[] Unprotect(CredentialEnvelope envelope);
    void ResetKey();
}

/// <summary>
/// 管理当前安装的可见 Base64 密钥文件。
/// 该密钥与凭据库同目录，目标是消除数据库明文，而不是抵御用户目录整体泄露。
/// </summary>
public sealed class InstallationKeyStore
{
    private const string Header = "BILIKEY1";
    private const int KeySize = 32;
    private readonly IBiliDataPaths _paths;
    private readonly object _gate = new();

    public InstallationKeyStore(IBiliDataPaths paths)
    {
        _paths = paths;
    }

    public byte[] GetOrCreateKey()
    {
        lock (_gate)
        {
            if (File.Exists(_paths.CredentialKeyPath))
            {
                return ReadKey();
            }

            Directory.CreateDirectory(_paths.DataDirectory);
            var key = RandomNumberGenerator.GetBytes(KeySize);
            var contents = $"{Header}{Environment.NewLine}{Convert.ToBase64String(key)}{Environment.NewLine}";
            var tempPath = _paths.CredentialKeyPath + ".tmp";

            File.WriteAllText(tempPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TryRestrictFilePermissions(tempPath);
            File.Move(tempPath, _paths.CredentialKeyPath, overwrite: true);
            TryRestrictFilePermissions(_paths.CredentialKeyPath);
            return key;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (File.Exists(_paths.CredentialKeyPath))
            {
                File.Delete(_paths.CredentialKeyPath);
            }
        }
    }

    private byte[] ReadKey()
    {
        var lines = File.ReadAllLines(_paths.CredentialKeyPath, Encoding.UTF8);
        if (lines.Length < 2 || !string.Equals(lines[0].Trim(), Header, StringComparison.Ordinal))
        {
            throw new InvalidDataException("credential.key 标识无效。");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(lines[1].Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("credential.key 不是有效的 Base64。", ex);
        }

        if (key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("credential.key 必须包含 32 字节密钥。");
        }

        return key;
    }

    private static void TryRestrictFilePermissions(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // 权限收紧属于尽力而为；本方案不把文件权限作为核心安全边界。
        }
    }
}

/// <summary>
/// 使用每安装随机密钥的 AES-256-GCM 实现。
/// </summary>
public sealed class AesGcmCredentialProtector : ICredentialProtector
{
    private const int Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes(
        "BiliDownloader/Credential/v1");
    private readonly InstallationKeyStore _keyStore;

    public AesGcmCredentialProtector(InstallationKeyStore keyStore)
    {
        _keyStore = keyStore;
    }

    public CredentialEnvelope Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var key = _keyStore.GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
            return new CredentialEnvelope(Version, nonce, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Unprotect(CredentialEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Version != Version
            || envelope.Nonce.Length != NonceSize
            || envelope.Tag.Length != TagSize)
        {
            throw new CryptographicException("凭据密文格式无效。");
        }

        var key = _keyStore.GetOrCreateKey();
        var plaintext = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                envelope.Nonce,
                envelope.Ciphertext,
                envelope.Tag,
                plaintext,
                AssociatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void ResetKey() => _keyStore.Reset();
}
