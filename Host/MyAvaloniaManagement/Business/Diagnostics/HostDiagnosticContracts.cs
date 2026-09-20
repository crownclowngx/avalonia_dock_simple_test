using System;
using System.Reflection;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.PluginSdk;
using PluginLifecycleStage = MyAvaloniaManagement.Business.Lifecycle.PluginLifecycleStage;

namespace MyAvaloniaManagement.Business.Diagnostics;

/// <summary>
/// 表示宿主诊断发生的稳定阶段。阶段名称会写入会话日志，因此新增阶段可以兼容，
/// 已发布名称不得随意修改。
/// </summary>
internal enum HostDiagnosticPhase
{
    DiagnosticInfrastructure,
    PluginRootDiscovery,
    PluginManifestPreflight,
    PluginAssemblyLoad,
    PluginTypePreflight,
    PluginModuleDiscovery,
    PluginServiceRegistration,
    HostContainerBuild,
    ExtensionDiscovery,
    PluginLifecycle,
    WorkflowAction,
    WorkbenchCommand,
    Layout,
    HostBootstrap,
    IconPresentation,
}

/// <summary>
/// 宿主诊断严重程度。它描述问题本身，不直接等价于宿主是否退出；
/// 是否继续由 <see cref="HostDiagnosticDisposition"/> 独立表达。
/// </summary>
internal enum HostDiagnosticSeverity
{
    Information,
    Warning,
    Error,
    Fatal,
}

/// <summary>
/// 表示一条诊断对当前启动会话的控制决策。
/// </summary>
internal enum HostDiagnosticDisposition
{
    Continue,
    AbortStartup,
}

/// <summary>
/// 业务阶段提交给诊断入口的最小信息。
/// </summary>
/// <remarks>
/// 设计意图：阶段代码只描述“发生了什么”，不自行决定严重程度和退出策略，
/// 从而避免同一个错误码在不同调用点产生相反的启动行为。
/// </remarks>
internal sealed record HostDiagnosticDraft(
    string Code,
    HostDiagnosticPhase Phase)
{
    internal PluginId? PluginId { get; init; }

    internal string? PluginDirectory { get; init; }

    internal AssemblyName? AssemblyName { get; init; }

    internal string? StableId { get; init; }

    internal Version? PluginVersion { get; init; }

    internal PluginVersionRange? SdkRange { get; init; }

    internal Exception? Exception { get; init; }

    internal PluginLifecycleStage? LifecycleStage { get; init; }

    internal TimeSpan? Duration { get; init; }
}

/// <summary>
/// 写入内存快照与 JSON Lines 文件的不可变诊断记录。
/// </summary>
internal sealed record HostDiagnosticRecord
{
    internal const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid SessionId { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string Code { get; init; }

    public required HostDiagnosticSeverity Severity { get; init; }

    public required HostDiagnosticPhase Phase { get; init; }

    public required HostDiagnosticDisposition Disposition { get; init; }

    public string? PluginId { get; init; }

    public string? PluginDirectory { get; init; }

    public string? AssemblyName { get; init; }

    public string? StableId { get; init; }

    public string? PluginVersion { get; init; }

    public string? SdkRange { get; init; }

    public required string UserMessage { get; init; }

    public string? ExceptionType { get; init; }

    public string? TechnicalDetail { get; init; }
}

/// <summary>
/// 宿主各阶段唯一依赖的诊断写入端口。
/// </summary>
internal interface IHostDiagnosticSink
{
    HostDiagnosticRecord Report(HostDiagnosticDraft draft);
}
