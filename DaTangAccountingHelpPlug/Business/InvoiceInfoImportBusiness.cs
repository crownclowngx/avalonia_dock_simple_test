using DaTangAccountingHelpPlug.Models;
using OfficeOpenXml;

namespace DaTangAccountingHelpPlug.Business;

public class InvoiceInfoImportBusiness
{
    // 定义日志回调委托，用于将日志信息传回ViewModel
    public delegate void LogDelegate(string logMessage);

    private readonly LogDelegate _logMethod;

    // 存储发票摘要项的字典，以发票编号为key
    public Dictionary<string, InvoiceSummaryItem> InvoiceSummaryItems { get; } =
        new Dictionary<string, InvoiceSummaryItem>();

    /// <summary>
    /// 发票付款分组详情 按照发票号进行分组 ，然后填写入详情
    /// </summary>
    public Dictionary<string, InvoicePaymentGroupDetailItem> InvoicePaymentGroupDetails { get; } =
        new Dictionary<string, InvoicePaymentGroupDetailItem>();

    /// <summary>
    /// 历史的发票收付款 信息
    /// </summary>
    public Dictionary<string, InvoicePaymentPreviousDetailItem> InvoicePaymentPreviousDetails { get; } =
        new Dictionary<string, InvoicePaymentPreviousDetailItem>();


    public Dictionary<string, string> SupplierTypeMapping { get; } = new Dictionary<string, string>();

    /**
     * 发票付款摘要
     */
    public List<InvoicePaymentSummaryItem> InvoicePaymentSummaryItems { get; } = new List<InvoicePaymentSummaryItem>();

    /// <summary>
    /// 所有需要展示的发票号
    /// </summary>
    public HashSet<string> AllNeedShowInvoiceNumbers { get; } = new HashSet<string>();

    // 构造函数，接受日志方法委托
    public InvoiceInfoImportBusiness(LogDelegate logMethod)
    {
        _logMethod = logMethod;
    }

    public async Task SaveInvoicePaymentSummaryToExcel(string filePath)
    {
        Log($"开始保存数据到文件: {Path.GetFileName(filePath)}");

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
                foreach (var item in InvoicePaymentSummaryItems)
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

        Log($"数据保存成功！文件路径：{filePath}");
    }

    public async Task CalculateNewInvoiceSummary()
    {
        foreach (var item in AllNeedShowInvoiceNumbers)
        {
            InvoiceSummaryItems.TryGetValue(item, out InvoiceSummaryItem summaryItem);

            if (summaryItem == null)
            {
                Log($"错误：：没有找到发票摘要 {item}");
                continue;
            }

            var response = GetOneInvoicePaymentSummaryCalc(item);
            SupplierTypeMapping.TryGetValue(summaryItem.SupplierName ?? string.Empty, out var supplierType);
            InvoicePaymentSummaryItem summaryItemNew = new InvoicePaymentSummaryItem()
            {
                InvoiceType = summaryItem.InvoiceType,
                SupplierName = summaryItem.SupplierName,
                SupplierLocation = summaryItem.SupplierLocation,
                InvoiceDate = summaryItem.InvoiceDate,
                InvoiceNumber = summaryItem.InvoiceNumber,
                Department = summaryItem.Department,
                LiabilityAccount = summaryItem.LiabilityAccount,
                InvoiceAmount = summaryItem.InvoiceAmount,
                CalculatedPaymentAmount = response.CalculatedPaymentAmount,
                CalculatedBalance = response.CalculatedBalance,
                DueDate = summaryItem.DueDate,
                Remarks = response.Remarks,
                Category = supplierType ?? string.Empty,
                PaymentAmount = response.PaymentAmount,
                PaymentDate = response.PaymentDate,
                SettlementAmount = response.SettlementAmount,
                SettlementDate = response.SettlementDate,
                InvoiceInfoPaymentAmount = summaryItem.PaymentAmount,
                InvoiceInfoBalance = summaryItem.Balance,
            };
            InvoicePaymentSummaryItems.Add(summaryItemNew);
        }

        // 按照发票时间倒序排序
        InvoicePaymentSummaryItems.Sort((x, y) =>
        {
            if (x.InvoiceDate == null && y.InvoiceDate == null) return 0;
            if (x.InvoiceDate == null) return 1; // 没有日期的排在后面
            if (y.InvoiceDate == null) return -1; // 没有日期的排在后面
            return y.InvoiceDate.Value.CompareTo(x.InvoiceDate.Value); // 倒序排序
        });
    }

    InvoicePaymentSummaryCalcItem GetOneInvoicePaymentSummaryCalc(String numbser)
    {
        InvoicePaymentGroupDetails.TryGetValue(numbser, out InvoicePaymentGroupDetailItem? groupDetailItem);
        InvoicePaymentPreviousDetails.TryGetValue(numbser, out InvoicePaymentPreviousDetailItem? previousDetailItem);
        InvoiceSummaryItems.TryGetValue(numbser, out InvoiceSummaryItem? summaryItem);
        List<InvoicePaymentOneWayPaymentDetailItem> paymentList = new List<InvoicePaymentOneWayPaymentDetailItem>();
        List<InvoicePaymentOneWayPaymentDetailItem> settlementList = new List<InvoicePaymentOneWayPaymentDetailItem>();
        if (previousDetailItem != null)
        {
            if (previousDetailItem.PaymentDate.HasValue)
            {
                paymentList.Add(new InvoicePaymentOneWayPaymentDetailItem()
                {
                    Amount = previousDetailItem.PaymentAmount,
                    AmountDateTime = previousDetailItem.PaymentDate,
                });
            }

            if (previousDetailItem.SettlementDate.HasValue)
            {
                settlementList.Add(new InvoicePaymentOneWayPaymentDetailItem()
                {
                    Amount = previousDetailItem.SettlementAmount,
                    AmountDateTime = previousDetailItem.SettlementDate,
                });
            }
        }

        if (groupDetailItem != null)
        {
            foreach (var item in groupDetailItem.PaymentInvoicePaymentDetailItems)
            {
                paymentList.Add(new InvoicePaymentOneWayPaymentDetailItem()
                {
                    Amount = item.PaymentAmount,
                    AmountDateTime = item.PaymentDate,
                });
            }

            foreach (var item in groupDetailItem.SettlementInvoicePaymentDetailItems)
            {
                settlementList.Add(new InvoicePaymentOneWayPaymentDetailItem()
                {
                    Amount = item.PaymentAmount,
                    AmountDateTime = item.PaymentDate,
                });
            }
        }

        var paymentAmountFinal = paymentList.Sum(k => k.Amount);
        var settlementAmountFinal = settlementList.Sum(k => k.Amount);
        var paymentDate = paymentList.OrderByDescending(k => k.AmountDateTime).FirstOrDefault()?.AmountDateTime;
        var settlementDate = settlementList.OrderByDescending(k => k.AmountDateTime).FirstOrDefault()?.AmountDateTime;
        InvoicePaymentSummaryCalcItem calcItem = new InvoicePaymentSummaryCalcItem()
        {
            CalculatedPaymentAmount = paymentAmountFinal + settlementAmountFinal,
            CalculatedBalance = summaryItem?.InvoiceAmount - paymentAmountFinal - settlementAmountFinal,
            Remarks = previousDetailItem?.ReMark ?? string.Empty,
            PaymentAmount = paymentAmountFinal,
            PaymentDate = paymentDate,
            SettlementAmount = settlementAmountFinal,
            SettlementDate = settlementDate,
        };
        return calcItem;
    }

    public async Task CreateAllNeedShowInvoiceNumber(DateTime? startDate, DateTime? endDate)
    {
        foreach (var invoicePaymentPreviousDetailItem in InvoicePaymentPreviousDetails)
        {
            if (!AllNeedShowInvoiceNumbers.Contains(invoicePaymentPreviousDetailItem.Value.InvoiceNumber))
            {
                AllNeedShowInvoiceNumbers.Add(invoicePaymentPreviousDetailItem.Value.InvoiceNumber);
            }
        }

        int newCount = 0;
        foreach (var item in InvoiceSummaryItems)
        {
            if (string.IsNullOrEmpty(item.Value.InvoiceNumber))
            {
                continue;
            }

            if (!AllNeedShowInvoiceNumbers.Contains(item.Value.InvoiceNumber) && item.Value.InvoiceDate >= startDate &&
                item.Value.InvoiceDate <= endDate)
            {
                newCount++;
                AllNeedShowInvoiceNumbers.Add(item.Value.InvoiceNumber);
            }
        }

        Log($"新增的发票号数量 {newCount}");
    }

    public async Task ReadInvoicePaymentDetailPreviousMonthTable(string filePath)
    {
        try
        {
            await Task.Run(() =>
            {
                ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug");
                using (ExcelPackage package = new ExcelPackage(new FileInfo(filePath)))
                {
                    // 获取第一个工作表
                    ExcelWorksheet worksheet = package.Workbook.Worksheets[0];
                    // 获取工作表的维度（包含数据的范围）
                    int startRow = 2; // 从第2行开始是真正的数据
                    int endRow = worksheet.Dimension.End.Row;
                    Log($"开始读取之前月付款数据，共 {endRow - startRow + 1} 行");
                    for (int row = startRow; row <= endRow; row++)
                    {
                        InvoicePaymentPreviousDetailItem item =
                            CreateInvoicePaymentDetailItemByPreviousMonthDetailTable(row, worksheet);
                        if (item == null)
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(item.SupplierName) && !string.IsNullOrEmpty(item.SupplierType) &&
                            !SupplierTypeMapping.ContainsKey(item.SupplierName))
                        {
                            SupplierTypeMapping.TryAdd(item.SupplierName, item.SupplierType);
                        }

                        if (InvoicePaymentPreviousDetails.ContainsKey(item.InvoiceNumber))
                        {
                            Log($"有重复的发票号跳过 {item.InvoiceNumber}");
                            continue;
                        }

                        InvoicePaymentPreviousDetails.Add(item.InvoiceNumber, item);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log($"读取之前付款总和表时出错: {ex.Message}");
        }
    }

    private InvoicePaymentPreviousDetailItem CreateInvoicePaymentDetailItemByPreviousMonthDetailTable(int row,
        ExcelWorksheet worksheet)
    {
        InvoicePaymentPreviousDetailItem item = new InvoicePaymentPreviousDetailItem();
        try
        {
            // 供应商名称 B列 第2列
            string supplierName = worksheet.Cells[row, 2].Text.Trim();
            item.SupplierName = supplierName;

            // 发票编号 E列 第5列
            string invoiceNumber = worksheet.Cells[row, 5].Text.Trim();
            if (string.IsNullOrEmpty(invoiceNumber))
            {
                return null;
            }

            item.InvoiceNumber = invoiceNumber;

            // 备注 L列 第12列
            string reMark = worksheet.Cells[row, 12].Text.Trim();
            item.ReMark = reMark;
            // 供应商类型 M列 第13列
            string supplierType = worksheet.Cells[row, 13].Text.Trim();
            item.SupplierType = supplierType;

            // 付 付款日期 O列 第15列 可空
            string paymentDateStr = worksheet.Cells[row, 15].Text.Trim();
            if (!string.IsNullOrEmpty(paymentDateStr))
            {
                if (DateTime.TryParse(paymentDateStr, out DateTime paymentDate))
                {
                    item.PaymentDate = paymentDate;
                }
                else
                {
                    Log($"警告：行 {row} 的付款日期格式不正确: {paymentDateStr}");
                }
            }

            // 付 付款金额 N列 第14列 可空
            string paymentAmountStr = worksheet.Cells[row, 14].Text.Trim();
            if (!string.IsNullOrEmpty(paymentAmountStr))
            {
                if (paymentAmountStr == "-")
                {
                    paymentAmountStr = "0";
                }

                // 移除千位分隔符
                paymentAmountStr = paymentAmountStr.Replace(",", "");
                if (decimal.TryParse(paymentAmountStr, out decimal paymentAmount))
                {
                    item.PaymentAmount = paymentAmount;
                }
                else
                {
                    Log($"警告：行 {row} 的付款金额格式不正确: {paymentAmountStr}");
                }
            }

            // 结 结款金额 P列 第16列 可空
            string settlementAmountStr = worksheet.Cells[row, 16].Text.Trim();
            if (!string.IsNullOrEmpty(settlementAmountStr))
            {
                if (settlementAmountStr == "-")
                {
                    settlementAmountStr = "0";
                }

                // 移除千位分隔符
                settlementAmountStr = settlementAmountStr.Replace(",", "");
                if (decimal.TryParse(settlementAmountStr, out decimal settlementAmount))
                {
                    item.SettlementAmount = settlementAmount;
                }
                else
                {
                    Log($"警告：行 {row} 的结款金额格式不正确: {settlementAmountStr}");
                }
            }

            // 结 结款日期 Q列 第17列 可空
            string settlementDateStr = worksheet.Cells[row, 17].Text.Trim();
            if (!string.IsNullOrEmpty(settlementDateStr))
            {
                if (DateTime.TryParse(settlementDateStr, out DateTime settlementDate))
                {
                    item.SettlementDate = settlementDate;
                }
                else
                {
                    Log($"警告：行 {row} 的结款日期格式不正确: {settlementDateStr}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"错误：处理行 {row} 的历史付款明细时发生异常: {ex.Message}");
        }

        return item;
    }

    public async Task ReadInvoicePaymentDetailCurrentMonthTable(string filePath)
    {
        try
        {
            await Task.Run(() =>
            {
                // 设置EPPlus非商业使用许可
                ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug");

                using (ExcelPackage package = new ExcelPackage(new FileInfo(filePath)))
                {
                    // 获取第一个工作表
                    ExcelWorksheet worksheet = package.Workbook.Worksheets[0];

                    // 获取工作表的维度（包含数据的范围）
                    int startRow = 4; // 从第四行开始是真正的数据
                    int endRow = worksheet.Dimension.End.Row;
                    InvoicePaymentGroupDetails.Clear();
                    Log($"开始读取当前月付款数据，共 {endRow - startRow + 1} 行");
                    for (int row = startRow; row <= endRow; row++)
                    {
                        InvoicePaymentDetailItem item =
                            CreateInvoicePaymentDetailItemByCurrentMonthDetailTable(row, worksheet);
                        if (!InvoicePaymentGroupDetails.ContainsKey(item.InvoiceNumber))
                        {
                            InvoicePaymentGroupDetailItem detailGroupItem = new InvoicePaymentGroupDetailItem()
                            {
                                InvoiceNumber = item.InvoiceNumber,
                                SettlementInvoicePaymentDetailItems = new List<InvoicePaymentDetailItem>(),
                                PaymentInvoicePaymentDetailItems = new List<InvoicePaymentDetailItem>()
                            };
                            InvoicePaymentGroupDetails.Add(item.InvoiceNumber, detailGroupItem);
                        }

                        if (item.PaymentSummary.StartsWith("结"))
                        {
                            InvoicePaymentGroupDetails[item.InvoiceNumber].SettlementInvoicePaymentDetailItems
                                .Add(item);
                        }
                        else if (item.PaymentSummary.StartsWith("付"))
                        {
                            InvoicePaymentGroupDetails[item.InvoiceNumber].PaymentInvoicePaymentDetailItems.Add(item);
                        }
                    }

                    // 处理完成后，记录成功读取的发票数量
                    Log($"当前月付款数据读取完成，成功加载 {InvoicePaymentGroupDetails.Count} 条发票记录");
                }
            });
        }
        catch (Exception ex)
        {
            Log($"读取当前月付款数据时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 创建发票付款详情项
    /// </summary>
    /// <param name="row"></param>
    /// <param name="worksheet"></param>
    /// <returns></returns>
    public InvoicePaymentDetailItem CreateInvoicePaymentDetailItemByCurrentMonthDetailTable(int row,
        ExcelWorksheet worksheet)
    {
        InvoicePaymentDetailItem item = new InvoicePaymentDetailItem();
        try
        {
            // 发票编号（F列，第6列）
            string invoiceNumber = worksheet.Cells[row, 6].Text.Trim();
            item.InvoiceNumber = invoiceNumber;

            // 付款日期（M列，第13列）
            string paymentDateStr = worksheet.Cells[row, 13].Text.Trim();
            if (!string.IsNullOrEmpty(paymentDateStr))
            {
                if (DateTime.TryParse(paymentDateStr, out DateTime paymentDate))
                {
                    item.PaymentDate = paymentDate;
                }
                else
                {
                    Log($"警告：行 {row} 的付款日期格式不正确: {paymentDateStr}");
                }
            }

            // 付款金额（N列，第14列）
            string paymentAmountStr = worksheet.Cells[row, 14].Text.Trim();
            if (!string.IsNullOrEmpty(paymentAmountStr))
            {
                // 移除千位分隔符
                paymentAmountStr = paymentAmountStr.Replace(",", "");
                if (decimal.TryParse(paymentAmountStr, out decimal paymentAmount))
                {
                    item.PaymentAmount = paymentAmount;
                }
                else
                {
                    Log($"警告：行 {row} 的付款金额格式不正确: {paymentAmountStr}");
                }
            }

            // 付款摘要（O列，第15列）
            string paymentSummary = worksheet.Cells[row, 15].Text.Trim();
            item.PaymentSummary = paymentSummary;

            // 银行账户（P列，第16列）
            string bankAccount = worksheet.Cells[row, 16].Text.Trim();
            item.BankAccount = bankAccount;

            // 付款方法（Q列，第17列）
            string paymentMethod = worksheet.Cells[row, 17].Text.Trim();
            item.PaymentMethod = paymentMethod;

            // 付款编号（R列，第18列）
            string paymentNumber = worksheet.Cells[row, 18].Text.Trim();
            item.PaymentNumber = paymentNumber;

            // 凭证编号（S列，第19列）
            string certificateNumber = worksheet.Cells[row, 19].Text.Trim();
            item.CertificateNumber = certificateNumber;

            // 记录创建行号，便于后续追溯
            item.SourceRowIndex = row;
        }
        catch (Exception ex)
        {
            Log($"错误：处理行 {row} 的付款明细时发生异常: {ex.Message}");
        }

        return item;
    }


    // 创建发票摘要项
    public InvoiceSummaryItem CreateInvoiceSummaryItem(int row, ExcelWorksheet worksheet)
    {
        // 创建新的发票摘要项
        InvoiceSummaryItem item = new InvoiceSummaryItem();

        // 读取数据并设置属性
        // 注意：AB列合并为发票类型（假设A列是第1列，B列是第2列）
        item.InvoiceType = worksheet.Cells[row, 1].Text.Trim();

        // 供应商名称 (第3列)
        item.SupplierName = worksheet.Cells[row, 3].Text.Trim();

        // 供应商类型 (第4列)
        item.SupplierType = worksheet.Cells[row, 4].Text.Trim();

        // 是否外部单位 (第5列)
        string isExternalUnitStr = worksheet.Cells[row, 5].Text.Trim().ToLower();
        if (isExternalUnitStr == "是" || isExternalUnitStr == "yes" || isExternalUnitStr == "true")
        {
            item.IsExternalUnit = true;
        }
        else if (isExternalUnitStr == "否" || isExternalUnitStr == "no" || isExternalUnitStr == "false")
        {
            item.IsExternalUnit = false;
        }

        // 供应商地点 (第6列)
        item.SupplierLocation = worksheet.Cells[row, 6].Text.Trim();

        // 发票日期 (第7列)
        string invoiceDateStr = worksheet.Cells[row, 7].Text.Trim();
        if (!string.IsNullOrEmpty(invoiceDateStr))
        {
            if (DateTime.TryParse(invoiceDateStr, out DateTime invoiceDate))
            {
                item.InvoiceDate = invoiceDate;
            }
        }

        // 发票编号 (第8列) - 作为字典的key
        string invoiceNumber = worksheet.Cells[row, 8].Text.Trim();
        item.InvoiceNumber = invoiceNumber;

        // 发票摘要 (第9列)
        item.InvoiceSummary = worksheet.Cells[row, 9].Text.Trim();

        // 部门 (第10列)
        item.Department = worksheet.Cells[row, 10].Text.Trim();

        // 合同编号 (第11列)
        item.ContractNumber = worksheet.Cells[row, 11].Text.Trim();

        // 合同名称 (第12列)
        item.ContractName = worksheet.Cells[row, 12].Text.Trim();

        // 负债账户 (第13列)
        item.LiabilityAccount = worksheet.Cells[row, 13].Text.Trim();

        // 发票金额 (第14列)
        string invoiceAmountStr = worksheet.Cells[row, 14].Text.Trim();
        if (!string.IsNullOrEmpty(invoiceAmountStr))
        {
            // 移除千位分隔符
            invoiceAmountStr = invoiceAmountStr.Replace(",", "");
            if (decimal.TryParse(invoiceAmountStr, out decimal invoiceAmount))
            {
                item.InvoiceAmount = invoiceAmount;
            }
        }

        // 付款金额 (第15列)
        string paymentAmountStr = worksheet.Cells[row, 15].Text.Trim();
        if (!string.IsNullOrEmpty(paymentAmountStr))
        {
            paymentAmountStr = paymentAmountStr.Replace(",", "");
            if (decimal.TryParse(paymentAmountStr, out decimal paymentAmount))
            {
                item.PaymentAmount = paymentAmount;
            }
        }

        // 核销预付款金额 (第16列)
        string writeOffPrepaymentAmountStr = worksheet.Cells[row, 16].Text.Trim();
        if (!string.IsNullOrEmpty(writeOffPrepaymentAmountStr))
        {
            writeOffPrepaymentAmountStr = writeOffPrepaymentAmountStr.Replace(",", "");
            if (decimal.TryParse(writeOffPrepaymentAmountStr, out decimal writeOffPrepaymentAmount))
            {
                item.WriteOffPrepaymentAmount = writeOffPrepaymentAmount;
            }
        }

        // 余额 (第17列)
        string balanceStr = worksheet.Cells[row, 17].Text.Trim();
        if (!string.IsNullOrEmpty(balanceStr))
        {
            balanceStr = balanceStr.Replace(",", "");
            if (decimal.TryParse(balanceStr, out decimal balance))
            {
                item.Balance = balance;
            }
        }

        // 凭证编号 (第18列)
        item.VoucherNumber = worksheet.Cells[row, 18].Text.Trim();

        // 到期日 (第19列)
        string dueDateStr = worksheet.Cells[row, 19].Text.Trim();
        if (!string.IsNullOrEmpty(dueDateStr))
        {
            if (DateTime.TryParse(dueDateStr, out DateTime dueDate))
            {
                item.DueDate = dueDate;
            }
        }

        // 共享单据编号 (第20列)
        item.SharedDocumentNumber = worksheet.Cells[row, 20].Text.Trim();
        return item;
    }

    // 读取并索引发票总表
    public async Task ReadAndIndexInvoiceSummary(string filePath)
    {
        try
        {
            await Task.Run(() =>
            {
                // 设置EPPlus非商业使用许可
                ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug");

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    // 获取第一个工作表
                    ExcelWorksheet worksheet = package.Workbook.Worksheets[0];

                    // 获取工作表的维度（包含数据的范围）
                    int startRow = 4; // 从第四行开始是真正的数据
                    int endRow = worksheet.Dimension.End.Row;
                    // 清空现有数据
                    InvoiceSummaryItems.Clear();

                    Log($"开始读取发票数据，共 {endRow - startRow + 1} 行");
                    for (int row = startRow; row <= endRow; row++)
                    {
                        InvoiceSummaryItem item = CreateInvoiceSummaryItem(row, worksheet);
                        // 将发票项添加到字典中，以发票编号为key
                        if (!string.IsNullOrEmpty(item.InvoiceNumber) &&
                            !InvoiceSummaryItems.ContainsKey(item.InvoiceNumber))
                        {
                            InvoiceSummaryItems.Add(item.InvoiceNumber, item);
                        }
                        else if (string.IsNullOrEmpty(item.InvoiceNumber))
                        {
                            Log($"警告：行 {row} 缺少发票编号，已跳过");
                        }
                        else if (InvoiceSummaryItems.ContainsKey(item.InvoiceNumber))
                        {
                            Log($"警告：行 {row} 的发票编号 '{item.InvoiceNumber}' 已存在，已跳过重复项");
                        }
                    }

                    // 处理完成后，记录成功读取的发票数量
                    Log($"发票数据读取完成，成功加载 {InvoiceSummaryItems.Count} 条记录");
                }
            });
        }
        catch (Exception ex)
        {
            Log($"读取发票总表时出错: {ex.Message}");
        }
    }

    // 内部日志方法，使用委托调用ViewModel的日志方法
    private void Log(string message)
    {
        _logMethod?.Invoke(message);
    }
}