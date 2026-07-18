using BiliDownloader.Models;

namespace BiliDownloader.Services;

/// <summary>
/// 下载任务状态机：集中定义合法状态转换路径
/// </summary>
public static class DownloadTaskStateMachine
{
    /// <summary>
    /// 合法的状态转换表
    /// </summary>
    private static readonly HashSet<(DownloadTaskStatus From, DownloadTaskStatus To)> ValidTransitions = new()
    {
        // 正向流程
        (DownloadTaskStatus.Ready, DownloadTaskStatus.FetchingMetadata),
        (DownloadTaskStatus.FetchingMetadata, DownloadTaskStatus.DownloadingVideo),
        (DownloadTaskStatus.DownloadingVideo, DownloadTaskStatus.VideoReady),
        (DownloadTaskStatus.VideoReady, DownloadTaskStatus.DownloadingAudio),
        (DownloadTaskStatus.DownloadingAudio, DownloadTaskStatus.AudioReady),
        (DownloadTaskStatus.AudioReady, DownloadTaskStatus.Merging),
        (DownloadTaskStatus.Merging, DownloadTaskStatus.Completed),

        // 从 Ready 直接开始下载（简化流程，兼容当前实现）
        (DownloadTaskStatus.Ready, DownloadTaskStatus.DownloadingVideo),
        (DownloadTaskStatus.Ready, DownloadTaskStatus.DownloadingAudio),
        (DownloadTaskStatus.Ready, DownloadTaskStatus.Merging),

        // 运行中 → 暂停/中断/失败/取消
        (DownloadTaskStatus.FetchingMetadata, DownloadTaskStatus.Paused),
        (DownloadTaskStatus.FetchingMetadata, DownloadTaskStatus.Interrupted),
        (DownloadTaskStatus.FetchingMetadata, DownloadTaskStatus.Failed),
        (DownloadTaskStatus.FetchingMetadata, DownloadTaskStatus.Canceled),

        (DownloadTaskStatus.DownloadingVideo, DownloadTaskStatus.Paused),
        (DownloadTaskStatus.DownloadingVideo, DownloadTaskStatus.Interrupted),
        (DownloadTaskStatus.DownloadingVideo, DownloadTaskStatus.Failed),
        (DownloadTaskStatus.DownloadingVideo, DownloadTaskStatus.Canceled),

        (DownloadTaskStatus.VideoReady, DownloadTaskStatus.Paused),
        (DownloadTaskStatus.VideoReady, DownloadTaskStatus.Interrupted),
        (DownloadTaskStatus.VideoReady, DownloadTaskStatus.Failed),
        (DownloadTaskStatus.VideoReady, DownloadTaskStatus.Canceled),

        (DownloadTaskStatus.DownloadingAudio, DownloadTaskStatus.Paused),
        (DownloadTaskStatus.DownloadingAudio, DownloadTaskStatus.Interrupted),
        (DownloadTaskStatus.DownloadingAudio, DownloadTaskStatus.Failed),
        (DownloadTaskStatus.DownloadingAudio, DownloadTaskStatus.Canceled),

        (DownloadTaskStatus.AudioReady, DownloadTaskStatus.Paused),
        (DownloadTaskStatus.AudioReady, DownloadTaskStatus.Interrupted),
        (DownloadTaskStatus.AudioReady, DownloadTaskStatus.Failed),
        (DownloadTaskStatus.AudioReady, DownloadTaskStatus.Canceled),

        (DownloadTaskStatus.Merging, DownloadTaskStatus.Paused),
        (DownloadTaskStatus.Merging, DownloadTaskStatus.Interrupted),
        (DownloadTaskStatus.Merging, DownloadTaskStatus.Failed),
        (DownloadTaskStatus.Merging, DownloadTaskStatus.Canceled),

        // 恢复路径
        (DownloadTaskStatus.Paused, DownloadTaskStatus.Ready),
        (DownloadTaskStatus.Failed, DownloadTaskStatus.Ready),       // 重试
        (DownloadTaskStatus.Interrupted, DownloadTaskStatus.Ready),  // 手动恢复
        (DownloadTaskStatus.Canceled, DownloadTaskStatus.Ready),     // 重新提交

        // 登录态变化
        (DownloadTaskStatus.Ready, DownloadTaskStatus.WaitingForLogin),
        (DownloadTaskStatus.WaitingForLogin, DownloadTaskStatus.Ready),
    };

    /// <summary>
    /// 验证状态转换是否合法
    /// </summary>
    public static bool TryTransition(DownloadTaskStatus from, DownloadTaskStatus to)
    {
        return ValidTransitions.Contains((from, to));
    }

    /// <summary>
    /// 获取下载流程中的下一个阶段
    /// </summary>
    public static DownloadTaskStatus GetNextStage(DownloadTaskStatus current) => current switch
    {
        DownloadTaskStatus.Ready => DownloadTaskStatus.DownloadingVideo,
        DownloadTaskStatus.DownloadingVideo => DownloadTaskStatus.VideoReady,
        DownloadTaskStatus.VideoReady => DownloadTaskStatus.DownloadingAudio,
        DownloadTaskStatus.DownloadingAudio => DownloadTaskStatus.AudioReady,
        DownloadTaskStatus.AudioReady => DownloadTaskStatus.Merging,
        DownloadTaskStatus.Merging => DownloadTaskStatus.Completed,
        _ => current,
    };
}
