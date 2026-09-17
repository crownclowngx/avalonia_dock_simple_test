using System;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 预览和提交共用的纯目标分类。这里不创建分组、不移动内容，也不保存一次拖动的状态。
/// 通过主树中的对象引用识别归属，避免浮窗内同名文档组误用主窗口的全宽策略。
/// </summary>
internal static class DockSplitPolicy
{
    internal static bool IsMainFullWidthTarget(IRootDock root, IDockable target)
    {
        if (!DockTreeNavigator.Enumerate(root).Any(item => ReferenceEquals(item, target))) return false;
        var rows = DockTreeNavigator.FindDockById<IDock>(root, DockLayoutIds.WorkspaceRows);
        var columns = DockTreeNavigator.FindDockById<IDock>(root, DockLayoutIds.WorkspaceColumns);
        return target is IDocumentDock || ReferenceEquals(target, rows) || ReferenceEquals(target, columns);
    }
}
