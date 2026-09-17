using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>保存单次工作区查询所需的文档成员、浮窗归属及工具显隐关系。</summary>
/// <remarks>
/// 设计思路：先沿既有工作区遍历顺序捕获节点，再为本次查询构造引用索引，避免逐页面、逐工具扫描整树。
/// 只在调用方原有的工作区线程内同步创建和消费；不订阅布局变化，不进入 Session 字段，也不负责资源释放。
/// 集合成员在捕获后不变，但其中的 Dock 对象仍由工作区拥有；标题、修改状态和业务可用性不在此缓存。
/// 执行动作时必须重新检查真实工作区，不能把展示快照当作执行许可。
/// </remarks>
internal sealed class WorkspaceLayoutQuerySnapshot
{
    private readonly Dictionary<IDockable, IDocumentDock> _documentDocks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IDockable, IDockWindow> _windows = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IDockable> _hidden = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IDockable> _pinned = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IDockable> _docked = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IDockable> _active = new(ReferenceEqualityComparer.Instance);

    private WorkspaceLayoutQuerySnapshot() { }

    /// <summary>同步捕获当前结构；空根得到空关系，不创建模型、视图或布局节点，也不发布通知。</summary>
    internal static WorkspaceLayoutQuerySnapshot Capture(IDockable? root)
    {
        var snapshot = new WorkspaceLayoutQuerySnapshot();
        if (root is null) return snapshot;

        // 沿用 Navigator 的 children／Windows 遍历及引用去重，绝不跟随 Owner 回边。
        var nodes = DockTreeNavigator.EnumerateWorkspace(root).ToArray();
        foreach (var dock in nodes.OfType<IDock>())
        {
            foreach (var child in dock.VisibleDockables ?? [])
            {
                snapshot._docked.Add(child);
                if (dock is IDocumentDock documents)
                    snapshot._documentDocks.TryAdd(child, documents);
            }
            if (dock.ActiveDockable is { } active) snapshot._active.Add(active);
        }

        var roots = nodes.OfType<IRootDock>().ToArray();
        foreach (var current in roots)
        {
            snapshot._hidden.UnionWith(current.HiddenDockables ?? []);
            snapshot._pinned.UnionWith(current.LeftPinnedDockables ?? []);
            snapshot._pinned.UnionWith(current.RightPinnedDockables ?? []);
            snapshot._pinned.UnionWith(current.TopPinnedDockables ?? []);
            snapshot._pinned.UnionWith(current.BottomPinnedDockables ?? []);
        }

        // 窗口归属保留 FindWindow 的“窗口声明顺序中的首个可见树匹配”。不能直接使用
        // 工作区 DFS 的首次到达窗口：共享节点或窗口回边会使两种顺序不同。
        // 每个窗口只扫描一次自己的可见树，不沿嵌套 Windows 扩大承载范围；主树查询入口不变。
        foreach (var window in roots.SelectMany(current => current.Windows ?? []).Distinct())
        {
            if (window.Layout is not { } layout) continue;
            foreach (var node in DockTreeNavigator.Enumerate(layout))
                snapshot._windows.TryAdd(node, window);
        }
        return snapshot;
    }

    /// <summary>按对象引用返回首个直接包含此文档的分组；相同 Id 或标题不代表同一实例。</summary>
    internal IDocumentDock? FindDocumentDock(IDockable document) => _documentDocks.GetValueOrDefault(document);

    /// <summary>返回捕获时首个匹配的浮窗；未被浮窗承载的主树对象返回 null。</summary>
    internal IDockWindow? FindWindow(IDockable target) => _windows.GetValueOrDefault(target);

    /// <summary>是否属于任一根的隐藏集合；与固定、可见集合重叠时由原查询模型决定优先级。</summary>
    internal bool IsHidden(IDockable target) => _hidden.Contains(target);

    /// <summary>是否属于任一根的四向固定集合，不隐含可用性或激活许可。</summary>
    internal bool IsPinned(IDockable target) => _pinned.Contains(target);

    /// <summary>是否为某个可见分组的直接成员；浮窗与主树均参与判断。</summary>
    internal bool IsDocked(IDockable target) => _docked.Contains(target);

    /// <summary>是否被任一捕获的 Dock 记录为活动成员，不重新计算或改变焦点。</summary>
    internal bool IsActive(IDockable target) => _active.Contains(target);
}
