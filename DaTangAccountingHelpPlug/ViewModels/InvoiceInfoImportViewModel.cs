using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    
    // 日志条目集合，用于在ListBox中显示
    public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();
    
    private const int MAX_LOG_LINES = 10;

    public InvoiceInfoImportViewModel()
    {
        Title = "发票信息导入和计算";
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
        // 清空日志
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            LogEntries.Clear();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => LogEntries.Clear());
        }
        
        AddLogLine("开始处理Excel文件...");

        try
        {
            // 读取发票总表
            if (!string.IsNullOrEmpty(InvoiceSummaryFilePath))
            {
                AddLogLine($"\n--- 读取发票总表: {Path.GetFileName(InvoiceSummaryFilePath)} ---");
                await ReadExcelFile(InvoiceSummaryFilePath);
            }

            // 读取当月付款表
            if (!string.IsNullOrEmpty(CurrentMonthPaymentFilePath))
            {
                AddLogLine($"\n--- 读取当月付款表: {Path.GetFileName(CurrentMonthPaymentFilePath)} ---");
                await ReadExcelFile(CurrentMonthPaymentFilePath);
            }

            // 读取之前付款总和表
            if (!string.IsNullOrEmpty(PreviousPaymentSummaryFilePath))
            {
                AddLogLine($"\n--- 读取之前付款总和表: {Path.GetFileName(PreviousPaymentSummaryFilePath)} ---");
                await ReadExcelFile(PreviousPaymentSummaryFilePath);
            }

            AddLogLine("Excel文件读取完成！");
        }
        catch (Exception ex)
        {
            AddLogLine($"处理Excel文件时出错: {ex.Message}");
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
    
    private async Task ReadExcelFile(string filePath)
    {
        // 异步读取Excel文件
        await Task.Run(() =>
        {
          
            // 设置EPPlus非商业使用许可
            ExcelPackage.License.SetNonCommercialPersonal ("DaTangAccountingHelpPlug");

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // 获取第一个工作表
                ExcelWorksheet worksheet = package.Workbook.Worksheets[0];

                // 添加工作表信息到日志
                string sheetInfo = $"工作表名称: {worksheet.Name}";
                AddLogLine(sheetInfo);

                // 获取工作表的维度（包含数据的范围）
                int startRow = worksheet.Dimension.Start.Row;
                int endRow = worksheet.Dimension.End.Row;
                int startCol = worksheet.Dimension.Start.Column;
                int endCol = worksheet.Dimension.End.Column;

                // 添加数据范围信息到日志
                string rangeInfo = $"数据范围: 行 {startRow}-{endRow}, 列 {startCol}-{endCol}";
                AddLogLine(rangeInfo);

                // 读取每一行数据并添加到日志
                for (int row = startRow; row <= endRow; row++)
                {
                    // 构建当前行的数据字符串
                    System.Text.StringBuilder rowData = new System.Text.StringBuilder();
                    rowData.Append($"行 {row}: ");

                    // 读取当前行的每一列数据
                    for (int col = startCol; col <= endCol; col++)
                    {
                        var cellValue = worksheet.Cells[row, col].Text;
                        rowData.Append($"{cellValue}");

                        // 如果不是最后一列，添加分隔符
                        if (col < endCol)
                        {
                            rowData.Append(" | ");
                        }
                    }

                    // 将当前行数据添加到日志
                    AddLogLine(rowData.ToString());
                }
            }
        });
    }
}