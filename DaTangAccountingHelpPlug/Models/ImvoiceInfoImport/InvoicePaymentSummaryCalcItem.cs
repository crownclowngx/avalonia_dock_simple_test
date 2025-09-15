namespace DaTangAccountingHelpPlug.Models;

public class InvoicePaymentSummaryCalcItem
{
    /// <summary>
    /// 计算时付款金额（付+结） (I列)
    /// </summary>
    public decimal? CalculatedPaymentAmount { get; set; }

    /// <summary>
    /// 计算时余额(发票金额-付款金额) (J列)
    /// </summary>
    public decimal? CalculatedBalance { get; set; }

    /// <summary>
    /// 备注 (L列)
    /// </summary>
    public string? Remarks { get; set; }


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
}