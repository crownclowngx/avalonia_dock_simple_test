using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MyPlugTest.Services;

public interface IExcelFileDialogService
{
    Task<string?> PickWorkbookAsync(CancellationToken cancellationToken = default);

    Task<string?> PickOutputTextFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}

public sealed class AvaloniaExcelFileDialogService : IExcelFileDialogService
{
    public async Task<string?> PickWorkbookAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner =
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Excel 工作簿",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Excel 工作簿")
                {
                    Patterns = ["*.xlsx", "*.xlsm"],
                },
            ],
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> PickOutputTextFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner =
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存生成的地址",
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
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path.LocalPath;
    }
}
