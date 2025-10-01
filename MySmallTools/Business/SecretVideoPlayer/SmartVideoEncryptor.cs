using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 智能视频加密器 - 支持AES-CTR模式加密，保护文件头确保格式兼容性
/// </summary>
public class SmartVideoEncryptor
{
    private const int BASIC_HEADER_SIZE = 64; // 基础加密信息头大小
    private const int MAX_METADATA_SIZE = 512; // 元数据最大大小
    private const int ENCRYPTION_HEADER_SIZE = BASIC_HEADER_SIZE + MAX_METADATA_SIZE; // 总加密信息头大小
    private const string MAGIC_HEADER = "SECVID02"; // 魔数标识（版本2支持元数据）
    
    /// <summary>
    /// 提取视频文件元数据
    /// </summary>
    /// <param name="videoPath">视频文件路径</param>
    /// <returns>视频元数据，如果提取失败返回null</returns>
    private async Task<VideoMetadata?> ExtractVideoMetadataAsync(string videoPath)
    {
        try
        {
            // 确保LibVLC已初始化
            Core.Initialize();
            
            using var libVLC = new LibVLC();
            using var mediaPlayer = new MediaPlayer(libVLC);
            using var media = new Media(libVLC, videoPath, FromType.FromPath);
            
            // 解析媒体信息
            await media.Parse(MediaParseOptions.ParseNetwork);
            
            // 等待解析完成
            var timeout = DateTime.Now.AddSeconds(10);
            while (media.ParsedStatus != MediaParsedStatus.Done && DateTime.Now < timeout)
            {
                await Task.Delay(100);
            }
            
            if (media.ParsedStatus != MediaParsedStatus.Done)
            {
                return null;
            }
            
            var fileInfo = new FileInfo(videoPath);
            var metadata = new VideoMetadata
            {
                Duration = media.Duration,
                FileSize = fileInfo.Length,
                OriginalFormat = Path.GetExtension(videoPath).ToLowerInvariant()
            };
            
            // 获取轨道信息
            var tracks = media.Tracks;
            foreach (var track in tracks)
            {
                if (track.TrackType == TrackType.Video)
                {
                    metadata.VideoTrackCount++;
                    if (track.Data.Video.Width > 0 && track.Data.Video.Height > 0)
                    {
                        metadata.Width = (int)track.Data.Video.Width;
                        metadata.Height = (int)track.Data.Video.Height;
                        if (track.Data.Video.FrameRateNum > 0 && track.Data.Video.FrameRateDen > 0)
                        {
                            metadata.FrameRate = (double)track.Data.Video.FrameRateNum / track.Data.Video.FrameRateDen;
                        }
                    }
                }
                else if (track.TrackType == TrackType.Audio)
                {
                    metadata.AudioTrackCount++;
                }
            }
            
            return metadata;
        }
        catch (Exception)
        {
            // 如果提取失败，返回基本信息
            try
            {
                var fileInfo = new FileInfo(videoPath);
                return new VideoMetadata
                {
                    FileSize = fileInfo.Length,
                    OriginalFormat = Path.GetExtension(videoPath).ToLowerInvariant()
                };
            }
            catch
            {
                return null;
            }
        }
    }
    

    /// <summary>
    /// 加密视频文件（带进度回调）
    /// </summary>
    /// <param name="inputPath">输入视频文件路径</param>
    /// <param name="outputPath">输出加密文件路径</param>
    /// <param name="password">加密密码</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="preserveHeaderSize">保留的原始文件头大小（默认自动检测）</param>
    public async Task EncryptVideoWithProgressAsync(string inputPath, string outputPath, string password, 
        IProgress<EncryptionProgress>? progressCallback = null, 
        CancellationToken cancellationToken = default,
        int? preserveHeaderSize = null)
    {
        // 报告元数据提取进度
        progressCallback?.Report(new EncryptionProgress
        {
            ProcessedBytes = 0,
            TotalBytes = 0,
            Percentage = 0,
            Status = "正在提取视频元数据..."
        });
        
        // 提取视频元数据
        var metadata = await ExtractVideoMetadataAsync(inputPath);
        
        using var inputStream = File.OpenRead(inputPath);
        using var outputStream = File.Create(outputPath);
        
        var totalBytes = inputStream.Length;
        long processedBytes = 0;

        // 检测视频格式并确定需要保留的文件头大小
        var headerSize = preserveHeaderSize ?? DetectVideoHeaderSize(inputStream);
        
        // 生成加密密钥和IV
        var (key, iv) = GenerateKeyAndIv(password);
        
        // 读取并保留原始文件头
        inputStream.Position = 0;
        var originalHeader = new byte[headerSize];
        await inputStream.ReadExactlyAsync(originalHeader, cancellationToken);
        processedBytes += headerSize;
        
        // 写入原始文件头（不加密）
        await outputStream.WriteAsync(originalHeader, cancellationToken);
        
        // 写入加密信息头（包含元数据）
        var encryptionHeader = CreateEncryptionHeader(key, iv, headerSize, metadata);
        await outputStream.WriteAsync(encryptionHeader, cancellationToken);
        
        // 报告初始进度
        progressCallback?.Report(new EncryptionProgress
        {
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes,
            Percentage = (double)processedBytes / totalBytes * 100,
            Status = "正在准备加密..."
        });
        
        // 加密剩余的视频数据（带进度回调）
        await EncryptVideoDataWithProgressAsync(inputStream, outputStream, key, iv, 
            totalBytes, processedBytes, progressCallback, cancellationToken);
    }
    
    /// <summary>
    /// 检测视频文件头大小
    /// </summary>
    private int DetectVideoHeaderSize(Stream stream)
    {
        stream.Position = 0;
        var buffer = new byte[32];
        stream.Read(buffer, 0, buffer.Length);
        
        // 检测常见视频格式的文件头
        var header = Encoding.ASCII.GetString(buffer, 0, Math.Min(12, buffer.Length));
        
        return header switch
        {
            var h when h.StartsWith("ftyp") => 32,  // MP4
            var h when h.Contains("AVI ") => 12,    // AVI
            var h when h.Contains("EBML") => 40,    // MKV/WebM
            var h when h.Contains("FLV") => 9,      // FLV
            _ => 32 // 默认保留32字节
        };
    }
    
    /// <summary>
    /// 生成加密密钥和初始向量
    /// </summary>
    private (byte[] key, byte[] iv) GenerateKeyAndIv(string password)
    {
        // 使用PBKDF2从密码生成密钥
        using var pbkdf2 = new Rfc2898DeriveBytes(password, 
            Encoding.UTF8.GetBytes("SecretVideoSalt2024"), 10000, HashAlgorithmName.SHA256);
        
        var key = pbkdf2.GetBytes(32); // AES-256
        var iv = pbkdf2.GetBytes(16);  // AES块大小
        
        return (key, iv);
    }
    
    /// <summary>
    /// 创建加密信息头（包含元数据）
    /// </summary>
    private byte[] CreateEncryptionHeader(byte[] key, byte[] iv, int originalHeaderSize, VideoMetadata? metadata)
    {
        var header = new byte[ENCRYPTION_HEADER_SIZE];
        var offset = 0;
        
        // 写入魔数
        var magicBytes = Encoding.ASCII.GetBytes(MAGIC_HEADER);
        Array.Copy(magicBytes, 0, header, offset, magicBytes.Length);
        offset += 8;
        
        // 写入版本号（版本2支持元数据）
        BitConverter.GetBytes((uint)2).CopyTo(header, offset);
        offset += 4;
        
        // 写入原始文件头大小
        BitConverter.GetBytes(originalHeaderSize).CopyTo(header, offset);
        offset += 4;
        
        // 写入IV（16字节）
        Array.Copy(iv, 0, header, offset, 16);
        offset += 16;
        
        // 写入密钥哈希（用于验证，32字节）
        using var sha256 = SHA256.Create();
        var keyHash = sha256.ComputeHash(key);
        Array.Copy(keyHash, 0, header, offset, 32);
        offset += 32;
        
        // 写入元数据（如果存在）
        if (metadata != null)
        {
            try
            {
                var metadataJson = JsonSerializer.Serialize(metadata);
                var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);
                
                // 写入元数据长度（4字节）
                BitConverter.GetBytes(metadataBytes.Length).CopyTo(header, offset);
                offset += 4;
                
                // 写入元数据内容（最多MAX_METADATA_SIZE - 4字节）
                var maxMetadataContentSize = MAX_METADATA_SIZE - 4;
                var actualMetadataSize = Math.Min(metadataBytes.Length, maxMetadataContentSize);
                Array.Copy(metadataBytes, 0, header, offset, actualMetadataSize);
            }
            catch
            {
                // 如果序列化失败，写入0长度
                BitConverter.GetBytes(0).CopyTo(header, offset);
            }
        }
        else
        {
            // 没有元数据，写入0长度
            BitConverter.GetBytes(0).CopyTo(header, offset);
        }
        
        return header;
    }

    /// <summary>
    /// 使用AES-CTR模式加密视频数据（带进度回调）
    /// </summary>
    private async Task EncryptVideoDataWithProgressAsync(Stream inputStream, Stream outputStream, byte[] key, byte[] iv,
        long totalBytes, long initialProcessedBytes, IProgress<EncryptionProgress>? progressCallback, CancellationToken cancellationToken)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB; // CTR模式需要手动实现
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        
        var buffer = new byte[64 * 1024]; // 64KB缓冲区
        var counter = new byte[16];
        Array.Copy(iv, counter, 16);
        
        long position = 0;
        long processedBytes = initialProcessedBytes;
        int bytesRead;
        
        while ((bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 生成CTR模式的密钥流
            var keyStream = GenerateCtrKeyStream(aes, counter, bytesRead);
            
            // XOR加密
            for (int i = 0; i < bytesRead; i++)
            {
                buffer[i] ^= keyStream[i];
            }
            
            await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            
            // 更新计数器和进度
            position += bytesRead;
            processedBytes += bytesRead;
            UpdateCtrCounter(counter, iv, position);
            
            // 报告进度
            var percentage = (double)processedBytes / totalBytes * 100;
            progressCallback?.Report(new EncryptionProgress
            {
                ProcessedBytes = processedBytes,
                TotalBytes = totalBytes,
                Percentage = percentage,
                Status = $"正在加密... {percentage:F1}%"
            });
        }
    }
    
    /// <summary>
    /// 生成CTR模式的密钥流
    /// </summary>
    private byte[] GenerateCtrKeyStream(Aes aes, byte[] counter, int length)
    {
        var keyStream = new byte[length];
        var blockSize = 16; // AES块大小
        var blocks = (length + blockSize - 1) / blockSize;
        
        using var encryptor = aes.CreateEncryptor();
        
        // 创建计数器的副本，避免修改原始计数器
        var workingCounter = new byte[16];
        Array.Copy(counter, workingCounter, 16);
        
        for (int i = 0; i < blocks; i++)
        {
            var currentCounter = new byte[16];
            Array.Copy(workingCounter, currentCounter, 16);
            
            // 加密计数器
            var encryptedCounter = new byte[16];
            encryptor.TransformBlock(currentCounter, 0, 16, encryptedCounter, 0);
            
            // 复制到密钥流
            var copyLength = Math.Min(blockSize, length - i * blockSize);
            Array.Copy(encryptedCounter, 0, keyStream, i * blockSize, copyLength);
            
            // 递增工作计数器（不修改原始计数器）
            IncrementCounter(workingCounter);
        }
        
        return keyStream;
    }
    
    /// <summary>
    /// 更新CTR计数器
    /// </summary>
    private void UpdateCtrCounter(byte[] counter, byte[] iv, long position)
    {
        Array.Copy(iv, counter, 16);
        var blockNumber = position / 16;
        
        // 将块号添加到计数器的低8字节
        var blockBytes = BitConverter.GetBytes(blockNumber);
        for (int i = 0; i < 8 && i < blockBytes.Length; i++)
        {
            counter[15 - i] = blockBytes[i];
        }
    }
    
    /// <summary>
    /// 递增计数器
    /// </summary>
    private void IncrementCounter(byte[] counter)
    {
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0)
                break;
        }
    }
    
    /// <summary>
    /// 验证加密文件
    /// </summary>
    /// <param name="filePath">加密文件路径</param>
    /// <returns>是否为有效的加密视频文件</returns>
    public bool IsEncryptedVideo(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return IsEncryptedVideo(stream);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 验证加密流
    /// </summary>
    public bool IsEncryptedVideo(Stream stream)
    {
        if (stream.Length < ENCRYPTION_HEADER_SIZE + 32)
            return false;
        
        try
        {
            var originalPosition = stream.Position;
            
            // 跳过可能的原始文件头，查找加密头
            stream.Position = 32; // 假设最大文件头为32字节
            
            var buffer = new byte[8];
            stream.Read(buffer, 0, 8);
            
            var magic = Encoding.ASCII.GetString(buffer);
            var isValid = magic == MAGIC_HEADER || magic == "SECVID01"; // 支持旧版本
            
            stream.Position = originalPosition;
            return isValid;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 获取加密文件信息
    /// </summary>
    public EncryptedVideoInfo GetEncryptedVideoInfo(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return GetEncryptedVideoInfo(stream);
    }
    
    /// <summary>
    /// 获取加密流信息
    /// </summary>
    public EncryptedVideoInfo GetEncryptedVideoInfo(Stream stream)
    {
        var originalPosition = stream.Position;
        
        try
        {
            // 查找加密头
            var headerPosition = FindEncryptionHeader(stream);
            if (headerPosition == -1)
                throw new InvalidOperationException("未找到有效的加密头");
            
            stream.Position = headerPosition;
            
            var header = new byte[ENCRYPTION_HEADER_SIZE];
            stream.ReadExactly(header);
            
            var info = new EncryptedVideoInfo
            {
                Magic = Encoding.ASCII.GetString(header, 0, 8),
                Version = BitConverter.ToUInt32(header, 8),
                OriginalHeaderSize = BitConverter.ToInt32(header, 12),
                IV = new byte[16],
                KeyHash = new byte[32],
                EncryptionHeaderPosition = headerPosition,
                EncryptedDataPosition = headerPosition + ENCRYPTION_HEADER_SIZE
            };
            
            Array.Copy(header, 16, info.IV, 0, 16);
            Array.Copy(header, 32, info.KeyHash, 0, 32);
            
            // 如果是版本2，尝试读取元数据
            if (info.Version >= 2)
            {
                try
                {
                    var metadataLengthOffset = 64; // 基础头部大小
                    var metadataLength = BitConverter.ToInt32(header, metadataLengthOffset);
                    
                    if (metadataLength > 0 && metadataLength <= MAX_METADATA_SIZE - 4)
                    {
                        var metadataOffset = metadataLengthOffset + 4;
                        var metadataBytes = new byte[metadataLength];
                        Array.Copy(header, metadataOffset, metadataBytes, 0, metadataLength);
                        
                        var metadataJson = Encoding.UTF8.GetString(metadataBytes);
                        info.Metadata = JsonSerializer.Deserialize<VideoMetadata>(metadataJson);
                    }
                }
                catch
                {
                    // 如果读取元数据失败，继续处理但不设置元数据
                    info.Metadata = null;
                }
            }
            
            return info;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
    
    /// <summary>
    /// 查找加密头位置
    /// </summary>
    private long FindEncryptionHeader(Stream stream)
    {
        var magicBytes = Encoding.ASCII.GetBytes(MAGIC_HEADER);
        var buffer = new byte[1024];
        
        stream.Position = 0;
        
        while (stream.Position < stream.Length - ENCRYPTION_HEADER_SIZE)
        {
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;
            
            for (int i = 0; i <= bytesRead - magicBytes.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < magicBytes.Length; j++)
                {
                    if (buffer[i + j] != magicBytes[j])
                    {
                        found = false;
                        break;
                    }
                }
                
                if (found)
                {
                    return stream.Position - bytesRead + i;
                }
            }
            
            // 回退一些字节以防魔数跨越缓冲区边界
            if (stream.Position < stream.Length)
            {
                stream.Position -= magicBytes.Length - 1;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// 验证密码是否正确
    /// </summary>
    public bool ValidatePassword(string encryptedFilePath, string password)
    {
        try
        {
            var videoInfo = GetEncryptedVideoInfo(encryptedFilePath);
            var (key, _) = GenerateKeyAndIv(password);
            
            // 验证密钥哈希
            using var sha256 = SHA256.Create();
            var keyHash = sha256.ComputeHash(key);
            
            return keyHash.SequenceEqual(videoInfo.KeyHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解密视频到内存流
    /// </summary>
    public async Task<MemoryStream> DecryptToStreamAsync(string encryptedFilePath, string password, 
        IProgress<(long processed, long total, string message)>? progress = null)
    {
        var videoInfo = GetEncryptedVideoInfo(encryptedFilePath);
        var (key, _) = GenerateKeyAndIv(password);
        
        
        // 验证密码
        using var sha256 = SHA256.Create();
        var keyHash = sha256.ComputeHash(key);
        if (!keyHash.SequenceEqual(videoInfo.KeyHash))
        {
            throw new UnauthorizedAccessException("密码错误");
        }

        using var inputStream = File.OpenRead(encryptedFilePath);
        
        // 计算总的输出大小：原始头部 + 解密后的数据
        var encryptedDataLength = inputStream.Length - videoInfo.EncryptedDataPosition;
        var totalOutputSize = videoInfo.OriginalHeaderSize + encryptedDataLength;
        var outputStream = new MemoryStream((int)totalOutputSize);

        // 首先写入原始视频头部（未加密部分）
        inputStream.Position = 0;
        var originalHeader = new byte[videoInfo.OriginalHeaderSize];
        await inputStream.ReadExactlyAsync(originalHeader);
        await outputStream.WriteAsync(originalHeader);
        
        // 报告头部写入进度
        progress?.Report((videoInfo.OriginalHeaderSize, totalOutputSize, "正在写入视频头部..."));

        // 然后解密并写入视频数据
        inputStream.Position = videoInfo.EncryptedDataPosition;

        const int bufferSize = 64 * 1024; // 64KB 缓冲区
        
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB; // 使用ECB模式手动实现CTR
        aes.Padding = PaddingMode.None;

        var buffer = new byte[bufferSize];
        var counter = new byte[16];
        Array.Copy(videoInfo.IV, counter, 16); // 初始化计数器为IV
        long totalProcessed = videoInfo.OriginalHeaderSize; // 已处理的字节数包括头部
        long encryptedDataProcessed = 0; // 已处理的加密数据字节数

        while (encryptedDataProcessed < encryptedDataLength)
        {
            var remainingEncryptedData = encryptedDataLength - encryptedDataProcessed;
            var toRead = (int)Math.Min(bufferSize, remainingEncryptedData);
            var bytesRead = await inputStream.ReadAsync(buffer, 0, toRead);

            if (bytesRead == 0) break;

            // 生成CTR模式的密钥流（使用当前计数器）
            var keyStream = GenerateCtrKeyStream(aes, counter, bytesRead);
            
            // XOR解密（CTR模式加密和解密是相同的操作）
            for (int i = 0; i < bytesRead; i++)
            {
                buffer[i] ^= keyStream[i];
            }
            
            // 写入解密后的数据
            await outputStream.WriteAsync(buffer, 0, bytesRead);

            // 更新位置和计数器（在处理完数据后）
            encryptedDataProcessed += bytesRead;
            totalProcessed += bytesRead;
            UpdateCtrCounter(counter, videoInfo.IV, encryptedDataProcessed);

            // 报告进度
            progress?.Report((totalProcessed, totalOutputSize, $"解密进度: {totalProcessed}/{totalOutputSize} 字节"));

            // 让出CPU时间
            if (totalProcessed % (bufferSize * 10) == 0)
            {
                await Task.Delay(1);
            }
        }

        // 重置流位置到开始
        outputStream.Position = 0;
        return outputStream;
    }
}

/// <summary>
/// 视频元数据信息
/// </summary>
public class VideoMetadata
{
    public long Duration { get; set; } = 0;           // 视频时长（毫秒）
    public int Width { get; set; } = 0;               // 视频宽度
    public int Height { get; set; } = 0;              // 视频高度
    public double FrameRate { get; set; } = 0;        // 帧率
    public int VideoTrackCount { get; set; } = 0;     // 视频轨道数
    public int AudioTrackCount { get; set; } = 0;     // 音频轨道数
    public string VideoCodec { get; set; } = string.Empty;  // 视频编码
    public string AudioCodec { get; set; } = string.Empty;  // 音频编码
    public long FileSize { get; set; } = 0;           // 原始文件大小
    public string OriginalFormat { get; set; } = string.Empty; // 原始格式
}

/// <summary>
/// 加密视频文件信息
/// </summary>
public class EncryptedVideoInfo
{
    public string Magic { get; set; } = string.Empty;
    public uint Version { get; set; }
    public int OriginalHeaderSize { get; set; }
    public byte[] IV { get; set; } = Array.Empty<byte>();
    public byte[] KeyHash { get; set; } = Array.Empty<byte>();
    public long EncryptionHeaderPosition { get; set; }
    public long EncryptedDataPosition { get; set; }
    
    // 新增：视频元数据信息
    public VideoMetadata? Metadata { get; set; }
    public bool HasMetadata => Metadata != null;
}