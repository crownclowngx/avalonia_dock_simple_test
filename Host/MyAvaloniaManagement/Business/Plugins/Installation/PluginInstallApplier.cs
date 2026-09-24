using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Compatibility;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>
/// 只在启动前持有独占租约时应用或恢复。目录移动本身可原子，但前后两次移动并不是事务；
/// 因此先提交 Applying，依靠前后摘要和备份恢复任何中断点，不依赖后一次日志一定写成功。
/// </summary>
internal sealed class PluginInstallApplier(PluginInstallPaths paths, PluginInstallationStore store,
    IPluginInstallFileCommit? files = null)
{
    private readonly IPluginInstallFileCommit _files = files ?? new PluginInstallFileCommit();

    internal async Task ApplyAsync(PluginInstallOperation operation, CancellationToken token)
    {
        if (operation.Phase != PluginInstallPhase.Staged) throw new InvalidOperationException("操作尚未准备好。");
        var target = paths.Target(operation.TargetDirectory);
        var current = await HashDirectoryAsync(target, token).ConfigureAwait(false);
        if (current != operation.PreviousHash) throw new PluginInstallException("PLUGIN_INSTALL_CONFLICT", "原插件内容已变化，安装尚未应用。");
        var payload = paths.Payload(operation.OperationId);
        var candidate = await PluginPayloadValidator.ValidateAsync(payload, token).ConfigureAwait(false);
        if (candidate.Manifest.PluginId.Value != operation.PluginId || candidate.Manifest.PluginVersion.ToString(3) != operation.Version ||
            candidate.Artifact.Sha256 != operation.ArtifactHash)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "待安装内容与已确认操作不一致。");
        if (operation.ArchiveHash is not null && operation.Action != PluginInstallAction.Restore &&
            await PluginPackageInspector.HashAsync(Path.Combine(paths.Stage(operation.OperationId), "package.zip"), token).ConfigureAwait(false) != operation.ArchiveHash)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "暂存 ZIP 已变化。");
        // 整根复查同时保护兄弟插件和人工部署身份；不能仅检查本次目标再制造重复 ID。
        var inventory = await PluginInstallInventory.ReadAsync(paths, store.ReadIndex(), token).ConfigureAwait(false);
        if (inventory.Count(item => item.Manifest.PluginId.Value == operation.PluginId) != (operation.PreviousHash is null ? 0 : 1))
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装根插件身份存在冲突。");
        token.ThrowIfCancellationRequested();
        var applying = operation with { Phase = PluginInstallPhase.Applying };
        store.WriteOperation(applying);
        // 从此处开始不再中途接受普通取消。错误交由恢复处理，不能留下一半替换后继续加载。
        if (operation.PreviousHash is not null) _files.MoveDirectory(target, paths.Backup(operation.OperationId));
        _files.MoveDirectory(payload, target);
        using var process = Process.GetCurrentProcess();
        store.WriteOperation(applying with
        {
            Phase = PluginInstallPhase.AwaitingStartup,
            OwnerPid = process.Id, OwnerStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks
        });
    }

    /// <summary>每个恢复步骤都允许重复。未知内容保留并拒绝覆盖，不删除“看起来像旧版”的目录。</summary>
    internal async Task RecoverAsync(PluginInstallOperation operation, CancellationToken token)
    {
        if (operation.Phase is not (PluginInstallPhase.Applying or PluginInstallPhase.AwaitingStartup or PluginInstallPhase.RecoveryRequired))
            throw new InvalidOperationException("当前阶段不需要恢复。");
        var recovering = operation with { Phase = PluginInstallPhase.RecoveryRequired };
        store.WriteOperation(recovering);
        var target = paths.Target(operation.TargetDirectory);
        var backup = paths.Backup(operation.OperationId);
        var current = await HashDirectoryAsync(target, token).ConfigureAwait(false);
        var saved = await HashDirectoryAsync(backup, token).ConfigureAwait(false);
        if (saved is not null && saved != operation.PreviousHash)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "恢复备份与原版本摘要不一致。");
        if (current != operation.PreviousHash)
        {
            if (current is not null)
            {
                if (current != operation.ArtifactHash) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "活动目录存在无法确认的内容，自动恢复已停止。");
                var failed = paths.Owned(Path.Combine(paths.Stage(operation.OperationId), "failed-payload"));
                _files.MoveDirectory(target, failed);
            }
            if (operation.PreviousHash is not null)
            {
                if (saved is null) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "原版本备份缺失，自动恢复已停止。");
                _files.MoveDirectory(backup, target);
            }
        }
        // 新装失败的 previousHash 为 null；活动目录若也是 null，已经回到安装前状态。
        var index = store.ReadIndex();
        var entries = index.Plugins.Where(item => item.PluginId != operation.PluginId).ToList();
        if (operation.PreviousRecord is { } previous) entries.Add(previous);
        store.WriteIndex(index with { Plugins = entries.ToArray() });
        store.WriteOperation(recovering with { Phase = PluginInstallPhase.RolledBack, Result = "安装未确认，已恢复安装前的插件文件。" });
    }

    /// <summary>主窗口真正交接后调用。此时只有共享运行租约，允许写确认元数据，绝不再移动 DLL。</summary>
    internal async Task ConfirmAsync(bool enabled, bool available, CancellationToken token)
    {
        using var write = await PluginInstallationLease.OperationsAsync(paths, token).ConfigureAwait(false);
        var operation = store.ReadOperation();
        if (operation?.Phase != PluginInstallPhase.AwaitingStartup) return;
        if (!IsCurrentOwner(operation)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "试启动不属于当前进程。");
        if (enabled && !available)
        {
            store.WriteOperation(operation with { Phase = PluginInstallPhase.RecoveryRequired, Result = "新版本加载或初始化失败，下次启动恢复原版本。" });
            return;
        }
        if (await HashDirectoryAsync(paths.Target(operation.TargetDirectory), token).ConfigureAwait(false) != operation.ArtifactHash)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "启动期间插件文件发生变化，未确认安装。");
        var index = store.ReadIndex();
        var entries = index.Plugins.Where(item => item.PluginId != operation.PluginId).ToList();
        entries.Add(new()
        {
            PluginId = operation.PluginId, DirectoryName = operation.TargetDirectory, Version = operation.Version,
            ArtifactHash = operation.ArtifactHash, ArchiveHash = operation.ArchiveHash, HasReleaseManifest = operation.HasReleaseManifest,
            Validation = enabled ? "startupConfirmed" : "installedDisabled",
            BackupOperationId = operation.PreviousHash is null ? null : operation.OperationId,
            BackupVersion = operation.PreviousVersion, BackupHash = operation.PreviousHash
        });
        store.WriteIndex(index with { Plugins = entries.ToArray() });
        // 最终日志是提交标志。这里失败时，下次启动仍按未确认处理并恢复旧登记。
        store.WriteOperation(operation with { Phase = PluginInstallPhase.Committed,
            Result = enabled ? "安装完成，新版本已运行。" : "安装完成；插件已禁用，尚未验证运行。" });
    }

    internal static bool IsCurrentOwner(PluginInstallOperation operation)
    {
        using var current = Process.GetCurrentProcess();
        return operation.OwnerPid == current.Id && operation.OwnerStartedUtcTicks == current.StartTime.ToUniversalTime().Ticks;
    }

    internal static bool OwnerAlive(PluginInstallOperation operation)
    {
        if (operation.OwnerPid is null) return false;
        try
        {
            using var process = Process.GetProcessById(operation.OwnerPid.Value);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == operation.OwnerStartedUtcTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        // 无法检查其他用户进程时保守保留，不能把访问失败解释成进程已退出。
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    internal static async Task<string?> HashDirectoryAsync(string path, CancellationToken token)
    {
        PluginInstallPaths.AssertNoLinks(path);
        if (File.Exists(path)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件目标被文件占用。");
        return Directory.Exists(path) ? (await ArtifactFingerprint.CaptureDirectoryAsync(path, token).ConfigureAwait(false)).Sha256 : null;
    }
}
