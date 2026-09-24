using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>
/// 进程入口拥有的安装会话。应用发生在发现之前，租约跨越 Runtime 关闭；
/// 共享转独占从不在运行中进行，独占转共享之后重新核对日志以封住竞争窗口。
/// </summary>
internal sealed class PluginInstallationSession : IDisposable
{
    private FileStream? _runtime;
    internal PluginInstallPaths Paths { get; }
    internal PluginInstallationStore Store { get; }
    internal PluginInstallationService Service { get; }
    internal PluginInstallApplier Applier { get; }

    internal PluginInstallationSession(string pluginsRoot)
    {
        Paths = new(pluginsRoot); Store = new(Paths); Applier = new(Paths, Store);
        Service = new(Paths, Store);
    }

    internal async Task PrepareAsync(CancellationToken token)
    {
        Paths.Ensure();
        FileStream? exclusive = null;
        try { exclusive = PluginInstallationLease.Runtime(Paths, true); }
        catch (IOException) { /* 其他实例可能仍运行；共享取得之后才能读取稳定日志。 */ }
        string? notice = null;
        if (exclusive is not null)
        {
            using (exclusive)
            using (await PluginInstallationLease.OperationsAsync(Paths, token).ConfigureAwait(false))
            {
                var operation = Store.ReadOperation();
                if (operation?.Phase is PluginInstallPhase.Applying or PluginInstallPhase.AwaitingStartup or PluginInstallPhase.RecoveryRequired)
                {
                    if (PluginInstallApplier.OwnerAlive(operation)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "另一个进程尚在验证安装，请先退出该实例。");
                    await Applier.RecoverAsync(operation, token).ConfigureAwait(false);
                }
                else if (operation?.Phase == PluginInstallPhase.Staged)
                {
                    try { await Applier.ApplyAsync(operation, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        var actual = Store.ReadOperation();
                        if (actual?.Phase == PluginInstallPhase.Applying)
                            await Applier.RecoverAsync(actual, CancellationToken.None).ConfigureAwait(false);
                        else if (actual?.Phase == PluginInstallPhase.Staged)
                            notice = "待安装包或原插件检查失败，当前版本保持不变；请在插件看板取消待办后重新检查。";
                        else throw;
                    }
                }
            }
        }
        _runtime = PluginInstallationLease.Runtime(Paths, false);
        try
        {
            using var write = await PluginInstallationLease.OperationsAsync(Paths, token).ConfigureAwait(false);
            var operation = Store.ReadOperation();
            if (operation?.Phase is PluginInstallPhase.Applying or PluginInstallPhase.RecoveryRequired ||
                operation?.Phase == PluginInstallPhase.AwaitingStartup && !PluginInstallApplier.IsCurrentOwner(operation))
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装尚待恢复或正在由其他进程验证，请退出其他实例后重新启动。");
            if (operation?.Phase == PluginInstallPhase.Staged && notice is null)
                notice = "另一个 Host 正在使用插件文件；请退出其他实例后重启应用待办。";
            Service.Publish(operation, notice);
        }
        catch { _runtime.Dispose(); _runtime = null; throw; }
    }

    /// <summary>
    /// 一旦准备执行插件代码，租约交给真正的进程退出事件。即使 Runtime 释放失败或仍有原生线程，
    /// Program 返回前的 Dispose 也不能提前允许另一个 Host 替换这些文件。
    /// </summary>
    internal void RetainRuntimeUntilProcessExit()
    {
        var lease = _runtime ?? throw new InvalidOperationException("插件运行租约尚未取得。");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => lease.Dispose();
        _runtime = null;
    }

    public void Dispose()
    {
        Service.StopAsync().GetAwaiter().GetResult();
        _runtime?.Dispose(); _runtime = null;
    }
}
