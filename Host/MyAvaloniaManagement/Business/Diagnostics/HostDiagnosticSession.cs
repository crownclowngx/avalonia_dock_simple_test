using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Diagnostics;

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

    /// <summary>先收窄草稿，再按同一序号写入内存与文件，最后输出受控镜像。</summary>
    /// <remarks>
    /// 会话独占写入锁和序号，避免文件顺序与内存快照分离；默认输出只接收脱敏后的记录。
    /// 原始异常仅交给显式开启的独立调试旁路，不能因文件写入失败而绕过脱敏。
    /// </remarks>
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
