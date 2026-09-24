using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>登记与日志唯一存储边界。调用者持有操作锁；先验证再提交，不因解析失败重置安装历史。</summary>
internal sealed class PluginInstallationStore(PluginInstallPaths paths, IPluginInstallFileCommit? files = null)
{
    private readonly IPluginInstallFileCommit _files = files ?? new PluginInstallFileCommit();
    private string OperationPath => paths.Owned(Path.Combine(paths.ManagementRoot, "operation-v1.json"));
    private string IndexPath => paths.Owned(Path.Combine(paths.ManagementRoot, "installed-v1.json"));

    internal PluginInstallOperation? ReadOperation()
    {
        if (!Exists(OperationPath)) return null;
        var value = PluginInstallJson.Read<PluginInstallOperation>(OperationPath, 1024 * 1024);
        Validate(value); return value;
    }

    internal PluginInstallationIndex ReadIndex()
    {
        if (!Exists(IndexPath)) return PluginInstallationIndex.Empty;
        var value = PluginInstallJson.Read<PluginInstallationIndex>(IndexPath, 4 * 1024 * 1024);
        Validate(value); return value;
    }

    internal void WriteOperation(PluginInstallOperation value)
    {
        Validate(value);
        Write(OperationPath, value, 1024 * 1024);
    }
    internal void WriteIndex(PluginInstallationIndex value)
    {
        Validate(value);
        Write(IndexPath, value, 4 * 1024 * 1024);
    }
    private void Write<T>(string path, T value, int limit)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, PluginInstallJson.Options);
        if (bytes.Length > limit) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装记录超过大小限制。");
        _files.WriteAtomic(path, bytes);
    }

    private static bool Exists(string path)
    {
        if (PluginInstallPaths.FilePresent(path)) return true;
        if (PluginInstallPaths.FilePresent(path + ".previous")) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装记录丢失，保留历史文件供恢复。");
        return false;
    }

    private static void Validate(PluginInstallationIndex index)
    {
        if (index.SchemaVersion != 1 || index.Plugins is null || index.Plugins.Length > 1000 ||
            index.Plugins.Any(item => item is null) || index.Plugins.Select(item => item.PluginId).Distinct().Count() != index.Plugins.Length ||
            index.Plugins.Select(item => item.DirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Plugins.Length)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装登记格式或身份无效。");
        foreach (var entry in index.Plugins) Validate(entry);
    }

    private static void Validate(PluginInstalledRecord record)
    {
        Identity(record.PluginId, record.Version, record.ArtifactHash);
        PluginInstallPaths.Segment(record.DirectoryName);
        if (record.ArchiveHash is not null && !PluginInstallJson.Hash(record.ArchiveHash) ||
            record.Validation is not ("startupConfirmed" or "installedDisabled" or "adopted"))
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装登记状态无效。");
        if ((record.BackupOperationId is null) != (record.BackupVersion is null) ||
            (record.BackupOperationId is null) != (record.BackupHash is null)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "恢复版本登记不完整。");
        if (record.BackupOperationId is not null)
        {
            PluginInstallPaths.OperationId(record.BackupOperationId);
            if (!VersionText(record.BackupVersion) || !PluginInstallJson.Hash(record.BackupHash)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "恢复版本身份无效。");
        }
    }

    private static void Validate(PluginInstallOperation operation)
    {
        Identity(operation.PluginId, operation.Version, operation.ArtifactHash);
        PluginInstallPaths.OperationId(operation.OperationId); PluginInstallPaths.Segment(operation.TargetDirectory);
        if (operation.SchemaVersion != 1 || !Enum.IsDefined(operation.Action) || operation.Action == PluginInstallAction.Unchanged ||
            !Enum.IsDefined(operation.Phase) || (operation.PreviousHash is null) != (operation.PreviousVersion is null) ||
            operation.PreviousHash is not null && (!PluginInstallJson.Hash(operation.PreviousHash) || !VersionText(operation.PreviousVersion)) ||
            operation.ArchiveHash is not null && !PluginInstallJson.Hash(operation.ArchiveHash) ||
            (operation.OwnerPid is null) != (operation.OwnerStartedUtcTicks is null) ||
            operation.OwnerPid is <= 0 || operation.OwnerStartedUtcTicks is <= 0 ||
            operation.Phase == PluginInstallPhase.AwaitingStartup && operation.OwnerPid is null ||
            operation.Action == PluginInstallAction.Install && operation.PreviousHash is not null ||
            operation.Action != PluginInstallAction.Install && operation.PreviousHash is null ||
            operation.Result?.Length > 200)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装操作日志无效。");
        if (operation.PreviousRecord is { } previous)
        {
            Validate(previous);
            if (previous.PluginId != operation.PluginId || previous.DirectoryName != operation.TargetDirectory ||
                previous.Version != operation.PreviousVersion || previous.ArtifactHash != operation.PreviousHash)
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装日志的旧登记与基线不一致。");
        }
    }

    private static void Identity(string id, string version, string hash)
    {
        if (!PluginId.TryParse(id, out _) || !id.StartsWith("myavalonia.plugin.", StringComparison.Ordinal) ||
            !VersionText(version) || !PluginInstallJson.Hash(hash)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装记录身份无效。");
    }
    private static bool VersionText(string? value) => value is not null &&
        System.Text.RegularExpressions.Regex.IsMatch(value, @"\A\d+\.\d+\.\d+\z") && Version.TryParse(value, out _);
}
