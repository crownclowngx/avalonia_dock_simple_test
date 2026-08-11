using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace DaTangAccountingHelpPlug.Business;

/// <summary>
/// 发票导入流程使用的文件选择边界。
/// </summary>
/// <remarks>
/// 将 Avalonia 原生窗口访问从 ViewModel 中抽离，便于测试关闭期间的迟到结果。原生选择器
/// 本身没有可靠的取消入口，因此令牌定义的是“结果是否仍可提交”，而不是强制关闭系统窗口。
/// </remarks>
public interface IInvoiceFileDialogService
{
    /// <summary>选择一个输入工作簿；关闭后返回的路径必须被丢弃。</summary>
    Task<string?> PickInputWorkbookAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>选择导出工作簿路径；关闭后返回的路径必须被丢弃。</summary>
    Task<string?> PickOutputWorkbookAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 Avalonia 主窗口 StorageProvider 的文件对话框实现。
/// </summary>
public sealed class AvaloniaInvoiceFileDialogService : IInvoiceFileDialogService
{
    public async Task<string?> PickInputWorkbookAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        // 调用前检查可避免已关闭 Document 再弹出新窗口；等待返回后再次检查则负责
        // 丢弃关闭期间产生的迟到结果，两次检查共同构成原生选择器的协作取消边界。
        cancellationToken.ThrowIfCancellationRequested();
        var owner = GetOwner();
        if (owner is null) return null;

        var result = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Excel 文件") { Patterns = ["*.xlsx"] }],
        });
        cancellationToken.ThrowIfCancellationRequested();
        return result.Count == 0 ? null : result[0].Path.LocalPath;
    }

    public async Task<string?> PickOutputWorkbookAsync(CancellationToken cancellationToken = default)
    {
        // 保存选择同样采用“调用前 + 返回后”门禁。这里不尝试强行关闭操作系统对话框，
        // 因为不同平台行为不一致，强制终止反而可能产生悬挂窗口或未观察任务。
        cancellationToken.ThrowIfCancellationRequested();
        var owner = GetOwner();
        if (owner is null) return null;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存发票汇总表",
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel 文件 (.xlsx)") { Patterns = ["*.xlsx"] }],
            SuggestedFileName = "发票汇总表",
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path.LocalPath;
    }

    private static Avalonia.Controls.Window? GetOwner() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
