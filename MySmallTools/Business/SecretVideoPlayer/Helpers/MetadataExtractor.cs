using System.Text.Json;
using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer.Helpers;

/// <summary>
/// 使用插件私有 LibVLC 解析普通视频文件元数据。
/// </summary>
/// <remarks>
/// 与安全播放器共用 <see cref="LibVlcRuntime"/>，确保两条调用路径不会分别从宿主根目录或系统环境加载不同版本的 VLC。
/// 元数据解析失败时只返回文件大小和扩展名，避免辅助信息失败阻断主加密流程。
/// </remarks>
public class MetadataExtractor
{
    /// <summary>
    /// 提取视频文件元数据
    /// </summary>
    public async Task<VideoMetadata?> ExtractVideoMetadataAsync(string videoPath)
    {
        try
        {
            LibVlcRuntime.EnsureInitialized();
            
            using var libVLC = new LibVLC();
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
}
