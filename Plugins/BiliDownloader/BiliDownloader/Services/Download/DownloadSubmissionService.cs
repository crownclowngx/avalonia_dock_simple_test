using BiliDownloader.Models;

namespace BiliDownloader.Services.Download;

/// <summary>
/// Document 使用的提交应用服务。它隐藏 Coordinator 和预检器的组合方式，
/// 使 ViewModel 只负责编排用户确认，不需要了解锁、事务或后台队列。
/// </summary>
public interface IDownloadSubmissionService
{
    Task<SubmissionPreflightReport> PreflightAsync(DownloadSubmission submission, CancellationToken cancellationToken = default);
    Task<SubmissionCommitResult> CommitAsync(PreparedSubmission prepared, CancellationToken cancellationToken = default);
}

public sealed class DownloadSubmissionService : IDownloadSubmissionService
{
    private readonly ISubmissionPreflightService _preflight;
    private readonly BiliDownloadCoordinator _coordinator;

    public DownloadSubmissionService(ISubmissionPreflightService preflight, BiliDownloadCoordinator coordinator)
    {
        _preflight = preflight;
        _coordinator = coordinator;
    }

    public Task<SubmissionPreflightReport> PreflightAsync(
        DownloadSubmission submission, CancellationToken cancellationToken = default)
        => _preflight.InspectAsync(submission, cancellationToken);

    public Task<SubmissionCommitResult> CommitAsync(
        PreparedSubmission prepared, CancellationToken cancellationToken = default)
        => _coordinator.CommitPreparedAsync(prepared, _preflight, cancellationToken);
}
