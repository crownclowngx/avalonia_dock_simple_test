using DaTangAccountingHelpPlug.Business;
using DaTangAccountingHelpPlug.Models;
using OfficeOpenXml;

namespace MyAvaloniaManagement.PluginTests;

public sealed class DaTangDependencyCompatibilityTests
{
    [Fact]
    public async Task EPPlus可保存并回读发票汇总工作簿()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DaTang-Phase3-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "invoice-summary.xlsx");
        Directory.CreateDirectory(root);

        try
        {
            var business = new InvoiceInfoImportBusiness(_ => { });
            business.InvoicePaymentSummaryItems.Add(new InvoicePaymentSummaryItem
            {
                InvoiceType = "专用发票",
                InvoiceNumber = "INV-PHASE3",
                SupplierName = "阶段三供应商",
                InvoiceAmount = 123.45m,
            });

            await business.SaveInvoicePaymentSummaryToExcel(path);

            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets["发票汇总表"];
                Assert.NotNull(sheet);
                Assert.Equal("发票类型", sheet.Cells[1, 1].Text);
                Assert.Equal("INV-PHASE3", sheet.Cells[2, 5].Text);
                Assert.Equal(123.45m, sheet.Cells[2, 8].GetValue<decimal>());
            }

            // 独占打开能证明 EPPlus 和业务入口都已释放工作簿句柄，
            // 避免升级后出现“保存成功但文件无法替换或删除”的隐性回归。
            using var exclusive = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.True(exclusive.Length > 0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
