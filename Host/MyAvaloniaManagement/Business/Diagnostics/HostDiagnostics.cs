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
    HostDiagnosticPhase Phase,
    string UserMessage)
{
    internal string? PluginId { get; init; }

    internal string? PluginDirectory { get; init; }

    internal string? AssemblyName { get; init; }

    internal string? StableId { get; init; }

    internal string? PluginVersion { get; init; }

    internal string? HostApiRange { get; init; }

    internal string? CommonContractRange { get; init; }

    internal Exception? Exception { get; init; }

    internal string? TechnicalDetail { get; init; }
}

/// <summary>
/// 写入内存快照与 JSON Lines 文件的不可变诊断记录。
/// </summary>
internal sealed record HostDiagnosticRecord
{
    internal const int CurrentSchemaVersion = 1;

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

    public string? HostApiRange { get; init; }

    public string? CommonContractRange { get; init; }

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
    internal const string PersistenceUnavailable = "DIAGNOSTIC_PERSISTENCE_UNAVAILABLE";
    internal const string PluginRootScanFailed = "PLUGIN_ROOT_SCAN_FAILED";
    internal const string PluginManifestMissing = "PLUGIN_MANIFEST_MISSING";
    internal const string PluginManifestInvalid = "PLUGIN_MANIFEST_INVALID";
    internal const string PluginManifestSchemaUnsupported = "PLUGIN_MANIFEST_SCHEMA_UNSUPPORTED";
    internal const string PluginHostApiIncompatible = "PLUGIN_HOST_API_INCOMPATIBLE";
    internal const string PluginCommonContractIncompatible = "PLUGIN_COMMON_CONTRACT_INCOMPATIBLE";
    internal const string PluginManifestIdentityDuplicate = "PLUGIN_MANIFEST_IDENTITY_DUPLICATE";
    internal const string PluginManifestDescriptionMismatch = "PLUGIN_MANIFEST_DESCRIPTION_MISMATCH";
    internal const string PluginEntryInvalid = "PLUGIN_ENTRY_INVALID";
    internal const string PluginEntryAmbiguous = "PLUGIN_ENTRY_AMBIGUOUS";
    internal const string PluginPrivateDependencyAmbiguous = "PLUGIN_PRIVATE_DEPENDENCY_AMBIGUOUS";
    internal const string PluginAssemblyLoadFailed = "PLUGIN_ASSEMBLY_LOAD_FAILED";
    internal const string PluginSharedAssemblyMismatch = "PLUGIN_SHARED_ASSEMBLY_MISMATCH";
    internal const string PluginTypePreflightFailed = "PLUGIN_TYPE_PREFLIGHT_FAILED";
    internal const string PluginServiceRegistrationFailed = "PLUGIN_SERVICE_REGISTRATION_FAILED";
    internal const string HostContainerBuildFailed = "HOST_CONTAINER_BUILD_FAILED";
    internal const string ExtensionDiscoveryFailed = "EXTENSION_DISCOVERY_FAILED";
    internal const string ExtensionActivationFailed = "EXTENSION_ACTIVATION_FAILED";
    internal const string LifecycleFailed = "LIFECYCLE_FAILED";
    internal const string HostStartupCleanupFailed = "HOST_STARTUP_CLEANUP_FAILED";
    internal const string HostStartupUnexpected = "HOST_STARTUP_UNEXPECTED";
}

/// <summary>
/// 将错误码和发生阶段映射为统一严重程度与启动决策。
/// </summary>
internal static class HostDiagnosticFailurePolicy
{
    private static readonly HashSet<string> RecoverablePluginLoadCodes = new(StringComparer.Ordinal)
    {
        HostDiagnosticCodes.PluginEntryInvalid,
        HostDiagnosticCodes.PluginEntryAmbiguous,
        HostDiagnosticCodes.PluginPrivateDependencyAmbiguous,
        HostDiagnosticCodes.PluginAssemblyLoadFailed,
        HostDiagnosticCodes.PluginSharedAssemblyMismatch,
        HostDiagnosticCodes.PluginTypePreflightFailed,
        HostDiagnosticCodes.PluginManifestMissing,
        HostDiagnosticCodes.PluginManifestInvalid,
        HostDiagnosticCodes.PluginManifestSchemaUnsupported,
        HostDiagnosticCodes.PluginHostApiIncompatible,
        HostDiagnosticCodes.PluginCommonContractIncompatible,
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
            phase == HostDiagnosticPhase.PluginLifecycle)
        {
            return (HostDiagnosticSeverity.Error, HostDiagnosticDisposition.Continue);
        }

        if (code == HostDiagnosticCodes.PluginRootScanFailed ||
            code == HostDiagnosticCodes.PluginManifestIdentityDuplicate ||
            code == HostDiagnosticCodes.PluginManifestDescriptionMismatch ||
            code == HostDiagnosticCodes.PluginServiceRegistrationFailed ||
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
        var classification = HostDiagnosticFailurePolicy.Classify(draft.Code, draft.Phase);
        var record = new HostDiagnosticRecord
        {
            SessionId = SessionId,
            Sequence = 0,
            TimestampUtc = DateTimeOffset.UtcNow,
            Code = draft.Code,
            Severity = classification.Severity,
            Phase = draft.Phase,
            Disposition = classification.Disposition,
            PluginId = draft.PluginId,
            PluginDirectory = draft.PluginDirectory,
            AssemblyName = draft.AssemblyName,
            StableId = draft.StableId,
            PluginVersion = draft.PluginVersion,
            HostApiRange = draft.HostApiRange,
            CommonContractRange = draft.CommonContractRange,
            UserMessage = draft.UserMessage,
            ExceptionType = draft.Exception?.GetType().FullName,
            TechnicalDetail = draft.TechnicalDetail ?? draft.Exception?.ToString(),
        };

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            record = record with { Sequence = ++_sequence };
            _records.Add(record);
            TryWriteRecord(record);
        }

        Mirror(record);
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
            var root = ResolveDataDirectory(dataDirectory);
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
                    HostDiagnosticPhase.DiagnosticInfrastructure,
                    "部分历史诊断日志无法清理，本次会话日志仍可使用。")
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
        var classification = HostDiagnosticFailurePolicy.Classify(
            HostDiagnosticCodes.PersistenceUnavailable,
            HostDiagnosticPhase.DiagnosticInfrastructure);
        var record = new HostDiagnosticRecord
        {
            SessionId = SessionId,
            Sequence = ++_sequence,
            TimestampUtc = DateTimeOffset.UtcNow,
            Code = HostDiagnosticCodes.PersistenceUnavailable,
            Severity = classification.Severity,
            Phase = HostDiagnosticPhase.DiagnosticInfrastructure,
            Disposition = classification.Disposition,
            UserMessage = "无法写入本次会话的诊断日志，诊断仍保留在内存中。",
            ExceptionType = exception.GetType().FullName,
            TechnicalDetail = exception.ToString(),
        };
        _records.Add(record);
        Mirror(record);
    }

    private static string ResolveDataDirectory(string? explicitDataDirectory)
    {
        var configured = explicitDataDirectory ??
                         Environment.GetEnvironmentVariable("MYAVALONIA_DATA_DIRECTORY");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyAvaloniaManagement")
            : Path.GetFullPath(configured);
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
