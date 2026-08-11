using DaTangAccountingHelpPlug.Business;
using DaTangAccountingHelpPlug.Models;

namespace MyAvaloniaManagement.PluginTests;

public sealed class DaTangInvoiceInfoImportBusinessTests
{
    [Fact]
    public void 数据模型默认值维持非空不变量()
    {
        var detail = new InvoicePaymentDetailItem();
        var group = new InvoicePaymentGroupDetailItem();
        var previous = new InvoicePaymentPreviousDetailItem();

        Assert.Equal(string.Empty, detail.InvoiceNumber);
        Assert.Equal(string.Empty, detail.PaymentSummary);
        Assert.Equal(string.Empty, detail.BankAccount);
        Assert.Equal(string.Empty, detail.PaymentMethod);
        Assert.Equal(string.Empty, detail.PaymentNumber);
        Assert.Equal(string.Empty, detail.CertificateNumber);
        Assert.Equal(string.Empty, group.InvoiceNumber);
        Assert.Empty(group.PaymentInvoicePaymentDetailItems);
        Assert.Empty(group.SettlementInvoicePaymentDetailItems);
        Assert.Equal(string.Empty, previous.InvoiceNumber);
        Assert.Equal(string.Empty, previous.ReMark);
    }

    [Fact]
    public void ClearAllData清空全部状态并同步完成()
    {
        var business = CreateBusiness();
        business.InvoiceSummaryItems.Add("INV-001", new InvoiceSummaryItem());
        business.InvoicePaymentGroupDetails.Add("INV-001", new InvoicePaymentGroupDetailItem());
        business.InvoicePaymentPreviousDetails.Add("INV-001", new InvoicePaymentPreviousDetailItem());
        business.SupplierTypeMapping.Add("供应商", "类别");
        business.InvoicePaymentSummaryItems.Add(new InvoicePaymentSummaryItem());
        business.AllNeedShowInvoiceNumbers.Add("INV-001");

        var clearTask = business.ClearAllData();

        Assert.True(clearTask.IsCompletedSuccessfully);
        Assert.Empty(business.InvoiceSummaryItems);
        Assert.Empty(business.InvoicePaymentGroupDetails);
        Assert.Empty(business.InvoicePaymentPreviousDetails);
        Assert.Empty(business.SupplierTypeMapping);
        Assert.Empty(business.InvoicePaymentSummaryItems);
        Assert.Empty(business.AllNeedShowInvoiceNumbers);
    }

    [Fact]
    public async Task 汇总计算在缺失映射时结果确定且不抛异常()
    {
        var logs = new List<string>();
        var business = new InvoiceInfoImportBusiness(logs.Add);
        business.InvoiceSummaryItems.Add("INV-001", new InvoiceSummaryItem
        {
            InvoiceNumber = "INV-001",
            SupplierName = "未映射供应商",
            InvoiceAmount = 100m,
        });
        business.AllNeedShowInvoiceNumbers.UnionWith(["INV-001", "MISSING"]);

        await business.CalculateNewInvoiceSummary();

        var result = Assert.Single(business.InvoicePaymentSummaryItems);
        Assert.Equal("INV-001", result.InvoiceNumber);
        Assert.Equal(string.Empty, result.Category);
        Assert.Equal(100m, result.CalculatedBalance);
        Assert.Contains(logs, message => message.Contains("MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 发票日期筛选包含起止边界并排除范围外记录()
    {
        var business = CreateBusiness();
        var start = new DateTime(2026, 7, 1);
        var end = new DateTime(2026, 7, 31);
        AddInvoice(business, "START", start);
        AddInvoice(business, "END", end);
        AddInvoice(business, "BEFORE", start.AddDays(-1));
        AddInvoice(business, "AFTER", end.AddDays(1));

        await business.CreateAllNeedShowInvoiceNumber(start, end);

        Assert.Equal(["END", "START"], business.AllNeedShowInvoiceNumbers.OrderBy(number => number));
    }

    [Fact]
    public async Task CancellationStopsCalculationAndIsNotLoggedAsBusinessFailure()
    {
        var logs = new List<string>();
        var business = new InvoiceInfoImportBusiness(logs.Add);
        business.AllNeedShowInvoiceNumbers.Add("INV-001");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await business.CalculateNewInvoiceSummary(cancellation.Token));

        Assert.Empty(business.InvoicePaymentSummaryItems);
        Assert.Empty(logs);
    }

    private static InvoiceInfoImportBusiness CreateBusiness() => new(_ => { });

    private static void AddInvoice(InvoiceInfoImportBusiness business, string invoiceNumber, DateTime invoiceDate)
    {
        business.InvoiceSummaryItems.Add(invoiceNumber, new InvoiceSummaryItem
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = invoiceDate,
        });
    }
}
