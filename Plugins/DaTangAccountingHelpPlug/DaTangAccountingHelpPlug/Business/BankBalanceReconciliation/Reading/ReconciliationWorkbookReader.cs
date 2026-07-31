using System.Globalization;
using System.Text;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using OfficeOpenXml;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reading;

/// <summary>通过 EPPlus 只读解析企业账和银行账。</summary>
public sealed class ReconciliationWorkbookReader : IReconciliationWorkbookReader
{
    private static readonly string[] IgnoredSummaryKeywords =
        ["本日合计", "本月合计", "本年合计", "本年累计", "期初余额", "年初余额", "操作人："];

    private readonly EntryNormalizer _normalizer;

    public ReconciliationWorkbookReader(EntryNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public Task<ReconciliationInputData> ReadAsync(
        ReconciliationRequest request,
        IProgress<ReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(request, progress, cancellationToken), cancellationToken);

    private ReconciliationInputData Read(
        ReconciliationRequest request,
        IProgress<ReconciliationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateInputPath(request.EnterpriseLedgerPath, "企业明细账");
        ValidateInputPath(request.BankStatementPath, "银行账");
        if (Path.GetFullPath(request.EnterpriseLedgerPath)
            .Equals(Path.GetFullPath(request.BankStatementPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("企业明细账和银行账不能选择同一个文件。");

        ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug");
        progress?.Report(new ReconciliationProgress("读取", "正在读取企业明细账", 10));
        using var enterprisePackage = OpenPackage(request.EnterpriseLedgerPath);
        var enterpriseSheet = FirstWorksheet(enterprisePackage, "企业明细账");
        VerifyContains(enterpriseSheet, request.EnterpriseLayout.VerifyUnitCell, request.Profile.UnitName, "企业账单位");
        VerifyContains(enterpriseSheet, request.EnterpriseLayout.VerifyAccountCell, request.Profile.AccountNumber, "企业账账号");
        var enterpriseBalance = ReadEnterpriseBalance(enterpriseSheet, request.EnterpriseLayout);
        var enterpriseEntries = ReadEnterpriseEntries(
            enterpriseSheet,
            request,
            cancellationToken);

        progress?.Report(new ReconciliationProgress("读取", "正在读取银行账", 35));
        using var bankPackage = OpenPackage(request.BankStatementPath);
        var bankSheet = FirstWorksheet(bankPackage, "银行账");
        VerifyContains(bankSheet, request.Profile.VerifyUnitCell, request.Profile.UnitName, "银行账单位");
        VerifyContains(bankSheet, request.Profile.VerifyAccountCell, request.Profile.AccountNumber, "银行账号");
        var bankBalance = ReadBankBalance(bankSheet, request.Profile);
        var enrichmentNames = ReadReceiptEnrichment(request.ReceiptEnrichmentPath, cancellationToken);
        var bankEntries = ReadBankEntries(bankSheet, request, enrichmentNames, cancellationToken);

        var warnings = enterpriseEntries.Concat(bankEntries)
            .Where(entry => entry.TransactionDate is not null && entry.TransactionDate.Value.Date > request.AsOfDate.Date)
            .Select(entry => $"{SourceName(entry.Source)}第 {entry.SourceRow} 行日期晚于截止日期。")
            .ToArray();

        progress?.Report(new ReconciliationProgress(
            "读取",
            $"读取完成：企业账 {enterpriseEntries.Count} 条，银行账 {bankEntries.Count} 条",
            50));
        return new ReconciliationInputData
        {
            EnterpriseEntries = enterpriseEntries,
            BankEntries = bankEntries,
            EnterpriseBalance = enterpriseBalance,
            BankBalance = bankBalance,
            Warnings = warnings
        };
    }

    private List<ReconciliationEntry> ReadEnterpriseEntries(
        ExcelWorksheet sheet,
        ReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        var layout = request.EnterpriseLayout;
        var endRow = sheet.Dimension?.End.Row
                     ?? throw new InvalidDataException("企业明细账没有可读取的数据。");
        var result = new List<ReconciliationEntry>();
        for (var row = layout.StartRow; row <= endRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = sheet.Cells[row, layout.SummaryColumn].Text.Trim();
            var debit = ReadMoney(sheet.Cells[row, layout.DebitColumn]);
            var credit = ReadMoney(sheet.Cells[row, layout.CreditColumn]);
            if (ShouldIgnore(summary, debit, credit))
                continue;

            var direction = debit != 0m
                ? ReconciliationDirection.EnterpriseReceived
                : ReconciliationDirection.EnterprisePaid;
            result.Add(new ReconciliationEntry
            {
                EntryId = $"E-{row}",
                Source = ReconciliationEntrySource.EnterpriseLedger,
                Direction = direction,
                SourceRow = row,
                TransactionDate = ReadDate(sheet.Cells[row, layout.DateColumn]),
                ReferenceNumber = sheet.Cells[row, layout.ReferenceColumn].Text.Trim(),
                Summary = summary,
                Counterparty = summary,
                NormalizedCounterparty = _normalizer.NormalizeText(summary),
                Debit = debit,
                Credit = credit,
                Amount = ReconciliationResult.Money(Math.Abs(debit != 0m ? debit : credit)),
                ExistingMarker = sheet.Cells[row, layout.MarkerColumn].Text.Trim()
            });
        }

        return result;
    }

    private List<ReconciliationEntry> ReadBankEntries(
        ExcelWorksheet sheet,
        ReconciliationRequest request,
        IReadOnlyDictionary<string, string> enrichmentNames,
        CancellationToken cancellationToken)
    {
        var profile = request.Profile;
        var endRow = sheet.Dimension?.End.Row
                     ?? throw new InvalidDataException("银行账没有可读取的数据。");
        if (profile.BalanceFromLastRow)
            endRow -= Math.Max(profile.BalanceTrailingRowOffset, 0);

        var result = new List<ReconciliationEntry>();
        for (var row = profile.StartRow; row <= endRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = sheet.Cells[row, profile.SummaryColumn].Text.Trim();
            var debit = ReadMoney(sheet.Cells[row, profile.DebitColumn]);
            var credit = ReadMoney(sheet.Cells[row, profile.CreditColumn]);
            if (ShouldIgnore(summary, debit, credit))
                continue;

            var counterparty = sheet.Cells[row, profile.CounterpartyColumn].Text.Trim();
            if (profile.ReceiptEnrichmentColumn > 0 && enrichmentNames.Count > 0)
            {
                var identifier = ExtractReceiptIdentifier(
                    sheet.Cells[row, profile.ReceiptEnrichmentColumn].Text);
                if (!string.IsNullOrWhiteSpace(identifier) &&
                    enrichmentNames.TryGetValue(identifier, out var enrichedName))
                    counterparty = enrichedName;
            }
            var receivedAmount = profile.DirectionMode == 2 ? debit : credit;
            var direction = receivedAmount != 0m
                ? ReconciliationDirection.BankReceived
                : ReconciliationDirection.BankPaid;
            result.Add(new ReconciliationEntry
            {
                EntryId = $"B-{row}",
                Source = ReconciliationEntrySource.BankStatement,
                Direction = direction,
                SourceRow = row,
                TransactionDate = ReadDate(sheet.Cells[row, profile.DateColumn]),
                ReferenceNumber = row.ToString(CultureInfo.InvariantCulture),
                Counterparty = counterparty,
                NormalizedCounterparty = _normalizer.NormalizeText(counterparty),
                Summary = summary,
                CounterpartyAccount = profile.CounterpartyAccountColumn > 0
                    ? sheet.Cells[row, profile.CounterpartyAccountColumn].Text.Trim()
                    : string.Empty,
                Debit = debit,
                Credit = credit,
                Amount = ReconciliationResult.Money(Math.Abs(receivedAmount != 0m
                    ? receivedAmount
                    : profile.DirectionMode == 2 ? credit : debit)),
                ExistingMarker = sheet.Cells[row, profile.MarkerColumn].Text.Trim()
            });
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadReceiptEnrichment(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ValidateInputPath(path, "到款表");
        using var package = OpenPackage(path);
        var sheet = FirstWorksheet(package, "到款表");
        var endRow = sheet.Dimension?.End.Row ?? 0;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var row = 2; row <= endRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identifier = sheet.Cells[row, 12].Text.Trim();
            var counterparty = sheet.Cells[row, 10].Text.Trim();
            if (identifier.Length > 0 && counterparty.Length > 0)
                result.TryAdd(identifier, counterparty);
        }

        return result;
    }

    private static string ExtractReceiptIdentifier(string text)
    {
        // 到款导出表使用商户订单号或平台交易流水号关联银行摘要。
        // 标识符按分隔符读取，避免复刻旧宏依赖固定字符位置的脆弱截取方式。
        foreach (var marker in new[] { "商户订单号:", "商户订单号：", "平台交易流水号:", "平台交易流水号：" })
        {
            var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;
            var start = markerIndex + marker.Length;
            var value = text[start..].TrimStart();
            var end = value.IndexOfAny([' ', '\t', '\r', '\n', ',', '，', ';', '；']);
            return (end < 0 ? value : value[..end]).Trim();
        }

        return string.Empty;
    }

    private static decimal ReadEnterpriseBalance(ExcelWorksheet sheet, EnterpriseLedgerLayout layout)
    {
        var endRow = sheet.Dimension?.End.Row
                     ?? throw new InvalidDataException("企业明细账没有余额行。");
        var row = FindBalanceRow(
            sheet,
            endRow - Math.Max(layout.BalanceTrailingRowOffset, 0),
            layout.StartRow,
            layout.BalanceColumn);
        if (row < layout.StartRow)
            throw new InvalidDataException("企业明细账余额行早于数据起始行。");
        var balance = ReadMoney(sheet.Cells[row, layout.BalanceColumn]);
        if (sheet.Cells[row, layout.BalanceDirectionColumn].Text.Trim()
            .Equals("贷", StringComparison.OrdinalIgnoreCase))
            balance = -balance;
        return ReconciliationResult.Money(balance);
    }

    private static decimal ReadBankBalance(ExcelWorksheet sheet, BankReconciliationProfile profile)
    {
        var row = profile.BalanceFromLastRow
            ? FindBalanceRow(
                  sheet,
                  (sheet.Dimension?.End.Row ?? profile.StartRow)
                  - Math.Max(profile.BalanceTrailingRowOffset, 0),
                  profile.StartRow,
                  profile.BalanceColumn)
            : profile.StartRow;
        if (row <= 0)
            throw new InvalidDataException("银行账余额行无效。");
        return ReconciliationResult.Money(ReadMoney(sheet.Cells[row, profile.BalanceColumn]));
    }

    private static int FindBalanceRow(
        ExcelWorksheet sheet,
        int endRow,
        int minimumRow,
        int valueColumn)
    {
        // CSV 文本通常以换行结束，EPPlus 可能将末尾空行计入 Dimension。
        // 余额行必须按目标列回溯，不能把该空行误认为真实的零余额。
        for (var row = endRow; row >= minimumRow; row--)
        {
            if (sheet.Cells[row, valueColumn].Value is not null ||
                !string.IsNullOrWhiteSpace(sheet.Cells[row, valueColumn].Text))
                return row;
        }

        throw new InvalidDataException("没有找到可读取的余额单元格。");
    }

    private static ExcelPackage OpenPackage(string filePath)
    {
        if (!Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return new ExcelPackage(new FileInfo(filePath));

        var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        var text = ReadCsvText(filePath);
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var format = new ExcelTextFormat
        {
            Delimiter = firstLine.Count(character => character == '\t') > firstLine.Count(character => character == ',')
                ? '\t'
                : ','
        };
        sheet.Cells["A1"].LoadFromText(text, format);
        return package;
    }

    private static string ReadCsvText(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    private static ExcelWorksheet FirstWorksheet(ExcelPackage package, string label) =>
        package.Workbook.Worksheets.FirstOrDefault()
        ?? throw new InvalidDataException($"{label}没有工作表。");

    private static void VerifyContains(
        ExcelWorksheet sheet,
        string address,
        string expected,
        string label)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(expected))
            return;
        var actual = sheet.Cells[address].Text.Trim();
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label}校验失败：{address} 未包含预期内容。") ;
    }

    private static bool ShouldIgnore(string summary, decimal debit, decimal credit) =>
        debit == 0m && credit == 0m ||
        IgnoredSummaryKeywords.Any(keyword => summary.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static decimal ReadMoney(ExcelRange cell)
    {
        if (cell.Value is decimal decimalValue)
            return decimalValue;
        if (cell.Value is double doubleValue)
            return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
        if (cell.Value is int intValue)
            return intValue;

        var text = cell.Text.Trim()
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("¥", string.Empty, StringComparison.Ordinal)
            .Replace("￥", string.Empty, StringComparison.Ordinal);
        var isParenthesized = text.StartsWith('(') && text.EndsWith(')');
        if (isParenthesized)
            text = text[1..^1];
        if (!decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) &&
            !decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out result))
            return 0m;
        return isParenthesized ? -result : result;
    }

    private static DateTime? ReadDate(ExcelRange cell)
    {
        if (cell.Value is DateTime date)
            return date;
        return DateTime.TryParse(cell.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date)
            ? date
            : null;
    }

    private static void ValidateInputPath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException($"{label}文件不存在。", path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".xls")
            throw new NotSupportedException($"{label}为旧式 .xls，请先另存为 .xlsx。");
        if (extension is not (".xlsx" or ".xlsm" or ".csv"))
            throw new NotSupportedException($"{label}格式不受支持：{extension}");
    }

    private static string SourceName(ReconciliationEntrySource source) =>
        source == ReconciliationEntrySource.EnterpriseLedger ? "企业账" : "银行账";
}
