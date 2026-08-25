using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.PluginSdk;
using DocumentTypeId = MyAvaloniaManagement.PluginSdk.DocumentTypeId;
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
    Layout,
    HostBootstrap,
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

/// <summary>
/// 集中定义稳定错误码，避免业务阶段继续散落无法检索的字符串字面量。
/// </summary>
internal static class HostDiagnosticCodes
{
    internal const string DiagnosticInputRejected = "HOST_DIAGNOSTIC_INPUT_REJECTED";
    internal const string PersistenceUnavailable = "DIAGNOSTIC_PERSISTENCE_UNAVAILABLE";
    internal const string PluginRootScanFailed = "PLUGIN_ROOT_SCAN_FAILED";
    internal const string PluginManifestMissing = "PLUGIN_MANIFEST_MISSING";
    internal const string PluginManifestInvalid = "PLUGIN_MANIFEST_INVALID";
    internal const string PluginManifestSchemaUnsupported = "PLUGIN_MANIFEST_SCHEMA_UNSUPPORTED";
    internal const string PluginSdkIncompatible = "PLUGIN_SDK_INCOMPATIBLE";
    internal const string PluginManifestIdentityDuplicate = "PLUGIN_MANIFEST_IDENTITY_DUPLICATE";
    internal const string PluginManifestDescriptionMismatch = "PLUGIN_MANIFEST_DESCRIPTION_MISMATCH";
    internal const string PluginEntryInvalid = "PLUGIN_ENTRY_INVALID";
    internal const string PluginDependencyManifestMissing = "PLUGIN_DEPENDENCY_MANIFEST_MISSING";
    internal const string PluginAssemblyLoadFailed = "PLUGIN_ASSEMBLY_LOAD_FAILED";
    internal const string PluginSharedAssemblyMismatch = "PLUGIN_SHARED_ASSEMBLY_MISMATCH";
    internal const string PluginTypePreflightFailed = "PLUGIN_TYPE_PREFLIGHT_FAILED";
    internal const string PluginModuleActivationFailed = "PLUGIN_MODULE_ACTIVATION_FAILED";
    internal const string PluginServiceRegistrationFailed = "PLUGIN_SERVICE_REGISTRATION_FAILED";
    internal const string PluginHostServiceRegistrationForbidden =
        "PLUGIN_HOST_SERVICE_REGISTRATION_FORBIDDEN";
    internal const string PluginContributionServiceRegistrationForbidden =
        "PLUGIN_CONTRIBUTION_SERVICE_REGISTRATION_FORBIDDEN";
    internal const string DocumentIdOwnerMismatch = "DOCUMENT_ID_OWNER_MISMATCH";
    internal const string ToolIdOwnerMismatch = "TOOL_ID_OWNER_MISMATCH";
    internal const string PluginContainerBuildFailed = "PLUGIN_CONTAINER_BUILD_FAILED";
    internal const string HostContainerBuildFailed = "HOST_CONTAINER_BUILD_FAILED";
    internal const string ExtensionDiscoveryFailed = "EXTENSION_DISCOVERY_FAILED";
    internal const string ExtensionActivationFailed = "EXTENSION_ACTIVATION_FAILED";
    internal const string ToolAdapterActivationFailed = "TOOL_ADAPTER_ACTIVATION_FAILED";
    internal const string LifecycleInitializeFailed = "LIFECYCLE_INITIALIZE_FAILED";
    internal const string LifecycleInitializeTimeout = "LIFECYCLE_INITIALIZE_TIMEOUT";
    internal const string LifecycleShutdownFailed = "LIFECYCLE_SHUTDOWN_FAILED";
    internal const string LifecycleShutdownTimeout = "LIFECYCLE_SHUTDOWN_TIMEOUT";
    internal const string LifecycleHostCancelled = "LIFECYCLE_HOST_CANCELLED";
    internal const string LifecycleCancellationFailed = "LIFECYCLE_CANCELLATION_FAILED";
    internal const string HostStartupCleanupFailed = "HOST_STARTUP_CLEANUP_FAILED";
    internal const string HostStartupUnexpected = "HOST_STARTUP_UNEXPECTED";
    internal const string WorkflowActionShutdownTimeout =
        "WORKFLOW_ACTION_SHUTDOWN_TIMEOUT";
}

/// <summary>
/// 将内部诊断草稿转换为可长期保存的白名单记录。
/// </summary>
/// <remarks>
/// 设计意图：草稿位于异常捕获边界，可能携带插件异常和未经验证的目录信息；记录则会同时进入
/// 内存界面、JSON Lines 和默认镜像，必须在两者之间完成一次不可绕过的收窄。这里不做关键词替换，
/// 因为密码、正文和签名地址没有可靠的通用词法特征；只复制已经具备明确格式的字段更容易审计。
/// </remarks>
internal static class HostDiagnosticRedactionPolicy
{
    private const int MaximumTokenLength = 128;

    internal static HostDiagnosticRecord Create(
        Guid sessionId,
        HostDiagnosticDraft draft,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var hasValidCode = IsSafeErrorCode(draft.Code);
        var code = hasValidCode
            ? draft.Code
            : HostDiagnosticCodes.DiagnosticInputRejected;
        var classification = HostDiagnosticFailurePolicy.Classify(code, draft.Phase);
        return new HostDiagnosticRecord
        {
            SessionId = sessionId,
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Code = code,
            Severity = classification.Severity,
            Phase = draft.Phase,
            Disposition = classification.Disposition,
            PluginId = draft.PluginId?.Value,
            PluginDirectory = ToSafeLeafToken(draft.PluginDirectory),
            AssemblyName = ToSafeAssemblySimpleName(draft.AssemblyName),
            StableId = ToSafeStableId(draft.StableId),
            PluginVersion = draft.PluginVersion is null
                ? null
                : PluginVersionText.Format(draft.PluginVersion),
            SdkRange = draft.SdkRange?.ToString(),
            UserMessage = CreateUserMessage(code, draft.Phase),
            ExceptionType = draft.Exception?.GetType().FullName,
            TechnicalDetail = CreateControlledDetail(draft),
        };
    }

    /// <summary>
    /// 仅根据宿主拥有的错误码和阶段生成用户说明。
    /// </summary>
    /// <remarks>
    /// 这里故意不接收调用点文本。清单属性名、入口名称、插件类型名等内容即使经过局部校验，
    /// 仍然可能由插件或文件控制；集中固定映射可以保证 UI 与 JSONL 不会因后续调用点疏忽而泄漏。
    /// 未知但格式合法的错误码也只得到阶段级固定说明，错误码本身仍保留，便于定位新增分支。
    /// </remarks>
    private static string CreateUserMessage(string code, HostDiagnosticPhase phase) => code switch
    {
        HostDiagnosticCodes.DiagnosticInputRejected =>
            "诊断输入未通过白名单校验，原始输入未被保存。",
        HostDiagnosticCodes.PersistenceUnavailable =>
            "诊断持久化暂不可用，本次会话仍保留受控内存记录。",
        HostDiagnosticCodes.PluginRootScanFailed =>
            "无法完成插件根目录扫描，宿主不能确认本次启动的插件集合。",
        HostDiagnosticCodes.PluginManifestMissing or
        HostDiagnosticCodes.PluginManifestInvalid or
        HostDiagnosticCodes.PluginManifestSchemaUnsupported or
        HostDiagnosticCodes.PluginSdkIncompatible or
        HostDiagnosticCodes.PluginManifestIdentityDuplicate or
        HostDiagnosticCodes.PluginManifestDescriptionMismatch =>
            "插件清单未通过预检，已隔离对应插件候选。",
        HostDiagnosticCodes.PluginEntryInvalid or
        HostDiagnosticCodes.PluginDependencyManifestMissing or
        HostDiagnosticCodes.PluginAssemblyLoadFailed or
        HostDiagnosticCodes.PluginSharedAssemblyMismatch or
        HostDiagnosticCodes.PluginTypePreflightFailed =>
            "插件入口或类型未通过预检，已隔离对应插件候选。",
        HostDiagnosticCodes.PluginServiceRegistrationFailed =>
            "插件显式注册失败，已隔离该插件，宿主与其他插件继续运行。",
        HostDiagnosticCodes.PluginHostServiceRegistrationForbidden or
        HostDiagnosticCodes.PluginContributionServiceRegistrationForbidden =>
            "插件登记了由宿主保留的服务类型，已在容器构建前隔离该插件。",
        HostDiagnosticCodes.DocumentIdOwnerMismatch or
        HostDiagnosticCodes.ToolIdOwnerMismatch =>
            "插件贡献 ID 不属于清单声明的插件命名空间，已隔离该插件。",
        HostDiagnosticCodes.PluginContainerBuildFailed =>
            "插件私有依赖注入容器构建失败，已隔离该插件。",
        HostDiagnosticCodes.HostContainerBuildFailed =>
            "宿主依赖注入容器构建失败，主工作台不能安全启动。",
        HostDiagnosticCodes.ExtensionDiscoveryFailed or
        HostDiagnosticCodes.ExtensionActivationFailed =>
            "扩展贡献激活或校验失败，主工作台不能安全启动。",
        HostDiagnosticCodes.ToolAdapterActivationFailed =>
            "Tool 适配或视图创建失败，已隔离该 Tool，其他工作区继续运行。",
        HostDiagnosticCodes.LifecycleInitializeFailed or
        HostDiagnosticCodes.LifecycleInitializeTimeout =>
            "插件初始化失败或超时，已隔离该插件贡献。",
        HostDiagnosticCodes.LifecycleShutdownFailed or
        HostDiagnosticCodes.LifecycleShutdownTimeout =>
            "插件关闭失败或超时，宿主将继续释放其他资源。",
        HostDiagnosticCodes.LifecycleHostCancelled =>
            "宿主取消了插件初始化。",
        HostDiagnosticCodes.LifecycleCancellationFailed =>
            "插件生命周期操作失败。",
        HostDiagnosticCodes.WorkflowActionShutdownTimeout =>
            "Workflow Action 在关闭宽限内没有退出，宿主已阻止不安全的 Provider 释放。",
        HostDiagnosticCodes.HostStartupCleanupFailed =>
            "启动失败后的资源清理发生异常，应用将退出。",
        HostDiagnosticCodes.HostStartupUnexpected =>
            "宿主启动发生未分类异常，主工作台没有启动。",
        "VIEW_CREATION_FAILED" =>
            "已登记的插件视图创建失败。",
        HostDiagnosticCodes.PluginModuleActivationFailed =>
            "插件模块无法通过公共无参构造创建。",
        _ when phase == HostDiagnosticPhase.Layout =>
            "布局恢复或保存失败，宿主已使用安全回退并保留诊断。",
        _ when phase == HostDiagnosticPhase.PluginLifecycle =>
            "插件生命周期操作失败。",
        _ when phase == HostDiagnosticPhase.WorkflowAction =>
            "Workflow Action 调用失败；参数正文和插件异常未写入诊断。",
        _ => "宿主操作失败，原始输入未被保存。",
    };

    private static string? CreateControlledDetail(HostDiagnosticDraft draft)
    {
        if (draft.LifecycleStage is null && draft.Duration is null)
        {
            return null;
        }

        var parts = new List<string>(capacity: 2);
        if (draft.LifecycleStage is { } stage)
        {
            parts.Add($"stage={stage}");
        }

        if (draft.Duration is { } duration)
        {
            parts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "durationMs={0:0.###}",
                duration.TotalMilliseconds));
        }

        return string.Join("; ", parts);
    }

    private static bool IsSafeErrorCode(string? value) =>
        IsSafeToken(value, allowLowercase: false);

    private static string? ToSafeStableId(string? value) =>
        DocumentTypeId.TryParse(value, out var stableId)
            ? stableId!.Value
            : null;

    private static string? ToSafeLeafToken(string? value)
    {
        if (!IsSafeToken(value, allowLowercase: true) ||
            Path.IsPathRooted(value!) ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
        {
            return null;
        }

        return value;
    }

    private static string? ToSafeAssemblySimpleName(AssemblyName? value)
    {
        var simpleName = value?.Name;
        return IsSafeToken(simpleName, allowLowercase: true)
            ? simpleName
            : null;
    }

    private static bool IsSafeToken(string? value, bool allowLowercase)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTokenLength)
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterUpper(character) ||
            allowLowercase && char.IsAsciiLetterLower(character) ||
            char.IsAsciiDigit(character) ||
            character is '_' or '-' or '.');
    }
}

/// <summary>
/// 在开发者显式承担风险时，把原始异常写入进程级临时输出。
/// </summary>
/// <remarks>
/// 该输出与 <see cref="HostDiagnosticRecord"/> 完全分离，不能写入宿主 JSONL、UI 或剪贴板。
/// 环境变量只接受精确值 <c>1</c>，避免部署环境中的模糊布尔值意外开启敏感输出。
/// </remarks>
internal static class HostSensitiveDiagnosticDebugOutput
{
    internal const string EnvironmentVariableName =
        "MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS";

    internal static bool IsEnabled => string.Equals(
        Environment.GetEnvironmentVariable(EnvironmentVariableName),
        "1",
        StringComparison.Ordinal);

    internal static void Write(
        string code,
        HostDiagnosticPhase phase,
        Exception? exception)
    {
        if (!IsEnabled || exception is null)
        {
            return;
        }

        var warning =
            "[敏感诊断已开启] 以下异常原文可能包含密码、Token、正文和本地路径；" +
            "宿主不会把它写入诊断 JSONL，请仅在本地短期使用。";
        var detail = $"{warning}{Environment.NewLine}" +
                     $"errorCode={code} phase={phase}{Environment.NewLine}{exception}";
        try
        {
            Trace.TraceWarning(detail);
            Console.Error.WriteLine(detail);
        }
        catch (Exception outputException) when (
            outputException is IOException or ObjectDisposedException)
        {
            // 调试旁路不是产品诊断通道，终端或 Trace 监听器不可用时不能反向破坏宿主。
        }
    }
}

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

        if (code == HostDiagnosticCodes.PersistenceUnavailable ||
            phase == HostDiagnosticPhase.Layout)
        {
            return (HostDiagnosticSeverity.Warning, HostDiagnosticDisposition.Continue);
        }

        if (RecoverablePluginLoadCodes.Contains(code) ||
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

/// <summary>
/// 当前进程的一次宿主诊断会话，同时承担内存快照、增量 JSON Lines 持久化和
/// Trace/Console 兼容镜像。
/// </summary>
/// <remarks>
/// 设计意图：诊断设施自身绝不能成为新的单点故障。任何目录、清理或写入异常都只会
/// 关闭本会话的文件输出，内存快照和用户可见错误窗口仍然可用。
/// </remarks>
internal sealed class HostDiagnosticSession : IHostDiagnosticSink, IDisposable
{
    private const int RetainedSessionCount = 20;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly List<HostDiagnosticRecord> _records = [];
    private StreamWriter? _writer;
    private long _sequence;
    private bool _disposed;

    private HostDiagnosticSession(Guid sessionId)
    {
        SessionId = sessionId;
    }

    internal Guid SessionId { get; }

    internal string? LogPath { get; private set; }

    internal IReadOnlyList<HostDiagnosticRecord> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    internal static HostDiagnosticSession Start(string? dataDirectory = null)
    {
        var session = new HostDiagnosticSession(Guid.NewGuid());
        session.TryStartPersistence(dataDirectory);
        return session;
    }

    public HostDiagnosticRecord Report(HostDiagnosticDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var record = HostDiagnosticRedactionPolicy.Create(
            SessionId,
            draft,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            record = record with { Sequence = ++_sequence };
            _records.Add(record);
            TryWriteRecord(record);
        }

        Mirror(record);
        HostSensitiveDiagnosticDebugOutput.Write(record.Code, record.Phase, draft.Exception);
        return record;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var writer = _writer;
            _writer = null;
            try
            {
                writer?.Dispose();
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException)
            {
                AddInfrastructureFailure(exception);
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    private void TryStartPersistence(string? dataDirectory)
    {
        try
        {
            var root = dataDirectory is null
                ? HostDataRootPolicy.ResolveDefault()
                : HostDataRootPolicy.Resolve(
                    dataDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var diagnosticDirectory = Path.Combine(root, "Diagnostics");
            Directory.CreateDirectory(diagnosticDirectory);
            var cleanupFailure = DeleteExpiredSessions(diagnosticDirectory);

            var fileName = string.Format(
                CultureInfo.InvariantCulture,
                "session-{0:yyyyMMddTHHmmssfffffffZ}-{1}-{2:N}.jsonl",
                DateTimeOffset.UtcNow,
                Environment.ProcessId,
                SessionId);
            LogPath = Path.Combine(diagnosticDirectory, fileName);
            _writer = new StreamWriter(
                new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
            if (cleanupFailure is not null)
            {
                Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.PersistenceUnavailable,
                    HostDiagnosticPhase.DiagnosticInfrastructure)
                {
                    Exception = cleanupFailure,
                });
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or System.Security.SecurityException)
        {
            LogPath = null;
            AddInfrastructureFailure(exception);
        }
    }

    private void TryWriteRecord(HostDiagnosticRecord record)
    {
        if (_writer is null)
        {
            return;
        }

        try
        {
            _writer.WriteLine(JsonSerializer.Serialize(record, SerializerOptions));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            try
            {
                _writer.Dispose();
            }
            catch (Exception disposeException) when (
                disposeException is IOException or ObjectDisposedException)
            {
                // 文件写入已经不可用，释放失败不应覆盖最初、更有诊断价值的异常。
            }
            _writer = null;
            LogPath = null;
            AddInfrastructureFailure(exception);
        }
    }

    private void AddInfrastructureFailure(Exception exception)
    {
        var draft = new HostDiagnosticDraft(
            HostDiagnosticCodes.PersistenceUnavailable,
            HostDiagnosticPhase.DiagnosticInfrastructure)
        {
            Exception = exception,
        };
        var record = HostDiagnosticRedactionPolicy.Create(
            SessionId,
            draft,
            DateTimeOffset.UtcNow) with
        {
            Sequence = ++_sequence,
        };
        _records.Add(record);
        Mirror(record);
        HostSensitiveDiagnosticDebugOutput.Write(record.Code, record.Phase, exception);
    }

    private static Exception? DeleteExpiredSessions(string directory)
    {
        Exception? firstFailure = null;
        var files = Directory.GetFiles(directory, "session-*.jsonl")
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Skip(RetainedSessionCount - 1)
            .ToArray();
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    System.Security.SecurityException)
            {
                firstFailure ??= exception;
            }
        }

        return firstFailure;
    }

    private static void Mirror(HostDiagnosticRecord record)
    {
        var text = $"HostDiagnostic errorCode={record.Code} phase={record.Phase} " +
                   $"pluginId={record.PluginId ?? "-"} plugin={record.PluginDirectory ?? "-"} " +
                   $"assembly={record.AssemblyName ?? "-"} stableId={record.StableId ?? "-"}";
        try
        {
            if (record.Severity is HostDiagnosticSeverity.Fatal or HostDiagnosticSeverity.Error)
            {
                Trace.TraceError(text);
                Console.Error.WriteLine(text);
                return;
            }

            Trace.TraceWarning(text);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
            // 兼容镜像不是主诊断通道，控制台关闭或 Trace 监听器失败时不得影响宿主。
        }
    }
}

/// <summary>
/// 把底层加载异常稳定映射为宿主错误码。
/// </summary>
internal static class PluginLoadExceptionMapper
{
    internal static string GetCode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var pending = new Stack<Exception>();
        pending.Push(exception);
        while (pending.TryPop(out var current))
        {
            if (current.Message.Contains(
                    HostDiagnosticCodes.PluginSharedAssemblyMismatch,
                    StringComparison.Ordinal))
            {
                return HostDiagnosticCodes.PluginSharedAssemblyMismatch;
            }

            if (current.InnerException is { } innerException)
            {
                pending.Push(innerException);
            }

            if (current is ReflectionTypeLoadException reflection)
            {
                foreach (var loaderException in reflection.LoaderExceptions)
                {
                    if (loaderException is not null)
                    {
                        pending.Push(loaderException);
                    }
                }
            }
        }

        return HostDiagnosticCodes.PluginAssemblyLoadFailed;
    }
}
