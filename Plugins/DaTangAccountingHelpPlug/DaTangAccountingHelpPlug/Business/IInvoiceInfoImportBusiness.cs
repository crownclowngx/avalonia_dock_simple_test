namespace DaTangAccountingHelpPlug.Business;

/// <summary>
/// 发票导入 Document 使用的业务编排边界。
/// </summary>
/// <remarks>
/// 接口把 Excel 读取、索引、计算和导出从 ViewModel 中隔离出来，使关闭取消可以在业务
/// 循环内部被确定性测试。所有令牌均为可选末尾参数，以保持旧调用方源码兼容；实现必须
/// 让 <see cref="OperationCanceledException"/> 向上传播，不能把取消记录成普通业务错误。
/// </remarks>
public interface IInvoiceInfoImportBusiness
{
    /// <summary>
    /// 业务阶段产生的日志。订阅者属于当前 Document Scope，Document Dispose 时必须解绑，
    /// 从而避免后台收尾阶段继续引用或更新已经关闭的 ViewModel。
    /// </summary>
    event Action<string>? LogEmitted;

    /// <summary>清空当前 Document 会话中的全部索引和计算结果。</summary>
    Task ClearAllData(CancellationToken cancellationToken = default);

    /// <summary>读取并索引发票总表；实现应在逐行处理时检查取消。</summary>
    Task ReadAndIndexInvoiceSummary(string filePath, CancellationToken cancellationToken = default);

    /// <summary>读取当月付款明细；实现应在逐行处理时检查取消。</summary>
    Task ReadInvoicePaymentDetailCurrentMonthTable(string filePath, CancellationToken cancellationToken = default);

    /// <summary>读取历史付款汇总；实现应在逐行处理时检查取消。</summary>
    Task ReadInvoicePaymentDetailPreviousMonthTable(string filePath, CancellationToken cancellationToken = default);

    /// <summary>按照日期范围生成需要展示的发票号集合。</summary>
    Task CreateAllNeedShowInvoiceNumber(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    /// <summary>根据已建立的索引计算新的付款汇总。</summary>
    Task CalculateNewInvoiceSummary(CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出付款汇总。进入 EPPlus 同步 SaveAs 前必须检查取消；SaveAs 已开始后允许完整写入，
    /// 避免通过强制终止留下损坏或半写文件。
    /// </summary>
    Task SaveInvoicePaymentSummaryToExcel(string filePath, CancellationToken cancellationToken = default);
}
