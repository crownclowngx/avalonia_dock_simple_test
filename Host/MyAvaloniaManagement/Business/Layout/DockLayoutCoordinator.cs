using System;
using System.IO;
using System.Threading;
using Dock.Model.Controls;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 协调布局持久化与主窗口生命周期，只负责 Prepare、Apply 和 Save 的执行顺序。
/// 快照映射、严格 V2 读取和运行时校验委托给专门组件，避免生命周期类理解所有 Dock 细节。
/// </summary>
internal sealed class DockLayoutLifecycle(DockLayoutStore store)
{
    private DockLayoutSnapshotV2? _pendingSnapshot;

    internal IRootDock Prepare(ManagementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _pendingSnapshot = store.Load();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        return root;
    }

    internal IRootDock ApplyPending(
        IRootDock defaultRoot,
        ManagementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(defaultRoot);
        ArgumentNullException.ThrowIfNull(factory);

        var snapshot = Interlocked.Exchange(ref _pendingSnapshot, null);
        if (snapshot is null)
        {
            return defaultRoot;
        }

        // Tool 声明与生命周期可用性不依赖 Dock 树，必须在补建任何快照所需 Pane 之前完成。
        // 否则坏快照虽然最终被拒绝，EnsureSnapshotDocks 仍可能把空 Pane 留在默认布局中。
        if (DockLayoutSnapshotMapper.ValidateContributions(snapshot, factory) is { } contributionError)
        {
            store.RejectLoadedSnapshot(
                contributionError.Code,
                contributionError.StableId);
            return defaultRoot;
        }

        DockLayoutSnapshotMapper.EnsureSnapshotDocks(
            snapshot,
            defaultRoot,
            factory);
        if (DockLayoutRuntimeValidator.Validate(
                snapshot,
                defaultRoot,
                factory) is { } error)
        {
            store.RejectLoadedSnapshot(error.Code, error.StableId);
            return defaultRoot;
        }

        try
        {
            DockLayoutSnapshotMapper.ApplySnapshot(
                snapshot,
                defaultRoot,
                factory);
            return defaultRoot;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            store.RejectLoadedSnapshot("LAYOUT_APPLY_FAILED", null);
            var replacement = factory.CreateLayout();
            factory.InitLayout(replacement);
            return replacement;
        }
    }

    internal void Save(IRootDock root, ManagementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(factory);

        try
        {
            store.Save(DockLayoutSnapshotMapper.Capture(root, factory));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
        {
            store.Report("LAYOUT_SAVE_FAILED", null, exception);
        }
    }

    // 测试与宿主内部调用共用同一映射入口，避免测试复制 Dock 树遍历规则。
    internal static DockLayoutSnapshotV2 Capture(
        IRootDock root,
        ManagementFactory factory) =>
        DockLayoutSnapshotMapper.Capture(root, factory);

    internal static void ApplySnapshot(
        DockLayoutSnapshotV2 snapshot,
        IRootDock root,
        ManagementFactory factory) =>
        DockLayoutSnapshotMapper.ApplySnapshot(snapshot, root, factory);
}

/// <summary>
/// 使用当前插件、稳定 ID 和 Dock 节点验证待应用快照。
/// 校验先于修改运行时布局，保证整体无效时仍可安全回退默认结构。
/// </summary>
internal static class DockLayoutRuntimeValidator
{
    internal static DockLayoutValidationError? Validate(
        DockLayoutSnapshotV2 snapshot,
        IRootDock root,
        ManagementFactory factory) =>
        DockLayoutSnapshotMapper.ValidateAgainstRuntime(
            snapshot,
            root,
            factory);
}
