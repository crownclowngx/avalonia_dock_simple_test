using System;
using System.IO;
using System.Text.Json;

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

    private readonly Action<string, string?> _log;

    public DockLayoutStore()
        : this(GetDefaultPath(), LogToStandardError)
    {
    }

    internal DockLayoutStore(
        string layoutPath,
        Action<string, string?>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutPath);
        LayoutPath = Path.GetFullPath(layoutPath);
        _log = log ?? LogToStandardError;
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
        catch (JsonException)
        {
            Quarantine("LAYOUT_JSON_INVALID", null);
            return null;
        }
        catch (IOException)
        {
            // 文件被其他实例占用时保留原文件；启动仍使用默认布局，避免争抢导致数据丢失。
            _log("LAYOUT_READ_IO_FAILED", null);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            _log("LAYOUT_READ_ACCESS_DENIED", null);
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

        var directory = Path.GetDirectoryName(LayoutPath)
            ?? throw new InvalidOperationException("布局文件没有父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $"{LayoutFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       16 * 1024,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, snapshot, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(LayoutPath))
            {
                // 同目录 File.Replace 保证旧文件或新文件完整存在，进程中断不会留下半份 JSON。
                File.Replace(temporaryPath, LayoutPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, LayoutPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal void RejectLoadedSnapshot(string errorCode, string? stableId) =>
        Quarantine(errorCode, stableId);

    private void Quarantine(string errorCode, string? stableId)
    {
        _log(errorCode, stableId);
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
        catch (IOException)
        {
            _log("LAYOUT_QUARANTINE_IO_FAILED", stableId);
        }
        catch (UnauthorizedAccessException)
        {
            _log("LAYOUT_QUARANTINE_ACCESS_DENIED", stableId);
        }
    }

    private static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyAvaloniaManagement",
            LayoutFileName);

    private static void LogToStandardError(string errorCode, string? stableId) =>
        Console.Error.WriteLine(
            $"DockLayout errorCode={errorCode} stableId={stableId ?? "-"}");
}
