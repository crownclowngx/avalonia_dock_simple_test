using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Contract;
using Dock.Model;
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

    /// <summary>保留一般停靠状态通知；此回调没有目标信息，不能据此改变分割策略。</summary>
    void OnDockableDocked(IDockable? dockable, DockOperation operation);

    /// <summary>基类分割完成并挂接新组后，携带原目标执行宿主的有限兼容策略。</summary>
    void OnDockSplitCompleted(IDock originalTarget, IDockable insertedDock, DockOperation operation);

    /// <summary>Tool 已隐藏后，提交只读状态与布局变化通知。</summary>
    void OnDockableHidden(IDockable? dockable);

    /// <summary>Dock 的选择或焦点变化后，重新计算语义上的活动 Document 与最近文档组。</summary>
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
/// 浮动边界和回调顺序，从而满足里氏替换原则而不再充当应用服务。
/// </remarks>
internal sealed class HostDockFactory : Factory, IDockDropGuard, IDockPreviewProvider
{
    private IWorkspaceDockCallbacks? _callbacks;
    private int _layoutChangeDepth;
    // 只暂存当前原生关闭已通过的业务项事件许可，防止 Closed 清理再次触发可取消事件。
    // 不拥有 Document/Tool；业务资源仍由 Session 管理，许可在消费、取消或窗口移除时撤销。
    private readonly Dictionary<IDockWindow, HashSet<IDockable>> _approvedWindowCloses = new(ReferenceEqualityComparer.Instance);
    internal bool IsLayoutChangeInProgress => _layoutChangeDepth != 0;

    internal WorkbenchWindowContext WindowContext { get; }

    /// <summary>
    /// 补丁在预览和松开提交时都会查询此端口。与 Move/Split 的最终防线保持同一限制，
    /// 全屏期间不再出现“看起来可以放下、实际没有变化”的高亮；其余能力仍由 Dock 校验。
    /// </summary>
    bool IDockDropGuard.CanDrop(IDockable source, IDockable target, DockOperation operation) =>
        !WindowContext.HasFullscreenContent;

    /// <summary>只把纯预览查询转交给宿主策略，不在 Factory 中维护鼠标状态或绘制逻辑。</summary>
    Rect? IDockPreviewProvider.GetPreviewBounds(IDockable source, IDockable target,
        DockOperation operation, Control dropControl) =>
        HostDockPreview.GetBounds(GetCallbacks().RootDock, source, target, operation, dropControl);

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
    /// 活动容器变化也会递归 InitDockable；框架默认再次调用 Locator 会为同一模型创建第二个壳。
    /// 仍可见且属于该模型的窗口必须复用，真正关闭后的模型才允许创建新的原生窗口。
    /// </summary>
    public override void InitDockWindow(IDockWindow window, IDockable? owner)
    {
        if (window.Host is HostFloatingWindow { IsVisible: true } host && ReferenceEquals(host.Window, window))
            base.InitDockWindow(window, owner, host);
        else base.InitDockWindow(window, owner);
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
        if (dockable is ITool && TryCloseLastContentWindow(dockable)) return;
        using var change = BeginLayoutChange();
        base.HideDockable(dockable);
    }

    /// <summary>
    /// 最后一个业务项必须先取得整窗关闭许可，再由框架清理内容。Document 若先移除，空组
    /// 折叠会解除 DockWindow 引用，即使原生关闭被异步确认暂时取消也无法再正确回收窗口。
    /// Tool 仍沿用隐藏语义，Document 最终释放；本入口不直接拥有或释放任何业务对象。
    /// </summary>
    public override void CloseDockable(IDockable? dockable)
    {
        // 关闭命令先保留基类能力约束；浮窗之外的显式隐藏仍由 HideDockable 沿用框架行为。
        if (dockable is ITool or IDocument && CanCloseContent(dockable) &&
            TryCloseLastContentWindow(dockable)) return;
        base.CloseDockable(dockable);
    }

    private static bool CanCloseContent(IDockable item) =>
        !(item.Owner is IDock { CanCloseLastDockable: false, VisibleDockables.Count: <= 1 }) &&
        DockCapabilityResolver.IsEnabled(item, DockCapability.Close, DockCapabilityResolver.ResolveOperationDock(item));

    /// <summary>
    /// 只识别当前工作区中仅剩目标业务项的浮窗，不影响主窗口和仍承载其他内容的窗口。
    /// Factory 借用现有窗口执行协议，不接管模型所有权。原生 Closing/Closed 的内容清理必须继续
    /// 进入基类，不能在回调内再次申请关闭；实际关闭失败或取消时，原对象图尚未被修改。
    /// </summary>
    private bool TryCloseLastContentWindow(IDockable dockable)
    {
        if (GetCallbacks().RootDock is not { } root ||
            DockTreeNavigator.FindWindow(root, dockable) is not { Layout: { } layout,
                Host: HostFloatingWindow { IsVisible: true, IsInNativeCloseCallback: false } host }) return false;
        var contents = DockTreeNavigator.Enumerate(layout).Where(item => item is ITool or IDocument).Take(2).ToArray();
        if (contents.Length != 1 || !ReferenceEquals(contents[0], dockable)) return false;
        host.Close();
        return true;
    }

    /// <summary>Pin 会从可见树取出工具，必须先保留其组身份和顺序。</summary>
    public override void PinDockable(IDockable dockable)
    {
        // 浮窗只提供普通停靠；自动隐藏边栏归属于主窗口，不能产生无法恢复的浮窗 Pinned 数据。
        if (GetCallbacks().RootDock is { } root && DockTreeNavigator.FindWindow(root, dockable) is not null) return;
        using var change = BeginLayoutChange();
        base.PinDockable(dockable);
    }

    /// <summary>先保持 Dock 基类语义，再让 Session 更新一般停靠状态。</summary>
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

    /// <summary>
    /// 点击另一分组中已选中的页面只产生焦点事件，不能仅监听标签选择事件。
    /// 两条框架通知进入同一语义端口；Session 按引用去重，Tool 焦点保留最近文档。
    /// </summary>
    public override void OnFocusedDockableChanged(IDockable? dockable)
    {
        base.OnFocusedDockableChanged(dockable);
        GetCallbacks().OnActiveDockableChanged(dockable);
    }

    /// <summary>只有 Session 的关闭保护允许后，才继续执行 Dock 基类关闭协议。</summary>
    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (dockable is ITool or IDocument && _approvedWindowCloses.Values.Any(items => items.Remove(dockable))) return true;
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
            _approvedWindowCloses.Remove(window);
            callbacks.OnWindowCloseCompleted(window);
            return false;
        }
        // 布局转移的旧空壳、工具中心的最后 Tool 显隐已由外层批量范围捕获原位置，
        // 沿用同步原生关闭；业务项能力与框架取消仍须检查，不能因为批量操作跳过它们。
        if (!IsLayoutChangeInProgress && !callbacks.OnWindowClosing(window)) return false;
        try
        {
            if (base.OnWindowClosing(window) && PrepareWindowContentClose(window))
            {
                if (!IsLayoutChangeInProgress) callbacks.OnLayoutChanging();
                return true;
            }
        }
        catch
        {
            _approvedWindowCloses.Remove(window);
            callbacks.OnWindowCloseCompleted(window);
            throw;
        }
        _approvedWindowCloses.Remove(window);
        callbacks.OnWindowCloseCompleted(window);
        return false;
    }

    /// <summary>
    /// CloseDockable 的能力与 DockableClosing 原本在拆除前执行。将最后业务项转接到窗口后，
    /// 必须在原生窗口仍可取消时执行这些检查，不能等 Closed 才发现拒绝。全部通过才暂存许可，
    /// 框架随后仍负责实际隐藏与 DockableClosed；窗口内容范围保护继续由原协调器负责。
    /// </summary>
    private bool PrepareWindowContentClose(IDockWindow window)
    {
        var contents = window.Layout is { } root
            ? DockTreeNavigator.Enumerate(root).Where(item => item is ITool or IDocument).ToArray() : [];
        // 整窗会取出组内全部成员，因此禁止关闭最后项的组即使目前有多项也必须保留。
        if (contents.Any(item => !CanCloseContent(item) || item.Owner is IDock { CanCloseLastDockable: false })) return false;
        foreach (var item in contents)
            if (!OnDockableClosing(item)) return false;
        // 可取消事件属于外部回调；若它改变了范围，旧许可不能授权关闭刚加入的内容。
        var current = window.Layout is { } layout
            ? DockTreeNavigator.Enumerate(layout).Where(item => item is ITool or IDocument) : [];
        if (!new HashSet<IDockable>(contents, ReferenceEqualityComparer.Instance).SetEquals(current)) return false;
        if (contents.Length > 0) _approvedWindowCloses[window] = new(contents, ReferenceEqualityComparer.Instance);
        return true;
    }

    /// <summary>
    /// Closing 获准时已经捕获原位置。Dock 在 Closed 事件之后才逐项隐藏内容，此时窗口位置
    /// 跟踪器已注销，不能再用默认位置覆盖记录；将整个清理作为一次变更，只提交最终隐藏状态。
    /// </summary>
    public override void CloseWindow(IDockWindow window)
    {
        using var change = BeginLayoutChange(captureBefore: false);
        try { base.CloseWindow(window); }
        finally { _approvedWindowCloses.Remove(window); }
    }

    /// <summary>框架已经完成关闭内容后撤销剩余许可，不能在 Closed 时才开始询问保存。</summary>
    public override void OnWindowClosed(IDockWindow? window)
    {
        try { base.OnWindowClosed(window); }
        finally
        {
            if (window is not null)
            {
                _approvedWindowCloses.Remove(window);
                GetCallbacks().OnWindowCloseCompleted(window);
            }
        }
    }

    /// <summary>拖回最后内容也会移除窗口，必须使尚在等待的关闭任务失效。</summary>
    public override void OnWindowRemoved(IDockWindow? window)
    {
        try { base.OnWindowRemoved(window); }
        finally
        {
            if (window is not null)
            {
                _approvedWindowCloses.Remove(window);
                GetCallbacks().OnWindowCloseCompleted(window);
            }
        }
    }

    /// <summary>单项浮动保留框架实现；固定主骨架从来不是用户可移动的业务项。</summary>
    public override void FloatDockable(IDockable dockable) => FloatDockable(dockable, null);

    public override void FloatDockable(IDockable dockable, DockWindowOptions? options)
    {
        if (!CanMigrate(dockable)) return;
        using var change = BeginLayoutChange();
        base.FloatDockable(dockable, options);
        UpdateFloatingPolicies();
    }

    /// <summary>Dock 的整组入口参数是组内成员，必须逐项检查，但不能搬走主 DocumentDock 容器。</summary>
    public override void FloatAllDockables(IDockable dockable) => FloatAllDockables(dockable, null);

    public override void FloatAllDockables(IDockable dockable, DockWindowOptions? options)
    {
        if (dockable.Owner is not IDock { VisibleDockables: { Count: > 0 } items } ||
            !items.All(CanMigrate)) return;
        using var change = BeginLayoutChange();
        base.FloatAllDockables(dockable, options);
        UpdateFloatingPolicies();
    }

    public override void MoveDockable(IDock sourceDock, IDock targetDock, IDockable sourceDockable, IDockable? targetDockable)
    {
        if (WindowContext.HasFullscreenContent) return;
        using var change = BeginLayoutChange();
        base.MoveDockable(sourceDock, targetDock, sourceDockable, targetDockable);
        UpdateFloatingPolicies();
    }

    /// <summary>
    /// 目标引用属于本次同步调用，不缓存为全局“当前拖放”。框架可能先 Move 再 Split，
    /// 此处仅在插入组实际挂接后报告完成；一般 Docked 事件仍由基类按原顺序发出。
    /// </summary>
    public override void SplitToDock(IDock dock, IDockable dockable, DockOperation operation)
    {
        if (WindowContext.HasFullscreenContent) return;
        using var change = BeginLayoutChange();
        // 基类对不在父列表中的目标直接返回；已挂接的传入节点不能把这类空操作伪装成完成。
        // 不合法的 operation 仍交给基类抛错，不能由适配层悄悄改变框架契约。
        var splitRequested = dock.Owner is IDock { VisibleDockables: { } siblings } && siblings.Contains(dock);
        base.SplitToDock(dock, dockable, operation);
        var callbacks = GetCallbacks();
        if (splitRequested && callbacks.RootDock is { } root && DockTreeNavigator.IsDockableAttached(root, dockable))
        {
            // 锁定 Dock 的同方向优化直接插入分隔条，却只初始化新内容组。补齐本次父容器中
            // 无 Owner 的分隔条，保持比例拖动和后续清理的框架归属；不重新初始化整个窗口树。
            if (dockable.Owner is IProportionalDock { VisibleDockables: { } children } parent)
                foreach (var splitter in children.OfType<IProportionalDockSplitter>().Where(item => item.Owner is null).ToArray())
                    InitDockable(splitter, parent);
            callbacks.OnDockSplitCompleted(dock, dockable, operation);
        }
        UpdateFloatingPolicies();
    }

    private bool CanMigrate(IDockable item) => !WindowContext.HasFullscreenContent &&
        item is ManagedDocumentDockable or ManagedToolDockable && item.CanFloat;

    /// <summary>菜单能力跟随实际窗口归属，回停主窗后重新允许 Pin。</summary>
    internal void UpdateFloatingPolicies()
    {
        if (GetCallbacks().RootDock is not { } root) return;
        foreach (var item in DockTreeNavigator.EnumerateWorkspace(root).OfType<ManagedToolDockable>())
            item.CanPin = DockTreeNavigator.FindWindow(root, item) is null;
    }

    private sealed class LayoutChangeScope(Action complete) : IDisposable
    {
        private Action? _complete = complete;
        public void Dispose() => System.Threading.Interlocked.Exchange(ref _complete, null)?.Invoke();
    }

    private IWorkspaceDockCallbacks GetCallbacks() => _callbacks ??
        throw new InvalidOperationException("HostDockFactory 尚未绑定 Workspace Session。");
}
