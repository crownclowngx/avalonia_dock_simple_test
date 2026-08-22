using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 让纯 Dock 几何测试显式把框架操作路由到 Session 所绑定的 HostDockFactory。
/// </summary>
/// <remarks>
/// 这些扩展只存在于测试程序集，不进入生产对象图，也不保存任何状态。保留与 Dock Factory 相同的
/// 方法名可以让布局测试聚焦几何断言，同时每个调用仍清楚地经由 Session 的唯一 Factory 实例执行。
/// </remarks>
internal static class WorkspaceSessionDockTestExtensions
{
    internal static IRootDock CreateWorkspaceLayout(
        this WorkspaceSession session,
        DocumentDock documentDock) => session.CommitWorkspaceLayout(documentDock);

    internal static void InitLayout(this WorkspaceSession session, IDockable layout) =>
        session.DockFactory.InitLayout(layout);

    internal static IList<T> CreateList<T>(this WorkspaceSession session, params T[] items) =>
        session.DockFactory.CreateList(items);

    internal static IRootDock CreateRootDock(this WorkspaceSession session) =>
        session.DockFactory.CreateRootDock();

    internal static IDockable CreateTool(this WorkspaceSession session) =>
        session.DockFactory.CreateTool();

    internal static IDockable CreateDocument(this WorkspaceSession session) =>
        session.DockFactory.CreateDocument();

    internal static void AddDockable(this WorkspaceSession session, IDock dock, IDockable dockable) =>
        session.DockFactory.AddDockable(dock, dockable);

    internal static void InsertDockable(
        this WorkspaceSession session,
        IDock dock,
        IDockable dockable,
        int index) => session.DockFactory.InsertDockable(dock, dockable, index);

    internal static void RemoveDockable(
        this WorkspaceSession session,
        IDockable dockable,
        bool collapse) => session.DockFactory.RemoveDockable(dockable, collapse);

    internal static void HideDockable(this WorkspaceSession session, IDockable dockable) =>
        session.DockFactory.HideDockable(dockable);

    internal static void PinDockable(this WorkspaceSession session, IDockable dockable) =>
        session.DockFactory.PinDockable(dockable);

    internal static void SetActiveDockable(this WorkspaceSession session, IDockable dockable) =>
        session.DockFactory.SetActiveDockable(dockable);

    internal static IRootDock? FindRoot(
        this WorkspaceSession session,
        IDockable dockable,
        Func<IDockable, bool> predicate) => session.DockFactory.FindRoot(dockable, predicate);

    internal static void MoveDockable(
        this WorkspaceSession session,
        IDock source,
        IDock target,
        IDockable dockable,
        IDockable? anchor) => session.DockFactory.MoveDockable(source, target, dockable, anchor);

    internal static void FloatDockable(this WorkspaceSession session, IDockable dockable) =>
        session.DockFactory.FloatDockable(dockable);

    internal static void FloatDockable(
        this WorkspaceSession session,
        IDockable dockable,
        DockWindowOptions? options) => session.DockFactory.FloatDockable(dockable, options);

    internal static void FloatAllDockables(this WorkspaceSession session, IDockable dockable) =>
        session.DockFactory.FloatAllDockables(dockable);

    internal static void FloatAllDockables(
        this WorkspaceSession session,
        IDockable dockable,
        DockWindowOptions? options) => session.DockFactory.FloatAllDockables(dockable, options);
}
