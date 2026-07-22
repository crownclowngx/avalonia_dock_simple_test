namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 批量解密用例：预检候选文件、顺序执行、隔离单项失败并汇总进度。
/// </summary>
public sealed class VideoDecryptionService : IVideoDecryptionService
{
    private const int InspectionConcurrency = 4;
    private readonly ISecvid03Decryptor _decryptor;
    private readonly DecryptionOutputPathResolver _outputPathResolver;

    public VideoDecryptionService(
        ISecvid03Decryptor decryptor,
        DecryptionOutputPathResolver outputPathResolver)
    {
        _decryptor = decryptor ?? throw new ArgumentNullException(nameof(decryptor));
        _outputPathResolver = outputPathResolver ?? throw new ArgumentNullException(nameof(outputPathResolver));
    }

    public async Task<IReadOnlyList<DecryptionCandidate>> InspectAsync(
        IReadOnlyList<string> inputPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        var uniquePaths = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch { fullPath = path; }

            if (seenPaths.Add(fullPath))
                uniquePaths.Add(fullPath);
        }

        using var gate = new SemaphoreSlim(InspectionConcurrency);
        var tasks = uniquePaths.Select(async (path, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var candidate = await Task.Run(() => InspectOne(path), cancellationToken).ConfigureAwait(false);
                return (index, candidate);
            }
            finally
            {
                gate.Release();
            }
        });

        var inspected = await Task.WhenAll(tasks).ConfigureAwait(false);
        return inspected.OrderBy(item => item.index).Select(item => item.candidate).ToArray();
    }

    public async Task<BatchDecryptionResult> DecryptBatchAsync(
        IReadOnlyList<DecryptionCandidate> candidates,
        string outputDirectory,
        string password,
        IProgress<BatchDecryptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!Directory.Exists(outputDirectory))
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.OutputUnavailable,
                "输出目录不存在或已被删除。");

        var validCandidates = candidates.Where(candidate => candidate.IsValid).ToArray();
        var totalBytes = validCandidates.Sum(candidate => Math.Max(0, candidate.OriginalFileLength));
        var allocatedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputPaths = new List<string>();
        long completedBytes = 0;
        var succeeded = 0;
        var failed = 0;

        for (var index = 0; index < validCandidates.Length; index++)
        {
            var candidate = validCandidates[index];
            if (cancellationToken.IsCancellationRequested)
            {
                ReportRemainingCancelled(validCandidates, index, completedBytes, totalBytes, progress);
                cancellationToken.ThrowIfCancellationRequested();
            }

            string outputPath;
            try
            {
                outputPath = _outputPathResolver.GetAvailablePath(outputDirectory, candidate, allocatedPaths);
            }
            catch (VideoDecryptionException ex)
            {
                failed++;
                completedBytes += candidate.OriginalFileLength;
                progress?.Report(CreateProgress(
                    candidate,
                    string.Empty,
                    DecryptionItemState.Failed,
                    0,
                    completedBytes,
                    totalBytes,
                    ex.Message,
                    ex.FailureCode));
                continue;
            }

            progress?.Report(CreateProgress(
                candidate,
                outputPath,
                DecryptionItemState.Running,
                0,
                completedBytes,
                totalBytes,
                "正在验证密码并准备解密..."));

            long currentProcessed = 0;
            var fileProgress = new InlineProgress<VideoDecryptionProgress>(item =>
            {
                currentProcessed = Math.Clamp(item.ProcessedBytes, 0, candidate.OriginalFileLength);
                progress?.Report(CreateProgress(
                    candidate,
                    outputPath,
                    DecryptionItemState.Running,
                    currentProcessed,
                    completedBytes,
                    totalBytes,
                    item.Status));
            });

            try
            {
                await _decryptor.DecryptAsync(
                    candidate.InputPath,
                    outputPath,
                    password,
                    fileProgress,
                    cancellationToken).ConfigureAwait(false);

                completedBytes += candidate.OriginalFileLength;
                succeeded++;
                outputPaths.Add(outputPath);
                progress?.Report(CreateProgress(
                    candidate,
                    outputPath,
                    DecryptionItemState.Succeeded,
                    candidate.OriginalFileLength,
                    completedBytes - candidate.OriginalFileLength,
                    totalBytes,
                    "解密完成"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(CreateProgress(
                    candidate,
                    outputPath,
                    DecryptionItemState.Cancelled,
                    currentProcessed,
                    completedBytes,
                    totalBytes,
                    "已取消"));
                ReportRemainingCancelled(validCandidates, index + 1, completedBytes, totalBytes, progress);
                throw;
            }
            catch (VideoDecryptionException ex)
            {
                failed++;
                completedBytes += candidate.OriginalFileLength;
                progress?.Report(CreateProgress(
                    candidate,
                    outputPath,
                    DecryptionItemState.Failed,
                    currentProcessed,
                    completedBytes - currentProcessed,
                    totalBytes,
                    ex.Message,
                    ex.FailureCode));
            }
            catch (Exception ex)
            {
                failed++;
                completedBytes += candidate.OriginalFileLength;
                progress?.Report(CreateProgress(
                    candidate,
                    outputPath,
                    DecryptionItemState.Failed,
                    currentProcessed,
                    completedBytes - currentProcessed,
                    totalBytes,
                    "解密时发生未预期错误。",
                    VideoDecryptionFailureCode.OutputUnavailable));
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        return new BatchDecryptionResult(
            validCandidates.Length,
            succeeded,
            failed,
            0,
            outputPaths);
    }

    private static DecryptionCandidate InspectOne(string inputPath)
    {
        var encryptedName = Path.GetFileName(inputPath);
        if (!string.Equals(Path.GetExtension(inputPath), ".secvid", StringComparison.OrdinalIgnoreCase))
            return Invalid(inputPath, encryptedName, "仅支持 .secvid 文件。");
        if (!File.Exists(inputPath))
            return Invalid(inputPath, encryptedName, "文件不存在或已被删除。");

        try
        {
            var info = EncryptedVideoContainer.ReadPublicInfo(inputPath);
            return new DecryptionCandidate(
                inputPath,
                encryptedName,
                info.OriginalFileName,
                info.OriginalExtension,
                info.Title,
                info.OriginalFileLength,
                true,
                string.Empty);
        }
        catch (InvalidDataException)
        {
            return Invalid(inputPath, encryptedName, "不是有效的 SECVID03，或公开信息已损坏。");
        }
        catch (UnauthorizedAccessException)
        {
            return Invalid(inputPath, encryptedName, "没有读取权限。");
        }
        catch (IOException)
        {
            return Invalid(inputPath, encryptedName, "文件被占用、已删除或发生磁盘错误。");
        }
        catch
        {
            return Invalid(inputPath, encryptedName, "文件预检失败。");
        }
    }

    private static DecryptionCandidate Invalid(string path, string encryptedName, string message) =>
        new(path, encryptedName, string.Empty, string.Empty, string.Empty, 0, false, message);

    private static BatchDecryptionProgress CreateProgress(
        DecryptionCandidate candidate,
        string outputPath,
        DecryptionItemState state,
        long fileProcessed,
        long completedBeforeFile,
        long totalBytes,
        string message,
        VideoDecryptionFailureCode? failureCode = null)
    {
        var filePercentage = candidate.OriginalFileLength == 0
            ? state == DecryptionItemState.Succeeded ? 100 : 0
            : Math.Clamp(fileProcessed * 100d / candidate.OriginalFileLength, 0, 100);
        var overallProcessed = completedBeforeFile + fileProcessed;
        var overallPercentage = totalBytes == 0
            ? state == DecryptionItemState.Succeeded ? 100 : 0
            : Math.Clamp(overallProcessed * 100d / totalBytes, 0, 100);
        return new BatchDecryptionProgress(
            candidate.InputPath,
            outputPath,
            state,
            fileProcessed,
            candidate.OriginalFileLength,
            filePercentage,
            overallPercentage,
            message,
            failureCode);
    }

    private static void ReportRemainingCancelled(
        IReadOnlyList<DecryptionCandidate> candidates,
        int startIndex,
        long completedBytes,
        long totalBytes,
        IProgress<BatchDecryptionProgress>? progress)
    {
        for (var index = startIndex; index < candidates.Count; index++)
        {
            progress?.Report(CreateProgress(
                candidates[index],
                string.Empty,
                DecryptionItemState.Cancelled,
                0,
                completedBytes,
                totalBytes,
                "未开始，批次已取消"));
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
