using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reading;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reporting;
using DaTangAccountingHelpPlug.ViewModels;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

namespace DaTangAccountingHelpPlug.Plugin;

/// <summary>
/// DaTang 会计辅助插件接入宿主依赖注入容器的模块入口。
/// </summary>
/// <remarks>
/// 仅注册配置、业务服务和 Document Scope 内对象；插件没有常驻后台任务，
/// 因此不增加没有实际职责的 <see cref="IPluginLifecycle"/>。
/// </remarks>
public sealed class DaTangAccountingHelpPluginModule : IPluginModule
{
    public string PluginId => "DaTangAccountingHelpPlug";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        // 发票 Document 由宿主的独立 Scope 托管，避免不同窗口共享路径、日志或计算结果。
        services.AddScoped<InvoiceInfoImportViewModel>();

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
        services.AddScoped<BankBalanceReconciliationViewModel>();
    }
}
