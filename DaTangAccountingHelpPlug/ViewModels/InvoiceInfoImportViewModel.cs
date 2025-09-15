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
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => LogEntries.Clear());
        }

        try
        {
            await ReadAllExcelData();
            AddLogLine("Excel文件读取完成！准备开始生成数据...");
            await _invoiceInfoImportBusiness.CreateAllNeedShowInvoiceNumber();
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
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
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
                    // 获取本地文件路径
                    var filePath = file.Path.LocalPath;

                    AddLogLine($"开始保存数据到文件: {Path.GetFileName(filePath)}");

                    // 在后台线程中创建和保存Excel文件
                    await Task.Run(() =>
                    {
                        // 设置EPPlus非商业使用许可
                        ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug");

                        using (var package = new ExcelPackage())
                        {
                            // 创建工作表
                            var worksheet = package.Workbook.Worksheets.Add("发票汇总表");

                            // 设置表头
                            worksheet.Cells[1, 1].Value = "发票类型";
                            worksheet.Cells[1, 2].Value = "供应商名称";
                            worksheet.Cells[1, 3].Value = "供应商地点";
                            worksheet.Cells[1, 4].Value = "发票日期";
                            worksheet.Cells[1, 5].Value = "发票号码";
                            worksheet.Cells[1, 6].Value = "部门";
                            worksheet.Cells[1, 7].Value = "负债科目";
                            worksheet.Cells[1, 8].Value = "发票金额";
                            worksheet.Cells[1, 9].Value = "计算付款金额";
                            worksheet.Cells[1, 10].Value = "计算余额";
                            worksheet.Cells[1, 11].Value = "到期日期";
                            worksheet.Cells[1, 12].Value = "备注";
                            worksheet.Cells[1, 13].Value = "类别";
                            worksheet.Cells[1, 14].Value = "付款金额";
                            worksheet.Cells[1, 15].Value = "付款日期";
                            worksheet.Cells[1, 16].Value = "结算金额";
                            worksheet.Cells[1, 17].Value = "结算日期";
                            worksheet.Cells[1, 18].Value = "发票信息付款金额";
                            worksheet.Cells[1, 19].Value = "发票信息余额";

                            // 设置表头样式
                            using (var range = worksheet.Cells["A1:S1"])
                            {
                                range.Style.Font.Bold = true;
                                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                            }

                            // 填充数据
                            int row = 2;
                            foreach (var item in _invoiceInfoImportBusiness.InvoicePaymentSummaryItems)
                            {
                                worksheet.Cells[row, 1].Value = item.InvoiceType;
                                worksheet.Cells[row, 2].Value = item.SupplierName;
                                worksheet.Cells[row, 3].Value = item.SupplierLocation;
                                worksheet.Cells[row, 4].Value = item.InvoiceDate?.ToString("yyyy-MM-dd");
                                worksheet.Cells[row, 5].Value = item.InvoiceNumber;
                                worksheet.Cells[row, 6].Value = item.Department;
                                worksheet.Cells[row, 7].Value = item.LiabilityAccount;
                                worksheet.Cells[row, 8].Value = item.InvoiceAmount;
                                worksheet.Cells[row, 9].Value = item.CalculatedPaymentAmount;
                                worksheet.Cells[row, 10].Value = item.CalculatedBalance;
                                worksheet.Cells[row, 11].Value = item.DueDate?.ToString("yyyy-MM-dd");
                                worksheet.Cells[row, 12].Value = item.Remarks;
                                worksheet.Cells[row, 13].Value = item.Category;
                                worksheet.Cells[row, 14].Value = item.PaymentAmount;
                                worksheet.Cells[row, 15].Value = item.PaymentDate?.ToString("yyyy-MM-dd");
                                worksheet.Cells[row, 16].Value = item.SettlementAmount;
                                worksheet.Cells[row, 17].Value = item.SettlementDate?.ToString("yyyy-MM-dd");
                                worksheet.Cells[row, 18].Value = item.InvoiceInfoPaymentAmount;
                                worksheet.Cells[row, 19].Value = item.InvoiceInfoBalance;

                                row++;
                            }

                            // 自动调整列宽
                            worksheet.Cells.AutoFitColumns();

                            // 保存文件
                            package.SaveAs(new FileInfo(filePath));
                        }
                    });

                    AddLogLine($"数据保存成功！文件路径：{filePath}");
                }
                else
                {
                    AddLogLine("用户取消了保存操作");
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
}