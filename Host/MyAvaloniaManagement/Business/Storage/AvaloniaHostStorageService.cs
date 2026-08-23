using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MyAvaloniaManagement.Business.Storage;

/// <summary>
/// 使用 Avalonia StorageProvider 和本机文件系统实现宿主存储边界。
/// </summary>
/// <remarks>
/// 选择器操作集中在此类中，可以防止 ViewModel 与主窗口生命周期耦合；
/// 文本读写仍使用异步 <see cref="File"/> API，以保持原有文件格式和行为。
/// </remarks>
internal sealed class AvaloniaHostStorageService : IHostStorageService
{
    private const string DocumentExtension = "mamdoc";
    private const string DocumentFileTypeName = "管理文档 (.mamdoc)";

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickOpenFilesAsync()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return [];
        }

        var files = await storageProvider.OpenFilePickerAsync(CreateOpenFilePickerOptions());

        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(string documentDisplayName)
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        var file = await storageProvider.SaveFilePickerAsync(
            CreateSaveFilePickerOptions(documentDisplayName));

        return file?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择文件夹",
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public long GetFileLength(string path) => new FileInfo(path).Length;

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string content) =>
        AtomicFileTransaction.WriteAllTextAsync(path, content);

    /// <summary>
    /// 创建统一的 Document 打开选项。该方法保持为内部可测试边界，避免打开和保存
    /// 对扩展名产生不同理解。
    /// </summary>
    internal static FilePickerOpenOptions CreateOpenFilePickerOptions() => new()
    {
        Title = "打开文档",
        AllowMultiple = true,
        FileTypeFilter = [CreateDocumentFileType(DocumentFileTypeName)]
    };

    /// <summary>
    /// 创建统一的 Document 保存选项。即使调用方暂时没有类型元数据，也必须使用
    /// 宿主管理文档类型，不能回退为纯文本文件。
    /// </summary>
    internal static FilePickerSaveOptions CreateSaveFilePickerOptions(
        string documentDisplayName) => new()
    {
        Title = "保存文档",
        DefaultExtension = DocumentExtension,
        FileTypeChoices =
        [
            CreateDocumentFileType(
                string.IsNullOrWhiteSpace(documentDisplayName)
                    ? DocumentFileTypeName
                    : documentDisplayName)
        ]
    };

    private static FilePickerFileType CreateDocumentFileType(string name) => new(name)
    {
        Patterns = [$"*.{DocumentExtension}"]
    };

    /// <summary>
    /// 获取当前桌面主窗口的存储提供器。
    /// </summary>
    /// <remarks>
    /// 设计器、单元测试或窗口尚未创建时允许返回空值，调用方会把它视为用户取消，
    /// 从而避免为获取选择器而强制创建全局窗口。
    /// </remarks>
    private static IStorageProvider? GetStorageProvider() =>
        (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow
        ?.StorageProvider;
}
