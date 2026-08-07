using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using BiliDownloader.Models;

namespace BiliDownloader.Services.History;

public sealed record HistoryExportDestination(string Path, TaskHistoryExportFormat Format);

/// <summary>隔离 Avalonia 保存文件对话框，使历史 ViewModel 在无窗口测试环境中仍可安全取消。</summary>
public interface IHistoryExportDestinationPicker
{
    Task<HistoryExportDestination?> PickAsync(TaskHistoryExportFormat format);
}

public sealed class AvaloniaHistoryExportDestinationPicker : IHistoryExportDestinationPicker
{
    public async Task<HistoryExportDestination?> PickAsync(TaskHistoryExportFormat format)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;
        var extension = format == TaskHistoryExportFormat.Csv ? "csv" : "json";
        var fileType = new FilePickerFileType(format == TaskHistoryExportFormat.Csv ? "CSV 历史文件" : "JSON 历史文件")
        {
            Patterns = [$"*.{extension}"],
        };
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出下载历史",
            SuggestedFileName = $"bili-history-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = [fileType],
            ShowOverwritePrompt = true,
        });
        return file is null ? null : new HistoryExportDestination(file.Path.LocalPath, format);
    }
}
