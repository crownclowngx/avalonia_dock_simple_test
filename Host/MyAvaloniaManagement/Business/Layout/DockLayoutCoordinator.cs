using System;
using System.IO;
using System.Linq;
using System.Threading;
using Dock.Model.Controls;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 协调布局持久化与主窗口生命周期，只负责 Prepare、Apply 和 Save 的执行顺序。
/// 快照映射、兼容迁移和运行时校验委托给专门组件，避免生命周期类理解所有 Dock 细节。
/// </summary>
internal sealed class DockLayoutLifecycle(DockLayoutStore store)
{
    private DockLayoutSnapshotV1? _pendingSnapshot;

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

        snapshot = DockLayoutSnapshotMigrator.Normalize(snapshot, factory);
        if (DockLayoutSnapshotValidator.Validate(snapshot) is { } migratedError)
        {
            store.RejectLoadedSnapshot(migratedError.Code, migratedError.StableId);
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
            Console.Error.WriteLine(
                $"DockLayout errorCode=LAYOUT_SAVE_FAILED stableId=- type={exception.GetType().Name}");
        }
    }

    // 保留兼容入口供既有宿主集成测试和内部调用使用，实际逻辑委托给职责单一的组件。
    internal static DockLayoutSnapshotV1 Capture(
        IRootDock root,
        ManagementFactory factory) =>
        DockLayoutSnapshotMapper.Capture(root, factory);

    internal static DockLayoutSnapshotV1 NormalizeLegacyTwoWaySnapshot(
        DockLayoutSnapshotV1 snapshot,
        ManagementFactory factory) =>
        DockLayoutSnapshotMigrator.Normalize(snapshot, factory);

    internal static void ApplySnapshot(
        DockLayoutSnapshotV1 snapshot,
        IRootDock root,
        ManagementFactory factory) =>
        DockLayoutSnapshotMapper.ApplySnapshot(snapshot, root, factory);
}

/// <summary>
/// 仅执行现有布局快照的兼容归一化。
/// 独立该职责是为了把版本兼容规则与运行时 Dock 校验分开，且明确不引入新版本格式。
/// </summary>
internal static class DockLayoutSnapshotMigrator
{
    internal static DockLayoutSnapshotV1 Normalize(
        DockLayoutSnapshotV1 snapshot,
        ManagementFactory factory)
    {
        // 先迁移 Tool 身份，再执行历史停靠方向修复；否则旧 ID 无法查询新元数据的默认方向。
        var migratedTools = snapshot.Tools
            .Select(tool => tool with { Id = factory.NormalizePersistedToolId(tool.Id) })
            .ToList();
        var activeToolId = snapshot.ActiveToolId is null
            ? null
            : factory.NormalizePersistedToolId(snapshot.ActiveToolId);
        var migrated = snapshot with
        {
            Tools = migratedTools,
            ActiveToolId = activeToolId,
        };
        return DockLayoutSnapshotMapper.NormalizeLegacyTwoWaySnapshot(migrated, factory);
    }
}

/// <summary>
/// 使用当前插件、稳定 ID 和 Dock 节点验证待应用快照。
/// 校验先于修改运行时布局，保证整体无效时仍可安全回退默认结构。
/// </summary>
internal static class DockLayoutRuntimeValidator
{
    internal static DockLayoutValidationError? Validate(
        DockLayoutSnapshotV1 snapshot,
        IRootDock root,
        ManagementFactory factory) =>
        DockLayoutSnapshotMapper.ValidateAgainstRuntime(
            snapshot,
            root,
            factory);
}
