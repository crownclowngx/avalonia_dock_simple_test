using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Recycling.Model;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>
/// 为 Dock 内容提供按 Document 释放能力的控件回收缓存。
/// </summary>
/// <remarks>
/// Dock 12.1.0.6 的具体回收器虽有 Remove，但只删除字典项；IControlRecycling 也不包含最终关闭协议。
/// 本实现保留标签切换时的控件复用，同时让宿主在关闭边界精确释放对应强引用，
/// 避免已释放的 scoped 播放器、文件监视器和视图长期滞留。
/// </remarks>
internal sealed class DocumentControlRecycling : AvaloniaObject, IControlRecycling
{
    /// <summary>
    /// App Resource 与 DockControl Style 共用的稳定键。键名是 XAML 资源协议，
    /// 而实例所有权属于当前 Host DI 容器。
    /// </summary>
    internal const string ResourceKey = "ControlRecyclingKey";

    private readonly Dictionary<object, object> _cache = [];
    private bool _tryToUseIdAsKey;

    public static readonly DirectProperty<DocumentControlRecycling, bool>
        TryToUseIdAsKeyProperty = AvaloniaProperty.RegisterDirect<
            DocumentControlRecycling,
            bool>(
                nameof(TryToUseIdAsKey),
                owner => owner.TryToUseIdAsKey,
                (owner, value) => owner.TryToUseIdAsKey = value);

    public bool TryToUseIdAsKey
    {
        get => _tryToUseIdAsKey;
        set => SetAndRaise(
            TryToUseIdAsKeyProperty,
            ref _tryToUseIdAsKey,
            value);
    }

    public bool TryGetValue(object? data, out object? control)
    {
        if (data is null)
        {
            control = null;
            return false;
        }

        return _cache.TryGetValue(data, out control);
    }

    public void Add(object data, object control) => _cache[data] = control;

    public object? Build(object? data, object? existing, object? parent)
    {
        var key = GetKey(data);
        if (key is null)
            return null;

        if (TryGetValue(key, out var cached))
        {
            // existing 是当前模板已经拥有的正文。只有交给另一个宿主时才解绑，
            // 否则立即 UpdateChild 会在同一 Presenter 的更新过程中清空它自己。
            if (cached is Visual visual && !ReferenceEquals(existing, cached))
                RemoveFromVisualParent(visual);
            return cached;
        }

        object? control;
        if (data is IManagedDockableViewHost adapter)
        {
            // 只有 Dock 正文使用的 ControlRecyclingDataTemplate 才会进入本路径。
            // 标签头及关闭按钮等辅助 Presenter 仍经过应用级 ViewLocator，并只能
            // 得到独立占位控件，从而保证 Adapter 的真实 View 始终只有一个宿主。
            control = adapter.PreparedView ?? throw new InvalidOperationException(
                "Dock Adapter 尚未完成 View 预构建，不能发布到正文回收器。");
            if (control is Visual visual && !ReferenceEquals(existing, control))
                RemoveFromVisualParent(visual);
        }
        else
        {
            var dataTemplate = (parent as Control)?.FindDataTemplate(data);
            control = dataTemplate?.Build(data);
        }

        if (control is not null)
            Add(key, control);
        return control;
    }

    /// <summary>
    /// 移除一个已经最终关闭的 Document 及其缓存 View。
    /// </summary>
    public bool Remove(object? data)
    {
        var key = GetKey(data);
        if (key is null || !_cache.Remove(key, out var cached))
            return false;

        // 最终关闭当前活动 Document 时，Dock 可能先发出 Closed 回调、稍后才刷新 Presenter。
        // 若这里只删除字典项，缓存 View 仍会作为 ContentPresenter 的当前内容保活；插件资源虽已
        // Dispose，弱引用却无法归零。先主动从视觉父级摘除，既立即触发 Detached 清理，也保证
        // 后续 Presenter 再次刷新只是幂等地清空旧内容。
        try
        {
            if (cached is Visual visual)
            {
                ClearKeyboardNavigationReference(visual);
                RemoveFromVisualParent(visual);
            }
        }
        finally
        {
            // 最终关闭与暂时复用不同：解绑失败仍必须结束资源所有权，并把失败交给调用者记录。
            // Adapter 的租约负责幂等释放，外层 DockDocumentLifetime 仍负责结束 Document scope。
            if (data is IManagedDockableViewHost adapter)
            {
                adapter.ReleasePreparedView();
            }
            else
            {
                try
                {
                    // 即使继承值已为 null，也清除显式 DataContext，断开子树中的业务绑定。
                    if (cached is Control control)
                        control.DataContext = null;
                }
                finally
                {
                    (cached as IDisposable)?.Dispose();
                }
            }
        }
        return true;
    }

    public void Clear() => _cache.Clear();

    private object? GetKey(object? data)
    {
        if (data is null)
            return null;

        if (TryToUseIdAsKey &&
            data is IControlRecyclingIdProvider idProvider &&
            !string.IsNullOrWhiteSpace(idProvider.GetControlRecyclingId()))
        {
            return idProvider.GetControlRecyclingId();
        }

        return data;
    }

    private static void RemoveFromVisualParent(Visual visual)
    {
        // 同一 View 只能有一个正文宿主。优先移除实际 Presenter.Child，避免对模板外层的
        // 逻辑父级误操作；之后再处理可能尚未脱离的逻辑 Content 所有权。最多分别处理两层。
        // 不能安全摘除时明确失败，不能返回仍有父级的 View，也不能偷偷创建第二个播放器。
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var visualParent = visual.GetVisualParent();
            var parent = visualParent is ContentPresenter
                ? visualParent
                : (visual as Control)?.Parent ?? visualParent;
            if (parent is null)
                return;

            switch (parent)
            {
                case ContentPresenter presenter when ReferenceEquals(presenter.Child, visual):
                    // SetCurrentValue 保留主题/模板绑定；UpdateChild 使摘除在返回前完成，
                    // 不依赖下一轮布局才释放视觉父级。
                    presenter.SetCurrentValue(ContentPresenter.ContentProperty, null);
                    presenter.UpdateChild();
                    break;
                case Panel panel when visual is Control child && panel.Children.Contains(child):
                    panel.Children.Remove(child);
                    break;
                case ContentControl content when ReferenceEquals(content.Content, visual):
                    content.SetCurrentValue(ContentControl.ContentProperty, null);
                    break;
                case Decorator decorator when ReferenceEquals(decorator.Child, visual):
                    decorator.SetCurrentValue(Decorator.ChildProperty, null);
                    break;
                default:
                    throw DetachFailure(visual, parent);
            }
        }

        var remaining = visual.GetVisualParent() ?? (visual as Control)?.Parent;
        if (remaining is not null)
            throw DetachFailure(visual, remaining);
    }

    private static InvalidOperationException DetachFailure(Visual visual, StyledElement parent) =>
        new($"Dock 正文 {visual.GetType().Name} 无法从父级 {parent.GetType().Name} 安全解绑；请检查正文模板的单一所有权。");

    /// <summary>清除视觉祖先对待关闭控件树中“上次 Tab 焦点”的强引用。</summary>
    private static void ClearKeyboardNavigationReference(Visual root)
    {
        // Avalonia 的 ItemsControl 会通过 TabOnceActiveElement 记住其最近聚焦的后代。直接关闭
        // 当前活动 Document 时，Dock 来不及先把焦点移到其他标签；即使 View 已脱离视觉树并
        // Dispose，这个附加属性仍会从 ItemsControl 强引用旧播放器。只清除指向本次关闭子树的
        // 值，不碰其他 Dock 或工具区的焦点记忆，避免扩大回收器的行为范围。
        foreach (var ancestor in root.GetVisualAncestors().OfType<InputElement>())
        {
            var active = KeyboardNavigation.GetTabOnceActiveElement(ancestor);
            if (active is not Visual activeVisual ||
                (!ReferenceEquals(activeVisual, root) &&
                 !activeVisual.GetVisualAncestors().Contains(root)))
            {
                continue;
            }

            KeyboardNavigation.SetTabOnceActiveElement(ancestor, null);
        }
    }
}
