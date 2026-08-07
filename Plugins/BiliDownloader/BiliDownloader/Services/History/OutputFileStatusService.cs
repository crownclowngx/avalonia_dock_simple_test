using System.Security;
using BiliDownloader.Models;

namespace BiliDownloader.Services.History;

public sealed record OutputFileReference(string TaskId, string? Path);
public sealed record OutputFileStatusResult(string TaskId, FilePresenceStatus Status);

/// <summary>
/// 按用户命令检查成品文件。接口同时提供单项和有界批量入口，调用方不需要自行创建
/// 大量并发磁盘请求；批量结果通过回调逐项交付，因此取消时已经完成的检查仍可保留。
/// </summary>
public interface IOutputFileStatusService
{
    Task<FilePresenceStatus> CheckAsync(string? path, CancellationToken cancellationToken = default);

    Task CheckManyAsync(
        IReadOnlyCollection<OutputFileReference> files,
        Func<OutputFileStatusResult, Task> onResult,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default);
}

internal interface IFileAttributeProbe
{
    FileAttributes GetAttributes(string fullPath);
}

internal sealed class SystemFileAttributeProbe : IFileAttributeProbe
{
    public FileAttributes GetAttributes(string fullPath) => File.GetAttributes(fullPath);
}

public sealed class OutputFileStatusService : IOutputFileStatusService
{
    private readonly IFileAttributeProbe _probe;

    public OutputFileStatusService() : this(new SystemFileAttributeProbe()) { }

    internal OutputFileStatusService(IFileAttributeProbe probe)
    {
        _probe = probe;
    }

    public Task<FilePresenceStatus> CheckAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(FilePresenceStatus.Missing);

        try
        {
            var fullPath = Path.GetFullPath(path);
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = _probe.GetAttributes(fullPath);
            return Task.FromResult(attributes.HasFlag(FileAttributes.Directory)
                ? FilePresenceStatus.Missing
                : FilePresenceStatus.Exists);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult(FilePresenceStatus.Missing);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            // File.Exists 会把权限不足、离线网络盘和真正缺失都折叠成 false，无法满足安全语义。
            // 使用 GetAttributes 并保留“不确定”状态，避免错误地引导用户覆盖或重新下载。
            return Task.FromResult(FilePresenceStatus.Inaccessible);
        }
    }

    public async Task CheckManyAsync(
        IReadOnlyCollection<OutputFileReference> files,
        Func<OutputFileStatusResult, Task> onResult,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onResult);
        if (maxConcurrency is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken,
            },
            async (file, token) =>
            {
                var status = await CheckAsync(file.Path, token);
                token.ThrowIfCancellationRequested();
                await onResult(new OutputFileStatusResult(file.TaskId, status));
            });
    }
}
