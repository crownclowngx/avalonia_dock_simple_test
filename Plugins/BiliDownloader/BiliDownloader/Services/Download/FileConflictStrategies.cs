using BiliDownloader.Models;

namespace BiliDownloader.Services.Download;

/// <summary>冲突策略输入只包含已检查事实，不允许策略直接访问磁盘或数据库。</summary>
public sealed record FileConflictContext(
    DownloadSubmissionItem Item,
    string DesiredOutputPath,
    bool HasConflict,
    DownloadTaskRecord? ResumeCandidate,
    bool ResumeCandidateValid,
    string ResumeInvalidReason,
    Func<string> AllocateNumberedPath);

/// <summary>策略决策只描述如何处理单项，真正的路径保留仍由 Coordinator 负责。</summary>
public sealed record FileConflictDecision(
    string OutputFilePath,
    bool ShouldSubmit = true,
    bool ShouldSkip = false,
    bool IsResume = false,
    string? ResumeTaskId = null,
    IReadOnlyList<PreflightIssue>? Issues = null)
{
    public IReadOnlyList<PreflightIssue> EffectiveIssues => Issues ?? Array.Empty<PreflightIssue>();
}

/// <summary>
/// 文件冲突策略扩展点。新增策略只需实现此接口并注册，不需要改动预检编排器，
/// 但所有策略仍必须经过 Coordinator 的统一复检和路径保留，不能绕过安全边界。
/// </summary>
public interface IFileConflictStrategy
{
    FileConflictPolicy Policy { get; }
    FileConflictDecision Decide(FileConflictContext context);
}

public sealed class SkipConflictStrategy : IFileConflictStrategy
{
    public FileConflictPolicy Policy => FileConflictPolicy.Skip;

    public FileConflictDecision Decide(FileConflictContext context)
        => context.HasConflict
            ? new(context.DesiredOutputPath, ShouldSubmit: false, ShouldSkip: true)
            : new(context.DesiredOutputPath);
}

public sealed class OverwriteConflictStrategy : IFileConflictStrategy
{
    public FileConflictPolicy Policy => FileConflictPolicy.Overwrite;

    public FileConflictDecision Decide(FileConflictContext context)
        => context.HasConflict
            ? new(context.DesiredOutputPath, Issues:
            [
                new("overwrite", PreflightIssueSeverity.Warning,
                    $"“{context.Item.Title}”将覆盖同名成品或附加资源。", context.Item.ItemId),
            ])
            : new(context.DesiredOutputPath);
}

public sealed class ResumeVerifiedConflictStrategy : IFileConflictStrategy
{
    public FileConflictPolicy Policy => FileConflictPolicy.ResumeVerified;

    public FileConflictDecision Decide(FileConflictContext context)
    {
        if (context.ResumeCandidate is not null)
        {
            if (!context.ResumeCandidateValid)
                return new(context.DesiredOutputPath, ShouldSubmit: false, Issues:
                [
                    new("resume_invalid", PreflightIssueSeverity.Blocking,
                        $"“{context.Item.Title}”的续传文件不可用：{context.ResumeInvalidReason}", context.Item.ItemId),
                ]);
            var output = string.IsNullOrWhiteSpace(context.ResumeCandidate.OutputFilePath)
                ? context.DesiredOutputPath
                : context.ResumeCandidate.OutputFilePath;
            return new(output, IsResume: true, ResumeTaskId: context.ResumeCandidate.TaskId, Issues:
            [
                new("resume", PreflightIssueSeverity.Warning,
                    $"“{context.Item.Title}”将从已校验的临时文件继续。", context.Item.ItemId),
            ]);
        }
        return context.HasConflict
            ? new(context.DesiredOutputPath, ShouldSubmit: false, Issues:
            [
                new("resume_conflict", PreflightIssueSeverity.Blocking,
                    $"“{context.Item.Title}”存在成品文件，但没有可验证的同任务续传事实。", context.Item.ItemId),
            ])
            : new(context.DesiredOutputPath);
    }
}

public sealed class AutoNumberConflictStrategy : IFileConflictStrategy
{
    public FileConflictPolicy Policy => FileConflictPolicy.AutoNumber;

    public FileConflictDecision Decide(FileConflictContext context)
        => new(context.HasConflict ? context.AllocateNumberedPath() : context.DesiredOutputPath);
}
