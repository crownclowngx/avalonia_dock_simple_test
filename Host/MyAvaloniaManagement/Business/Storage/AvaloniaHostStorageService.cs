using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MyAvaloniaManagementCommon.DocumentCreation;

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
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickOpenFilesAsync()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return [];
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开文档",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.TextPlain]
        });

        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(DocumentMetadata? metadata)
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        // Document 身份属于分派协议，不再承担文件扩展名职责。统一扩展名使用户可以
        // 识别宿主管理文档，同时信封内的强类型 ID 仍负责选择具体插件策略。
        const string extension = "mamdoc";
        var fileType = metadata is null
            ? FilePickerFileTypes.TextPlain
            : new FilePickerFileType(metadata.DisplayName)
            {
                Patterns = [$"*.{extension}"]
            };

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存文档",
            DefaultExtension = extension,
            FileTypeChoices = [fileType]
        });

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
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string content) =>
        AtomicFileTransaction.WriteAllTextAsync(path, content);

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
