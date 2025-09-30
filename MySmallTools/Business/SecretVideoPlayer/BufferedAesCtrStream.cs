using System.Security.Cryptography;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 带缓冲的AES-CTR解密流 - 支持随机访问和高效解密
/// </summary>
public class BufferedAesCtrStream : Stream
{
    private readonly Stream _baseStream;
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly EncryptedVideoInfo _videoInfo;
    private readonly MultiLevelVideoBuffer _buffer;
    private readonly Aes _aes;
    
    private long _position;
    private bool _disposed;
    
    public BufferedAesCtrStream(Stream baseStream, byte[] key, EncryptedVideoInfo videoInfo, 
        MultiLevelVideoBuffer? buffer = null)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _videoInfo = videoInfo ?? throw new ArgumentNullException(nameof(videoInfo));
        _iv = videoInfo.IV;
        
        _buffer = buffer ?? new MultiLevelVideoBuffer(1024 * 1024, 10); // 默认1MB块，10个缓存
        
        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.Key = _key;
        
        _position = 0;
        
        // 验证密钥
        ValidateKey();
    }
    
    /// <summary>
    /// 验证密钥是否正确
    /// </summary>
    private void ValidateKey()
    {
        using var sha256 = SHA256.Create();
        var keyHash = sha256.ComputeHash(_key);
        
        if (!keyHash.SequenceEqual(_videoInfo.KeyHash))
        {
            throw new UnauthorizedAccessException("密钥不正确");
        }
    }
    
    public override bool CanRead => !_disposed && _baseStream.CanRead;
    public override bool CanSeek => !_disposed && _baseStream.CanSeek;
    public override bool CanWrite => false;
    
    public override long Length
    {
        get
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BufferedAesCtrStream));
            
            // 返回解密后的数据长度（总长度 - 原始文件头 - 加密头）
            return _baseStream.Length - _videoInfo.OriginalHeaderSize - 64;
        }
    }
    
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }
    
    public override void Flush()
    {
        // 只读流，无需实现
    }
    
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BufferedAesCtrStream));
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException();
        
        if (count == 0 || _position >= Length)
            return 0;
        
        // 限制读取长度
        count = (int)Math.Min(count, Length - _position);
        
        var totalBytesRead = 0;
        var currentOffset = offset;
        var remainingCount = count;
        
        while (remainingCount > 0 && _position < Length)
        {
            // 计算当前块的信息
            var blockIndex = _position / _buffer.BlockSize;
            var blockOffset = (int)(_position % _buffer.BlockSize);
            var blockDataLength = (int)Math.Min(_buffer.BlockSize - blockOffset, remainingCount);
            
            // 从缓冲区获取解密数据
            var blockData = GetDecryptedBlock(blockIndex);
            if (blockData == null || blockData.Length == 0)
                break;
            
            // 复制数据到输出缓冲区
            var actualCopyLength = Math.Min(blockDataLength, blockData.Length - blockOffset);
            Array.Copy(blockData, blockOffset, buffer, currentOffset, actualCopyLength);
            
            totalBytesRead += actualCopyLength;
            currentOffset += actualCopyLength;
            remainingCount -= actualCopyLength;
            _position += actualCopyLength;
        }
        
        return totalBytesRead;
    }
    
    /// <summary>
    /// 获取解密的数据块
    /// </summary>
    private byte[]? GetDecryptedBlock(long blockIndex)
    {
        // 先尝试从缓冲区获取
        var cachedBlock = _buffer.GetBlock(blockIndex);
        if (cachedBlock != null)
            return cachedBlock;
        
        // 从文件读取并解密
        var encryptedBlock = ReadEncryptedBlockFromFile(blockIndex);
        if (encryptedBlock == null)
            return null;
        
        var decryptedBlock = DecryptBlock(encryptedBlock, blockIndex);
        
        // 存入缓冲区
        _buffer.PutBlock(blockIndex, decryptedBlock);
        
        return decryptedBlock;
    }
    
    /// <summary>
    /// 从文件读取加密的数据块
    /// </summary>
    private byte[]? ReadEncryptedBlockFromFile(long blockIndex)
    {
        var filePosition = _videoInfo.EncryptedDataPosition + blockIndex * _buffer.BlockSize;
        var maxReadLength = _baseStream.Length - filePosition;
        
        if (maxReadLength <= 0)
            return null;
        
        var readLength = (int)Math.Min(_buffer.BlockSize, maxReadLength);
        var buffer = new byte[readLength];
        
        lock (_baseStream)
        {
            _baseStream.Position = filePosition;
            var bytesRead = _baseStream.Read(buffer, 0, readLength);
            
            if (bytesRead == 0)
                return null;
            
            if (bytesRead < readLength)
            {
                Array.Resize(ref buffer, bytesRead);
            }
        }
        
        return buffer;
    }
    
    /// <summary>
    /// 解密数据块
    /// </summary>
    private byte[] DecryptBlock(byte[] encryptedData, long blockIndex)
    {
        var decryptedData = new byte[encryptedData.Length];
        var basePosition = blockIndex * _buffer.BlockSize;
        
        // 生成CTR模式的密钥流
        var keyStream = GenerateCtrKeyStream(basePosition, encryptedData.Length);
        
        // XOR解密
        for (int i = 0; i < encryptedData.Length; i++)
        {
            decryptedData[i] = (byte)(encryptedData[i] ^ keyStream[i]);
        }
        
        return decryptedData;
    }
    
    /// <summary>
    /// 生成CTR模式的密钥流
    /// </summary>
    private byte[] GenerateCtrKeyStream(long position, int length)
    {
        var keyStream = new byte[length];
        var blockSize = 16; // AES块大小
        var startBlock = position / blockSize;
        var startOffset = (int)(position % blockSize);
        
        using var encryptor = _aes.CreateEncryptor();
        
        var currentBlock = startBlock;
        var outputOffset = 0;
        
        while (outputOffset < length)
        {
            // 构造CTR计数器
            var counter = new byte[16];
            Array.Copy(_iv, counter, 16);
            
            // 将块号添加到计数器
            var blockBytes = BitConverter.GetBytes(currentBlock);
            for (int i = 0; i < 8 && i < blockBytes.Length; i++)
            {
                counter[15 - i] = blockBytes[i];
            }
            
            // 加密计数器生成密钥流块
            var keyStreamBlock = new byte[16];
            encryptor.TransformBlock(counter, 0, 16, keyStreamBlock, 0);
            
            // 复制到输出密钥流
            var sourceOffset = (currentBlock == startBlock) ? startOffset : 0;
            var copyLength = Math.Min(blockSize - sourceOffset, length - outputOffset);
            
            Array.Copy(keyStreamBlock, sourceOffset, keyStream, outputOffset, copyLength);
            
            outputOffset += copyLength;
            currentBlock++;
        }
        
        return keyStream;
    }
    
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BufferedAesCtrStream));
        
        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
        };
        
        if (newPosition < 0)
            newPosition = 0;
        else if (newPosition > Length)
            newPosition = Length;
        
        _position = newPosition;
        
        // 预读取下一个块以提高性能
        if (_position < Length)
        {
            var nextBlockIndex = _position / _buffer.BlockSize;
            _ = Task.Run(() => GetDecryptedBlock(nextBlockIndex));
        }
        
        return _position;
    }
    
    public override void SetLength(long value)
    {
        throw new NotSupportedException("Cannot set length on read-only stream");
    }
    
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Cannot write to read-only stream");
    }
    
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _aes?.Dispose();
            _buffer?.Dispose();
            _baseStream?.Dispose();
            _disposed = true;
        }
        
        base.Dispose(disposing);
    }
    
    /// <summary>
    /// 预加载指定范围的数据块
    /// </summary>
    public async Task PreloadRangeAsync(long startPosition, long endPosition)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BufferedAesCtrStream));
        
        var startBlock = startPosition / _buffer.BlockSize;
        var endBlock = endPosition / _buffer.BlockSize;
        
        var tasks = new List<Task>();
        
        for (var blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            if (!_buffer.HasBlock(blockIndex))
            {
                tasks.Add(Task.Run(() => GetDecryptedBlock(blockIndex)));
            }
        }
        
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// 获取缓冲区统计信息
    /// </summary>
    public BufferStatistics GetBufferStatistics()
    {
        return _buffer.GetStatistics();
    }
}