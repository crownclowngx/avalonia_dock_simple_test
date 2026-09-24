using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.Plugins.Enablement;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>看板只得到安装用例与值快照；无启动、加载或直接文件提交权限。</summary>
internal interface IPluginInstallationActions
{
    PluginInstallationStatus Status { get; }
    PluginInstallPreview? Preview { get; }
    Task InspectAsync(string zip, string? sidecar);
    void CancelInspection();
    Task CommitAsync(bool explicitReplacement);
    Task CancelPendingAsync();
    Task PrepareRestoreAsync(string pluginId);
    Task RefreshAsync();
}

internal interface IPluginInstallationRestartBarrier
{
    Task<IDisposable> PauseForRestartAsync(CancellationToken token);
}

/// <summary>
/// 安装任务由 Host 会话拥有，关闭看板不会取消已接受的提交。串行入口和短时磁盘操作锁
/// 分别防止本实例重入与跨实例丢失更新；耗时检查在后台进行，不长期占用跨进程操作锁。
/// </summary>
internal sealed class PluginInstallationService(PluginInstallPaths paths, PluginInstallationStore store)
    : IPluginInstallationActions, IPluginInstallationRestartBarrier
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    // 复用既有“冻结准入并排空”实现，不复制一套计数与取消状态机；它不读取启用配置。
    private readonly PluginEnablementOperationGate _admission = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _checkingLock = new();
    private CancellationTokenSource? _checking;
    private volatile bool _stopped;
    private PluginInstallationStatus _status = new(null, "请选择本地 ZIP 检查安装或更新。");
    private PluginInstallPreview? _preview;
    public PluginInstallationStatus Status => Volatile.Read(ref _status);
    public PluginInstallPreview? Preview => Volatile.Read(ref _preview);

    public Task InspectAsync(string zip, string? sidecar) => ExecuteAsync(async () =>
    {
        DiscardPreview();
        using var checking = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        lock (_checkingLock) _checking = checking;
        try
        {
            var package = await new PluginPackageInspector(paths).InspectAsync(zip, sidecar, checking.Token).ConfigureAwait(false);
            try
            {
                using var write = await PluginInstallationLease.OperationsAsync(paths, checking.Token).ConfigureAwait(false);
                AssertNoPending();
                var inventory = await PluginInstallInventory.ReadAsync(paths, store.ReadIndex(), checking.Token).ConfigureAwait(false);
                Volatile.Write(ref _preview, new(package, PluginInstallPlanner.Plan(package, inventory)));
            }
            catch { paths.DeleteStage(package.OperationId); throw; }
        }
        finally { lock (_checkingLock) _checking = null; }
    });

    public void CancelInspection()
    {
        lock (_checkingLock) _checking?.Cancel();
    }

    public Task CommitAsync(bool explicitReplacement) => ExecuteAsync(async () =>
    {
        var preview = Preview ?? throw new PluginInstallException("PLUGIN_INSTALL_UNAVAILABLE", "请先检查安装包。");
        if (preview.Plan.Action == PluginInstallAction.Unchanged) throw new PluginInstallException("PLUGIN_INSTALL_UNAVAILABLE", "当前已安装相同内容。");
        if (preview.Plan.RequiresExplicitChoice && !explicitReplacement) throw new PluginInstallException("PLUGIN_INSTALL_UNAVAILABLE", "重装或降级需要明确选择。");
        var candidate = await PluginPayloadValidator.ValidateAsync(preview.Package.PayloadPath, _lifetime.Token).ConfigureAwait(false);
        if (candidate.Artifact.Sha256 != preview.Package.ArtifactHash) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "候选内容已变化，请重新检查。");
        if (preview.Plan.Action != PluginInstallAction.Restore &&
            await PluginPackageInspector.HashAsync(Path.Combine(paths.Stage(preview.Package.OperationId), "package.zip"), _lifetime.Token).ConfigureAwait(false) != preview.Package.ArchiveHash)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "候选 ZIP 已变化，请重新检查。");
        using var write = await PluginInstallationLease.OperationsAsync(paths, _lifetime.Token).ConfigureAwait(false);
        AssertNoPending();
        var index = store.ReadIndex();
        var inventory = await PluginInstallInventory.ReadAsync(paths, index, _lifetime.Token).ConfigureAwait(false);
        var plan = PluginInstallPlanner.Plan(preview.Package, inventory);
        // 预览之后的任何磁盘变化都要求用户重新审阅，不能把旧许可套到新基线上。
        if (plan.TargetDirectory != preview.Plan.TargetDirectory || plan.PreviousHash != preview.Plan.PreviousHash ||
            plan.PreviousVersion != preview.Plan.PreviousVersion ||
            preview.Plan.Action != PluginInstallAction.Restore && plan.Action != preview.Plan.Action)
            throw new PluginInstallException("PLUGIN_INSTALL_CONFLICT", "磁盘版本或内容已变化，请重新检查安装包。");
        var operation = new PluginInstallOperation
        {
            SchemaVersion = 1, OperationId = preview.Package.OperationId, Action = preview.Plan.Action,
            Phase = PluginInstallPhase.Staged, PluginId = preview.Package.Manifest.PluginId.Value,
            TargetDirectory = plan.TargetDirectory, PreviousVersion = plan.PreviousVersion, PreviousHash = plan.PreviousHash,
            Version = preview.Package.Manifest.PluginVersion.ToString(3), ArtifactHash = preview.Package.ArtifactHash,
            ArchiveHash = preview.Plan.Action == PluginInstallAction.Restore ? null : preview.Package.ArchiveHash,
            HasReleaseManifest = preview.Package.HasReleaseManifest,
            PreviousRecord = index.Plugins.SingleOrDefault(item => item.PluginId == preview.Package.Manifest.PluginId.Value),
            OwnerPid = null, OwnerStartedUtcTicks = null, Result = null
        };
        store.WriteOperation(operation);
        Volatile.Write(ref _preview, null); // 已提交暂存归日志所有，后续窗口/预览清理不能删除它。
        Publish(operation);
    });

    public Task CancelPendingAsync() => ExecuteAsync(async () =>
    {
        using var write = await PluginInstallationLease.OperationsAsync(paths, _lifetime.Token).ConfigureAwait(false);
        var operation = store.ReadOperation();
        if (operation?.Phase != PluginInstallPhase.Staged) throw new PluginInstallException("PLUGIN_INSTALL_UNAVAILABLE", "当前操作不能取消。");
        var cancelled = operation with { Phase = PluginInstallPhase.Cancelled, Result = "已取消待应用安装，当前插件未改变。" };
        store.WriteOperation(cancelled);
        Publish(cancelled);
        try { paths.DeleteStage(operation.OperationId); }
        catch (IOException) { Publish(cancelled, "待办已取消；暂存清理未完成，不影响当前版本。"); }
        catch (UnauthorizedAccessException) { Publish(cancelled, "待办已取消；暂存清理未完成，不影响当前版本。"); }
    });

    /// <summary>恢复同样先形成预览。备份身份来自登记，不能仅扫描目录猜哪个版本可以恢复。</summary>
    public Task PrepareRestoreAsync(string pluginId) => ExecuteAsync(async () =>
    {
        DiscardPreview();
        using var write = await PluginInstallationLease.OperationsAsync(paths, _lifetime.Token).ConfigureAwait(false);
        AssertNoPending();
        var index = store.ReadIndex();
        var entry = index.Plugins.SingleOrDefault(item => item.PluginId == pluginId);
        if (entry?.BackupOperationId is null) throw new PluginInstallException("PLUGIN_INSTALL_UNAVAILABLE", "该插件没有可恢复的上一版本。");
        var source = paths.Backup(entry.BackupOperationId);
        var facts = await PluginPayloadValidator.ValidateAsync(source, _lifetime.Token).ConfigureAwait(false);
        if (facts.Artifact.Sha256 != entry.BackupHash || facts.Manifest.PluginId.Value != pluginId || facts.Manifest.PluginVersion.ToString(3) != entry.BackupVersion)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "恢复备份已变化。");
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var payload = paths.Payload(id);
            Directory.CreateDirectory(payload);
            foreach (var file in facts.Artifact.Files)
            {
                var destination = PluginInstallPaths.Under(payload, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await PluginPackageInspector.CopySnapshotAsync(PluginInstallPaths.Under(source, file.Path), destination,
                    file.Length, _lifetime.Token).ConfigureAwait(false);
            }
            if ((await ArtifactFingerprint.CaptureDirectoryAsync(payload, _lifetime.Token).ConfigureAwait(false)).Sha256 != entry.BackupHash)
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "复制恢复备份时内容发生变化。");
            var package = new CheckedPluginPackage(id, entry.DirectoryName, payload, facts.Manifest, string.Empty, facts.Artifact.Sha256, false);
            var inventory = await PluginInstallInventory.ReadAsync(paths, index, _lifetime.Token).ConfigureAwait(false);
            var plan = PluginInstallPlanner.Plan(package, inventory);
            Volatile.Write(ref _preview, new(package, plan with { Action = PluginInstallAction.Restore, RequiresExplicitChoice = true }));
        }
        catch { paths.DeleteStage(id); throw; }
    });

    public Task RefreshAsync() => ExecuteAsync(async () =>
    {
        paths.Ensure();
        using var write = await PluginInstallationLease.OperationsAsync(paths, _lifetime.Token).ConfigureAwait(false);
        var actual = store.ReadOperation();
        // 同一待办的启动阻断提示不能被看板初次刷新抹掉；换操作或阶段后再使用新的事实文案。
        Publish(actual, actual?.OperationId == Status.Operation?.OperationId && actual?.Phase == PluginInstallPhase.Staged ? Status.Message : null);
    });

    internal void Publish(PluginInstallOperation? operation, string? message = null) => Volatile.Write(ref _status,
        new(operation, message ?? (operation?.Phase switch
        {
            PluginInstallPhase.Staged => $"{operation.PluginId}：{operation.PreviousVersion ?? "未安装"} → {operation.Version}，待重启应用。",
            PluginInstallPhase.AwaitingStartup => "文件已就位，正在等待本次启动确认。",
            PluginInstallPhase.RecoveryRequired => "新版本未确认，下次启动将恢复原版本。",
            _ => operation?.Result ?? "请选择本地 ZIP 检查安装或更新。"
        })));

    public Task<IDisposable> PauseForRestartAsync(CancellationToken token)
    {
        CancelInspection();
        return _admission.PauseForRestartAsync(token);
    }

    internal async Task StopAsync()
    {
        _stopped = true;
        _lifetime.Cancel(); CancelInspection();
        await _serial.WaitAsync().ConfigureAwait(false);
        try { DiscardPreview(); }
        finally { _serial.Release(); }
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_stopped || !_admission.TryEnter()) throw new PluginInstallException("PLUGIN_INSTALL_UNAVAILABLE", "Host 正在退出或准备重启，暂不能修改安装。");
        var success = false;
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            await Task.Run(action).ConfigureAwait(false); success = true;
        }
        catch (OperationCanceledException) { success = true; throw; }
        finally { _serial.Release(); _admission.Exit(success); }
    }

    private void AssertNoPending()
    {
        if (store.ReadOperation()?.IsPending == true) throw new PluginInstallException("PLUGIN_INSTALL_UNAVAILABLE", "已有待应用或待恢复操作，请先完成或取消。");
    }

    private void DiscardPreview()
    {
        var previous = Interlocked.Exchange(ref _preview, null);
        if (previous is null) return;
        // 提交可能已经落盘却在返回前失败。只要日志引用该暂存，就由事务接管，不能因窗口/服务收尾删除它。
        try { if (store.ReadOperation() is { IsPending: true } operation && operation.OperationId == previous.Package.OperationId) return; }
        catch (Exception) { return; } // 无法确认归属时保留现场。
        paths.DeleteStage(previous.Package.OperationId);
    }
}
