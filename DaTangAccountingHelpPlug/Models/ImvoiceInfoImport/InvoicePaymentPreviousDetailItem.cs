namespace DaTangAccountingHelpPlug.Models;

public class InvoicePaymentPreviousDetailItem
{
    /// <summary>
    /// 发票编号
    /// </summary>
    public string InvoiceNumber { get; set; }
    
    /// <summary>
    /// 备注
    /// </summary>
    public string ReMark { get; set; }
    
    /// <summary>
    /// 付 付款日期
    /// </summary>
    public DateTime? PaymentDate { get; set; }
    /// <summary>
    /// 付 付款金额
    /// </summary>
    public decimal? PaymentAmount { get; set; }
    
    /// <summary>
    /// 结 结款金额
    /// </summary>
    public decimal? SettlementAmount { get; set; }
    
    /// <summary>
    /// 结 结款日期
    /// </summary>
    public DateTime? SettlementDate { get; set; }
    
    /// <summary>
    /// 供应商类型
    /// </summary>
    public string? SupplierType{get;set;}
    
    /// <summary>
    /// 供应商名称
    /// </summary>
    public string? SupplierName{get;set;}
}