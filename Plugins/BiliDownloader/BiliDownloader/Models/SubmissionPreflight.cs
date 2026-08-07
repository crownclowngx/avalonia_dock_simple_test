namespace BiliDownloader.Models;

/// <summary>预检问题的严重程度；阻止项禁止整批提交，警告项必须由用户明确确认。</summary>
public enum PreflightIssueSeverity { Warning, Blocking }

/// <summary>机器可判断的问题码与面向用户的中文说明。</summary>
public sealed record PreflightIssue(
    string Code,
    PreflightIssueSeverity Severity,
    string Message,
    string? ItemId = null);

/// <summary>单个下载项经过冲突策略处理后的不可变结果。</summary>
public sealed record PreflightItemResult(
    DownloadSubmissionItem Item,
    string OutputFilePath,
    string OutputPathKey,
    bool ShouldSubmit,
    bool ShouldSkip,
    bool IsResume,
    string? ResumeTaskId,
    bool HasConflict,
    long EstimatedRequiredBytes,
    IReadOnlyList<PreflightIssue> Issues,
    MediaOutputPlan? OutputPlan = null);

/// <summary>
/// 提交预检报告。报告只描述检查时刻观察到的事实，不能代替 Coordinator 提交锁内的最终复检。
/// <para>
/// Fingerprint 会覆盖目录、文件长度和最后写入时间等冲突事实。用户确认后事实若变化，提交边界
/// 必须拒绝旧报告并重新预检，避免“检查时安全、入库时已变化”的 TOCTOU 覆盖漏洞。
/// </para>
/// </summary>
public sealed record SubmissionPreflightReport(
    DownloadSubmission Submission,
    IReadOnlyList<PreflightItemResult> Items,
    IReadOnlyList<PreflightIssue> GlobalIssues,
    string Fingerprint,
    long? AvailableBytes)
{
    public int ReadyCount => Items.Count(item => item.ShouldSubmit);
    public int SkipCount => Items.Count(item => item.ShouldSkip);
    public int WarningCount => GlobalIssues.Count(issue => issue.Severity == PreflightIssueSeverity.Warning)
        + Items.Sum(item => item.Issues.Count(issue => issue.Severity == PreflightIssueSeverity.Warning));
    public int BlockedCount => GlobalIssues.Count(issue => issue.Severity == PreflightIssueSeverity.Blocking)
        + Items.Count(item => item.Issues.Any(issue => issue.Severity == PreflightIssueSeverity.Blocking));
    public bool IsBlocked => BlockedCount > 0;
    public bool RequiresConfirmation => WarningCount > 0 || Items.Any(item => item.HasConflict);
}

/// <summary>已经通过用户确认、等待 Coordinator 原子提交的不可变批次。</summary>
public sealed record PreparedSubmission(SubmissionPreflightReport Report, bool UserConfirmed);

public enum SubmissionCommitStatus
{
    Committed,
    Blocked,
    Stale,
    /// <summary>增量预览之后任务身份发生变化，调用方必须刷新分类且不得沿用旧确认。</summary>
    StaleComparison,
}

/// <summary>提交结果明确区分成功、阻止和事实过期，ViewModel 不再猜测消息总线后台是否成功。</summary>
public sealed record SubmissionCommitResult(
    SubmissionCommitStatus Status,
    int SubmittedCount,
    int SkippedCount,
    string Message,
    IReadOnlyList<CommittedTaskReference>? CommittedTasks = null)
{
    /// <summary>兼容旧调用方的非空视图；成功提交时映射 Document 会话项与独立任务 ID。</summary>
    public IReadOnlyList<CommittedTaskReference> EffectiveCommittedTasks
        => CommittedTasks ?? Array.Empty<CommittedTaskReference>();
}
