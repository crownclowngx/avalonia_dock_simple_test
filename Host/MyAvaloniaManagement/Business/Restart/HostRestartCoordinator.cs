using System;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Plugins.Enablement;

namespace MyAvaloniaManagement.Business.Restart;

/// <summary>主菜单与看板共用的窄用例；调用者只能请求，不能直接拉起进程或跳过关闭确认。</summary>
internal interface IHostRestartActions
{
    bool CanRequest { get; }
    bool IsRequested { get; }
    string Message { get; }
    event EventHandler? Changed;
    void Request();
}

/// <summary>UI 线程上的单次重启协调器。窗口拥有关闭许可，设置服务拥有写入，进程入口拥有交接。</summary>
/// <remarks>不在这里 Dispose Runtime。窗口否决时可撤销；窗口关闭后只允许进程入口完成交接。</remarks>
internal sealed class HostRestartCoordinator(IHostRestartHandoff? handoff,
    IPluginEnablementRestartBarrier? settings, Action<string> report) : IHostRestartActions, IDisposable
{
    private Action? _close;
    private Func<bool>? _canClose;
    private IDisposable? _settingsLease;
    private CancellationTokenSource? _preparing;
    private bool _windowClosed, _disposed;
    public bool CanRequest => !_disposed && !_windowClosed && !IsRequested && handoff is not null && _canClose?.Invoke() == true;
    public bool IsRequested { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public event EventHandler? Changed;

    internal void Attach(Action close, Func<bool> canClose) { _close = close; _canClose = canClose; Notify(); }
    internal void RefreshAvailability() => Notify();

    public void Request()
    {
        if (!CanRequest) return;
        try { handoff!.Validate(); }
        catch (Exception) { Fail("无法确认当前 Host 启动信息，自动重启未开始。"); return; }
        IsRequested = true; Message = "正在准备重启…"; Notify();
        try { _close!(); }
        catch (Exception) { Fail("无法开始关闭工作区，自动重启已取消。"); }
    }

    /// <summary>在文档确认前冻结设置；等待超时可撤销，但不取消服务已经接受的保存操作。</summary>
    internal async Task<bool> BeginPreparationAsync()
    {
        if (!IsRequested) return true;
        _preparing = new CancellationTokenSource(RestartProtocol.HandshakeTimeout);
        try
        {
            if (settings is not null) _settingsLease = await settings.PauseForRestartAsync(_preparing.Token);
            return IsRequested;
        }
        catch (Exception) { Fail("插件设置尚未完成保存，自动重启已取消；请确认设置后重试。"); return false; }
        finally { _preparing?.Dispose(); _preparing = null; }
    }

    /// <summary>文档及布局准备全部通过后才启动助手，Ready 失败仍可保留原窗口及实例。</summary>
    internal async Task<bool> PrepareHandoffAsync()
    {
        if (!IsRequested) return true;
        try { await handoff!.PrepareAsync(); return IsRequested; }
        catch (Exception) { Fail("重启助手未能就绪，工作区保持打开，请重试。"); return false; }
    }

    internal void WindowClosed()
    {
        _windowClosed = true;
        if (IsRequested) handoff!.ConfirmWindowClosed();
        Notify();
    }

    internal void Cancel()
    {
        if (_windowClosed) return;
        _preparing?.Cancel(); handoff?.Abort();
        _settingsLease?.Dispose(); _settingsLease = null;
        IsRequested = false; Message = string.Empty; Notify();
    }

    private void Fail(string message) { Cancel(); Message = message; report(message); Notify(); }
    private void Notify()
    {
        foreach (EventHandler observer in Changed?.GetInvocationList() ?? [])
            try { observer(this, EventArgs.Empty); } catch { /* 展示订阅失败不能改变交接许可。 */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_windowClosed) Cancel();
        _close = null; _canClose = null; Changed = null;
        // 已提交退出时保持设置冻结直到旧进程结束，不在 Provider 释放中重新开放写入。
    }
}
