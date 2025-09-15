using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTangAccountingHelpPlug.Models;

public class InvoiceSummaryItem : ObservableObject
{
        private string? _invoiceType;
    private string? _supplierName;
    private string? _supplierType;
    private bool? _isExternalUnit;
    private string? _supplierLocation;
    private DateTime? _invoiceDate;
    private string? _invoiceNumber;
    private string? _invoiceSummary;
    private string? _department;
    private string? _contractNumber;
    private string? _contractName;
    private string? _liabilityAccount;
    private decimal? _invoiceAmount;
    private decimal? _paymentAmount;
    private decimal? _writeOffPrepaymentAmount;
    private decimal? _balance;
    private string? _voucherNumber;
    private DateTime? _dueDate;
    private string? _sharedDocumentNumber;

    /// <summary>
    /// 发票类型
    /// </summary>
    public string? InvoiceType
    {
        get => _invoiceType;
        set => SetProperty(ref _invoiceType, value);
    }

    /// <summary>
    /// 供应商名称
    /// </summary>
    public string? SupplierName
    {
        get => _supplierName;
        set => SetProperty(ref _supplierName, value);
    }

    /// <summary>
    /// 供应商类型
    /// </summary>
    public string? SupplierType
    {
        get => _supplierType;
        set => SetProperty(ref _supplierType, value);
    }

    /// <summary>
    /// 是否外部单位
    /// </summary>
    public bool? IsExternalUnit
    {
        get => _isExternalUnit;
        set => SetProperty(ref _isExternalUnit, value);
    }

    /// <summary>
    /// 供应商地点
    /// </summary>
    public string? SupplierLocation
    {
        get => _supplierLocation;
        set => SetProperty(ref _supplierLocation, value);
    }

    /// <summary>
    /// 发票日期
    /// </summary>
    public DateTime? InvoiceDate
    {
        get => _invoiceDate;
        set => SetProperty(ref _invoiceDate, value);
    }

    /// <summary>
    /// 发票编号
    /// </summary>
    public string? InvoiceNumber
    {
        get => _invoiceNumber;
        set => SetProperty(ref _invoiceNumber, value);
    }

    /// <summary>
    /// 发票摘要
    /// </summary>
    public string? InvoiceSummary
    {
        get => _invoiceSummary;
        set => SetProperty(ref _invoiceSummary, value);
    }

    /// <summary>
    /// 部门
    /// </summary>
    public string? Department
    {
        get => _department;
        set => SetProperty(ref _department, value);
    }

    /// <summary>
    /// 合同编号
    /// </summary>
    public string? ContractNumber
    {
        get => _contractNumber;
        set => SetProperty(ref _contractNumber, value);
    }

    /// <summary>
    /// 合同名称
    /// </summary>
    public string? ContractName
    {
        get => _contractName;
        set => SetProperty(ref _contractName, value);
    }

    /// <summary>
    /// 负债账户
    /// </summary>
    public string? LiabilityAccount
    {
        get => _liabilityAccount;
        set => SetProperty(ref _liabilityAccount, value);
    }

    /// <summary>
    /// 发票金额
    /// </summary>
    public decimal? InvoiceAmount
    {
        get => _invoiceAmount;
        set => SetProperty(ref _invoiceAmount, value);
    }

    /// <summary>
    /// 付款金额
    /// </summary>
    public decimal? PaymentAmount
    {
        get => _paymentAmount;
        set => SetProperty(ref _paymentAmount, value);
    }

    /// <summary>
    /// 核销预付款金额
    /// </summary>
    public decimal? WriteOffPrepaymentAmount
    {
        get => _writeOffPrepaymentAmount;
        set => SetProperty(ref _writeOffPrepaymentAmount, value);
    }

    /// <summary>
    /// 余额
    /// </summary>
    public decimal? Balance
    {
        get => _balance;
        set => SetProperty(ref _balance, value);
    }

    /// <summary>
    /// 凭证编号
    /// </summary>
    public string? VoucherNumber
    {
        get => _voucherNumber;
        set => SetProperty(ref _voucherNumber, value);
    }

    /// <summary>
    /// 到期日
    /// </summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    /// <summary>
    /// 共享单据编号
    /// </summary>
    public string? SharedDocumentNumber
    {
        get => _sharedDocumentNumber;
        set => SetProperty(ref _sharedDocumentNumber, value);
    }
}