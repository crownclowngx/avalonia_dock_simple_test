using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>
/// 管理一个宿主窗口内容覆盖层的唯一活动展示和恢复租约。
/// </summary>
/// <remarks>
/// 本类型只维护 Avalonia 视觉内容的临时迁移，不知道 Document、Dock 或播放器。调用方仍然拥有传入控件，
/// 而本会话只拥有“何时把控件从覆盖层移除”这一恢复责任。会话是具体的 Host internal 协作者；当前只有
/// 一个生产实现，因此不额外建立没有替换需求的内部接口。
/// </remarks>
internal sealed class WindowContentFullscreenSession : IDisposable
{
    private readonly Border _layer;
    private readonly ContentControl _contentHost;
    private PresentationLease? _activeLease;
    private bool _unavailable;

    internal WindowContentFullscreenSession(
        Border layer,
        ContentControl contentHost)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _contentHost = contentHost ?? throw new ArgumentNullException(nameof(contentHost));
        _contentHost.DetachedFromVisualTree += OnContentHostDetached;
    }

    /// <summary>尝试发布一个新的独占展示租约。</summary>
    internal IDisposable? TryPresent(Control content)
    {
        EnsureUiThread();
        ArgumentNullException.ThrowIfNull(content);

        // ContentHost 非空而没有活动租约意味着视觉状态已经被外部代码破坏。此时同样拒绝覆盖，
        // 避免通过“顺手清空”隐藏真正的所有权错误。
        if (_unavailable || _activeLease is not null || _contentHost.Content is not null)
        {
            return null;
        }

        var lease = new PresentationLease(this);
        try
        {
            // 先完成可能因已有视觉父级而失败的内容挂载，最后才发布活动租约。这样调用方永远
            // 不会拿到一个对应视觉状态并未成功建立的令牌。
            _contentHost.Content = content;
            _layer.IsVisible = true;
            _activeLease = lease;
            return lease;
        }
        catch
        {
            // 挂载失败不能占用全屏槽位。清空 Content 和隐藏覆盖层使下一次合法请求仍可成功；
            // 原异常继续向上传播，由插件边界转换为不包含敏感正文的稳定业务失败。
            _contentHost.Content = null;
            _layer.IsVisible = false;
            throw;
        }
    }

    private void Release(PresentationLease lease)
    {
        EnsureUiThread();
        if (!lease.TryInvalidate())
        {
            return;
        }

        // 使用租约引用身份而不是内容引用，防止已经释放的旧租约在新一轮展示建立后产生 ABA 误释放。
        if (!ReferenceEquals(_activeLease, lease))
        {
            return;
        }

        _activeLease = null;
        RestoreVisualState();
    }

    private void RestoreVisualState()
    {
        try
        {
            _contentHost.Content = null;
        }
        finally
        {
            // 即使自定义控件在脱离逻辑树时抛出，覆盖层也不能继续拦截窗口输入。
            _layer.IsVisible = false;
        }
    }

    private void OnContentHostDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // ContentHost 脱离窗口视觉树后不可能继续履行展示契约。自动失效先切断租约回调，
        // 再恢复视觉状态，因此插件控件的 Detached 回调即使同步释放同一租约也只会成为无操作。
        Dispose();
    }

    public void Dispose()
    {
        EnsureUiThread();
        if (_unavailable)
        {
            return;
        }

        _unavailable = true;
        _contentHost.DetachedFromVisualTree -= OnContentHostDetached;
        var lease = _activeLease;
        _activeLease = null;
        lease?.TryInvalidate();
        RestoreVisualState();
    }

    private void EnsureUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                "窗口内容区全屏租约只能在 Avalonia UI 线程创建或首次释放。");
        }
    }

    /// <summary>
    /// 把一次成功展示的恢复权限封装为不透明令牌。
    /// </summary>
    private sealed class PresentationLease(WindowContentFullscreenSession owner) : IDisposable
    {
        private int _invalidated;

        public void Dispose()
        {
            // 已失效租约在任意线程重复 Dispose 都是无操作；有效租约则先由会话检查 UI 线程，
            // 检查失败不会修改 _invalidated，调用方仍能回到 UI 线程完成正确释放。
            if (Volatile.Read(ref _invalidated) != 0)
            {
                return;
            }

            owner.Release(this);
        }

        internal bool TryInvalidate() =>
            Interlocked.Exchange(ref _invalidated, 1) == 0;
    }
}
