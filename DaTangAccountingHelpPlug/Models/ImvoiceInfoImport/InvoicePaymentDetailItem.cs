namespace DaTangAccountingHelpPlug.Models;

public class InvoicePaymentDetailItem
{
    /// <summary>
    /// 发票编号
    /// </summary>
    public string InvoiceNumber { get; set; }
    
    /// <summary>
    /// 付款日期
    /// </summary>
    public DateTime PaymentDate { get; set; }
    
    /// <summary>
    /// 付款金额
    /// </summary>
    public decimal PaymentAmount { get; set; }
    
    /// <summary>
    /// 付款摘要
    /// </summary>
    public string PaymentSummary { get; set; }
    
    /// <summary>
    /// 银行账户
    /// </summary>
    public string BankAccount { get; set; }
    
    /// <summary>
    /// 付款方法
    /// </summary>
    public string PaymentMethod { get; set; }
    
    /// <summary>
    /// 付款编号
    /// </summary>
    public string PaymentNumber { get; set; }
    
    /// <summary>
    /// 凭证编号
    /// </summary>
    public string CertificateNumber { get; set; }

    /// <summary>
    /// 源行索引
    /// </summary>
    public int SourceRowIndex { get; set; }
}