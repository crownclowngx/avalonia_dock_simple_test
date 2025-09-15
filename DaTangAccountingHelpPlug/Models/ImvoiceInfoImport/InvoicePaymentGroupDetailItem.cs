namespace DaTangAccountingHelpPlug.Models;

public class InvoicePaymentGroupDetailItem
{
    /// <summary>
    /// 发票编号
    /// </summary>
    public string InvoiceNumber { get; set; }
    
    /// <summary>
    /// 结款的一个发票的分组
    /// </summary>
    public List<InvoicePaymentDetailItem> SettlementInvoicePaymentDetailItems { get; set; }
    
    /// <summary>
    /// 付款的一个发票的分组
    /// </summary>
    public List<InvoicePaymentDetailItem> PaymentInvoicePaymentDetailItems { get; set; }
}