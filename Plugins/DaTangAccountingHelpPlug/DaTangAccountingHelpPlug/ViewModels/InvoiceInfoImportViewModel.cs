using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTangAccountingHelpPlug.Business;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace DaTangAccountingHelpPlug.ViewModels;

public partial class InvoiceInfoImportViewModel : Document, IDisposable
{
    private const int MaxLogLines = 10000;

    private readonly IInvoiceInfoImportBusiness _business;
    private readonly IInvoiceFileDialogService _fileDialogs;
    private readonly IDocumentLifetime? _documentLifetime;
    // 宿主 ClosingToken 是正式运行时的关闭信号；本地 CTS 用于保留无参构造、直接构造测试
    // 和显式 Dispose 的兼容语义。每次命令都会把两者与命令令牌链接，任何入口发出取消都能
    // 停止后续 Excel 阶段，但已经进入 EPPlus 同步 SaveAs 的写入不会被强行中断。
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    [ObservableProperty] private string _invoiceSummaryFilePath = string.Empty;
    [ObservableProperty] private string _currentMonthPaymentFilePath = string.Empty;
    [ObservableProperty] private string _previousPaymentSummaryFilePath = string.Empty;
    [ObservableProperty] private string _processText = string.Empty;
    [ObservableProperty] private bool _isCalculating;
    [ObservableProperty] private DateTimeOffset? _startDate =
        new(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1));
    [ObservableProperty] private DateTimeOffset? _endDate =
        new(new DateTime(
            DateTime.Now.Year,
            DateTime.Now.Month,
            DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month)));

    public ObservableCollection<string> LogEntries { get; } = [];

    public InvoiceInfoImportViewModel()
        : this(
            new InvoiceInfoImportBusiness(),
            new AvaloniaInvoiceFileDialogService(),
            null)
    {
    }

    public InvoiceInfoImportViewModel(
        IInvoiceInfoImportBusiness business,
        IInvoiceFileDialogService fileDialogs,
        IDocumentLifetime? documentLifetime = null)
    {
        Title = "发票信息导入和计算";
        _business = business;
        _fileDialogs = fileDialogs;
        _documentLifetime = documentLifetime;
        _business.LogEmitted += AddLogLine;
    }

    [RelayCommand]
    public Task SelectInvoiceSummaryFile(CancellationToken cancellationToken) =>
        SelectFolder("InvoiceSummaryFile", cancellationToken);

    [RelayCommand]
    public Task SelectCurrentMonthPaymentFile(CancellationToken cancellationToken) =>
        SelectFolder("CurrentMonthPaymentFile", cancellationToken);

    [RelayCommand]
    public Task SelectPreviousPaymentSummaryFile(CancellationToken cancellationToken) =>
        SelectFolder("PreviousPaymentSummaryFile", cancellationToken);

    public async Task SelectFolder(string type, CancellationToken commandToken = default)
    {
        using var linked = CreateOperationCancellation(commandToken);
        try
        {
            var localPath = await _fileDialogs.PickInputWorkbookAsync("选择 Excel 文件", linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(localPath) || IsClosing) return;

            switch (type)
            {
                case "PreviousPaymentSummaryFile":
                    PreviousPaymentSummaryFilePath = localPath;
                    break;
                case "CurrentMonthPaymentFile":
                    CurrentMonthPaymentFilePath = localPath;
                    break;
                case "InvoiceSummaryFile":
                    InvoiceSummaryFilePath = localPath;
                    break;
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // 原生文件选择器没有统一的取消 API，关闭 Document 时不强制终止系统窗口。
            // 令牌在等待返回后再次检查，确保迟到路径不会写回已关闭 ViewModel，也不会启动
            // 后续 Excel 读取；这是“无法取消外部窗口，但可以取消结果提交”的明确边界。
        }
    }

    [RelayCommand]
    public async Task StartCalculation(CancellationToken commandToken)
    {
        using var linked = CreateOperationCancellation(commandToken);
        var cancellationToken = linked.Token;
        if (IsClosing) return;

        IsCalculating = true;
        if (Dispatcher.UIThread.CheckAccess())
        {
            LogEntries.Clear();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsClosing) LogEntries.Clear();
            });
        }

        try
        {
            await _business.ClearAllData(cancellationToken);
            await ReadAllExcelData(cancellationToken);
            AddLogLine("Excel 文件读取完成，准备生成数据……");
            await _business.CreateAllNeedShowInvoiceNumber(
                StartDate?.DateTime,
                EndDate?.DateTime,
                cancellationToken);
            AddLogLine("识别完成，开始计算新表……");
            await _business.CalculateNewInvoiceSummary(cancellationToken);
            AddLogLine("计算完成，开始导出表格");
            await SaveInvoicePaymentSummaryToExcel(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 关闭标签表示用户放弃当前导入会话，属于预期控制流。这里保持静默，不追加错误
            // 日志；Scope Dispose 已经失效日志订阅，后台操作只需协作退出，不阻塞 UI 线程。
        }
        catch (Exception ex)
        {
            AddLogLine($"处理 Excel 文件时出错：{ex.Message}");
        }
        finally
        {
            if (!IsClosing) IsCalculating = false;
        }
    }

    private async Task ReadAllExcelData(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(InvoiceSummaryFilePath))
        {
            AddLogLine($"--- 读取发票总表：{Path.GetFileName(InvoiceSummaryFilePath)} ---");
            await _business.ReadAndIndexInvoiceSummary(InvoiceSummaryFilePath, cancellationToken);
        }

        if (!string.IsNullOrEmpty(CurrentMonthPaymentFilePath))
        {
            AddLogLine($"--- 读取当月付款表：{Path.GetFileName(CurrentMonthPaymentFilePath)} ---");
            await _business.ReadInvoicePaymentDetailCurrentMonthTable(
                CurrentMonthPaymentFilePath,
                cancellationToken);
        }

        if (!string.IsNullOrEmpty(PreviousPaymentSummaryFilePath))
        {
            AddLogLine($"--- 读取历史付款汇总表：{Path.GetFileName(PreviousPaymentSummaryFilePath)} ---");
            await _business.ReadInvoicePaymentDetailPreviousMonthTable(
                PreviousPaymentSummaryFilePath,
                cancellationToken);
        }
    }

    private async Task SaveInvoicePaymentSummaryToExcel(CancellationToken cancellationToken)
    {
        try
        {
            var filePath = await _fileDialogs.PickOutputWorkbookAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(filePath) && !IsClosing)
            {
                await _business.SaveInvoicePaymentSummaryToExcel(filePath, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AddLogLine($"保存 Excel 文件时出错：{ex.Message}");
        }
    }

    private void AddLogLine(string logLine)
    {
        if (IsClosing) return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (!IsClosing) UpdateLog(logLine);
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsClosing) UpdateLog(logLine);
            });
        }
    }

    private void UpdateLog(string logLine)
    {
        lock (LogEntries)
        {
            LogEntries.Add(logLine);
            if (LogEntries.Count > MaxLogLines) LogEntries.RemoveAt(0);
        }
    }

    [RelayCommand]
    public async Task CopyAllLogs(CancellationToken commandToken)
    {
        using var linked = CreateOperationCancellation(commandToken);
        try
        {
            var mainWindow =
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var clipboard = mainWindow?.Clipboard;
            if (clipboard is null)
            {
                AddLogLine("当前窗口不支持剪贴板，无法复制日志");
                return;
            }

            linked.Token.ThrowIfCancellationRequested();
            await clipboard.SetTextAsync(string.Join(Environment.NewLine, LogEntries));
            linked.Token.ThrowIfCancellationRequested();
            AddLogLine("所有日志已复制到剪贴板");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // 剪贴板调用也可能跨越 Dispatcher 调度点。Document 关闭后即使系统调用返回，
            // 也不再追加“复制成功”等日志，避免已经关闭的对象产生迟到界面状态。
        }
        catch (Exception ex)
        {
            AddLogLine($"复制日志失败：{ex.Message}");
        }
    }

    private bool IsClosing => Volatile.Read(ref _disposed) != 0 || _documentLifetime?.IsClosing == true;

    private CancellationTokenSource CreateOperationCancellation(CancellationToken commandToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _disposeCts.Token,
            _documentLifetime?.ClosingToken ?? CancellationToken.None);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // 先解绑日志，再发出本地取消。这样即使业务循环或不可取消的系统对话框稍后才返回，
        // 也无法再经由事件持有或更新已关闭的 Document；Dispose 不同步等待后台收尾，符合
        // Dock 立即关闭的交互语义。原子 disposed 门禁保证宿主释放与测试显式释放可安全汇合。
        _business.LogEmitted -= AddLogLine;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
