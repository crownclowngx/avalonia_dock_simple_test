using System;
using System.IO;
using System.Security.Cryptography;
using LibVLCSharp.Shared;
using MySmallTools.Models.SecretVideoPlayer;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 流式解密媒体输入 - 按需解密数据块，避免完整内存解密
    /// 提供高性能的加密视频播放支持
    /// </summary>
    public unsafe class StreamingDecryptionMediaInput : MediaInput
    {
        private readonly string _encryptedFilePath;
        private readonly string _password;
        private readonly EncryptedVideoInfo _videoInfo;
        private readonly byte[] _decryptionKey;
        private readonly byte[] _originalHeader;
        
        private FileStream? _fileStream;
        private long _currentPosition = 0;
        private readonly object _lockObject = new object();
        
        // 缓存相关
        private readonly int _cacheBlockSize = 1024 * 1024; // 1MB缓存块
        private byte[]? _cachedBlock;
        private long _cachedBlockPosition = -1;
        
        // AES解密器（重用以提高性能）
        private Aes? _aes;
        
        public StreamingDecryptionMediaInput(string encryptedFilePath, string password, EncryptedVideoInfo videoInfo)
        {
            _encryptedFilePath = encryptedFilePath ?? throw new ArgumentNullException(nameof(encryptedFilePath));
            _password = password ?? throw new ArgumentNullException(nameof(password));
            _videoInfo = videoInfo ?? throw new ArgumentNullException(nameof(videoInfo));
            
            // 生成解密密钥
            _decryptionKey = GenerateDecryptionKey();
            
            // 读取原始文件头
            _originalHeader = ReadOriginalHeader();
            
            // 设置为可寻址
            CanSeek = true;
            
            // 初始化AES解密器
            InitializeAes();
        }
        
        /// <summary>
        /// 生成解密密钥
        /// </summary>
        private byte[] GenerateDecryptionKey()
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(_password, 
                System.Text.Encoding.UTF8.GetBytes("SecretVideoSalt2024"), 10000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32); // AES-256
        }
        
        /// <summary>
        /// 初始化AES解密器
        /// </summary>
        private void InitializeAes()
        {
            _aes = Aes.Create();
            _aes.Key = _decryptionKey;
            _aes.Mode = CipherMode.ECB; // 使用ECB模式手动实现CTR
            _aes.Padding = PaddingMode.None;
        }
        
        /// <summary>
        /// 读取原始文件头
        /// </summary>
        private byte[] ReadOriginalHeader()
        {
            using var stream = new FileStream(_encryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Position = _videoInfo.EncryptedDataPosition;
            
            var header = new byte[_videoInfo.OriginalHeaderSize];
            stream.Read(header, 0, header.Length);
            
            // 解密文件头
            var counter = new byte[16];
            Array.Copy(_videoInfo.IV, counter, 16);
            
            var keyStream = GenerateCtrKeyStream(counter, header.Length);
            for (int i = 0; i < header.Length; i++)
            {
                header[i] ^= keyStream[i];
            }
            
            return header;
        }
        
        /// <summary>
        /// LibVLC调用此方法打开媒体
        /// </summary>
        public override bool Open(out ulong size)
        {
            try
            {
                _fileStream = new FileStream(_encryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                // 计算总的解密后文件大小
                var encryptedDataLength = _fileStream.Length - _videoInfo.EncryptedDataPosition;
                size = (ulong)(_videoInfo.OriginalHeaderSize + encryptedDataLength);
                
                _currentPosition = 0;
                return true;
            }
            catch
            {
                size = 0;
                return false;
            }
        }
        
        /// <summary>
        /// LibVLC调用此方法读取数据
        /// </summary>
        public override int Read(IntPtr buf, uint len)
        {
            lock (_lockObject)
            {
                if (_fileStream == null)
                    return 0;
                
                var totalSize = _videoInfo.OriginalHeaderSize + (_fileStream.Length - _videoInfo.EncryptedDataPosition);
                if (_currentPosition >= totalSize)
                    return 0; // EOF
                
                var bytesToRead = (int)Math.Min(len, totalSize - _currentPosition);
                if (bytesToRead <= 0)
                    return 0;
                
                var buffer = new byte[bytesToRead];
                var bytesRead = ReadDecryptedData(buffer, 0, bytesToRead);
                
                if (bytesRead > 0)
                {
                    // 直接复制到LibVLC缓冲区
                    fixed (byte* bufferPtr = buffer)
                    {
                        Buffer.MemoryCopy(bufferPtr, buf.ToPointer(), len, bytesRead);
                    }
                    _currentPosition += bytesRead;
                }
                
                return bytesRead;
            }
        }
        
        /// <summary>
        /// 读取解密后的数据
        /// </summary>
        private int ReadDecryptedData(byte[] buffer, int offset, int count)
        {
            var totalRead = 0;
            var currentOffset = offset;
            var remainingCount = count;
            
            while (remainingCount > 0 && _currentPosition < GetTotalSize())
            {
                int bytesRead;
                
                if (_currentPosition < _videoInfo.OriginalHeaderSize)
                {
                    // 读取文件头部分
                    bytesRead = ReadFromHeader(buffer, currentOffset, remainingCount);
                }
                else
                {
                    // 读取加密数据部分
                    bytesRead = ReadFromEncryptedData(buffer, currentOffset, remainingCount);
                }
                
                if (bytesRead <= 0)
                    break;
                
                totalRead += bytesRead;
                currentOffset += bytesRead;
                remainingCount -= bytesRead;
            }
            
            return totalRead;
        }
        
        /// <summary>
        /// 从文件头读取数据
        /// </summary>
        private int ReadFromHeader(byte[] buffer, int offset, int count)
        {
            var headerPosition = (int)_currentPosition;
            var bytesToRead = Math.Min(count, _originalHeader.Length - headerPosition);
            
            if (bytesToRead <= 0)
                return 0;
            
            Array.Copy(_originalHeader, headerPosition, buffer, offset, bytesToRead);
            return bytesToRead;
        }
        
        /// <summary>
        /// 从加密数据读取并解密
        /// </summary>
        private int ReadFromEncryptedData(byte[] buffer, int offset, int count)
        {
            var dataPosition = _currentPosition - _videoInfo.OriginalHeaderSize;
            var filePosition = _videoInfo.EncryptedDataPosition + dataPosition;
            
            // 检查是否可以从缓存读取
            if (_cachedBlock != null && 
                dataPosition >= _cachedBlockPosition && 
                dataPosition < _cachedBlockPosition + _cachedBlock.Length)
            {
                var cacheOffset = (int)(dataPosition - _cachedBlockPosition);
                var bytesToRead = Math.Min(count, _cachedBlock.Length - cacheOffset);
                Array.Copy(_cachedBlock, cacheOffset, buffer, offset, bytesToRead);
                return bytesToRead;
            }
            
            // 需要读取新的数据块
            return ReadAndDecryptNewBlock(buffer, offset, count, filePosition, dataPosition);
        }
        
        /// <summary>
        /// 读取并解密新的数据块
        /// </summary>
        private int ReadAndDecryptNewBlock(byte[] buffer, int offset, int count, long filePosition, long dataPosition)
        {
            if (_fileStream == null)
                return 0;
            
            // 计算要读取的块大小
            var blockSize = Math.Min(_cacheBlockSize, count * 2); // 读取稍大的块以提高缓存效率
            var maxAvailable = _fileStream.Length - filePosition;
            blockSize = (int)Math.Min(blockSize, maxAvailable);
            
            if (blockSize <= 0)
                return 0;
            
            // 读取加密数据
            var encryptedBlock = new byte[blockSize];
            _fileStream.Position = filePosition;
            var actualRead = _fileStream.Read(encryptedBlock, 0, blockSize);
            
            if (actualRead <= 0)
                return 0;
            
            // 解密数据块
            var decryptedBlock = DecryptDataBlock(encryptedBlock, dataPosition, actualRead);
            
            // 更新缓存
            _cachedBlock = decryptedBlock;
            _cachedBlockPosition = dataPosition;
            
            // 复制到输出缓冲区
            var bytesToCopy = Math.Min(count, actualRead);
            Array.Copy(decryptedBlock, 0, buffer, offset, bytesToCopy);
            
            return bytesToCopy;
        }
        
        /// <summary>
        /// 解密数据块
        /// </summary>
        private byte[] DecryptDataBlock(byte[] encryptedData, long dataPosition, int length)
        {
            var decryptedData = new byte[length];
            
            // 计算CTR模式的计数器起始值
            var counter = new byte[16];
            Array.Copy(_videoInfo.IV, counter, 16);
            
            // 调整计数器到当前位置
            var blockOffset = (_videoInfo.OriginalHeaderSize + dataPosition) / 16;
            AddToCounter(counter, blockOffset);
            
            // 生成密钥流并解密
            var keyStream = GenerateCtrKeyStream(counter, length);
            for (int i = 0; i < length; i++)
            {
                decryptedData[i] = (byte)(encryptedData[i] ^ keyStream[i]);
            }
            
            return decryptedData;
        }
        
        /// <summary>
        /// 生成CTR模式的密钥流
        /// </summary>
        private byte[] GenerateCtrKeyStream(byte[] counter, int length)
        {
            if (_aes == null)
                throw new InvalidOperationException("AES not initialized");
            
            var keyStream = new byte[length];
            var encryptor = _aes.CreateEncryptor();
            
            var blockCount = (length + 15) / 16; // 向上取整
            var tempCounter = new byte[16];
            
            for (int i = 0; i < blockCount; i++)
            {
                Array.Copy(counter, tempCounter, 16);
                
                var encryptedCounter = new byte[16];
                encryptor.TransformBlock(tempCounter, 0, 16, encryptedCounter, 0);
                
                var copyLength = Math.Min(16, length - i * 16);
                Array.Copy(encryptedCounter, 0, keyStream, i * 16, copyLength);
                
                // 递增计数器
                IncrementCounter(counter);
            }
            
            return keyStream;
        }
        
        /// <summary>
        /// 递增计数器
        /// </summary>
        private void IncrementCounter(byte[] counter)
        {
            for (int i = 15; i >= 0; i--)
            {
                if (++counter[i] != 0)
                    break;
            }
        }
        
        /// <summary>
        /// 将值添加到计数器
        /// </summary>
        private void AddToCounter(byte[] counter, long value)
        {
            for (int i = 15; i >= 0 && value > 0; i--)
            {
                var sum = counter[i] + (value & 0xFF);
                counter[i] = (byte)(sum & 0xFF);
                value = (value >> 8) + (sum >> 8);
            }
        }
        
        /// <summary>
        /// 获取总文件大小
        /// </summary>
        private long GetTotalSize()
        {
            if (_fileStream == null)
                return 0;
            return _videoInfo.OriginalHeaderSize + (_fileStream.Length - _videoInfo.EncryptedDataPosition);
        }
        
        /// <summary>
        /// LibVLC调用此方法进行寻址
        /// </summary>
        public override bool Seek(ulong offset)
        {
            lock (_lockObject)
            {
                var totalSize = GetTotalSize();
                if ((long)offset > totalSize)
                    return false;
                
                _currentPosition = (long)offset;
                
                // 清除缓存（因为位置改变了）
                _cachedBlock = null;
                _cachedBlockPosition = -1;
                
                return true;
            }
        }
        
        /// <summary>
        /// LibVLC调用此方法关闭媒体
        /// </summary>
        public override void Close()
        {
            lock (_lockObject)
            {
                _fileStream?.Dispose();
                _fileStream = null;
                _currentPosition = 0;
                _cachedBlock = null;
                _cachedBlockPosition = -1;
            }
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileStream?.Dispose();
                _aes?.Dispose();
                _cachedBlock = null;
            }
            base.Dispose(disposing);
        }
    }
}