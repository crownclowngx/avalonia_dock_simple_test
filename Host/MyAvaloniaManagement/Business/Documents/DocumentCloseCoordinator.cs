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
    private readonly WorkbenchDocumentCommandLeaseStore _commandLeases =
        commandLeases ?? throw new ArgumentNullException(nameof(commandLeases));

    internal bool IsDirty(ManagedDocumentDockable document) =>
        persistenceStates.IsDirty(document);

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

        if (_pending.Contains(document))
        {
            return false;
        }

        if (document.PersistableModel is not null && persistenceStates.IsDirty(document))
        {
            _pending.Add(document);
            _ = ConfirmDockCloseAsync(document, retryClose);
            return false;
        }

        var drain = _commandLeases.BeginClose(document);
        if (drain.IsCompletedSuccessfully)
        {
            return true;
        }

        _pending.Add(document);
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

    internal async Task<bool> ConfirmWindowCloseAsync(
        IReadOnlyList<ManagedDocumentDockable> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (_windowRequestPending)
        {
            return false;
        }

        var dirty = documents
            .Where(persistenceStates.IsDirty)
            .ToArray();
        if (dirty.Length == 0)
        {
            return true;
        }

        _windowRequestPending = true;
        try
        {
            var choice = await interactionService.ConfirmCloseAsync(
                dirty.Select(GetDisplayName).ToArray(),
                isApplicationExit: true);
            if (choice == DocumentCloseChoice.Cancel)
            {
                return false;
            }

            if (choice == DocumentCloseChoice.Discard)
            {
                return true;
            }

            foreach (var document in dirty)
            {
                var result = await saveService.SaveAsync(document);
                if (!result.IsSaved)
                {
                    if (!string.IsNullOrWhiteSpace(result.Message))
                    {
                        await ShowErrorSafelyAsync(result.Message);
                    }
                    return false;
                }

                if (result.HasPendingChanges)
                {
                    await ShowErrorSafelyAsync(CombineMessages(
                        result.Message,
                        PendingChangesMessage));
                    return false;
                }

                if (result.Status == DocumentSaveStatus.SavedWithWarning)
                {
                    await ShowErrorSafelyAsync(result.Message);
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            // 确认窗口和插件保存回调都属于可失败边界。窗口退出时一律选择“保持打开”，
            // 防止 UI 异常绕过脏文档保护；异常正文只进入内部诊断。
            DocumentPersistenceErrorMapper.Report(
                "DOCUMENT_WINDOW_CLOSE_CALLBACK_FAILED",
                exception);
            await ShowErrorSafelyAsync("无法完成关闭确认。Document 保持打开。");
            return false;
        }
        finally
        {
            _windowRequestPending = false;
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
