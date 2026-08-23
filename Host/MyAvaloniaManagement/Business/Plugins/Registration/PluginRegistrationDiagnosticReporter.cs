using System;
using System.Linq;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Discovery;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>把插件局部组合错误投影为默认脱敏的 Host 诊断。</summary>
/// <remarks>
/// HostCompositionException 内部保留贡献类型，便于单元测试和本地调试；本协作者只发布稳定错误码、
/// manifest PluginId、合法 Contribution ID 与入口程序集，不携带异常正文、路径或插件 payload。
/// 它只有一个静态实现且只在组合根调用，因此没有为测试额外引入接口或策略模式。
/// </remarks>
internal static class PluginRegistrationDiagnosticReporter
{
    internal static void Report(
        HostCompositionException exception,
        PluginManifest manifest,
        PluginModuleEntry entry,
        IHostDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var reportable = exception.Diagnostics.Where(item =>
            item.Code == HostDiagnosticCodes.PluginHostServiceRegistrationForbidden ||
            item.Code == HostDiagnosticCodes.PluginContributionServiceRegistrationForbidden ||
            item.Code == HostDiagnosticCodes.DocumentIdOwnerMismatch ||
            item.Code == HostDiagnosticCodes.ToolIdOwnerMismatch).ToArray();

        if (reportable.Length == 0)
        {
            // 历史局部重复、模型映射等错误继续使用通用码，避免 G4 无意扩大既有诊断协议。
            diagnostics.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.PluginServiceRegistrationFailed,
                HostDiagnosticPhase.PluginServiceRegistration)
            {
                PluginId = manifest.PluginId,
                AssemblyName = entry.Assembly.GetName(),
                Exception = exception,
            });
            return;
        }

        foreach (var diagnostic in reportable)
        {
            diagnostics.Report(new HostDiagnosticDraft(
                diagnostic.Code,
                HostDiagnosticPhase.PluginServiceRegistration)
            {
                PluginId = manifest.PluginId,
                AssemblyName = entry.Assembly.GetName(),
                StableId = diagnostic.Code is
                    HostDiagnosticCodes.DocumentIdOwnerMismatch or
                    HostDiagnosticCodes.ToolIdOwnerMismatch
                        ? diagnostic.StableId
                        : null,
            });
        }
    }
}
