using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using MyAvaloniaManagement.Business.Layout;

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

    /// <summary>按规范或暂时保留的兼容 ID 解析当前会话拥有的 Dockable。</summary>
    IDockable? ResolveDockable(string dockableId);

    /// <summary>Docked 基类行为完成后，归一化宿主要求的稳定结构。</summary>
    void OnDockableDocked(IDockable? dockable, DockOperation operation);

    /// <summary>Tool 已隐藏后，提交只读状态与布局变化通知。</summary>
    void OnDockableHidden(IDockable? dockable);

    /// <summary>在 Dock 执行可取消关闭前完成脏 Document 保护。</summary>
    bool OnDockableClosing(IDockable? dockable);

    /// <summary>Dock 最终关闭后结束工作区对 Document 的所有权。</summary>
    void OnDockableClosed(IDockable? dockable);
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
        foreach (var toolId in callbacks.CreatedToolIds)
        {
            ContextLocator[toolId] = () => layout;
        }

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [DockLayoutIds.Root] = () => callbacks.RootDock,
            [DockLayoutIds.Workspace] = () => callbacks.RootDock?.ActiveDockable,
            [DockLayoutIds.Documents] = () => callbacks.ResolveDockable(DockLayoutIds.Documents),
            // Plug 属于 G9 才删除的兼容别名；G6 只删除历史 Files 查询。
            ["Plug"] = () => callbacks.ResolveDockable("Plug"),
        };
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = static () => new HostWindow()
        };

        base.InitLayout(layout);
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

    /// <summary>只有 Session 的关闭保护允许后，才继续执行 Dock 基类关闭协议。</summary>
    public override bool OnDockableClosing(IDockable? dockable) =>
        GetCallbacks().OnDockableClosing(dockable) && base.OnDockableClosing(dockable);

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

    private IWorkspaceDockCallbacks GetCallbacks() => _callbacks ??
        throw new InvalidOperationException("HostDockFactory 尚未绑定 Workspace Session。");
}
