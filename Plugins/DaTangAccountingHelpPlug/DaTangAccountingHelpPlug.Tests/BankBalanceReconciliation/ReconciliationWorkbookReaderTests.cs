using System.Security.Cryptography;
using System.Text;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reading;
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
    public async Task 方向模式二把银行借方解释为收款()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var enterprisePath = Path.Combine(directory, "enterprise.xlsx");
            var bankPath = Path.Combine(directory, "bank.xlsx");
            CreateEnterpriseFile(enterprisePath);
            CreateBankFile(bankPath, directionMode: 2);
            var request = ReconciliationTestData.Request(
                enterprisePath: enterprisePath,
                bankPath: bankPath);
            request.Profile.DirectionMode = 2;

            var input = await new ReconciliationWorkbookReader(new EntryNormalizer()).ReadAsync(request);

            Assert.Equal(
                DaTangAccountingHelpPlug.Models.BankBalanceReconciliation.ReconciliationDirection.BankReceived,
                Assert.Single(input.BankEntries).Direction);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CreateEnterpriseFile(string path)
    {
        var rows = new[]
        {
            new[] { "日期", "凭证", "摘要", "借方", "贷方", "标记", "余额", "方向" },
            new[] { "2026-07-30", "记-1", "测试客户", "100", "0", "", "1000", "借" }
        };
        SaveRows(path, rows);
    }

    private static void CreateBankFile(string path, int directionMode = 1)
    {
        var rows = new[]
        {
            new[] { "日期", "户名", "摘要", "借方", "贷方", "标记", "余额" },
            directionMode == 2
                ? new[] { "2026-07-30", "测试客户", "收款", "100", "0", "", "1000" }
                : new[] { "2026-07-30", "测试客户", "收款", "0", "100", "", "1000" }
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
