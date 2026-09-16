using System;
using System.Collections.Generic;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>
/// 定义 Dock 框架回调进入工作区会话时所需的最小内部端口。
/// </summary>
/// <remarks>
/// Dock 要求应用继承 <see cref="Factory"/> 才能接收同步关闭等回调，而工作区对象又必须独占
/// Document、Tool 和根布局状态。本接口只连接这两个边界，不向 ViewModel 或 Plugin SDK 暴露，
/// 也不承担通用事件总线职责。
/// </remarks>
internal interface IWorkspaceDockCallbacks
{
    /// <summary>取得当前会话已经提交的根布局；布局建立前返回 null。</summary>
    IRootDock? RootDock { get; }

    /// <summary>取得当前会话已经创建的全部 Tool 稳定 ID。</summary>
    IReadOnlyCollection<string> CreatedToolIds { get; }

    /// <summary>由 Dock Framework 请求创建当前会话的唯一布局。</summary>
    IRootDock CreateLayout();

    /// <summary>按规范 ID 解析当前会话拥有的 Dockable。</summary>
    IDockable? ResolveDockable(string dockableId);

    /// <summary>Docked 基类行为完成后，归一化宿主要求的稳定结构。</summary>
    void OnDockableDocked(IDockable? dockable, DockOperation operation);

    /// <summary>Tool 已隐藏后，提交只读状态与布局变化通知。</summary>
    void OnDockableHidden(IDockable? dockable);

    /// <summary>Dock 的活动对象变化后，重新计算语义上的活动 Document。</summary>
    void OnActiveDockableChanged(IDockable? dockable);

    /// <summary>在 Dock 执行可取消关闭前完成脏 Document 保护。</summary>
    bool OnDockableClosing(IDockable? dockable);

    /// <summary>Dock 最终关闭后结束工作区对 Document 的所有权。</summary>
    void OnDockableClosed(IDockable? dockable);

    /// <summary>Session 已允许关闭但 Dock 基类最终拒绝时，撤销命令关闭状态。</summary>
    void OnDockableCloseRejected(IDockable? dockable);

    /// <summary>在框架拆除浮窗内容之前准备整组关闭许可。</summary>
    bool OnWindowClosing(IDockWindow window);

    /// <summary>关闭、合并移除或框架拒绝后，解除窗口范围许可。</summary>
    void OnWindowCloseCompleted(IDockWindow window);

    /// <summary>在工具原位置仍完整时捕获恢复依据，不在此写文件。</summary>
    void OnLayoutChanging();

    /// <summary>批量布局操作结束后发布最终结构和工具状态。</summary>
    void OnLayoutChanged();
}

/// <summary>
/// 只负责把 Dock Framework 的 Factory 协议适配到宿主工作区。
/// </summary>
/// <remarks>
/// 本类型不拥有 Root Dock、Document 或 Tool 集合。所有应用状态均由一次性绑定的
/// <see cref="IWorkspaceDockCallbacks"/> 提供；Factory 只保留框架要求的 Locator、override、
/// 禁浮动策略和回调顺序，从而满足里氏替换原则而不再充当应用服务。
/// </remarks>
internal sealed class HostDockFactory : Factory
{
    private IWorkspaceDockCallbacks? _callbacks;
    private int _layoutChangeDepth;
    internal bool IsLayoutChangeInProgress => _layoutChangeDepth != 0;

    internal WorkbenchWindowContext WindowContext { get; }

    public HostDockFactory(WorkbenchWindowContext? windows = null)
    {
        WindowContext = windows ?? new WorkbenchWindowContext();
        // 标题栏的关闭命令也必须保留 Tool 和原停靠点，供工具中心再次显示。
        HideToolsOnClose = true;
    }

    /// <summary>把 Factory 与唯一 Workspace Session 绑定。</summary>
    /// <remarks>
    /// 绑定只允许发生一次。显式的一次性绑定让组合根能够先构造低层 Dock Adapter，再构造拥有
    /// 状态的 Session，同时避免在任一对象中引入 <see cref="IServiceProvider"/> 或延迟服务定位。
    /// </remarks>
    internal void AttachCallbacks(IWorkspaceDockCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        if (_callbacks is not null)
        {
            throw new InvalidOperationException("HostDockFactory 已经绑定 Workspace Session。");
        }

        _callbacks = callbacks;
    }

    /// <summary>由 Dock Framework 创建当前 Session 的唯一布局。</summary>
    public override IRootDock CreateLayout() => GetCallbacks().CreateLayout();

    /// <summary>初始化规范 Locator，并继续执行 Dock Framework 的初始化逻辑。</summary>
    public override void InitLayout(IDockable layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var callbacks = GetCallbacks();
        ContextLocator = new Dictionary<string, Func<object?>>();
        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [DockLayoutIds.Root] = () => callbacks.RootDock,
            [DockLayoutIds.Workspace] = () => callbacks.RootDock?.ActiveDockable,
            [DockLayoutIds.Documents] = () => callbacks.ResolveDockable(DockLayoutIds.Documents),
        };
        foreach (var toolId in callbacks.CreatedToolIds)
        {
            ContextLocator[toolId] = () => layout;
            var stableToolId = toolId;
            DockableLocator[stableToolId] = () => callbacks.ResolveDockable(stableToolId);
        }
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostFloatingWindow(WindowContext)
        };

        base.InitLayout(layout);
    }

    /// <summary>
    /// 合并一次框架批量结构变化的前后通知。恢复调用方传入 captureBefore=false，
    /// 防止旧运行树覆盖待恢复快照；Scope 只拥有通知时序，不拥有业务资源或窗口。
    /// </summary>
    internal IDisposable BeginLayoutChange(bool captureBefore = true)
    {
        if (_layoutChangeDepth == 0 && captureBefore) GetCallbacks().OnLayoutChanging();
        _layoutChangeDepth++;
        return new LayoutChangeScope(() =>
        {
            if (--_layoutChangeDepth == 0) GetCallbacks().OnLayoutChanged();
        });
    }

    /// <summary>最后一个工具隐藏前保留分组和窗口位置；窗口清理后 OriginalOwner 可能已经脱离工作区。</summary>
    public override void HideDockable(IDockable dockable)
    {
        using var change = BeginLayoutChange();
        base.HideDockable(dockable);
    }

    /// <summary>Pin 会从可见树取出工具，必须先保留其组身份和顺序。</summary>
    public override void PinDockable(IDockable dockable)
    {
        using var change = BeginLayoutChange();
        base.PinDockable(dockable);
    }

    /// <summary>先保持 Dock 基类语义，再让 Session 归一化稳定停靠结构。</summary>
    public override void OnDockableDocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableDocked(dockable, operation);
        GetCallbacks().OnDockableDocked(dockable, operation);
    }

    /// <summary>先完成框架隐藏，再向 Session 提交一次最终状态变化。</summary>
    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        GetCallbacks().OnDockableHidden(dockable);
    }

    public override void OnDockablePinned(IDockable? dockable)
    {
        base.OnDockablePinned(dockable);
        GetCallbacks().OnDockableHidden(dockable);
    }

    public override void OnDockableUnpinned(IDockable? dockable)
    {
        base.OnDockableUnpinned(dockable);
        GetCallbacks().OnDockableHidden(dockable);
    }

    /// <summary>保留框架通知后，让 Session 只发布真正改变的活动 Document 事实。</summary>
    public override void OnActiveDockableChanged(IDockable? dockable)
    {
        base.OnActiveDockableChanged(dockable);
        GetCallbacks().OnActiveDockableChanged(dockable);
    }

    /// <summary>只有 Session 的关闭保护允许后，才继续执行 Dock 基类关闭协议。</summary>
    public override bool OnDockableClosing(IDockable? dockable)
    {
        var callbacks = GetCallbacks();
        if (!callbacks.OnDockableClosing(dockable))
        {
            return false;
        }

        try
        {
            if (base.OnDockableClosing(dockable))
            {
                return true;
            }
        }
        catch
        {
            callbacks.OnDockableCloseRejected(dockable);
            throw;
        }

        // Session 可能已经把 Document 标记为 closing。若 Dock 的其他框架规则最终否决，
        // 必须显式恢复命令入口，不能让仍可见的标签永久处于不可执行状态。
        callbacks.OnDockableCloseRejected(dockable);
        return false;
    }

    /// <summary>无论其他关闭通知是否失败，最终都把资源释放交还唯一 Session。</summary>
    public override void OnDockableClosed(IDockable? dockable)
    {
        try
        {
            base.OnDockableClosed(dockable);
        }
        finally
        {
            GetCallbacks().OnDockableClosed(dockable);
        }
    }

    /// <summary>先完成范围确认，再保留 Dock 原生的可取消窗口关闭事件。</summary>
    public override bool OnWindowClosing(IDockWindow? window)
    {
        if (window is null) return base.OnWindowClosing(window);
        var callbacks = GetCallbacks();
        if (window.Host is HostFloatingWindow { IsCloseCancelled: true })
        {
            callbacks.OnWindowCloseCompleted(window);
            return false;
        }
        // 布局转移已把原实例放入候选容器，只关闭清空的旧窗口；不能再异步询问空范围。
        if (IsLayoutChangeInProgress) return base.OnWindowClosing(window);
        if (!callbacks.OnWindowClosing(window)) return false;
        try
        {
            if (base.OnWindowClosing(window))
            {
                if (!IsLayoutChangeInProgress) callbacks.OnLayoutChanging();
                return true;
            }
        }
        catch
        {
            callbacks.OnWindowCloseCompleted(window);
            throw;
        }
        callbacks.OnWindowCloseCompleted(window);
        return false;
    }

    /// <summary>框架已经完成关闭内容后撤销剩余许可，不能在 Closed 时才开始询问保存。</summary>
    public override void OnWindowClosed(IDockWindow? window)
    {
        try { base.OnWindowClosed(window); }
        finally { if (window is not null) GetCallbacks().OnWindowCloseCompleted(window); }
    }

    /// <summary>拖回最后内容也会移除窗口，必须使尚在等待的关闭任务失效。</summary>
    public override void OnWindowRemoved(IDockWindow? window)
    {
        try { base.OnWindowRemoved(window); }
        finally { if (window is not null) GetCallbacks().OnWindowCloseCompleted(window); }
    }

    /// <summary>主工作区不允许单个 Dockable 浮动。</summary>
    public override void FloatDockable(IDockable dockable)
    {
    }

    /// <summary>主工作区不允许带窗口参数浮动单个 Dockable。</summary>
    public override void FloatDockable(IDockable dockable, DockWindowOptions? options)
    {
    }

    /// <summary>主工作区不允许整个 Dock 浮动。</summary>
    public override void FloatAllDockables(IDockable dockable)
    {
    }

    /// <summary>主工作区不允许带窗口参数浮动整个 Dock。</summary>
    public override void FloatAllDockables(IDockable dockable, DockWindowOptions? options)
    {
    }

    /// <summary>把根级能力限制为不可浮动，同时保留拖动和主窗口内停靠。</summary>
    internal static void DisableFloating(IRootDock rootDock)
    {
        ArgumentNullException.ThrowIfNull(rootDock);
        rootDock.RootDockCapabilityPolicy = new DockCapabilityPolicy
        {
            CanFloat = false
        };
    }

    private sealed class LayoutChangeScope(Action complete) : IDisposable
    {
        private Action? _complete = complete;
        public void Dispose() => System.Threading.Interlocked.Exchange(ref _complete, null)?.Invoke();
    }

    private IWorkspaceDockCallbacks GetCallbacks() => _callbacks ??
        throw new InvalidOperationException("HostDockFactory 尚未绑定 Workspace Session。");
}
