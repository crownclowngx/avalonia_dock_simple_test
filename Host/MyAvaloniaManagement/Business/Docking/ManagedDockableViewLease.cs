using System;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>
/// 表示一个已经由统一 View Locator 构造、但仍由 Host Adapter 拥有的 View。
/// </summary>
/// <remarks>
/// Dock 的回收器、正常关闭和 Runtime 退出可能重复汇合到释放入口。本租约用幂等门禁确保
/// DataContext 只断开一次、View 最多释放一次，从而避免把控件回收细节扩散到 Document/Tool 模型。
/// </remarks>
internal sealed class ManagedDockableViewLease
{
    private Control? _view;
    private bool _released;

    internal Control? View => _view;

    internal void Attach(Control view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_released)
        {
            throw new ObjectDisposedException(nameof(ManagedDockableViewLease));
        }

        if (_view is not null)
        {
            throw new InvalidOperationException("同一个 Dock Adapter 不能准备多个 View。");
        }

        _view = view;
    }

    internal void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        var view = _view;
        _view = null;
        if (view is null)
        {
            return;
        }

        // 必须先断开绑定再释放控件。否则 View 中的 Binding、事件或原生资源可能继续持有
        // 已经结束的插件 Scope，造成关闭后仍能观察到模型的迟到回调。
        try
        {
            view.DataContext = null;
        }
        finally
        {
            // 自定义 Avalonia 属性通知可能在清空 DataContext 时抛出；显式 View 资源仍需释放。
            (view as IDisposable)?.Dispose();
        }
    }
}

/// <summary>统一 View Locator 识别 Host internal Dock Adapter 的最小端口。</summary>
internal interface IManagedDockableViewHost
{
    object Model { get; }
    IWorkspaceViewRegistration ViewRegistration { get; }
    Control? PreparedView { get; }
    void AttachPreparedView(Control view);
    void ReleasePreparedView();
}
