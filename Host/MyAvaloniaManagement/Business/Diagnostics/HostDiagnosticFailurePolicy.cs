using System;
using System.Collections.Generic;

namespace MyAvaloniaManagement.Business.Diagnostics;

/// <summary>
/// 将错误码和发生阶段映射为统一严重程度与启动决策。
/// </summary>
internal static class HostDiagnosticFailurePolicy
{
    private static readonly HashSet<string> RecoverablePluginLoadCodes = new(StringComparer.Ordinal)
    {
        HostDiagnosticCodes.PluginEntryInvalid,
        HostDiagnosticCodes.PluginDependencyManifestMissing,
        HostDiagnosticCodes.PluginAssemblyLoadFailed,
        HostDiagnosticCodes.PluginSharedAssemblyMismatch,
        HostDiagnosticCodes.PluginTypePreflightFailed,
        HostDiagnosticCodes.PluginManifestMissing,
        HostDiagnosticCodes.PluginManifestInvalid,
        HostDiagnosticCodes.PluginManifestSchemaUnsupported,
        HostDiagnosticCodes.PluginSdkIncompatible,
    };

    internal static (HostDiagnosticSeverity Severity, HostDiagnosticDisposition Disposition) Classify(
        string code,
        HostDiagnosticPhase phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (code == "PLUGIN_ENABLEMENT_RECOVERED" || code == HostDiagnosticCodes.PersistenceUnavailable ||
            phase is HostDiagnosticPhase.Layout or HostDiagnosticPhase.IconPresentation)
        {
            return (HostDiagnosticSeverity.Warning, HostDiagnosticDisposition.Continue);
        }

        if (RecoverablePluginLoadCodes.Contains(code) ||
            code == "ICON_REFERENCE_DUPLICATE" ||
            code == HostDiagnosticCodes.PluginServiceRegistrationFailed ||
            code == HostDiagnosticCodes.PluginHostServiceRegistrationForbidden ||
            code == HostDiagnosticCodes.PluginContributionServiceRegistrationForbidden ||
            code == HostDiagnosticCodes.DocumentIdOwnerMismatch ||
            code == HostDiagnosticCodes.ToolIdOwnerMismatch ||
            code == HostDiagnosticCodes.PluginContainerBuildFailed ||
            code == HostDiagnosticCodes.PluginModuleActivationFailed ||
            phase == HostDiagnosticPhase.PluginLifecycle)
        {
            return (HostDiagnosticSeverity.Error, HostDiagnosticDisposition.Continue);
        }

        if (code == HostDiagnosticCodes.PluginRootScanFailed ||
            code == HostDiagnosticCodes.PluginManifestIdentityDuplicate ||
            code == HostDiagnosticCodes.PluginManifestDescriptionMismatch ||
            code == HostDiagnosticCodes.HostContainerBuildFailed ||
            code == HostDiagnosticCodes.HostStartupUnexpected ||
            phase is HostDiagnosticPhase.PluginModuleDiscovery
                or HostDiagnosticPhase.PluginServiceRegistration
                or HostDiagnosticPhase.HostContainerBuild
                or HostDiagnosticPhase.ExtensionDiscovery
                or HostDiagnosticPhase.HostBootstrap)
        {
            return (HostDiagnosticSeverity.Fatal, HostDiagnosticDisposition.AbortStartup);
        }

        return (HostDiagnosticSeverity.Error, HostDiagnosticDisposition.Continue);
    }
}
