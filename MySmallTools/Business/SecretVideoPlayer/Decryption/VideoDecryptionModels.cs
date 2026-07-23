namespace MySmallTools.Business.SecretVideoPlayer.Decryption;

public enum DecryptionItemState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum VideoDecryptionFailureCode
{
    InvalidContainer,
    AuthenticationFailed,
    CorruptedContent,
    InputUnavailable,
    OutputUnavailable,
    OutputConflict
}

public sealed class VideoDecryptionException : Exception
{
    public VideoDecryptionException(
        VideoDecryptionFailureCode failureCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureCode = failureCode;
    }

    public VideoDecryptionFailureCode FailureCode { get; }
}

public sealed record VideoDecryptionProgress(
    long ProcessedBytes,
    long TotalBytes,
    double Percentage,
    string Status);

public sealed record DecryptionCandidate(
    string InputPath,
    string EncryptedFileName,
    string OriginalFileName,
    string OriginalExtension,
    string PublicTitle,
    long OriginalFileLength,
    bool IsValid,
    string ValidationMessage);

public sealed record BatchDecryptionProgress(
    string InputPath,
    string OutputPath,
    DecryptionItemState State,
    long ProcessedBytes,
    long TotalBytes,
    double FilePercentage,
    double OverallPercentage,
    string Message,
    VideoDecryptionFailureCode? FailureCode = null);

public sealed record BatchDecryptionResult(
    int TotalCount,
    int SucceededCount,
    int FailedCount,
    int CancelledCount,
    IReadOnlyList<string> OutputPaths);
