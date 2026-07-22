using System.Security.Cryptography;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// SECVID03 认证成功后持有的短生命周期密码学上下文。
/// </summary>
internal sealed class Secvid03AuthenticationContext : IDisposable
{
    private bool _disposed;

    public Secvid03AuthenticationContext(Secvid03Header header, byte[] key, byte[] immutableDigest)
    {
        Header = header;
        Key = key;
        ImmutableDigest = immutableDigest;
    }

    public Secvid03Header Header { get; }
    public byte[] Key { get; }
    public byte[] ImmutableDigest { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        CryptographicOperations.ZeroMemory(Key);
        CryptographicOperations.ZeroMemory(ImmutableDigest);
        _disposed = true;
    }
}

internal sealed class Secvid03AuthenticationException(string message, Exception innerException)
    : CryptographicException(message, innerException);

internal sealed class Secvid03ContentAuthenticationException(string message, Exception innerException)
    : CryptographicException(message, innerException);

/// <summary>
/// 播放流和明文导出共享的 SECVID03 认证规则，集中维护 key、nonce、AAD 和标签语义。
/// </summary>
internal static class Secvid03Cryptography
{
    public static Secvid03AuthenticationContext Authenticate(
        string password,
        Secvid03Header header,
        ReadOnlySpan<byte> originalHeader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var key = Secvid03Format.DeriveKey(password, header);
        try
        {
            var immutableAad = Secvid03Format.CreateImmutableHeaderAad(header, originalHeader);
            var immutableDigest = SHA256.HashData(immutableAad);
            try
            {
                using var aes = new AesGcm(key, Secvid03Format.TagSize);
                aes.Decrypt(
                    Secvid03Format.CreateNonce(header, 0),
                    ReadOnlySpan<byte>.Empty,
                    header.HeaderTag,
                    Span<byte>.Empty,
                    immutableAad);
            }
            catch (CryptographicException ex)
            {
                CryptographicOperations.ZeroMemory(immutableDigest);
                throw new Secvid03AuthenticationException("密码错误或固定头已损坏。", ex);
            }

            return new Secvid03AuthenticationContext(header, key, immutableDigest);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    public static void DecryptChunk(
        Secvid03AuthenticationContext context,
        long chunkIndex,
        ReadOnlySpan<byte> cipher,
        ReadOnlySpan<byte> tag,
        Span<byte> plain)
    {
        try
        {
            using var aes = new AesGcm(context.Key, Secvid03Format.TagSize);
            aes.Decrypt(
                Secvid03Format.CreateNonce(context.Header, checked((uint)chunkIndex + 1)),
                cipher,
                tag,
                plain,
                Secvid03Format.CreateChunkAad(context.ImmutableDigest, chunkIndex));
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new Secvid03ContentAuthenticationException("视频内容认证失败。", ex);
        }
    }
}
