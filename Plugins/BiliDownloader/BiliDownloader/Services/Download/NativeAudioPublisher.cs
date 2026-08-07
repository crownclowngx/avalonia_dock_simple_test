namespace BiliDownloader.Services.Download;

/// <summary>
/// 原生音频成品发布边界。实现必须先在最终输出目录写入完整 staging 文件，随后以同卷移动发布，
/// 不能把下载临时目录中的文件直接跨卷移动到用户目录。
/// </summary>
public interface INativeAudioPublisher
{
    /// <summary>复制、落盘并原子发布已经完成完整性校验的音频流。</summary>
    Task PublishAsync(
        string sourcePath,
        string stagingPath,
        string outputPath,
        bool overwrite,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文件系统原生音频发布器。该类只负责发布事务，不知道 DASH、任务状态或冲突授权来源，
/// 从而使下载编排、文件系统写入和用户授权三个职责保持分离。
/// </summary>
public sealed class NativeAudioPublisher : INativeAudioPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync(
        string sourcePath,
        string stagingPath,
        string outputPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var stagingDirectory = Path.GetFullPath(Path.GetDirectoryName(stagingPath)!);
        var outputDirectory = Path.GetFullPath(Path.GetDirectoryName(outputPath)!);
        if (!stagingDirectory.Equals(outputDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("原生音频 staging 必须与最终输出位于同一目录。", nameof(stagingPath));

        try
        {
            await using (var source = new FileStream(
                             sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                             stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            // 这是唯一的可见性切换点：移动前用户目录没有半成品，移动后立即得到完整成品。
            File.Move(stagingPath, outputPath, overwrite);
        }
        catch
        {
            try { if (File.Exists(stagingPath)) File.Delete(stagingPath); } catch { }
            throw;
        }
    }
}
