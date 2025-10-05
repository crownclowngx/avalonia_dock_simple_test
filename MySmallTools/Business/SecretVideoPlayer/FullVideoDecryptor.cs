using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MySmallTools.Models.SecretVideoPlayer;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 一次性完整视频解密器
    /// 将整个加密视频解密到内存流中，避免实时解密的复杂性
    /// </summary>
    public class FullVideoDecryptor : IDisposable
    {
        private readonly string _encryptedFilePath;
        private readonly string _password;
        private EncryptedVideoInfo? _videoInfo;
        private MemoryStream? _decryptedStream;
        private bool _disposed = false;

        public FullVideoDecryptor(string encryptedFilePath, string password)
        {
            _encryptedFilePath = encryptedFilePath ?? throw new ArgumentNullException(nameof(encryptedFilePath));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        /// <summary>
        /// 获取解密后的视频流
        /// </summary>
        public Stream? DecryptedStream => _decryptedStream;

        /// <summary>
        /// 获取视频信息
        /// </summary>
        public EncryptedVideoInfo? VideoInfo => _videoInfo;

        /// <summary>
        /// 解密进度事件
        /// </summary>
        public event EventHandler<DecryptionProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// 异步解密整个视频文件
        /// </summary>
        public async Task<bool> DecryptVideoAsync()
        {
            try
            {
                // 1. 读取视频信息
                if (!await LoadVideoInfoAsync())
                {
                    return false;
                }

                // 2. 验证密码
                if (!ValidatePassword())
                {
                    return false;
                }

                // 3. 执行完整解密
                return await PerformFullDecryptionAsync();
            }
            catch (Exception ex)
            {
                OnProgressChanged($"解密失败: {ex.Message}", 0, true);
                return false;
            }
        }

        /// <summary>
        /// 加载视频信息
        /// </summary>
        private async Task<bool> LoadVideoInfoAsync()
        {
            try
            {
                OnProgressChanged("正在读取视频信息...", 0);
                var videoInfo = await Task.Run(() =>
                {
                    var encryptor = new SmartVideoEncryptor();
                    return encryptor.GetEncryptedVideoInfo(_encryptedFilePath);
                });
                _videoInfo = videoInfo;
                OnProgressChanged("视频信息读取完成", 10);
                return true;
            }
            catch (Exception ex)
            {
                OnProgressChanged($"读取视频信息失败: {ex.Message}", 0, true);
                return false;
            }
        }

        /// <summary>
        /// 验证密码
        /// </summary>
        private bool ValidatePassword()
        {
            try
            {
                OnProgressChanged("验证密码...", 15);

                var encryptor = new SmartVideoEncryptor();
                var key = GenerateDecryptionKey();

                // 使用SmartVideoEncryptor验证密码
                var isValid = encryptor.ValidatePassword(_encryptedFilePath, _password);
                
                if (isValid)
                {
                    OnProgressChanged("密码验证成功", 20);
                }
                else
                {
                    OnProgressChanged("密码错误或文件损坏", 20, true);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                OnProgressChanged($"密码验证失败: {ex.Message}", 20, true);
                return false;
            }
        }

        /// <summary>
        /// 执行完整解密
        /// </summary>
        private async Task<bool> PerformFullDecryptionAsync()
        {
            try
            {
                OnProgressChanged("开始解密视频...", 25);

                var encryptor = new SmartVideoEncryptor();
                
                // 创建进度回调
                var progress = new Progress<(long processed, long total, string message)>(p =>
                {
                    var progressPercent = 25 + (int)((p.processed * 70) / p.total);
                    OnProgressChanged(p.message, progressPercent);
                });

                // 使用SmartVideoEncryptor解密到内存流
                _decryptedStream = await encryptor.DecryptToStreamAsync(_encryptedFilePath, _password, progress);
                encryptor=null;
                OnProgressChanged($"解密完成! 总大小: {_decryptedStream.Length} 字节", 100);
                return true;
            }
            catch (Exception ex)
            {
                OnProgressChanged($"解密过程失败: {ex.Message}", 0, true);
                return false;
            }
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
        /// 触发进度更新事件
        /// </summary>
        private void OnProgressChanged(string message, int percentage, bool isError = false)
        {
            ProgressChanged?.Invoke(this, new DecryptionProgressEventArgs
            {
                Message = message,
                Percentage = percentage,
                IsError = isError
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _decryptedStream?.Dispose();
                _decryptedStream = null;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 解密进度事件参数
    /// </summary>
    public class DecryptionProgressEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public int Percentage { get; set; }
        public bool IsError { get; set; }
    }
}