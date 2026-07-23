namespace DaTangAccountingHelpPlug.Models;

public class InvoicePaymentSummaryItem
{
     /// <summary>
    /// 发票类型 (A列)
    /// </summary>
    public string? InvoiceType { get; set; }
    
    /// <summary>
    /// 供应商名称 (B列)
    /// </summary>
    public string? SupplierName { get; set; }
    
    /// <summary>
    /// 供应商地点 (C列)
    /// </summary>
    public string? SupplierLocation { get; set; }
    
    /// <summary>
    /// 发票日期 (D列)
    /// </summary>
    public DateTime? InvoiceDate { get; set; }
    
    /// <summary>
    /// 发票编号 (E列)
    /// </summary>
    public string? InvoiceNumber { get; set; }
    
    /// <summary>
    /// 部门 (F列)
    /// </summary>
    public string? Department { get; set; }
    
    /// <summary>
    /// 负债账户 (G列)
    /// </summary>
    public string? LiabilityAccount { get; set; }
    
    /// <summary>
    /// 发票金额 (H列)
    /// </summary>
    public decimal? InvoiceAmount { get; set; }
    
    /// <summary>
    /// 计算时付款金额（付+结） (I列)
    /// </summary>
    public decimal? CalculatedPaymentAmount { get; set; }
    
    /// <summary>
    /// 计算时余额(发票金额-付款金额) (J列)
    /// </summary>
    public decimal? CalculatedBalance { get; set; }
    
    /// <summary>
    /// 到期日 (K列)
    /// </summary>
    public DateTime? DueDate { get; set; }
    
    /// <summary>
    /// 备注 (L列)
    /// </summary>
    public string? Remarks { get; set; }
    
    /// <summary>
    /// 类别 (M列)
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// 付款金额 (N列)
    /// </summary>
    public decimal? PaymentAmount { get; set; }
    
    /// <summary>
    /// 付款日期 (O列)
    /// </summary>
    public DateTime? PaymentDate { get; set; }
    
    /// <summary>
    /// 结票金额 (P列)
    /// </summary>
    public decimal? SettlementAmount { get; set; }
    
    /// <summary>
    /// 开票日期 (Q列)
    /// </summary>
    public DateTime? SettlementDate { get; set; }
    
    /// <summary>
    /// 发票信息付款金额 (R列)
    /// </summary>
    public decimal? InvoiceInfoPaymentAmount { get; set; }
    
    /// <summary>
    /// 发票信息余额 (S列)
    /// </summary>
    public decimal? InvoiceInfoBalance { get; set; }
}