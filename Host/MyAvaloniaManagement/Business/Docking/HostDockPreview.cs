using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>
/// 将宿主已有的 Tool 全宽分屏约定转换为预览矩形。Dock 补丁只接受绘制结果，
/// 不需要了解主窗口骨架、稳定工具组 ID 或工具布局的持久化策略。
/// </summary>
internal static class HostDockPreview
{
    internal static Rect? GetBounds(IRootDock? root, IDockable source, IDockable target,
        DockOperation operation, Control dropControl)
    {
        // Document 分屏、Tool 组内分屏和浮窗内部操作继续使用框架的局部预览。
        if (root is null || operation is not (DockOperation.Top or DockOperation.Bottom) ||
            !IsToolSource(source) || !DockSplitPolicy.IsMainFullWidthTarget(root, target)) return null;
        var host = dropControl.FindAncestorOfType<DockControl>();
        var rows = DockTreeNavigator.FindDockById<IDock>(root, DockLayoutIds.WorkspaceRows);
        var columns = DockTreeNavigator.FindDockById<IDock>(root, DockLayoutIds.WorkspaceColumns);
        if (host is null || columns is null || rows?.VisibleDockables?.Contains(columns) != true) return null;
        var alignment = operation == DockOperation.Top ? Alignment.Top : Alignment.Bottom;

        // 已有稳定目标时，提交实际会合并到该组，预览也使用该组的当前屏幕范围。
        var existing = DockTreeNavigator.FindDockById<IToolDock>(root, ToolDockPlacement.GetDockId(alignment));
        var existingView = host.GetVisualDescendants().OfType<ToolDockControl>()
            .FirstOrDefault(control => existing is not null && ReferenceEquals(control.DataContext, existing));
        if (existingView is { IsEffectivelyVisible: true } && existingView.Bounds.Height > 0)
            return TranslateBounds(existingView, dropControl, new Rect(existingView.Bounds.Size));

        var rowsView = host.GetVisualDescendants().OfType<ProportionalDockControl>()
            .FirstOrDefault(control => ReferenceEquals(control.DataContext, rows));
        if (rowsView is null || rowsView.Bounds.Height <= 0) return null;
        var height = rowsView.Bounds.Height * ToolDockPlacement.GetDefaultProportion(alignment);
        var y = operation == DockOperation.Top ? 0 : rowsView.Bounds.Height - height;
        return TranslateBounds(rowsView, dropControl, new Rect(0, y, rowsView.Bounds.Width, height));
    }

    private static bool IsToolSource(IDockable source) => source is ITool ||
        source is IToolDock { VisibleDockables: { Count: > 0 } items } && items.All(item => item is ITool);

    private static Rect? TranslateBounds(Control source, Control target, Rect bounds) =>
        source.TranslatePoint(bounds.TopLeft, target) is { } origin ? new Rect(origin, bounds.Size) : null;
}
