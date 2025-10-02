namespace MySmallTools.Business.SecretVideoPlayer.Helpers;

using System.Security.Cryptography;

/// <summary>
/// AES-CTR模式加密解密助手
/// </summary>
public class AesCtrHelper
{
    /// <summary>
    /// 生成CTR模式的密钥流
    /// </summary>
    public byte[] GenerateCtrKeyStream(Aes aes, byte[] counter, int length)
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
    public void UpdateCtrCounter(byte[] counter, byte[] iv, long position)
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
    public void IncrementCounter(byte[] counter)
    {
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0)
                break;
        }
    }
}