using LibVLCSharp.Shared;
using System;
using System.IO;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 优化的LibVLC播放器配置
    /// 专门针对内存流播放进行优化
    /// </summary>
    public static class OptimizedLibVLCPlayer
    {
        /// <summary>
        /// 创建优化的LibVLC实例，专门用于内存流播放
        /// </summary>
        public static LibVLC CreateOptimizedLibVLC()
        {
            var options = new string[]
            {
                // 网络缓冲设置
                "--network-caching=0",          // 禁用网络缓冲（我们是本地内存）
                "--file-caching=0",             // 禁用文件缓冲（我们是内存流）
                "--live-caching=0",             // 禁用直播缓冲
                
                // 解码器优化
                "--avcodec-fast",               // 启用快速解码
                "--avcodec-skiploopfilter=4",   // 跳过循环滤波器以提高性能
                "--avcodec-skip-frame=0",       // 不跳过帧
                "--avcodec-skip-idct=0",        // 不跳过IDCT
                
                // 输出优化
                "--no-audio",                   // 如果不需要音频，可以禁用
                "--vout=direct3d11",            // 使用硬件加速视频输出
                "--aout=directsound",           // 使用DirectSound音频输出
                
                // 线程优化
                "--avcodec-threads=0",          // 自动检测CPU核心数
                
                // 预读取优化
                "--prefetch-buffer-size=0",     // 禁用预读取缓冲
                "--prefetch-read-size=0",       // 禁用预读取
                
                // 其他性能优化
                "--no-stats",                   // 禁用统计信息收集
                "--no-osd",                     // 禁用屏幕显示
                "--disable-screensaver",        // 禁用屏保
                "--no-video-title-show",        // 不显示视频标题
                
                // 内存优化
                "--no-plugins-cache",           // 禁用插件缓存
                "--reset-plugins-cache",        // 重置插件缓存
            };
            
            return new LibVLC(options);
        }
        
        /// <summary>
        /// 创建优化的MediaPlayer实例
        /// </summary>
        public static MediaPlayer CreateOptimizedMediaPlayer(LibVLC libVLC)
        {
            var player = new MediaPlayer(libVLC);
            
            // 设置播放器选项
            player.EnableHardwareDecoding = true;  // 启用硬件解码
            player.Volume = 100;                   // 设置音量
            
            return player;
        }
        
        /// <summary>
        /// 为内存流创建优化的Media对象
        /// </summary>
        public static Media CreateOptimizedMemoryMedia(LibVLC libVLC, byte[] data, bool enableSeeking = false)
        {
            MediaInput mediaInput;
            
            if (enableSeeking)
            {
                // 使用优化的可寻址版本
                mediaInput = new OptimizedSeekableMemoryMediaInput(data);
            }
            else
            {
                // 使用非可寻址版本（通常性能更好）
                mediaInput = new NonSeekableMemoryMediaInput(data);
            }
            
            var media = new Media(libVLC, mediaInput);
            
            // 设置媒体选项
            media.AddOption(":no-audio-display");      // 不显示音频可视化
            media.AddOption(":no-video-title-show");   // 不显示视频标题
            media.AddOption(":no-snapshot-preview");   // 不显示快照预览
            
            return media;
        }
    }
}