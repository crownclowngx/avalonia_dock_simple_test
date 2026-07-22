using System.Runtime.CompilerServices;

namespace MySmallTools.Business.SecretVideoPlayer;

public enum VideoLibraryMetadataState
{
    Ready,
    Failed
}

public sealed record VideoLibraryScanResult(
    string FilePath,
    string FileNameWithoutExtension,
    string PublicTitle,
    string PublicDescription,
    VideoLibraryMetadataState State,
    string ErrorMessage);

public interface IVideoLibraryScanner
{
    IAsyncEnumerable<VideoLibraryScanResult> ScanAsync(
        string folderPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// 枚举当前目录中的 .secvid 文件，并以固定并发度读取无需密码的公开信息。
/// </summary>
public sealed class VideoLibraryScanner : IVideoLibraryScanner
{
    private const int MaxConcurrency = 4;

    public async IAsyncEnumerable<VideoLibraryScanResult> ScanAsync(
        string folderPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("视频文件夹不能为空。", nameof(folderPath));

        var filePaths = await Task.Run(() => EnumerateCandidates(folderPath), cancellationToken);
        var activeReads = new List<Task<VideoLibraryScanResult>>(MaxConcurrency);
        var nextIndex = 0;

        while (nextIndex < filePaths.Length && activeReads.Count < MaxConcurrency)
            activeReads.Add(ReadOneAsync(filePaths[nextIndex++], cancellationToken));

        while (activeReads.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(activeReads);
            activeReads.Remove(completed);
            yield return await completed;

            if (nextIndex < filePaths.Length)
                activeReads.Add(ReadOneAsync(filePaths[nextIndex++], cancellationToken));
        }
    }

    private static string[] EnumerateCandidates(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"文件夹不存在: {folderPath}");

        return Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".secvid", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Task<VideoLibraryScanResult> ReadOneAsync(
        string filePath,
        CancellationToken cancellationToken) =>
        Task.Run(() => ReadOne(filePath, cancellationToken), cancellationToken);

    private static VideoLibraryScanResult ReadOne(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        try
        {
            var publicInfo = EncryptedVideoContainer.ReadPublicInfo(filePath);
            return new VideoLibraryScanResult(
                filePath,
                fileName,
                publicInfo.Title,
                publicInfo.Description,
                VideoLibraryMetadataState.Ready,
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            return Failed(filePath, fileName, "不是有效的 SECVID03，或公开信息已损坏");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(filePath, fileName, "没有读取权限");
        }
        catch (IOException)
        {
            return Failed(filePath, fileName, "文件被占用、已删除或发生磁盘错误");
        }
        catch
        {
            return Failed(filePath, fileName, "公开信息读取失败");
        }
    }

    private static VideoLibraryScanResult Failed(string path, string fileName, string error) =>
        new(path, fileName, string.Empty, string.Empty, VideoLibraryMetadataState.Failed, error);
}
