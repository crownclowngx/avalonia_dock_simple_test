using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reading;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reporting;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation;

/// <summary>连接工作簿 I/O 与纯匹配引擎的一次性业务编排入口。</summary>
public sealed class BankBalanceReconciliationService
{
    private readonly IReconciliationWorkbookReader _reader;
    private readonly IReconciliationEngine _engine;
    private readonly IReconciliationReportWriter _writer;
    private readonly ReconciliationProfileLoader _profileLoader;

    public BankBalanceReconciliationService(
        IReconciliationWorkbookReader reader,
        IReconciliationEngine engine,
        IReconciliationReportWriter writer,
        ReconciliationProfileLoader profileLoader)
    {
        _reader = reader;
        _engine = engine;
        _writer = writer;
        _profileLoader = profileLoader;
    }

    public async Task<ReconciliationResult> ExecuteAsync(
        ReconciliationRequest request,
        IProgress<ReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ReconciliationProgress("预检", "正在校验输入", 5));
        ValidateRequest(request);
        var input = await _reader.ReadAsync(request, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ReconciliationProgress("匹配", "正在执行逐笔匹配和汇总规则", 55));
        var result = _engine.Reconcile(request, input, cancellationToken);
        progress?.Report(new ReconciliationProgress(
            "匹配",
            $"匹配完成：已匹配 {result.MatchedCount} 条，复核 {result.ReviewIssueCount} 组，歧义 {result.AmbiguousCount} 条",
            70));
        await _writer.WriteAsync(result, progress, cancellationToken);
        return result;
    }

    private void ValidateRequest(ReconciliationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _profileLoader.Validate(request.Configuration);
        if (!request.Configuration.BankProfiles.Any(item =>
                item.Id.Equals(request.Profile.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"当前配置不包含银行账户 {request.Profile.Id}。");
        if (!request.Configuration.EnterpriseLayouts.Any(item =>
                item.Id.Equals(request.EnterpriseLayout.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"当前配置不包含企业账布局 {request.EnterpriseLayout.Id}。");
        if (string.IsNullOrWhiteSpace(request.OutputPath) ||
            !Path.GetExtension(request.OutputPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("输出文件必须使用 .xlsx 格式。");

        var outputPath = Path.GetFullPath(request.OutputPath);
        foreach (var inputPath in new[]
                 {
                     request.EnterpriseLedgerPath,
                     request.BankStatementPath,
                     request.ReceiptEnrichmentPath
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (outputPath.Equals(Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase))
            {
                // 输入文件是会计原始凭据，任何输出路径都不能指向原文件。
                // 这条预检发生在 Reader 打开文件之前，避免错误配置留下任何写入机会。
                throw new InvalidDataException("输出文件不能覆盖企业账、银行账或到款表原文件。");
            }
        }
    }
}
