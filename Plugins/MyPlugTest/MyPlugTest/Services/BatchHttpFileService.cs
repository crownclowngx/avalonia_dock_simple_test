using System.Text;
using Avalonia.Platform.Storage;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyPlugTest.Services;

/// <summary>批量 HTTP 文件模式所需的文件选择和一次性文本读写边界。</summary>
public interface IBatchHttpFileService
{
    Task<string?> PickInputFileAsync(CancellationToken cancellationToken = default);

    Task<string?> PickOutputFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);

    Task<string[]> ReadAllLinesAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task WriteAllTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 使用 Host 窗口端口选择文件；输出先写同目录临时文件，完整成功后才替换目标文件。
/// </summary>
public sealed class BatchHttpFileService(IPluginWindowInteraction windowInteraction) :
    IBatchHttpFileService
{
    private readonly IPluginWindowInteraction _windowInteraction =
        windowInteraction ?? throw new ArgumentNullException(nameof(windowInteraction));

    public async Task<string?> PickInputFileAsync(
        CancellationToken cancellationToken = default)
    {
        var files = await _windowInteraction.PickOpenFilesAsync(new FilePickerOpenOptions
        {
            Title = "选择批量 GET 地址文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("文本地址列表")
                {
                    Patterns = ["*.txt", "*.list", "*.log"],
                    MimeTypes = ["text/plain"],
                },
                FilePickerFileTypes.All,
            ],
        }, cancellationToken);
        return files.Count == 0 ? null : files[0];
    }

    public Task<string?> PickOutputFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        return _windowInteraction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "保存批量 GET 响应",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "txt",
            FileTypeChoices =
            [
                new FilePickerFileType("文本文件")
                {
                    Patterns = ["*.txt"],
                    MimeTypes = ["text/plain"],
                },
            ],
        }, cancellationToken);
    }

    public Task<string[]> ReadAllLinesAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);
    }

    public async Task WriteAllTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("输出文件没有有效的目录。");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // 临时文件清理失败不能覆盖主要的请求或写入异常。
            }
        }
    }
}
