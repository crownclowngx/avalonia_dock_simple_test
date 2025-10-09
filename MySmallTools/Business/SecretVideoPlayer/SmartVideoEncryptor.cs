using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySmallTools.Business.SecretVideoPlayer.Helpers;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 智能视频加密器 - 支持AES-CTR模式加密，保护文件头确保格式兼容性
/// </summary>
public class SmartVideoEncryptor
{
    private readonly AesCtrHelper _ctrHelper;
    private readonly MetadataExtractor _metadataExtractor;
    private readonly HeaderHelper _headerHelper;

    public SmartVideoEncryptor()
    {
        _ctrHelper = new AesCtrHelper();
        _metadataExtractor = new MetadataExtractor();
        _headerHelper = new HeaderHelper();
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
        var metadata = await _metadataExtractor.ExtractVideoMetadataAsync(inputPath);

        using var inputStream = File.OpenRead(inputPath);
        using var outputStream = File.Create(outputPath);

        var totalBytes = inputStream.Length;
        long processedBytes = 0;

        // 检测视频格式并确定需要保留的文件头大小
        var headerSize = preserveHeaderSize ?? _headerHelper.DetectVideoHeaderSize(inputStream);

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
        var encryptionHeader = _headerHelper.CreateEncryptionHeader(key, iv, headerSize, metadata);
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
    /// 生成加密密钥和初始向量
    /// </summary>
    private static (byte[] key, byte[] iv) GenerateKeyAndIv(string password)
    {
        // 使用PBKDF2从密码生成密钥
        using var pbkdf2 = new Rfc2898DeriveBytes(password,
            Encoding.UTF8.GetBytes("SecretVideoSalt2024"), 10000, HashAlgorithmName.SHA256);

        var key = pbkdf2.GetBytes(32); // AES-256
        var iv = pbkdf2.GetBytes(16); // AES块大小

        return (key, iv);
    }

    /// <summary>
    /// 使用AES-CTR模式加密视频数据（带进度回调）
    /// </summary>
    private async Task EncryptVideoDataWithProgressAsync(Stream inputStream, Stream outputStream, byte[] key, byte[] iv,
        long totalBytes, long initialProcessedBytes, IProgress<EncryptionProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        // 调用统一的CTR处理方法
        await ProcessCtrDataWithProgressAsync(
            inputStream,
            outputStream,
            key,
            iv,
            totalBytes - initialProcessedBytes, // 剩余需要加密的数据长度
            initialProcessedBytes,
            progressCallback,
            "正在加密...",
            cancellationToken
        );
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
        if (stream.Length < HeaderHelper.ENCRYPTION_HEADER_SIZE + 32)
            return false;

        try
        {
            var originalPosition = stream.Position;

            // 跳过可能的原始文件头，查找加密头
            stream.Position = 32; // 假设最大文件头为32字节

            var buffer = new byte[8];
            var bytesRead = stream.Read(buffer, 0, 8);

            if (bytesRead < 8)
            {
                stream.Position = originalPosition;
                return false;
            }

            var magic = Encoding.ASCII.GetString(buffer);
            var isValid = magic == HeaderHelper.MAGIC_HEADER;

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
            var headerPosition = _headerHelper.FindEncryptionHeader(stream);
            if (headerPosition == -1)
                throw new InvalidOperationException("未找到有效的加密头");

            stream.Position = headerPosition;

            var header = new byte[HeaderHelper.ENCRYPTION_HEADER_SIZE];
            stream.ReadExactly(header);

            var info = new EncryptedVideoInfo
            {
                Magic = Encoding.ASCII.GetString(header, 0, 8),
                Version = BitConverter.ToUInt32(header, 8),
                OriginalHeaderSize = BitConverter.ToInt32(header, 12),
                IV = new byte[16],
                KeyHash = new byte[32],
                EncryptionHeaderPosition = headerPosition,
                EncryptedDataPosition = headerPosition + HeaderHelper.ENCRYPTION_HEADER_SIZE
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

                    if (metadataLength > 0 && metadataLength <= HeaderHelper.MAX_METADATA_SIZE - 4)
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
    /// 验证密码是否正确
    /// </summary>
    public bool ValidatePassword(string encryptedFilePath, string password)
    {
        try
        {
            var videoInfo = GetEncryptedVideoInfo(encryptedFilePath);
            var (key, _) = GenerateKeyAndIv(password);
            return VerifyPassword(key, videoInfo.KeyHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 验证密码是否正确
    /// </summary>
    private bool VerifyPassword(byte[] key, byte[] keyHash)
    {
        using var sha256 = SHA256.Create();
        var computedHash = sha256.ComputeHash(key);
        return computedHash.SequenceEqual(keyHash);
    }

    /// <summary>
    /// 解密视频到内存流
    /// </summary>
    public async Task<MemoryStream> DecryptToStreamAsync(string encryptedFilePath, string password,
        IProgress<EncryptionProgress>? progress = null)
    {
        var videoInfo = GetEncryptedVideoInfo(encryptedFilePath);
        var (key, _) = GenerateKeyAndIv(password);

        // 验证密码
        if (!VerifyPassword(key, videoInfo.KeyHash))
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

        // 报告头部写入进度 - 使用统一的 EncryptionProgress 类型
        progress?.Report(new EncryptionProgress
        {
            ProcessedBytes = videoInfo.OriginalHeaderSize,
            TotalBytes = totalOutputSize,
            Percentage = (double)videoInfo.OriginalHeaderSize / totalOutputSize * 100,
            Status = "正在写入视频头部..."
        });

        // 然后解密并写入视频数据
        inputStream.Position = videoInfo.EncryptedDataPosition;

        // 调用统一的 CTR 处理方法
        await ProcessCtrDataWithProgressAsync(
            inputStream,
            outputStream,
            key,
            videoInfo.IV,
            encryptedDataLength,
            videoInfo.OriginalHeaderSize,
            progress,
            "解密中...",
            CancellationToken.None // 可以根据需要传入实际的取消令牌
        );

        // 重置流位置到开始
        outputStream.Position = 0;
        return outputStream;
    }

    /// <summary>
    /// 统一的 CTR 模式数据处理方法（同时适用于加密和解密）
    /// </summary>
    private async Task ProcessCtrDataWithProgressAsync(
        Stream inputStream,
        Stream outputStream,
        byte[] key,
        byte[] iv,
        long totalDataLength,
        long initialProcessedBytes,
        IProgress<EncryptionProgress>? progressCallback,
        string statusPrefix,
        CancellationToken cancellationToken = default)
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

        while (position < totalDataLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = totalDataLength - position;
            var toRead = (int)Math.Min(buffer.Length, remaining);
            bytesRead = await inputStream.ReadAsync(buffer, 0, toRead, cancellationToken);

            if (bytesRead == 0) break;

            // 生成CTR模式的密钥流
            var keyStream = _ctrHelper.GenerateCtrKeyStream(aes, counter, bytesRead);

            // XOR操作（CTR模式加密和解密相同）
            for (int i = 0; i < bytesRead; i++)
            {
                buffer[i] ^= keyStream[i];
            }

            await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

            // 更新计数器和进度
            position += bytesRead;
            processedBytes += bytesRead;
            _ctrHelper.UpdateCtrCounter(counter, iv, position);

            // 报告进度
            var totalBytes = initialProcessedBytes + totalDataLength;
            var percentage = (double)processedBytes / totalBytes * 100;
            progressCallback?.Report(new EncryptionProgress
            {
                ProcessedBytes = processedBytes,
                TotalBytes = totalBytes,
                Percentage = percentage,
                Status = $"{statusPrefix} {percentage:F1}%"
            });

            // 让出CPU时间（每处理10个缓冲区）
            if (processedBytes % (buffer.Length * 10) == 0)
            {
                await Task.Delay(1, cancellationToken);
            }
        }
    }
}

/// <summary>
/// 视频元数据信息
/// </summary>
public class VideoMetadata
{
    public long Duration { get; set; } = 0; // 视频时长（毫秒）
    public int Width { get; set; } = 0; // 视频宽度
    public int Height { get; set; } = 0; // 视频高度
    public double FrameRate { get; set; } = 0; // 帧率
    public int VideoTrackCount { get; set; } = 0; // 视频轨道数
    public int AudioTrackCount { get; set; } = 0; // 音频轨道数
    public string VideoCodec { get; set; } = string.Empty; // 视频编码
    public string AudioCodec { get; set; } = string.Empty; // 音频编码
    public long FileSize { get; set; } = 0; // 原始文件大小
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

/// <summary>
/// 加密进度信息
/// </summary>
public class EncryptionProgress
{
    /// <summary>
    /// 已处理字节数
    /// </summary>
    public long ProcessedBytes { get; set; }

    /// <summary>
    /// 总字节数
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// 完成百分比
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// 状态描述
    /// </summary>
    public string Status { get; set; } = string.Empty;
}