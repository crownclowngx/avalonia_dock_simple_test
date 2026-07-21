using System.Security.Cryptography;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 把 SECVID03 暴露为与原视频内容完全一致的只读、可随机定位流。
/// </summary>
/// <remarks>
/// 虚拟流布局为“明文原视频前缀 + 按需解密的视频主体”。打开时仅执行一次 PBKDF2 并验证固定头，
/// 不解密视频块；真正读取时只认证涉及的块。四块 LRU 缓存把常见的解码器回读和小范围拖动控制在约 4 MiB 明文内存。
/// Stream 本身不承诺多线程并发安全，LibVLC 适配器会在回调入口进行串行化。
/// </remarks>
public sealed class SeekableEncryptedVideoStream : Stream
{
    private const int MaxCachedChunks = 4;
    private readonly FileStream _file;
    private readonly Secvid03Header _header;
    private readonly byte[] _key;
    private readonly byte[] _immutableDigest;
    private readonly Dictionary<long, CacheEntry> _cache = [];
    private readonly LinkedList<long> _lru = [];
    private long _position;
    private bool _disposed;

    private sealed record CacheEntry(byte[] Data, int Length, LinkedListNode<long> Node);

    private SeekableEncryptedVideoStream(FileStream file, Secvid03Header header, byte[] key, byte[] immutableDigest)
    {
        _file = file;
        _header = header;
        _key = key;
        _immutableDigest = immutableDigest;
    }

    /// <summary>
    /// 打开容器、派生密钥并验证不可变固定头，不预读或解密视频主体。
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">密码错误，或固定头/原视频前缀已被修改。</exception>
    public static SeekableEncryptedVideoStream Open(string path, string password)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Secvid03Format.ChunkSize, false);
        byte[]? key = null;
        try
        {
            var headerBytes = new byte[Secvid03Format.FixedHeaderSize];
            file.ReadExactly(headerBytes);
            var header = Secvid03Format.ParseHeader(headerBytes, file.Length);
            var originalHeader = new byte[header.OriginalHeaderLength];
            file.Position = Secvid03Format.OriginalHeaderOffset;
            file.ReadExactly(originalHeader);
            key = Secvid03Format.DeriveKey(password, header);
            var immutableAad = Secvid03Format.CreateImmutableHeaderAad(header, originalHeader);
            var immutableDigest = SHA256.HashData(immutableAad);
            try
            {
                // 使用“空明文 + 固定头 AAD”的 GCM 标签验证密码。文件中不保存可离线比较的明文 key hash。
                using var aes = new AesGcm(key, Secvid03Format.TagSize);
                aes.Decrypt(Secvid03Format.CreateNonce(header, 0), ReadOnlySpan<byte>.Empty, header.HeaderTag,
                    Span<byte>.Empty, immutableAad);
            }
            catch (CryptographicException ex)
            {
                throw new UnauthorizedAccessException("密码错误或文件已损坏。", ex);
            }
            return new SeekableEncryptedVideoStream(file, header, key, immutableDigest);
        }
        catch
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            file.Dispose();
            throw;
        }
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length { get { ThrowIfDisposed(); return _header.OriginalFileLength; } }
    public override long Position
    {
        get { ThrowIfDisposed(); return _position; }
        set
        {
            ThrowIfDisposed();
            if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        ThrowIfDisposed();
        if (destination.Length == 0 || _position >= Length) return 0;
        var totalRead = 0;
        var remaining = (int)Math.Min(destination.Length, Length - _position);

        while (remaining > 0)
        {
            if (_position < _header.OriginalHeaderLength)
            {
                // 原视频前缀物理上位于公开区之后，虚拟流中则必须从偏移 0 开始呈现给解码器。
                var count = (int)Math.Min(remaining, _header.OriginalHeaderLength - _position);
                _file.Position = Secvid03Format.OriginalHeaderOffset + _position;
                ReadExactly(_file, destination.Slice(totalRead, count));
                Advance(count, ref totalRead, ref remaining);
                continue;
            }

            var bodyPosition = _position - _header.OriginalHeaderLength;
            var chunkIndex = bodyPosition / _header.ChunkSize;
            var offsetInChunk = (int)(bodyPosition % _header.ChunkSize);
            var chunk = GetDecryptedChunk(chunkIndex);
            var countFromChunk = Math.Min(remaining, chunk.Length - offsetInChunk);
            chunk.Data.AsSpan(offsetInChunk, countFromChunk).CopyTo(destination.Slice(totalRead, countFromChunk));
            Advance(countFromChunk, ref totalRead, ref remaining);
        }
        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (newPosition < 0 || newPosition > Length) throw new IOException("Seek 位置超出视频范围。");
        _position = newPosition;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 先关闭文件句柄，再清零缓存明文和派生密钥，确保切换视频后可立即删除或覆盖原容器。
                _file.Dispose();
                foreach (var entry in _cache.Values) CryptographicOperations.ZeroMemory(entry.Data);
                _cache.Clear();
                _lru.Clear();
                CryptographicOperations.ZeroMemory(_key);
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    private (byte[] Data, int Length) GetDecryptedChunk(long chunkIndex)
    {
        if (_cache.TryGetValue(chunkIndex, out var cached))
        {
            // 命中缓存时把节点提升到链表头，尾节点始终代表最久未使用的块。
            _lru.Remove(cached.Node);
            _lru.AddFirst(cached.Node);
            return (cached.Data, cached.Length);
        }

        // 物理偏移可以 O(1) 计算：每个完整块固定由 1 MiB 密文和 16 字节标签组成。
        // 最后一块可以较短，但它之前的所有块必然是完整块，因此公式对尾块同样成立。
        var chunkPlainOffset = checked(chunkIndex * _header.ChunkSize);
        var plainLength = (int)Math.Min(_header.ChunkSize, _header.PlainBodyLength - chunkPlainOffset);
        if (plainLength <= 0) throw new EndOfStreamException("请求的加密块超出视频范围。");
        var cipher = new byte[plainLength];
        var tag = new byte[Secvid03Format.TagSize];
        var physicalOffset = checked(_header.EncryptedDataOffset + chunkIndex * (_header.ChunkSize + Secvid03Format.TagSize));
        _file.Position = physicalOffset;
        _file.ReadExactly(cipher);
        _file.ReadExactly(tag);
        var plain = new byte[plainLength];
        try
        {
            // 必须先完成 GCM 验证才能把明文放入缓存。标签失败时立即终止当前读取，绝不返回部分未认证数据。
            using var aes = new AesGcm(_key, Secvid03Format.TagSize);
            aes.Decrypt(Secvid03Format.CreateNonce(_header, checked((uint)chunkIndex + 1)), cipher, tag, plain,
                Secvid03Format.CreateChunkAad(_immutableDigest, chunkIndex));
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new InvalidDataException("密码错误或文件已损坏。", ex);
        }

        if (_cache.Count >= MaxCachedChunks && _lru.Last is { } oldest)
        {
            // 淘汰时主动清零旧明文，缓存容量因此与视频文件大小完全解耦。
            var oldEntry = _cache[oldest.Value];
            CryptographicOperations.ZeroMemory(oldEntry.Data);
            _cache.Remove(oldest.Value);
            _lru.RemoveLast();
        }
        var node = _lru.AddFirst(chunkIndex);
        _cache[chunkIndex] = new CacheEntry(plain, plainLength, node);
        return (plain, plainLength);
    }

    private void Advance(int count, ref int totalRead, ref int remaining)
    {
        _position += count;
        totalRead += count;
        remaining -= count;
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        // 随机读取也可能发生短读；若到达 EOF 仍未填满，说明容器在打开后被截断或底层存储损坏。
        var read = 0;
        while (read < destination.Length)
        {
            var current = stream.Read(destination[read..]);
            if (current == 0) throw new EndOfStreamException("加密视频被截断。");
            read += current;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
