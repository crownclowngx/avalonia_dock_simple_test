namespace BiliDownloader.Messages;

/// <summary>
/// Tool -> Document：任务已被删除通知
/// </summary>
public class DownloadTaskDeletedMessage
{
    /// <summary>
    /// 目标 Document 实例 ID
    /// </summary>
    public string TargetDocumentId { get; }

    /// <summary>
    /// 被删除的任务 ID
    /// </summary>
    public string TaskId { get; }

    public DownloadTaskDeletedMessage(string targetDocumentId, string taskId)
    {
        TargetDocumentId = targetDocumentId;
        TaskId = taskId;
    }
}
