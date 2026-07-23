namespace MySmallTools.Business.SecretVideoPlayer.Decryption;

public interface ISecvid03Decryptor
{
    Task DecryptAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<VideoDecryptionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IVideoDecryptionService
{
    Task<IReadOnlyList<DecryptionCandidate>> InspectAsync(
        IReadOnlyList<string> inputPaths,
        CancellationToken cancellationToken = default);

    Task<BatchDecryptionResult> DecryptBatchAsync(
        IReadOnlyList<DecryptionCandidate> candidates,
        string outputDirectory,
        string password,
        IProgress<BatchDecryptionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
