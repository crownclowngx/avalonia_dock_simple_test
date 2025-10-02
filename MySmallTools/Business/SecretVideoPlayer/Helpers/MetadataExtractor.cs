using System.Text.Json;
using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer.Helpers;

/// <summary>
/// 视频元数据提取器
/// </summary>
public class MetadataExtractor
{
    /// <summary>
    /// 提取视频文件元数据
    /// </summary>
    public async Task<VideoMetadata?> ExtractVideoMetadataAsync(string videoPath)
    {
        try
        {
            // 确保LibVLC已初始化
            Core.Initialize();
            
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