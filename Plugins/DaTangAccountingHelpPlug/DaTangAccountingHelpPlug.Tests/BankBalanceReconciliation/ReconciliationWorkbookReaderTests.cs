using System.Security.Cryptography;
using System.Text;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reading;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using OfficeOpenXml;
using Xunit;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

public sealed class ReconciliationWorkbookReaderTests
{
    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    [InlineData(".csv")]
    public async Task 现代格式只读解析且不改变输入文件(string extension)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, $"enterprise{extension}");
            var bankPath = Path.Combine(directory, $"bank{extension}");
            CreateEnterpriseFile(enterprisePath);
            CreateBankFile(bankPath);
            var enterpriseHash = SHA256.HashData(await File.ReadAllBytesAsync(enterprisePath));
            var bankHash = SHA256.HashData(await File.ReadAllBytesAsync(bankPath));
            var reader = new ReconciliationWorkbookReader(new EntryNormalizer());

            var input = await reader.ReadAsync(ReconciliationTestData.Request(
                enterprisePath: enterprisePath,
                bankPath: bankPath));

            Assert.Single(input.EnterpriseEntries);
            Assert.Single(input.BankEntries);
            Assert.Equal(1000m, input.EnterpriseBalance);
            Assert.Equal(1000m, input.BankBalance);
            Assert.Equal(enterpriseHash, SHA256.HashData(await File.ReadAllBytesAsync(enterprisePath)));
            Assert.Equal(bankHash, SHA256.HashData(await File.ReadAllBytesAsync(bankPath)));

            // 独占打开成功可证明 Reader 已释放工作簿句柄。
            using var enterpriseHandle = File.Open(enterprisePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var bankHandle = File.Open(bankPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 旧式Xls返回可操作的格式提示()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xls");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            await File.WriteAllTextAsync(enterprisePath, "legacy");
            CreateBankFile(bankPath);
            var reader = new ReconciliationWorkbookReader(new EntryNormalizer());

            var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
                reader.ReadAsync(ReconciliationTestData.Request(
                    enterprisePath: enterprisePath,
                    bankPath: bankPath)));

            Assert.Contains("另存为 .xlsx", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 方向模式二按源行降序读取并从首条有效流水取余额()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xlsx");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            CreateEnterpriseFile(enterprisePath);
            SaveRows(bankPath,
            [
                ["查询条件", "", "", "", "", "", ""],
                ["日期", "户名", "摘要", "借方", "贷方", "标记", "余额"],
                ["", "", "", "", "", "", ""],
                ["2026-07-31", "最新客户", "收款", "0", "100", "", "286915"],
                ["2026-07-30", "较早客户", "付款", "50", "0", "", "286815"]
            ]);
            var request = ReconciliationTestData.Request(
                enterprisePath: enterprisePath,
                bankPath: bankPath);
            request.Profile.DirectionMode = 2;
            request.Profile.StartRow = 2;

            var input = await new ReconciliationWorkbookReader(new EntryNormalizer()).ReadAsync(request);

            Assert.Equal(["B-5", "B-4"], input.BankEntries.Select(entry => entry.EntryId));
            Assert.Equal(ReconciliationDirection.BankPaid, input.BankEntries[0].Direction);
            Assert.Equal(ReconciliationDirection.BankReceived, input.BankEntries[1].Direction);
            Assert.Equal(286915m, input.BankBalance);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("100", "0", ReconciliationDirection.EnterpriseReceived)]
    [InlineData("-100", "0", ReconciliationDirection.EnterprisePaid)]
    [InlineData("0", "100", ReconciliationDirection.EnterprisePaid)]
    [InlineData("0", "-100", ReconciliationDirection.EnterpriseReceived)]
    public async Task 企业账按借贷列和金额符号确定真实方向(
        string debit,
        string credit,
        ReconciliationDirection expectedDirection)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xlsx");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            CreateEnterpriseFile(enterprisePath, debit, credit);
            CreateBankFile(bankPath);

            var input = await new ReconciliationWorkbookReader(new EntryNormalizer()).ReadAsync(
                ReconciliationTestData.Request(enterprisePath: enterprisePath, bankPath: bankPath));

            var entry = Assert.Single(input.EnterpriseEntries);
            Assert.Equal(expectedDirection, entry.Direction);
            Assert.Equal(100m, entry.Amount);
            Assert.Equal(decimal.Parse(debit), entry.Debit);
            Assert.Equal(decimal.Parse(credit), entry.Credit);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(1, "0", "100", ReconciliationDirection.BankReceived)]
    [InlineData(1, "0", "-100", ReconciliationDirection.BankPaid)]
    [InlineData(1, "100", "0", ReconciliationDirection.BankPaid)]
    [InlineData(1, "-100", "0", ReconciliationDirection.BankReceived)]
    [InlineData(2, "100", "0", ReconciliationDirection.BankPaid)]
    [InlineData(2, "-100", "0", ReconciliationDirection.BankReceived)]
    [InlineData(2, "0", "100", ReconciliationDirection.BankReceived)]
    [InlineData(2, "0", "-100", ReconciliationDirection.BankPaid)]
    public async Task 银行账两种方向模式都按金额符号反转收付方向(
        int directionMode,
        string debit,
        string credit,
        ReconciliationDirection expectedDirection)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xlsx");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            CreateEnterpriseFile(enterprisePath);
            CreateBankFile(bankPath, directionMode, debit, credit);
            var request = ReconciliationTestData.Request(
                enterprisePath: enterprisePath,
                bankPath: bankPath);
            request.Profile.DirectionMode = directionMode;

            var input = await new ReconciliationWorkbookReader(new EntryNormalizer()).ReadAsync(request);

            var entry = Assert.Single(input.BankEntries);
            Assert.Equal(expectedDirection, entry.Direction);
            Assert.Equal(100m, entry.Amount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 企业账借贷双方同时非零时报告来源行()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xlsx");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            CreateEnterpriseFile(enterprisePath, "100", "50");
            CreateBankFile(bankPath);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ReconciliationWorkbookReader(new EntryNormalizer()).ReadAsync(
                    ReconciliationTestData.Request(enterprisePath: enterprisePath, bankPath: bankPath)));

            Assert.Contains("企业账第 2 行", exception.Message);
            Assert.Contains("借方和贷方同时存在非零金额", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 银行账借贷双方同时非零时报告来源行()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xlsx");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            CreateEnterpriseFile(enterprisePath);
            CreateBankFile(bankPath, debit: "100", credit: "50");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ReconciliationWorkbookReader(new EntryNormalizer()).ReadAsync(
                    ReconciliationTestData.Request(enterprisePath: enterprisePath, bankPath: bankPath)));

            Assert.Contains("银行账第 2 行", exception.Message);
            Assert.Contains("借方和贷方同时存在非零金额", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 汇总行在双边金额校验前过滤()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xlsx");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            CreateEnterpriseFile(enterprisePath, "100", "50", "本日合计");
            CreateBankFile(bankPath);

            var input = await new ReconciliationWorkbookReader(new EntryNormalizer()).ReadAsync(
                ReconciliationTestData.Request(enterprisePath: enterprisePath, bankPath: bankPath));

            Assert.Empty(input.EnterpriseEntries);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CreateEnterpriseFile(
        string path,
        string debit = "100",
        string credit = "0",
        string summary = "测试客户")
    {
        var rows = new[]
        {
            new[] { "日期", "凭证", "摘要", "借方", "贷方", "标记", "余额", "方向" },
            new[] { "2026-07-30", "记-1", summary, debit, credit, "", "1000", "借" }
        };
        SaveRows(path, rows);
    }

    private static void CreateBankFile(
        string path,
        int directionMode = 1,
        string? debit = null,
        string? credit = null)
    {
        debit ??= "0";
        credit ??= "100";
        var rows = new[]
        {
            new[] { "日期", "户名", "摘要", "借方", "贷方", "标记", "余额" },
            new[] { "2026-07-30", "测试客户", "收款", debit, credit, "", "1000" }
        };
        SaveRows(path, rows);
    }

    private static void SaveRows(string path, IReadOnlyList<string[]> rows)
    {
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllLines(path, rows.Select(row => string.Join(',', row)), new UTF8Encoding(true));
            return;
        }

        ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug.Tests");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        for (var row = 0; row < rows.Count; row++)
        for (var column = 0; column < rows[row].Length; column++)
            sheet.Cells[row + 1, column + 1].Value = rows[row][column];
        package.SaveAs(new FileInfo(path));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "datang-reconciliation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
