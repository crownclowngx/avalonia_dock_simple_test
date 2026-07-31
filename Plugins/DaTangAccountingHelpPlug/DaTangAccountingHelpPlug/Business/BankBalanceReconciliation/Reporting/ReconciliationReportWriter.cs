using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reporting;

/// <summary>生成宏无关、公式可追溯的余额调节工作簿。</summary>
public sealed class ReconciliationReportWriter : IReconciliationReportWriter
{
    private const string CurrencyFormat = "#,##0.00;[Red](#,##0.00);-";
    private static readonly System.Drawing.Color HeaderColor = System.Drawing.Color.FromArgb(31, 78, 121);
    private static readonly System.Drawing.Color AccentColor = System.Drawing.Color.FromArgb(221, 235, 247);
    private static readonly System.Drawing.Color WarningColor = System.Drawing.Color.FromArgb(255, 235, 156);

    public Task WriteAsync(
        ReconciliationResult result,
        IProgress<ReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Write(result, progress, cancellationToken), cancellationToken);

    private static void Write(
        ReconciliationResult result,
        IProgress<ReconciliationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug");
        progress?.Report(new ReconciliationProgress("输出", "正在生成余额调节工作簿", 75));

        using var package = new ExcelPackage();
        BuildBalanceSheet(package.Workbook.Worksheets.Add("余额调节表"), result);
        BuildDetailSheet(
            package.Workbook.Worksheets.Add("收款明细"),
            "收款",
            result.BankReceivedUnrecorded,
            result.EnterpriseReceivedUnrecorded);
        BuildDetailSheet(
            package.Workbook.Worksheets.Add("付款明细"),
            "付款",
            result.BankPaidUnrecorded,
            result.EnterprisePaidUnrecorded);
        BuildAuditSheet(package.Workbook.Worksheets.Add("匹配审计"), result);

        cancellationToken.ThrowIfCancellationRequested();
        var outputPath = Path.GetFullPath(result.Request.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath)
                              ?? throw new InvalidOperationException("输出路径没有有效目录。");
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            package.SaveAs(new FileInfo(temporaryPath));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        progress?.Report(new ReconciliationProgress("完成", "工作簿已生成", 100));
    }

    private static void BuildBalanceSheet(ExcelWorksheet sheet, ReconciliationResult result)
    {
        sheet.View.ShowGridLines = false;
        sheet.Cells.Style.Font.Name = "宋体";
        sheet.Cells.Style.Font.Size = 10;
        sheet.Cells["A1:I1"].Merge = true;
        sheet.Cells["A1"].Value = $"{result.Request.Profile.UnitName}银行存款余额调节表";
        sheet.Cells["A1"].Style.Font.Size = 18;
        sheet.Cells["A1"].Style.Font.Bold = true;
        sheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        sheet.Row(1).Height = 30;

        sheet.Cells["A2"].Value = "开户行：";
        sheet.Cells["B2:D2"].Merge = true;
        sheet.Cells["B2"].Value = result.Request.Profile.BankName;
        sheet.Cells["E2"].Value = "账号：";
        sheet.Cells["F2:H2"].Merge = true;
        sheet.Cells["F2"].Value = result.Request.Profile.AccountNumber;
        sheet.Cells["I2"].Value = result.Request.PreviousUnreconciledDifference == 0m
            ? string.Empty
            : $"上月未达：{Math.Abs(result.Request.PreviousUnreconciledDifference):N2}";

        sheet.Cells["A3:I3"].Merge = true;
        sheet.Cells["A3"].Value = $"截止日期：{result.Request.AsOfDate:yyyy-MM-dd}";
        sheet.Cells["A3"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        sheet.Cells["A4:C4"].Merge = true;
        sheet.Cells["A4"].Value = "企业账面余额";
        sheet.Cells["D4"].Value = ReconciliationResult.Money(
            result.Input.EnterpriseBalance + Math.Max(result.Request.PreviousUnreconciledDifference, 0m));
        sheet.Cells["F4:G4"].Merge = true;
        sheet.Cells["F4"].Value = "银行对账单余额";
        sheet.Cells["H4"].Value = ReconciliationResult.Money(
            result.Input.BankBalance + Math.Max(-result.Request.PreviousUnreconciledDifference, 0m));
        sheet.Cells["D4,H4"].Style.Numberformat.Format = CurrencyFormat;

        var headers = new object[,]
        {
            { "未达账项", "序号", "银行账明细", "银行已收企业未收", "银行已付企业未付", "序号", "企业账明细", "企业已收银行未收", "企业已付银行未付" }
        };
        sheet.Cells[5, 1, 5, 9].Value = headers;
        StyleHeader(sheet.Cells[5, 1, 5, 9]);

        var left = result.BankReceivedUnrecorded
            .Select(item => (Entry: item, Received: true))
            .Concat(result.BankPaidUnrecorded.Select(item => (Entry: item, Received: false)))
            .ToArray();
        var right = result.EnterpriseReceivedUnrecorded
            .Select(item => (Entry: item, Received: true))
            .Concat(result.EnterprisePaidUnrecorded.Select(item => (Entry: item, Received: false)))
            .ToArray();
        var dataRows = Math.Max(Math.Max(left.Length, right.Length), 1);
        var firstDataRow = 6;
        for (var index = 0; index < dataRows; index++)
        {
            var row = firstDataRow + index;
            if (index < left.Length)
            {
                var item = left[index];
                sheet.Cells[row, 2].Value = item.Entry.SourceRow;
                sheet.Cells[row, 3].Value = Describe(item.Entry);
                sheet.Cells[row, item.Received ? 4 : 5].Value = item.Entry.Amount;
                AddSourceComment(sheet.Cells[row, 2], item.Entry);
            }

            if (index < right.Length)
            {
                var item = right[index];
                sheet.Cells[row, 6].Value = item.Entry.ReferenceNumber;
                sheet.Cells[row, 7].Value = item.Entry.Summary;
                sheet.Cells[row, item.Received ? 8 : 9].Value = item.Entry.Amount;
                AddSourceComment(sheet.Cells[row, 6], item.Entry);
            }
        }

        var subtotalRow = firstDataRow + dataRows;
        sheet.Cells[subtotalRow, 2, subtotalRow, 3].Merge = true;
        sheet.Cells[subtotalRow, 2].Value = "小计";
        sheet.Cells[subtotalRow, 6, subtotalRow, 7].Merge = true;
        sheet.Cells[subtotalRow, 6].Value = "小计";
        foreach (var column in new[] { 4, 5, 8, 9 })
            sheet.Cells[subtotalRow, column].Formula = $"SUM({sheet.Cells[firstDataRow, column].Address}:{sheet.Cells[subtotalRow - 1, column].Address})";

        var finalRow = subtotalRow + 2;
        sheet.Cells[finalRow, 1, finalRow, 3].Merge = true;
        sheet.Cells[finalRow, 1].Value = "调整后企业余额";
        sheet.Cells[finalRow, 4].Formula = $"D4+D{subtotalRow}-E{subtotalRow}";
        sheet.Cells[finalRow, 6, finalRow, 7].Merge = true;
        sheet.Cells[finalRow, 6].Value = "调整后银行余额";
        sheet.Cells[finalRow, 8].Formula = $"H4+H{subtotalRow}-I{subtotalRow}";
        sheet.Cells[finalRow + 1, 1, finalRow + 1, 3].Merge = true;
        sheet.Cells[finalRow + 1, 1].Value = "银企差额";
        sheet.Cells[finalRow + 1, 4].Formula = $"D{finalRow}-H{finalRow}";
        sheet.Cells[finalRow + 1, 6, finalRow + 1, 7].Merge = true;
        sheet.Cells[finalRow + 1, 6].Value = "对账结果";
        sheet.Cells[finalRow + 1, 8].Formula = $"IF(ROUND(D{finalRow}-H{finalRow},2)=0,\"平\",\"不平\")";

        sheet.Cells[4, 1, finalRow + 1, 9].Style.Border.Top.Style = ExcelBorderStyle.Thin;
        sheet.Cells[4, 1, finalRow + 1, 9].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        sheet.Cells[4, 1, finalRow + 1, 9].Style.Border.Left.Style = ExcelBorderStyle.Thin;
        sheet.Cells[4, 1, finalRow + 1, 9].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        sheet.Cells[firstDataRow, 4, finalRow + 1, 5].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[firstDataRow, 8, finalRow + 1, 9].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[finalRow, 4, finalRow + 1, 4].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[finalRow, 8].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[subtotalRow, 1, subtotalRow, 9].Style.Fill.SetBackground(AccentColor);
        sheet.Cells[finalRow, 1, finalRow + 1, 9].Style.Fill.SetBackground(AccentColor);
        sheet.Cells[finalRow, 1, finalRow + 1, 9].Style.Font.Bold = true;
        sheet.Cells[finalRow + 1, 8].Style.Fill.SetBackground(result.IsBalanced ? System.Drawing.Color.Honeydew : WarningColor);
        sheet.Cells[1, 1, finalRow + 1, 9].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        sheet.Cells[firstDataRow, 3, subtotalRow - 1, 3].Style.WrapText = true;
        sheet.Cells[firstDataRow, 7, subtotalRow - 1, 7].Style.WrapText = true;
        sheet.Column(1).Width = 12;
        sheet.Column(2).Width = 9;
        sheet.Column(3).Width = 42;
        sheet.Column(4).Width = 17;
        sheet.Column(5).Width = 17;
        sheet.Column(6).Width = 12;
        sheet.Column(7).Width = 42;
        sheet.Column(8).Width = 17;
        sheet.Column(9).Width = 17;
        sheet.View.FreezePanes(6, 2);
        sheet.PrinterSettings.PrintArea = sheet.Cells[1, 1, finalRow + 1, 9];
        sheet.PrinterSettings.FitToPage = true;
        sheet.PrinterSettings.FitToWidth = 1;
        sheet.PrinterSettings.FitToHeight = 0;
    }

    private static void BuildDetailSheet(
        ExcelWorksheet sheet,
        string kind,
        IReadOnlyList<ReconciliationEntry> bankEntries,
        IReadOnlyList<ReconciliationEntry> enterpriseEntries)
    {
        sheet.View.ShowGridLines = false;
        var headers = new object[,]
        {
            { "银行序号", "银行账明细", $"银行{kind}金额", "企业凭证", "企业账明细", $"企业{kind}金额", "差额" }
        };
        sheet.Cells[1, 1, 1, 7].Value = headers;
        StyleHeader(sheet.Cells[1, 1, 1, 7]);
        var rowCount = Math.Max(Math.Max(bankEntries.Count, enterpriseEntries.Count), 1);
        for (var index = 0; index < rowCount; index++)
        {
            var row = index + 2;
            if (index < bankEntries.Count)
            {
                sheet.Cells[row, 1].Value = bankEntries[index].SourceRow;
                sheet.Cells[row, 2].Value = Describe(bankEntries[index]);
                sheet.Cells[row, 3].Value = bankEntries[index].Amount;
            }
            if (index < enterpriseEntries.Count)
            {
                sheet.Cells[row, 4].Value = enterpriseEntries[index].ReferenceNumber;
                sheet.Cells[row, 5].Value = enterpriseEntries[index].Summary;
                sheet.Cells[row, 6].Value = enterpriseEntries[index].Amount;
            }
            sheet.Cells[row, 7].Formula = $"C{row}-F{row}";
        }

        sheet.Cells[2, 3, rowCount + 1, 3].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[2, 6, rowCount + 1, 7].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[1, 1, rowCount + 1, 7].AutoFilter = true;
        sheet.Cells[1, 1, rowCount + 1, 7].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
        sheet.Column(1).Width = 11;
        sheet.Column(2).Width = 46;
        sheet.Column(3).Width = 17;
        sheet.Column(4).Width = 14;
        sheet.Column(5).Width = 46;
        sheet.Column(6).Width = 17;
        sheet.Column(7).Width = 17;
        sheet.View.FreezePanes(2, 1);
        var address = new ExcelAddress(2, 7, rowCount + 1, 7).Address;
        sheet.ConditionalFormatting.AddEqual(new ExcelAddress(address)).Formula = "0";
    }

    private static void BuildAuditSheet(ExcelWorksheet sheet, ReconciliationResult result)
    {
        sheet.View.ShowGridLines = false;
        sheet.Cells["A1:N1"].Merge = true;
        sheet.Cells["A1"].Value = "银行余额调节匹配审计";
        sheet.Cells["A1"].Style.Font.Size = 16;
        sheet.Cells["A1"].Style.Font.Bold = true;
        sheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        var metadata = new object[,]
        {
            { "单位", result.Request.Profile.UnitName, "银行", result.Request.Profile.BankName, "账号", MaskAccount(result.Request.Profile.AccountNumber) },
            { "截止日期", result.Request.AsOfDate, "模式", result.Request.Mode == ReconciliationMode.Strict ? "严格审计" : "旧宏兼容", "配置版本", result.Request.Configuration.SchemaVersion },
            { "企业账文件", Path.GetFileName(result.Request.EnterpriseLedgerPath), "银行账文件", Path.GetFileName(result.Request.BankStatementPath), "输出文件", Path.GetFileName(result.Request.OutputPath) },
            { "匹配数", result.MatchedCount, "歧义数", result.AmbiguousCount, "银企差额", result.Difference },
            { "模型状态", result.IsBalanced && result.AmbiguousCount == 0 ? "PASS" : "FAIL", "企业调节后余额", result.AdjustedEnterpriseBalance, "银行调节后余额", result.AdjustedBankBalance }
        };
        sheet.Cells[2, 1, 6, 6].Value = metadata;
        sheet.Cells[2, 1, 6, 1].Style.Font.Bold = true;
        sheet.Cells[2, 3, 6, 3].Style.Font.Bold = true;
        sheet.Cells[2, 5, 6, 5].Style.Font.Bold = true;
        sheet.Cells[2, 1, 6, 6].Style.Fill.SetBackground(AccentColor);
        sheet.Cells[3, 2].Style.Numberformat.Format = "yyyy-mm-dd";
        sheet.Cells[5, 6, 6, 6].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[6, 4].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[6, 6].Style.Numberformat.Format = CurrencyFormat;

        const int headerRow = 8;
        var headers = new object[,]
        {
            { "状态", "来源", "方向", "金额", "来源行", "日期", "编号", "对方/摘要", "匹配来源行", "匹配编号", "规则", "候选数", "原因", "候选行" }
        };
        sheet.Cells[headerRow, 1, headerRow, 14].Value = headers;
        StyleHeader(sheet.Cells[headerRow, 1, headerRow, 14]);
        var row = headerRow + 1;
        foreach (var decision in result.Decisions
                     .OrderBy(item => item.PrimaryEntry.Source)
                     .ThenBy(item => item.PrimaryEntry.SourceRow))
        {
            sheet.Cells[row, 1].Value = StatusName(decision.Status);
            sheet.Cells[row, 2].Value = decision.PrimaryEntry.Source == ReconciliationEntrySource.BankStatement ? "银行账" : "企业账";
            sheet.Cells[row, 3].Value = DirectionName(decision.PrimaryEntry.Direction);
            sheet.Cells[row, 4].Value = decision.PrimaryEntry.Amount;
            sheet.Cells[row, 5].Value = decision.PrimaryEntry.SourceRow;
            sheet.Cells[row, 6].Value = decision.PrimaryEntry.TransactionDate;
            sheet.Cells[row, 7].Value = decision.PrimaryEntry.ReferenceNumber;
            sheet.Cells[row, 8].Value = Describe(decision.PrimaryEntry);
            sheet.Cells[row, 9].Value = decision.MatchedEntry?.SourceRow;
            sheet.Cells[row, 10].Value = decision.MatchedEntry?.ReferenceNumber;
            sheet.Cells[row, 11].Value = decision.RuleId;
            sheet.Cells[row, 12].Value = decision.Candidates.Count;
            sheet.Cells[row, 13].Value = decision.Reason;
            sheet.Cells[row, 14].Value = string.Join(",", decision.Candidates.Select(item => item.SourceRow));
            row++;
        }

        var lastRow = Math.Max(row - 1, headerRow + 1);
        sheet.Cells[headerRow, 1, lastRow, 14].AutoFilter = true;
        sheet.Cells[headerRow + 1, 4, lastRow, 4].Style.Numberformat.Format = CurrencyFormat;
        sheet.Cells[headerRow + 1, 6, lastRow, 6].Style.Numberformat.Format = "yyyy-mm-dd";
        sheet.Cells[headerRow + 1, 8, lastRow, 8].Style.WrapText = true;
        sheet.Cells[headerRow + 1, 13, lastRow, 13].Style.WrapText = true;
        sheet.View.FreezePanes(headerRow + 1, 1);
        var widths = new[] { 12d, 10d, 16d, 16d, 9d, 12d, 14d, 42d, 11d, 14d, 24d, 9d, 42d, 18d };
        for (var index = 0; index < widths.Length; index++)
            sheet.Column(index + 1).Width = widths[index];
    }

    private static void StyleHeader(ExcelRange range)
    {
        range.Style.Fill.SetBackground(HeaderColor);
        range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        range.Style.Font.Bold = true;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        range.Style.WrapText = true;
    }

    private static void AddSourceComment(ExcelRange cell, ReconciliationEntry entry) =>
        cell.AddComment(
            $"来源：{(entry.Source == ReconciliationEntrySource.BankStatement ? "银行账" : "企业账")}第 {entry.SourceRow} 行；日期：{entry.TransactionDate:yyyy-MM-dd}；对方账号：{entry.CounterpartyAccount}",
            "DaTang");

    private static string Describe(ReconciliationEntry entry) =>
        string.Join("─", new[] { entry.Counterparty, entry.Summary }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string MaskAccount(string account) =>
        account.Length <= 4 ? account : new string('*', account.Length - 4) + account[^4..];

    private static string StatusName(MatchDecisionStatus status) => status switch
    {
        MatchDecisionStatus.Matched => "已匹配",
        MatchDecisionStatus.Unmatched => "未匹配",
        MatchDecisionStatus.Ambiguous => "有歧义",
        MatchDecisionStatus.Excluded => "已排除",
        MatchDecisionStatus.Aggregated => "汇总匹配",
        _ => status.ToString()
    };

    private static string DirectionName(ReconciliationDirection direction) => direction switch
    {
        ReconciliationDirection.BankReceived => "银收企未收",
        ReconciliationDirection.BankPaid => "银付企未付",
        ReconciliationDirection.EnterpriseReceived => "企收银未收",
        ReconciliationDirection.EnterprisePaid => "企付银未付",
        _ => direction.ToString()
    };
}
