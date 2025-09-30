using System.Security.Cryptography;
using System.Text;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 智能视频加密器 - 支持AES-CTR模式加密，保护文件头确保格式兼容性
/// </summary>
public class SmartVideoEncryptor
{
    private const int ENCRYPTION_HEADER_SIZE = 64; // 加密信息头大小
    private const string MAGIC_HEADER = "SECVID01"; // 魔数标识
    
    /// <summary>
    /// 加密视频文件
    /// </summary>
    /// <param name="inputPath">输入视频文件路径</param>
    /// <param name="outputPath">输出加密文件路径</param>
    /// <param name="password">加密密码</param>
    /// <param name="preserveHeaderSize">保留的原始文件头大小（默认自动检测）</param>
    public async Task EncryptVideoAsync(string inputPath, string outputPath, string password, int? preserveHeaderSize = null)
    {
        using var inputStream = File.OpenRead(inputPath);
        using var outputStream = File.Create(outputPath);
        
        // 检测视频格式并确定需要保留的文件头大小
        var headerSize = preserveHeaderSize ?? DetectVideoHeaderSize(inputStream);
        
        // 生成加密密钥和IV
        var (key, iv) = GenerateKeyAndIv(password);
        
        // 读取并保留原始文件头
        inputStream.Position = 0;
        var originalHeader = new byte[headerSize];
        await inputStream.ReadExactlyAsync(originalHeader);
        
        // 写入原始文件头（不加密）
        await outputStream.WriteAsync(originalHeader);
        
        // 写入加密信息头
        var encryptionHeader = CreateEncryptionHeader(key, iv, headerSize);
        await outputStream.WriteAsync(encryptionHeader);
        
        // 加密剩余的视频数据
        await EncryptVideoDataAsync(inputStream, outputStream, key, iv);
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
    /// 创建加密信息头
    /// </summary>
    private byte[] CreateEncryptionHeader(byte[] key, byte[] iv, int originalHeaderSize)
    {
        var header = new byte[ENCRYPTION_HEADER_SIZE];
        var offset = 0;
        
        // 写入魔数
        var magicBytes = Encoding.ASCII.GetBytes(MAGIC_HEADER);
        Array.Copy(magicBytes, 0, header, offset, magicBytes.Length);
        offset += 8;
        
        // 写入版本号
        BitConverter.GetBytes((uint)1).CopyTo(header, offset);
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
        
        return header;
    }
    
    /// <summary>
    /// 使用AES-CTR模式加密视频数据
    /// </summary>
    private async Task EncryptVideoDataAsync(Stream inputStream, Stream outputStream, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB; // CTR模式需要手动实现
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        
        var buffer = new byte[64 * 1024]; // 64KB缓冲区
        var counter = new byte[16];
        Array.Copy(iv, counter, 16);
        
        long position = 0;
        int bytesRead;
        
        while ((bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            // 生成CTR模式的密钥流
            var keyStream = GenerateCtrKeyStream(aes, counter, bytesRead);
            
            // XOR加密
            for (int i = 0; i < bytesRead; i++)
            {
                buffer[i] ^= keyStream[i];
            }
            
            await outputStream.WriteAsync(buffer, 0, bytesRead);
            
            // 更新计数器
            position += bytesRead;
            UpdateCtrCounter(counter, iv, position);
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
        
        for (int i = 0; i < blocks; i++)
        {
            var currentCounter = new byte[16];
            Array.Copy(counter, currentCounter, 16);
            
            // 加密计数器
            var encryptedCounter = new byte[16];
            encryptor.TransformBlock(currentCounter, 0, 16, encryptedCounter, 0);
            
            // 复制到密钥流
            var copyLength = Math.Min(blockSize, length - i * blockSize);
            Array.Copy(encryptedCounter, 0, keyStream, i * blockSize, copyLength);
            
            // 递增计数器
            IncrementCounter(counter);
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
            var isValid = magic == MAGIC_HEADER;
            
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
}