using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Plugins.Enablement;

/// <summary>状态查询只取得内存中的已保存意图，不触发磁盘读取或插件加载。</summary>
internal interface IPluginEnablementState
{
    PluginEnablementReadResult Current { get; }
}

/// <summary>看板的窄写入端口。修改仅影响下次启动，没有生命周期或加载能力。</summary>
internal interface IPluginEnablementActions : IPluginEnablementState
{
    Task<PluginEnablementSaveResult> SetEnabledAsync(PluginId id, bool enabled);
    Task ReloadAsync();
}

/// <summary>Runtime 拥有的用户意图服务，串行写入并在提交成功后原子发布内存快照。</summary>
/// <remarks>
/// 文件 I/O 在线程池执行；服务不持有窗口。已接受的保存不随窗口关闭取消，防止落盘完成后
/// 窗口误认为设置被撤销。窗口负责忽略迟到展示结果；本服务从不修改启动快照或插件实例。
/// </remarks>
internal sealed class PluginEnablementService(
    PluginEnablementSettingsStore store,
    PluginEnablementReadResult initial,
    IEnumerable<PluginId> knownIds,
    IHostDiagnosticSink? diagnostics = null) : IPluginEnablementActions
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly FrozenSet<PluginId> _knownIds = knownIds.ToFrozenSet();
    private PluginEnablementReadResult _current = initial;
    public PluginEnablementReadResult Current => Volatile.Read(ref _current);

    public async Task<PluginEnablementSaveResult> SetEnabledAsync(PluginId id, bool enabled)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            var current = Current;
            if (!_knownIds.Contains(id)) return new(false, current, "PLUGIN_ENABLEMENT_UNKNOWN_PLUGIN");
            if (!current.CanWrite) return new(false, current, "PLUGIN_ENABLEMENT_READ_ONLY");
            var result = await Task.Run(() => store.TrySave(current, current.Settings!.WithEnabled(id, enabled))).ConfigureAwait(false);
            if (result.Success) Volatile.Write(ref _current, result.Snapshot);
            else diagnostics?.Report(new(result.ErrorCode!, HostDiagnosticPhase.PluginRootDiscovery));
            return result;
        }
        finally { _writes.Release(); }
    }

    /// <summary>显式读取外部新设置以解决写冲突；只更改下次意图，本次启动策略仍由发现快照拥有。</summary>
    public async Task ReloadAsync()
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try { Volatile.Write(ref _current, await Task.Run(store.Load).ConfigureAwait(false)); }
        finally { _writes.Release(); }
    }
}
