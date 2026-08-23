using System;
using System.IO;
using System.Threading;
using Dock.Model.Controls;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 协调布局持久化与主窗口生命周期，只负责 Prepare、Apply 和 Save 的执行顺序。
/// 快照映射、严格 V2 读取和运行时校验委托给专门组件，避免生命周期类理解所有 Dock 细节。
/// </summary>
internal sealed class DockLayoutLifecycle(DockLayoutStore store)
{
    private DockLayoutSnapshotV2? _pendingSnapshot;

    internal IRootDock Prepare(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.RootDock is { } existing)
        {
            return existing;
        }
        _pendingSnapshot = store.Load();
        var root = session.DockFactory.CreateLayout();
        session.DockFactory.InitLayout(root);
        return root;
    }

    internal IRootDock ApplyPending(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var defaultRoot = session.RootDock ??
            throw new InvalidOperationException("Workspace 尚未准备根布局。");

        var snapshot = Interlocked.Exchange(ref _pendingSnapshot, null);
        if (snapshot is null)
        {
            return defaultRoot;
        }

        // Tool 声明与生命周期可用性不依赖 Dock 树，必须在补建任何快照所需 Pane 之前完成。
        // 否则坏快照虽然最终被拒绝，EnsureSnapshotDocks 仍可能把空 Pane 留在默认布局中。
        if (DockLayoutRuntimeValidator.ValidateContributions(
                snapshot,
                session) is { } contributionError)
        {
            store.RejectLoadedSnapshot(
                contributionError.Code,
                contributionError.StableId);
            return defaultRoot;
        }

        DockLayoutSnapshotMapper.EnsureSnapshotDocks(
            snapshot,
            defaultRoot,
            session);
        if (DockLayoutRuntimeValidator.Validate(
                snapshot,
                defaultRoot,
                session) is { } error)
        {
            store.RejectLoadedSnapshot(error.Code, error.StableId);
            return defaultRoot;
        }

        try
        {
            DockLayoutSnapshotMapper.ApplySnapshot(
                snapshot,
                defaultRoot,
                session);
            return defaultRoot;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            store.RejectLoadedSnapshot("LAYOUT_APPLY_FAILED", null);
            var replacement = session.RecreateLayoutAfterFailedRestore();
            session.DockFactory.InitLayout(replacement);
            return replacement;
        }
    }

    internal void Save(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.RootDock is not { } root)
        {
            return;
        }

        try
        {
            store.Save(DockLayoutSnapshotMapper.Capture(root, session));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
        {
            store.Report("LAYOUT_SAVE_FAILED", null, exception);
        }
    }

}
