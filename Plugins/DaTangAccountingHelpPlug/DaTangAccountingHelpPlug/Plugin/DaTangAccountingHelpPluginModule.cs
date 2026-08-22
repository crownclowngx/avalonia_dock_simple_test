using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reading;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reporting;
using DaTangAccountingHelpPlug.Business;
using DaTangAccountingHelpPlug.ViewModels;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using Microsoft.Extensions.DependencyInjection;
using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.Views;
using DaTangAccountingHelpPlug.Views.BankBalanceReconciliation;
using MyAvaloniaManagement.PluginSdk.UI;

namespace DaTangAccountingHelpPlug.Plugin;

/// <summary>
/// DaTang 会计辅助插件接入当前 V3 私有 Provider 的唯一组合入口。
/// </summary>
/// <remarks>
/// 模块只负责组合：业务服务进入当前插件的独立集合，Document、View 与 Descriptor 通过一次声明
/// 同时冻结。插件没有常驻后台任务，因此不增加没有实际职责的生命周期实现。
/// </remarks>
public sealed class DaTangAccountingHelpPluginModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var services = registration.Services;

        // 三个窄业务端口共享一个无状态适配器；真正的窗口能力在本方法返回并通过 G4 校验后
        // 由 Host 最终追加，插件既不寻找主窗口，也不能影子覆盖该端口。
        services.AddSingleton<DaTangWindowInteractionService>();
        services.AddSingleton<IInvoiceFileDialogService>(provider =>
            provider.GetRequiredService<DaTangWindowInteractionService>());
        services.AddSingleton<IReconciliationFileDialogService>(provider =>
            provider.GetRequiredService<DaTangWindowInteractionService>());
        services.AddSingleton<IPluginClipboardService>(provider =>
            provider.GetRequiredService<DaTangWindowInteractionService>());

        services.AddScoped<IInvoiceInfoImportBusiness, InvoiceInfoImportBusiness>();

        // 内置配置是不可变的版本化资源，跨 Document 复用加载器不会共享运行状态。
        services.AddSingleton<ReconciliationProfileLoader>();

        // 读取、规范化、匹配和写出服务无界面状态，按需创建可以保持依赖关系清晰。
        services.AddTransient<EntryNormalizer>();
        services.AddTransient<AggregationRuleMatcher>();
        services.AddTransient<ReferenceAggregationMatcher>();
        services.AddTransient<IReconciliationWorkbookReader, ReconciliationWorkbookReader>();
        services.AddTransient<IReconciliationEngine, ReconciliationEngine>();
        services.AddTransient<IReconciliationReportWriter, ReconciliationReportWriter>();

        // 以下对象持有单个标签页的路径、选项、日志和取消令牌，必须由 Document Scope 隔离。
        services.AddScoped<BankBalanceReconciliationService>();
        services.AddScoped<ReconciliationSourceViewModel>();
        services.AddScoped<ReconciliationOptionsViewModel>();
        services.AddScoped<ReconciliationRunViewModel>();

        // 注册 API 只冻结声明；G4 校验通过后由 Host 最终追加两个 scoped 模型。这里不重复注册模型。
        registration.AddDocument<InvoiceInfoImportViewModel, InvoiceInfoImportView>(
            new DocumentDescriptor(
                DaTangContributionIds.InvoiceInfoImportDocument,
                "综合计算发票信息",
                "依照发票表、当月明细和历史付款汇总计算当月综合表",
                "大唐-会计"));
        registration.AddPersistableDocument<BankBalanceReconciliationViewModel, BankBalanceReconciliationView>(
            new DocumentDescriptor(
                DaTangContributionIds.BankBalanceReconciliationDocument,
                "银行余额调节表",
                "只读分析企业账与银行账，生成调节表、收付款明细和匹配审计",
                "大唐-会计"));
    }
}
