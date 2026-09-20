using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 一次新建借用的来源根和优先文档组。只描述运行期插入位置，不拥有窗口或文档，
/// 不进入 SDK 或磁盘格式；异步等待后必须重新验证组仍属于同一个来源根。
/// </summary>
internal sealed record DocumentCreationTarget(IRootDock SourceRoot, IDocumentDock? PreferredDock);

/// <summary>
/// 只负责文档落点规则。工作区仍拥有布局树，窗口关闭事实由调用方提供；本类不访问
/// Avalonia 焦点、创建 Scope 或修改 Dock。弱记录避免已移除浮窗被最近分组缓存长期持有。
/// </summary>
internal sealed class DocumentCreationTargetResolver
{
    private readonly ConditionalWeakTable<IRootDock, WeakReference<IDocumentDock>> _recent = new();

    internal void Remember(IRootDock root, IDockable? active)
    {
        var group = active as IDocumentDock ?? active?.Owner as IDocumentDock;
        if (group is null) return;
        var source = DockTreeNavigator.FindWindow(root, group)?.Layout ?? root;
        if (!DockTreeNavigator.Enumerate(source).Any(item => ReferenceEquals(item, group))) return;
        _recent.Remove(source);
        _recent.Add(source, new(group));
    }

    /// <summary>面板获取输入焦点前捕获当前窗口的组；不读取其他浮窗的活动文档。</summary>
    internal DocumentCreationTarget Capture(IRootDock source) => new(source, Select(source));

    /// <summary>
    /// 发布时只在原窗口与主窗口内回退。null 目标表示旧入口明确沿用主默认组，不暗中
    /// 采用当前活动浮窗。原组迁往另一窗口也视为失效，不能追随它改变本次请求的归属。
    /// </summary>
    internal IDocumentDock? Resolve(IRootDock root, IDocumentDock defaultDock,
        DocumentCreationTarget? target, Func<IRootDock, bool> isAvailable)
    {
        if (target is null)
            return isAvailable(root) && IsMember(root, defaultDock) && CanAccept(defaultDock) ? defaultDock : null;
        var source = target.SourceRoot;
        if ((ReferenceEquals(source, root) || DockTreeNavigator.EnumerateWindows(root).Any(window => ReferenceEquals(window.Layout, source)))
            && isAvailable(source) && Select(source, target.PreferredDock) is { } selected) return selected;
        if (!isAvailable(root)) return null;
        return Select(root, fallback: defaultDock);
    }

    private IDocumentDock? Select(IRootDock root, IDocumentDock? preferred = null, IDocumentDock? fallback = null)
    {
        // 局部遍历不进入 root.Windows；相同标题或相同 Id 不能替代引用身份。
        var groups = DockTreeNavigator.Enumerate(root).OfType<IDocumentDock>().Where(CanAccept).ToArray();
        if (preferred is not null && groups.Contains(preferred)) return preferred;
        if (_recent.TryGetValue(root, out var recent) && recent.TryGetTarget(out var remembered) && groups.Contains(remembered)) return remembered;
        var focused = root.FocusedDockable as IDocumentDock ?? root.FocusedDockable?.Owner as IDocumentDock;
        if (focused is not null && groups.Contains(focused)) return focused;
        if (fallback is not null && groups.Contains(fallback)) return fallback;
        return groups.FirstOrDefault();
    }

    private static bool IsMember(IRootDock root, IDocumentDock group) =>
        DockTreeNavigator.Enumerate(root).Any(item => ReferenceEquals(item, group));

    private static bool CanAccept(IDocumentDock group) =>
        DockCapabilityResolver.IsEnabled(group, DockCapability.Drop, group) &&
        (group is not IDockableDockingRestrictions restrictions || (restrictions.AllowedDropOperations & DockOperationMask.Fill) != 0);
}
