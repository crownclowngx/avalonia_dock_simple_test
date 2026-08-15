using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Recycling.Model;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace MyAvaloniaManagement.Business.Helpers;

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

        var dataTemplate = (parent as Control)?.FindDataTemplate(data);
        var control = dataTemplate?.Build(data);
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

        // DataContext 可能继续指向已释放的 Document；在移除缓存时主动断开，
        // 使 View 即使被 Avalonia 短暂保留也不会延长业务作用域生命周期。
        if (cached is Control control)
        {
            // 控件离开逻辑树后继承的 DataContext 可能已表现为 null，但子级的显式
            // Binding 仍持有最后一次求值。无条件清空根值，才能让整棵绑定树同步解绑。
            control.DataContext = null;
        }

        // 允许包含原生窗口或显式事件订阅的复合 View 在“最终关闭”边界释放；
        // 普通标签切换只调用 Build，不会触发这里，因此仍保留控件复用。
        (cached as IDisposable)?.Dispose();
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
}
