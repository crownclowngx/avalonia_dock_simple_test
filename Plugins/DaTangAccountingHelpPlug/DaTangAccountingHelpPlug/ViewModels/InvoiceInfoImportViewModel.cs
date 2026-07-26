using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTangAccountingHelpPlug.Business;
using DaTangAccountingHelpPlug.Models;
using Dock.Model.Mvvm.Controls;
using OfficeOpenXml;

namespace DaTangAccountingHelpPlug.ViewModels;

public partial class InvoiceInfoImportViewModel : Document
{
    // 发票总表文件路径属性
    [ObservableProperty] private string _invoiceSummaryFilePath = string.Empty;

    // 当月付款表文件路径属性
    [ObservableProperty] private string _currentMonthPaymentFilePath = string.Empty;

    // 之前付款总和表文件路径属性
    [ObservableProperty] private string _previousPaymentSummaryFilePath = string.Empty;

    // 处理状态信息
    [ObservableProperty] private string _processText = string.Empty;

    // 计算状态标志，用于控制按钮启用/禁用
    [ObservableProperty] private bool _isCalculating = false;

    // 添加开始日期和结束日期属性，默认值设置为当月第一天和最后一天
    [ObservableProperty] private DateTimeOffset? _startDate = new DateTimeOffset(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1));

    [ObservableProperty] private DateTimeOffset? _endDate = new DateTimeOffset(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month)));

    // 日志条目集合，用于在ListBox中显示
    public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();

    private const int MAX_LOG_LINES = 10000;

    // 业务层实例
    private readonly InvoiceInfoImportBusiness _invoiceInfoImportBusiness;

    public InvoiceInfoImportViewModel()
    {
        Title = "发票信息导入和计算";
        // 初始化业务层，传入日志方法
        _invoiceInfoImportBusiness = new InvoiceInfoImportBusiness(AddLogLine);
    }

    private void InitializeFileSystemTree(ObservableCollection<FileSystemNode> rootNodes)
    {
        // 添加系统驱动器作为根节点
        var drives = Directory.GetLogicalDrives();
        foreach (var drive in drives)
        {
            rootNodes.Add(new FileSystemNode(drive));
        }
    }

    // 选择发票总表文件命令
    [RelayCommand]
    public void SelectInvoiceSummaryFile()
    {
        SelectFolder("InvoiceSummaryFile");
    }

    // 选择当月付款表文件命令
    [RelayCommand]
    public void SelectCurrentMonthPaymentFile()
    {
        SelectFolder("CurrentMonthPaymentFile");
    }

    // 选择之前付款总和表文件命令
    [RelayCommand]
    public void SelectPreviousPaymentSummaryFile()
    {
        SelectFolder("PreviousPaymentSummaryFile");
    }

    public async void SelectFolder(String type)
    {
        // 使用正确的方式获取主窗口
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (mainWindow == null) return;

        var options = new FilePickerOpenOptions
        {
            Title = "选择文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("excel新版文件") { Patterns = new[] { "*.xlsx" } },
            },
        };

        var result = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        if (result != null && result.Count > 0)
        {
            var localPath = result[0].Path.LocalPath;
            if ("PreviousPaymentSummaryFile" == type)
            {
                PreviousPaymentSummaryFilePath = localPath;
            }
            else if ("CurrentMonthPaymentFile" == type)
            {
                CurrentMonthPaymentFilePath = localPath;
            }
            else if ("InvoiceSummaryFile" == type)
            {
                InvoiceSummaryFilePath = localPath;
            }
        }
    }

    // 开始计算命令
    [RelayCommand]
    public async Task StartCalculation()
    {
        IsCalculating = true;
        // 清空日志
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            LogEntries.Clear();
        }
        else
        {
            // 清空日志必须先完成，避免后续后台读取把新日志与上一轮残留日志交错。
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => LogEntries.Clear());
        }

        try
        {
            await _invoiceInfoImportBusiness.ClearAllData();
            await ReadAllExcelData();
            AddLogLine("Excel文件读取完成！准备开始生成数据...");
            await _invoiceInfoImportBusiness.CreateAllNeedShowInvoiceNumber(StartDate?.DateTime, EndDate?.DateTime);
            AddLogLine("识别完成，开始计算新表...");
            await _invoiceInfoImportBusiness.CalculateNewInvoiceSummary();
            AddLogLine("计算完成！开始导出表");
            // 添加文件保存功能
            await SaveInvoicePaymentSummaryToExcel();
        }
        catch (Exception ex)
        {
            AddLogLine($"处理Excel文件时出错: {ex.Message}");
        }
        finally
        {
            IsCalculating = false;
        }
    }

    private async Task SaveInvoicePaymentSummaryToExcel()
    {
        try
        {
            // 获取主窗口
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is not null)
            {
                var mainWindow = desktop.MainWindow;

                // 创建文件保存选项
                var options = new FilePickerSaveOptions
                {
                    Title = "保存发票汇总表",
                    DefaultExtension = "xlsx",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Excel文件 (.xlsx)") { Patterns = new[] { "*.xlsx" } },
                    },
                    SuggestedFileName = "发票汇总表"
                };
                // 显示保存文件对话框
                var file = await mainWindow.StorageProvider.SaveFilePickerAsync(options);
                if (file != null)
                {
                    var filePath = file.Path.LocalPath;
                    await _invoiceInfoImportBusiness.SaveInvoicePaymentSummaryToExcel(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            AddLogLine($"保存Excel文件时出错: {ex.Message}");
        }
    }

    private async Task ReadAllExcelData()
    {
        // 读取发票总表
        if (!string.IsNullOrEmpty(InvoiceSummaryFilePath))
        {
            AddLogLine($"--- 读取发票总表: {Path.GetFileName(InvoiceSummaryFilePath)} ---");
            await _invoiceInfoImportBusiness.ReadAndIndexInvoiceSummary(InvoiceSummaryFilePath);
        }

        // 读取当月付款表
        if (!string.IsNullOrEmpty(CurrentMonthPaymentFilePath))
        {
            AddLogLine($"--- 读取当月付款表: {Path.GetFileName(CurrentMonthPaymentFilePath)} ---");
            await _invoiceInfoImportBusiness.ReadInvoicePaymentDetailCurrentMonthTable(CurrentMonthPaymentFilePath);
        }

        // 读取之前付款总和表
        if (!string.IsNullOrEmpty(PreviousPaymentSummaryFilePath))
        {
            AddLogLine($"--- 读取之前付款总和表: {Path.GetFileName(PreviousPaymentSummaryFilePath)} ---");
            await _invoiceInfoImportBusiness.ReadInvoicePaymentDetailPreviousMonthTable(PreviousPaymentSummaryFilePath);
        }
    }

    // 添加日志行并保持只有最后10行
    private void AddLogLine(string logLine)
    {
        // 由于UI操作需要在主线程进行，使用Avalonia的Dispatcher
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            UpdateLog(logLine);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => UpdateLog(logLine));
        }
    }

    private void UpdateLog(string logLine)
    {
        lock (LogEntries)
        {
            LogEntries.Add(logLine);

            // 如果日志行数超过10行，移除最早的日志
            if (LogEntries.Count > MAX_LOG_LINES)
            {
                LogEntries.RemoveAt(0);
            }
        }
    }
    
    // 添加复制所有日志的命令
    [RelayCommand]
    public async Task CopyAllLogs()
    {
        try
        {
            // 获取主窗口
            var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow == null) return;

            var clipboard = mainWindow.Clipboard;
            if (clipboard == null)
            {
                AddLogLine("当前窗口不支持剪贴板，无法复制日志");
                return;
            }

            // 将所有日志条目合并为一个字符串，每行之间用换行符分隔
            var allLogs = string.Join(Environment.NewLine, LogEntries);
            
            // 使用剪贴板设置文本
            await clipboard.SetTextAsync(allLogs);
            
            // 添加一条日志，表示复制成功
            AddLogLine("所有日志已复制到剪贴板");
        }
        catch (Exception ex)
        {
            AddLogLine($"复制日志失败: {ex.Message}");
        }
    }
}
