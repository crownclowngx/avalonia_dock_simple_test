using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reporting;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using OfficeOpenXml;
using Xunit;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

public sealed class ReconciliationReportWriterTests
{
    [Fact]
    public async Task 输出包含四张工作表关键公式和审计字段()
    {
        var directory = Path.Combine(Path.GetTempPath(), "datang-reconciliation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "result.xlsx");
        try
        {
            var bankEntry = ReconciliationTestData.Entry(
                "B1", ReconciliationDirection.BankReceived, 100m, "银行未达", 8);
            var enterpriseEntry = ReconciliationTestData.Entry(
                "E1", ReconciliationDirection.EnterprisePaid, 50m, "企业未达", 9);
            var request = ReconciliationTestData.Request(outputPath: outputPath);
            var result = new ReconciliationResult
            {
                Request = request,
                Input = new ReconciliationInputData
                {
                    EnterpriseEntries = [enterpriseEntry],
                    BankEntries = [bankEntry],
                    EnterpriseBalance = 1000m,
                    BankBalance = 1150m
                },
                Decisions =
                [
                    new MatchDecision
                    {
                        Status = MatchDecisionStatus.Unmatched,
                        PrimaryEntry = bankEntry,
                        RuleId = "no-candidate",
                        Reason = "测试银行未达"
                    },
                    new MatchDecision
                    {
                        Status = MatchDecisionStatus.Ambiguous,
                        PrimaryEntry = enterpriseEntry,
                        Candidates = [enterpriseEntry],
                        RuleId = "test-review",
                        Reason = "测试待复核"
                    }
                ]
            };

            await new ReconciliationReportWriter().WriteAsync(result);

            Assert.True(File.Exists(outputPath));
            ExcelPackage.License.SetNonCommercialPersonal("DaTangAccountingHelpPlug.Tests");
            using var package = new ExcelPackage(new FileInfo(outputPath));
            Assert.Equal(
                ["余额调节表", "收款明细", "付款明细", "匹配审计"],
                package.Workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
            var balance = package.Workbook.Worksheets["余额调节表"];
            Assert.Contains("SUM(", balance.Cells[balance.Dimension.End.Row - 3, 4].Formula);
            Assert.Contains("ROUND(", balance.Cells[balance.Dimension.End.Row, 8].Formula);
            var audit = package.Workbook.Worksheets["匹配审计"];
            Assert.Equal("来源行", audit.Cells[8, 5].Text);
            Assert.Equal("候选数", audit.Cells[8, 12].Text);
            Assert.Equal("FAIL", audit.Cells[6, 2].Text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 已存在输出文件仅在新工作簿完成后被替换()
    {
        var directory = Path.Combine(Path.GetTempPath(), "datang-reconciliation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "replace.xlsx");
        await File.WriteAllTextAsync(outputPath, "old-content");
        try
        {
            var request = ReconciliationTestData.Request(outputPath: outputPath);
            var result = new ReconciliationResult
            {
                Request = request,
                Input = new ReconciliationInputData
                {
                    EnterpriseEntries = [],
                    BankEntries = [],
                    EnterpriseBalance = 0m,
                    BankBalance = 0m
                }
            };

            await new ReconciliationReportWriter().WriteAsync(result);

            Assert.NotEqual("old-content", await File.ReadAllTextAsync(outputPath));
            Assert.Empty(Directory.GetFiles(directory, ".*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
