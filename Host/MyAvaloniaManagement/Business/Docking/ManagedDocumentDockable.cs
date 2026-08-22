using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>
/// 把普通插件 Document 模型投影为 Dock Document 的唯一 Host internal Adapter。
/// </summary>
/// <remarks>
/// Adapter 只拥有 Dock 状态、预构建 View 和 Document Scope 租约。插件模型不知道 Dock，
/// Dock 也不会取得插件 Provider；两侧只在这个可审阅边界相遇。
/// </remarks>
internal sealed class ManagedDocumentDockable : Document, IManagedDockableViewHost, IDisposable
{
    private readonly ActivatedPluginDocument _activation;
    private readonly ManagedDockableViewLease _view = new();
    private string _hostTitle;
    private bool _hasCommittedHostTitle;
    private bool _hostRequiresSave;
    private int _disposed;

    internal ManagedDocumentDockable(
        ActivatedPluginDocument activation,
        string requestedTitle)
    {
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _hostTitle = requestedTitle ?? throw new ArgumentNullException(nameof(requestedTitle));
        Context = activation.Model;
        CanClose = true;
        CanPin = false;
        CanFloat = false;
        Title = SelectTitle();
        activation.Model.PresentationChanged += OnPresentationChanged;
        if (PersistableModel is { } persistable)
        {
            persistable.IsDirtyChanged += OnIsDirtyChanged;
            IsModified = persistable.IsDirty;
        }
    }

    internal PluginId OwnerId => _activation.Registration.OwnerId;
    internal PluginDocumentRegistration Registration => _activation.Registration;
    internal CancellationToken ClosingToken => _activation.ClosingToken;
    internal IPersistablePluginDocument? PersistableModel =>
        Registration.IsPersistable
            ? _activation.Model as IPersistablePluginDocument ??
              throw new InvalidOperationException("声明为可持久化的 Document 模型未实现最终保存契约。")
            : null;
    internal string HostTitle => _hostTitle;
    public object Model => _activation.Model;
    public PluginViewRegistration ViewRegistration => new(
        OwnerId,
        Registration.ModelType,
        Registration.ViewType,
        Registration.ViewFactory);
    public Control? PreparedView => _view.View;
    public void AttachPreparedView(Control view) => _view.Attach(view);
    public void ReleasePreparedView() => _view.Release();

    /// <summary>在主文件成功提交后更新只由 Host 持有的信封标题。</summary>
    /// <remarks>
    /// 未保存时插件 Presentation 可以决定标签显示；一旦 Host 提交磁盘标题，该标题也成为
    /// 当前标签的权威标题，插件后续展示通知不能把文件名覆盖回旧值。
    /// </remarks>
    internal void CommitHostTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _hostTitle = title;
        _hasCommittedHostTitle = true;
        ApplyPresentation();
    }

    /// <summary>同步只由 Host 持有的强制保存状态到 Dock 修改标记。</summary>
    internal void SetHostRequiresSave(bool requiresSave)
    {
        _hostRequiresSave = requiresSave;
        ApplyModifiedState();
    }

    /// <summary>在 Host 保存提交回调结束后立即重读最终脏状态。</summary>
    internal void RefreshModifiedState() => ApplyModifiedState();

    private void OnPresentationChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // SDK 明确允许插件从工作线程发出展示变化。Dock 属性只能在 UI 线程更新，
        // 因而 Adapter 在边界处统一切换，而不是要求每个插件重复了解 Avalonia Dispatcher。
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyPresentation();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyPresentation);
        }
    }

    private void OnIsDirtyChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyModifiedState();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyModifiedState);
        }
    }

    private void ApplyPresentation()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            Title = SelectTitle();
        }
    }

    private void ApplyModifiedState()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            IsModified = _hostRequiresSave || PersistableModel?.IsDirty == true;
        }
    }

    private string SelectTitle()
    {
        if (_hasCommittedHostTitle)
        {
            return _hostTitle;
        }

        var modelTitle = _activation.Model.Presentation.Title;
        if (!string.IsNullOrWhiteSpace(modelTitle))
        {
            return modelTitle;
        }

        return !string.IsNullOrWhiteSpace(_hostTitle)
            ? _hostTitle
            : Registration.Descriptor.DisplayName;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            try
            {
                try
                {
                    _activation.Model.PresentationChanged -= OnPresentationChanged;
                }
                finally
                {
                    if (PersistableModel is { } persistable)
                    {
                        persistable.IsDirtyChanged -= OnIsDirtyChanged;
                    }
                }
            }
            finally
            {
                // 插件可以自定义事件访问器。即使退订逻辑有缺陷，Host 仍继续断开 View，
                // 不能让插件事件实现决定业务 Scope 是否得到关闭。
                _view.Release();
            }
        }
        finally
        {
            // View 清理属于 UI 资源，插件 Scope 才是业务资源所有权底线。即使控件释放失败，
            // 也必须取消 ClosingToken 并释放整个 Scope。
            _activation.Dispose();
        }
    }
}
