using System;
using System.IO;
using System.Text.Json;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 负责布局快照的校验、原子读写和坏文件隔离，不解释 Dock 树语义。
/// </summary>
internal sealed class DockLayoutStore
{
    internal const string LayoutFileName = "layout-v1.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Action<string, string?, Exception?> _log;

    public DockLayoutStore()
        : this(GetDefaultPath(), (code, stableId, _) => LogToStandardError(code, stableId))
    {
    }

    /// <summary>
    /// 创建接入宿主统一诊断会话的生产布局存储。
    /// </summary>
    internal DockLayoutStore(IHostDiagnosticSink diagnostics)
        : this(
            GetDefaultPath(),
            (code, stableId, exception) => ReportToDiagnostics(
                diagnostics,
                code,
                stableId,
                exception))
    {
    }

    internal DockLayoutStore(
        string layoutPath,
        Action<string, string?>? log = null)
        : this(
            layoutPath,
            log is null
                ? (code, stableId, _) => LogToStandardError(code, stableId)
                : (code, stableId, _) => log(code, stableId))
    {
    }

    private DockLayoutStore(
        string layoutPath,
        Action<string, string?, Exception?> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutPath);
        LayoutPath = Path.GetFullPath(layoutPath);
        _log = log;
    }

    internal string LayoutPath { get; }

    internal DockLayoutSnapshotV1? Load()
    {
        if (!File.Exists(LayoutPath))
        {
            return null;
        }

        try
        {
            DockLayoutSnapshotV1? snapshot;
            using (var stream = new FileStream(
                       LayoutPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                snapshot = JsonSerializer.Deserialize<DockLayoutSnapshotV1>(
                    stream,
                    SerializerOptions);
            }

            // 必须先关闭读取句柄再隔离坏文件，否则 Windows 会因共享模式拒绝改名。
            var validationError = DockLayoutSnapshotValidator.Validate(snapshot);
            if (validationError is { } error)
            {
                Quarantine(error.Code, error.StableId);
                return null;
            }

            return snapshot;
        }
        catch (JsonException exception)
        {
            Quarantine("LAYOUT_JSON_INVALID", null, exception);
            return null;
        }
        catch (IOException exception)
        {
            // 文件被其他实例占用时保留原文件；启动仍使用默认布局，避免争抢导致数据丢失。
            _log("LAYOUT_READ_IO_FAILED", null, exception);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            _log("LAYOUT_READ_ACCESS_DENIED", null, exception);
            return null;
        }
    }

    internal void Save(DockLayoutSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (DockLayoutSnapshotValidator.Validate(snapshot) is { } error)
        {
            throw new InvalidDataException(
                $"布局快照未通过校验：{error.Code}，稳定 ID：{error.StableId ?? "-"}。");
        }

        AtomicFileTransaction.Write(
            LayoutPath,
            stream => JsonSerializer.Serialize(
                stream,
                snapshot,
                SerializerOptions));
    }

    internal void RejectLoadedSnapshot(string errorCode, string? stableId) =>
        Quarantine(errorCode, stableId);

    /// <summary>
    /// 记录没有触发布局隔离动作的读写诊断，例如关闭窗口时保存失败。
    /// </summary>
    internal void Report(string errorCode, string? stableId, Exception? exception = null) =>
        _log(errorCode, stableId, exception);

    private void Quarantine(
        string errorCode,
        string? stableId,
        Exception? sourceException = null)
    {
        _log(errorCode, stableId, sourceException);
        if (!File.Exists(LayoutPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(LayoutPath)!;
        var stem = Path.GetFileNameWithoutExtension(LayoutPath);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ");
        var backupPath = Path.Combine(
            directory,
            $"{stem}.{timestamp}.invalid.bak");

        try
        {
            File.Move(LayoutPath, backupPath);
        }
        catch (IOException exception)
        {
            _log("LAYOUT_QUARANTINE_IO_FAILED", stableId, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _log("LAYOUT_QUARANTINE_ACCESS_DENIED", stableId, exception);
        }
    }

    /// <summary>
    /// 计算生产布局文件路径，并允许自动化测试覆盖数据目录。
    /// </summary>
    /// <remarks>
    /// 未设置 <c>MYAVALONIA_DATA_DIRECTORY</c> 时保持原 LocalAppData 行为；
    /// 设置后使用调用方提供的隔离目录，确保真实启动冒烟不会污染用户布局。
    /// </remarks>
    private static string GetDefaultPath()
    {
        var configuredDataDirectory =
            Environment.GetEnvironmentVariable("MYAVALONIA_DATA_DIRECTORY");
        var dataDirectory = string.IsNullOrWhiteSpace(configuredDataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MyAvaloniaManagement")
            : Path.GetFullPath(configuredDataDirectory);

        return Path.Combine(dataDirectory, LayoutFileName);
    }

    private static void LogToStandardError(string errorCode, string? stableId) =>
        Console.Error.WriteLine(
            $"DockLayout errorCode={errorCode} stableId={stableId ?? "-"}");

    private static void ReportToDiagnostics(
        IHostDiagnosticSink diagnostics,
        string errorCode,
        string? stableId,
        Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        diagnostics.Report(new HostDiagnosticDraft(
            errorCode,
            HostDiagnosticPhase.Layout,
            "布局恢复或保存失败，宿主已使用安全回退并保留诊断。")
        {
            StableId = stableId,
            Exception = exception,
        });
    }
}
