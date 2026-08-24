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
using MyAvaloniaManagement.Business.Docking;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>
/// 为 Dock 内容提供按 Document 释放能力的控件回收缓存。
/// </summary>
/// <remarks>
/// Dock 官方回收器只提供全量清空，无法在 Document 最终关闭时移除单项。
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
            if (cached is Visual visual)
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
            if (control is Visual visual)
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
        if (cached is Visual visual)
        {
            ClearKeyboardNavigationReference(visual);
            RemoveFromVisualParent(visual);
        }

        // DataContext 可能继续指向已释放的 Document；在移除缓存时主动断开，
        // 使 View 即使被 Avalonia 短暂保留也不会延长业务作用域生命周期。
        if (data is IManagedDockableViewHost adapter)
        {
            // Adapter 的 View 租约负责幂等断开与 Dispose；正常关闭、Runtime 兜底和回收器
            // 可能重复到达这里，不能让控件自行释放路径形成第二套所有权算法。
            adapter.ReleasePreparedView();
        }
        else if (cached is Control control)
        {
            // 控件离开逻辑树后继承的 DataContext 可能已表现为 null，但子级的显式
            // Binding 仍持有最后一次求值。无条件清空根值，才能让整棵绑定树同步解绑。
            control.DataContext = null;
        }

        // 允许包含原生窗口或显式事件订阅的复合 View 在“最终关闭”边界释放；
        // 普通标签切换只调用 Build，不会触发这里，因此仍保留控件复用。
        if (data is not IManagedDockableViewHost)
        {
            (cached as IDisposable)?.Dispose();
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
        var parent = visual.GetVisualParent();
        switch (parent)
        {
            case Panel panel when visual is Control control:
                panel.Children.Remove(control);
                break;
            case ContentPresenter contentPresenter:
                contentPresenter.Content = null;
                break;
            case ContentControl contentControl:
                contentControl.Content = null;
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
        }
    }

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
