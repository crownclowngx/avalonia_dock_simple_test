using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// Layout V3 的文件所有者：持有数据根独占写入句柄，严格读取、只读迁移、保留坏文件及上一有效备份。
/// 它不访问 Dock 或 UI；生命周期通过单一保存队列调用 Save，Dispose 必须晚于该队列排空。
/// </summary>
internal sealed class DockLayoutV3Store : IDisposable
{
    internal const string LayoutFileName = "layout-v3.json";
    private readonly Action<string, Exception?> _report;
    private readonly string _directory;
    private FileStream? _writeLease;
    private bool _readOnly;
    private bool _futureSchema;
    private bool _disposed;

    internal DockLayoutV3Store(string dataDirectory, Action<string, Exception?>? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _directory = Path.GetFullPath(dataDirectory);
        _report = report ?? ((code, _) => Console.Error.WriteLine($"DockLayout errorCode={code}"));
        LayoutPath = Path.Combine(_directory, LayoutFileName);
        try
        {
            Directory.CreateDirectory(_directory);
            // 锁文件留在磁盘上没有占用意义，只有句柄拥有写入权；进程崩溃时 OS 自动解除。
            // 不轮询、不自动接管，第二实例整个会话保持只读，避免两套活动布局轮流覆盖。
            _writeLease = new FileStream(Path.Combine(_directory, "layout-v3.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _readOnly = true;
            Report("LAYOUT_READ_ONLY", exception);
        }
    }

    internal string LayoutPath { get; }
    internal string BackupPath => LayoutPath + ".bak";
    internal bool CanWrite => !_disposed && !_readOnly && _writeLease is not null;

    /// <summary>V3 主文件与备份优先；只有首次且持有写入权的实例才从 V2 纯转换，不改旧文件字节。</summary>
    internal DockLayoutSnapshotV3? Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            var hasHistory = File.Exists(LayoutPath) || File.Exists(BackupPath) ||
                (Directory.Exists(_directory) && Directory.EnumerateFiles(_directory, "layout-v3*.invalid.bak").Any());
            var main = ReadV3(LayoutPath);
            if (_futureSchema) return null; // 已识别未来格式，不能使用旧备份覆盖语义。
            if (main is not null) return main;
            var backup = ReadV3(BackupPath);
            if (_futureSchema) return null;
            if (backup is not null) return backup;
            if (hasHistory || !CanWrite) return null;
            var legacy = Path.Combine(_directory, "layout-v2.json");
            if (!File.Exists(legacy)) return null;
            using var bytes = ReadBounded(legacy);
            return DockLayoutV2Migration.Convert(DockLayoutSnapshotV2Json.Read(bytes));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DockLayoutFormatException)
        {
            // 读取失败不把 V2 或被其他程序占用的文件当坏文件隔离；I/O 不确定时拒绝本会话覆盖。
            if (exception is IOException or UnauthorizedAccessException) _readOnly = true;
            Report("LAYOUT_LOAD_FAILED", exception);
            return null;
        }
    }

    /// <summary>
    /// 验证新快照和现存文件后，先更新上一有效备份，再原子提交主文件。两次提交不是跨文件事务；
    /// 任一失败都向保存调度报告，已有主文件或备份继续有效。不可写实例不创建或隔离任何布局文件。
    /// </summary>
    internal bool Save(DockLayoutSnapshotV3 snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanWrite) return false;
        DockLayoutV3Validator.Validate(snapshot);
        // 提前完成线格式大小验证，避免新快照不能写出时已经改变备份。
        using var next = new MemoryStream();
        DockLayoutV3Json.Write(next, snapshot);
        var previous = ReadV3(LayoutPath);
        if (!CanWrite) return false;
        _ = ReadV3(BackupPath); // 损坏备份先隔离，未知未来备份也必须保留。
        if (!CanWrite) return false;
        if (previous is not null)
            AtomicFileTransaction.Write(BackupPath, stream => DockLayoutV3Json.Write(stream, previous));
        AtomicFileTransaction.Write(LayoutPath, stream => stream.Write(next.GetBuffer(), 0, checked((int)next.Length)));
        return true;
    }

    /// <summary>结构错误可保留诊断副本；未来 schema 仅报告并停止写入，不归类为损坏。</summary>
    private DockLayoutSnapshotV3? ReadV3(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var bytes = ReadBounded(path);
            return DockLayoutV3Json.Read(bytes);
        }
        catch (DockLayoutFormatException exception)
        {
            if (exception.Code == "LAYOUT_SCHEMA_FUTURE")
            {
                _readOnly = true;
                _futureSchema = true;
                Report(exception.Code, exception);
                return null;
            }
            Report(exception.Code, exception);
            if (CanWrite) PreserveInvalid(path);
            return null;
        }
    }

    /// <summary>V2 与 V3 共用有界读取，防止旧版 reader 在迁移入口无界分配文件内容。</summary>
    private static MemoryStream ReadBounded(string path)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new MemoryStream();
        try
        {
            var block = new byte[8192];
            int count;
            while ((count = source.Read(block, 0, block.Length)) != 0)
            {
                if (buffer.Length + count > DockLayoutV3Validator.MaximumFileBytes)
                    throw new DockLayoutFormatException("LAYOUT_SIZE_EXCEEDED");
                buffer.Write(block, 0, count);
            }
            buffer.Position = 0;
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    private void PreserveInvalid(string path)
    {
        // 保留原位置，同时留下独立副本。即使下一次默认布局提交失败，也不会重新导入过时 V2。
        // 隔离失败向上抛出，禁止继续覆盖尚未得到保留的损坏输入。
        File.Copy(path, Path.Combine(_directory,
            $"{Path.GetFileName(path)}.{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}.{Guid.NewGuid():N}.invalid.bak"));
    }

    internal void Report(string code, Exception? exception = null)
    {
        try { _report(code, exception); }
        catch { /* 诊断接收端失败不能改变文件是否已经提交的事实。 */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLease?.Dispose();
        _writeLease = null;
    }
}
