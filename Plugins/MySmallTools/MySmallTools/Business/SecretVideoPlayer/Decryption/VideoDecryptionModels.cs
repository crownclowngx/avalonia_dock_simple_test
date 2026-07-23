using MySmallTools.Business.SecretVideoPlayer.Operations;

namespace MySmallTools.Business.SecretVideoPlayer.Decryption;

public sealed record DecryptionCandidate(
    string InputPath,
    string EncryptedFileName,
    string OriginalFileName,
    string OriginalExtension,
    string PublicTitle,
    long OriginalFileLength,
    bool IsValid,
    string ValidationMessage,
    VideoTaskFailureCode? FailureCode = null);

public sealed record CandidateDecryptionPreflight(
    DecryptionCandidate Candidate,
    string OutputPath,
    VideoPreflightResult Result);

public sealed record BatchDecryptionPreflightResult(
    VideoPreflightResult Overall,
    IReadOnlyList<CandidateDecryptionPreflight> Items)
{
    public bool HasRunnableItems => Items.Any(item => item.Result.CanProceed);
}

public sealed record BatchDecryptionProgress(
    string InputPath,
    string OutputPath,
    VideoTaskState State,
    long ProcessedBytes,
    long TotalBytes,
    double FilePercentage,
    double OverallPercentage,
    string Message,
    VideoTaskFailureCode? FailureCode = null);

public sealed record BatchDecryptionResult(
    int TotalCount,
    int SucceededCount,
    int FailedCount,
    int CancelledCount,
    IReadOnlyList<string> OutputPaths);
