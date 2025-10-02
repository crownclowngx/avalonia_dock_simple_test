using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MySmallTools.Business.SecretVideoPlayer.Helpers;

/// <summary>
/// 加密头处理助手
/// </summary>
public class HeaderHelper
{
    public const int BASIC_HEADER_SIZE = 64; // 基础加密信息头大小
    public const int MAX_METADATA_SIZE = 512; // 元数据最大大小
    public const int ENCRYPTION_HEADER_SIZE = BASIC_HEADER_SIZE + MAX_METADATA_SIZE; // 总加密信息头大小
    public const string MAGIC_HEADER = "SECVID02"; // 魔数标识（版本2支持元数据）

    /// <summary>
    /// 创建加密信息头（包含元数据）
    /// </summary>
    public byte[] CreateEncryptionHeader(byte[] key, byte[] iv, int originalHeaderSize, VideoMetadata? metadata)
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
    /// 查找加密头位置
    /// </summary>
    public long FindEncryptionHeader(Stream stream)
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
    /// 检测视频文件头大小
    /// </summary>
    public int DetectVideoHeaderSize(Stream stream)
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
}