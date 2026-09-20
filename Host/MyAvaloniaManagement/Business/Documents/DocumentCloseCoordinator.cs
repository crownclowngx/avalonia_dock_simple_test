using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Docking;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 协调同步 Dock 关闭回调与异步用户确认、保存操作。
/// </summary>
/// <remarks>
/// Dock 的 OnDockableClosing 必须同步返回，文件选择器和确认窗口却是异步的。因此首次关闭
/// 必须先被否决；用户完成决策后再授予一次性许可并重入 CloseDockable。一次性许可防止
/// 第二次回调重复弹窗，同时不会在真正关闭前触发 Document 生命周期取消。
/// </remarks>
internal sealed class DocumentCloseCoordinator(
    DocumentSaveService saveService,
    IDocumentInteractionService interactionService,
    DocumentPersistenceStateStore persistenceStates,
    WorkbenchDocumentCommandLeaseStore commandLeases)
{
    private const string PendingChangesMessage =
        "保存期间 Document 又发生了修改，请再次保存后再关闭。";
    private readonly HashSet<ManagedDocumentDockable> _approvedOnce = [];
    private readonly HashSet<ManagedDocumentDockable> _pending = [];
    private bool _windowRequestPending;
    private bool _rangeRequestPending;
    private readonly WorkbenchDocumentCommandLeaseStore _commandLeases =
        commandLeases ?? throw new ArgumentNullException(nameof(commandLeases));

    /// <summary>关闭等待开始或解除时通知展示层重读；通知异常不能改变关闭决策。</summary>
    internal event EventHandler? StateChanged;

    private void NotifyStateChanged()
    {
        foreach (EventHandler handler in StateChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception) { DocumentPersistenceErrorMapper.Report("DOCUMENT_CLOSE_OBSERVER_FAILED", exception); }
        }
    }

    internal bool IsDirty(ManagedDocumentDockable document) =>
        persistenceStates.IsDirty(document);

    /// <summary>页面定位遵守与关闭相同的事实，包括正在询问保存和等待命令排空的阶段。</summary>
    internal bool IsClosing(ManagedDocumentDockable document) =>
        _windowRequestPending || _pending.Contains(document) || _approvedOnce.Contains(document) || _commandLeases.IsClosing(document);

    internal bool TryBeginDockClose(
        ManagedDocumentDockable document,
        Action retryClose)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(retryClose);

        if (_approvedOnce.Remove(document))
        {
            return true;
        }

        if (_windowRequestPending || _pending.Contains(document))
        {
            return false;
        }

        if (document.PersistableModel is not null && persistenceStates.IsDirty(document))
        {
            _pending.Add(document);
            NotifyStateChanged();
            _ = ConfirmDockCloseAsync(document, retryClose);
            return false;
        }

        var drain = _commandLeases.BeginClose(document);
        if (drain.IsCompletedSuccessfully)
        {
            return true;
        }

        _pending.Add(document);
        NotifyStateChanged();
        _ = RetryAfterCommandDrainAsync(document, retryClose, drain);
        return false;
    }

    /// <summary>Dock 基类最终拒绝关闭时恢复该 Document 的命令入口。</summary>
    internal void ReopenAfterDockRejection(ManagedDocumentDockable document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _approvedOnce.Remove(document);
        _pending.Remove(document);
        _commandLeases.Reopen(document);
        NotifyStateChanged();
    }

    /// <summary>Document 最终关闭或创建回滚后清除命令租约和关闭协调状态。</summary>
    internal void CompleteDockClose(ManagedDocumentDockable? document)
    {
        if (document is null)
        {
            return;
        }
        _approvedOnce.Remove(document);
        _pending.Remove(document);
        _commandLeases.CompleteClose(document);
    }

    /// <summary>保留主窗口现有确认入口；正在关闭的浮窗范围必须先结束，避免两轮确认相互授权。</summary>
    internal async Task<bool> ConfirmWindowCloseAsync(
        IReadOnlyList<ManagedDocumentDockable> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (_windowRequestPending || _rangeRequestPending || documents.Any(_pending.Contains)) return false;
        _windowRequestPending = true;
        NotifyStateChanged();
        try { return await RequestDecisionAsync(documents, isApplicationExit: true) is not null; }
        finally
        {
            _windowRequestPending = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// 为固定文档集合准备一次关闭许可。先完整确认和保存，再禁止新命令并等待所有目标排空；
    /// 这里不拆除标签、不取消 Document 最终生命周期，也不释放 Scope。调用者提交或撤销后
    /// 必须释放许可，未被框架真正关闭的文档才能重新接受命令。
    /// </summary>
    /// <remarks>
    /// 本协调器按 UI 线程串行使用。只保留一轮范围关闭，防止主窗、子窗和单页回调交错消费
    /// 彼此的许可。命令排空后的脏状态再次检查，覆盖原本干净的页面在等待期间出现修改。
    /// </remarks>
    internal async Task<DocumentCloseApproval?> PrepareRangeCloseAsync(
        IReadOnlyList<ManagedDocumentDockable> documents,
        bool isApplicationExit = false)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var targets = documents.Distinct().ToArray();
        // 单页最初还有相邻页面，因此先按单页协议确认；等待期间邻页可能被关闭/移走，重试时
        // 它已成为浮窗最后一项。此时借用已取得的一次性许可交给整窗收尾，不能再次申请范围
        // 确认而被自己的 pending/命令关闭状态拒绝。仅允许精确的一页接续，不扩大授权范围。
        if (!isApplicationExit && !_windowRequestPending && !_rangeRequestPending &&
            targets is [var approved] && _approvedOnce.Contains(approved))
        {
            _rangeRequestPending = true;
            return new DocumentCloseApproval(() => ReleaseRange(targets));
        }
        if (_windowRequestPending || _rangeRequestPending || targets.Any(IsClosing)) return null;
        _rangeRequestPending = true;
        foreach (var document in targets) _pending.Add(document);
        NotifyStateChanged();
        var granted = false;
        try
        {
            var discarded = await RequestDecisionAsync(targets, isApplicationExit);
            if (discarded is null) return null;
            await Task.WhenAll(targets.Select(_commandLeases.BeginClose));
            if (targets.Any(document => persistenceStates.IsDirty(document) && !discarded.Contains(document)))
            {
                await ShowErrorSafelyAsync(PendingChangesMessage);
                return null;
            }
            foreach (var document in targets) _approvedOnce.Add(document);
            granted = true;
            return new DocumentCloseApproval(() => ReleaseRange(targets));
        }
        catch (Exception exception)
        {
            DocumentPersistenceErrorMapper.Report("DOCUMENT_RANGE_CLOSE_FAILED", exception);
            await ShowErrorSafelyAsync("无法完成关闭确认。Document 保持打开。");
            return null;
        }
        finally
        {
            if (!granted) ReleaseRange(targets);
        }
    }

    /// <summary>撤销仍未消费的许可；已关闭文档在 CompleteDockClose 中清理，因此重复解除安全。</summary>
    private void ReleaseRange(IReadOnlyList<ManagedDocumentDockable> targets)
    {
        foreach (var document in targets)
        {
            _approvedOnce.Remove(document);
            _pending.Remove(document);
            TryReopenCommands(document);
        }
        _rangeRequestPending = false;
        NotifyStateChanged();
    }

    /// <summary>返回允许放弃修改的集合；空集合表示无需确认或保存完成，null 表示保持打开。</summary>
    private async Task<HashSet<ManagedDocumentDockable>?> RequestDecisionAsync(
        IReadOnlyList<ManagedDocumentDockable> documents,
        bool isApplicationExit)
    {
        try
        {
            var dirty = documents.Where(persistenceStates.IsDirty).ToArray();
            if (dirty.Length == 0) return [];
            var choice = await interactionService.ConfirmCloseAsync(
                dirty.Select(GetDisplayName).ToArray(), isApplicationExit);
            if (choice == DocumentCloseChoice.Cancel) return null;
            if (choice == DocumentCloseChoice.Discard) return new HashSet<ManagedDocumentDockable>(dirty);
            foreach (var document in dirty)
            {
                var result = await saveService.SaveAsync(document);
                if (!result.IsSaved)
                {
                    if (!string.IsNullOrWhiteSpace(result.Message)) await ShowErrorSafelyAsync(result.Message);
                    return null;
                }
                if (result.HasPendingChanges)
                {
                    await ShowErrorSafelyAsync(CombineMessages(result.Message, PendingChangesMessage));
                    return null;
                }
                if (result.Status == DocumentSaveStatus.SavedWithWarning)
                    await ShowErrorSafelyAsync(result.Message);
            }
            return [];
        }
        catch (Exception exception)
        {
            // 确认和插件保存都是可失败边界。无论哪一处失败，默认保持页面，异常正文仅入诊断。
            DocumentPersistenceErrorMapper.Report("DOCUMENT_WINDOW_CLOSE_CALLBACK_FAILED", exception);
            await ShowErrorSafelyAsync("无法完成关闭确认。Document 保持打开。");
            return null;
        }
    }

    private async Task ConfirmDockCloseAsync(
        ManagedDocumentDockable document,
        Action retryClose)
    {
        try
        {
            var choice = await interactionService.ConfirmCloseAsync(
                [GetDisplayName(document)],
                isApplicationExit: false);
            if (choice == DocumentCloseChoice.Cancel)
            {
                return;
            }

            if (choice == DocumentCloseChoice.Save)
            {
                var result = await saveService.SaveAsync(document);
                if (!result.IsSaved)
                {
                    if (!string.IsNullOrWhiteSpace(result.Message))
                    {
                        await ShowErrorSafelyAsync(result.Message);
                    }
                    return;
                }

                if (result.HasPendingChanges)
                {
                    await ShowErrorSafelyAsync(CombineMessages(
                        result.Message,
                        PendingChangesMessage));
                    return;
                }

                if (result.Status == DocumentSaveStatus.SavedWithWarning)
                {
                    await ShowErrorSafelyAsync(result.Message);
                }
            }

            var drain = _commandLeases.BeginClose(document);
            await drain;
            _approvedOnce.Add(document);
            retryClose();
        }
        catch (Exception exception)
        {
            // 此任务由同步 Dock 回调启动，不能把异常遗留为未观察任务。任何交互或重入失败
            // 都维持 Document 打开，并清除 pending，允许用户稍后重新尝试。
            DocumentPersistenceErrorMapper.Report(
                "DOCUMENT_DOCK_CLOSE_CALLBACK_FAILED",
                exception);
            _approvedOnce.Remove(document);
            TryReopenCommands(document);
            await ShowErrorSafelyAsync("无法完成关闭确认。Document 保持打开。");
        }
        finally
        {
            _pending.Remove(document);
            NotifyStateChanged();
        }
    }

    /// <summary>等待干净 Document 的在途命令退出，再授予一次性关闭许可并重试。</summary>
    private async Task RetryAfterCommandDrainAsync(
        ManagedDocumentDockable document,
        Action retryClose,
        Task drain)
    {
        try
        {
            await drain;
            _approvedOnce.Add(document);
            retryClose();
        }
        catch (Exception exception)
        {
            DocumentPersistenceErrorMapper.Report(
                "DOCUMENT_COMMAND_DRAIN_CALLBACK_FAILED",
                exception);
            _approvedOnce.Remove(document);
            TryReopenCommands(document);
            await ShowErrorSafelyAsync("无法安全排空 Document 命令。Document 保持打开。");
        }
        finally
        {
            _pending.Remove(document);
            NotifyStateChanged();
        }
    }

    private void TryReopenCommands(ManagedDocumentDockable document)
    {
        try
        {
            _commandLeases.Reopen(document);
        }
        catch (Exception exception)
        {
            DocumentPersistenceErrorMapper.Report(
                "DOCUMENT_COMMAND_REOPEN_FAILED",
                exception);
        }
    }

    private static string GetDisplayName(ManagedDocumentDockable document) =>
        string.IsNullOrWhiteSpace(document.Title) ? "未命名 Document" : document.Title;

    private static string CombineMessages(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first} {second}";

    /// <summary>错误提示自身失败时只记录诊断，不改变已经完成的保存或关闭决策。</summary>
    private async Task ShowErrorSafelyAsync(string message)
    {
        try
        {
            await interactionService.ShowErrorAsync(message);
        }
        catch (Exception exception)
        {
            DocumentPersistenceErrorMapper.Report(
                "DOCUMENT_ERROR_DIALOG_FAILED",
                exception);
        }
    }
}

/// <summary>
/// 一轮范围关闭的短期许可。它只拥有撤销动作，不拥有文档资源；窗口必须在同步提交完成、
/// 最终拒绝或目标变化时释放。交换委托使重复关闭通知不会二次撤销下一轮许可。
/// </summary>
internal sealed class DocumentCloseApproval(Action release) : IDisposable
{
    private Action? _release = release;
    public void Dispose() => System.Threading.Interlocked.Exchange(ref _release, null)?.Invoke();
}
