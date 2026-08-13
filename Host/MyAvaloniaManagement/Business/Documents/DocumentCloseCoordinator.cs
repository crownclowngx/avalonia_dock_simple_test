using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Save;

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
    IDocumentInteractionService interactionService)
{
    private readonly HashSet<Document> _approvedOnce = [];
    private readonly HashSet<Document> _pending = [];
    private bool _windowRequestPending;

    internal bool TryBeginDockClose(
        Document document,
        DocumentMetadata? metadata,
        Action retryClose)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(retryClose);

        if (_approvedOnce.Remove(document))
        {
            return true;
        }

        if (document is not ISavableDocument)
        {
            return true;
        }

        if (document is not IDocumentSaveState state)
        {
            _ = interactionService.ShowErrorAsync(
                "该 Document 未实现公共保存状态契约，宿主已拒绝关闭以避免丢失数据。");
            return false;
        }

        if (!state.IsDirty)
        {
            return true;
        }

        if (!_pending.Add(document))
        {
            return false;
        }

        _ = ConfirmDockCloseAsync(document, metadata, retryClose);
        return false;
    }

    internal async Task<bool> ConfirmWindowCloseAsync(
        IReadOnlyList<Document> documents,
        Func<Document, DocumentMetadata?> metadataResolver)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(metadataResolver);

        if (_windowRequestPending)
        {
            return false;
        }

        var dirty = documents
            .Where(document => document is ISavableDocument)
            .Select(document => new
            {
                Document = document,
                State = document as IDocumentSaveState,
            })
            .Where(item => item.State?.IsDirty == true)
            .Select(item => item.Document)
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
                var result = await saveService.SaveAsync(
                    document,
                    metadataResolver(document));
                if (!result.IsSaved)
                {
                    if (!string.IsNullOrWhiteSpace(result.Message))
                    {
                        await interactionService.ShowErrorAsync(result.Message);
                    }
                    return false;
                }

                if (result.Status == DocumentSaveStatus.SavedWithBackupWarning)
                {
                    await interactionService.ShowErrorAsync(result.Message);
                }
            }

            return true;
        }
        finally
        {
            _windowRequestPending = false;
        }
    }

    private async Task ConfirmDockCloseAsync(
        Document document,
        DocumentMetadata? metadata,
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
                var result = await saveService.SaveAsync(document, metadata);
                if (!result.IsSaved)
                {
                    if (!string.IsNullOrWhiteSpace(result.Message))
                    {
                        await interactionService.ShowErrorAsync(result.Message);
                    }
                    return;
                }

                if (result.Status == DocumentSaveStatus.SavedWithBackupWarning)
                {
                    await interactionService.ShowErrorAsync(result.Message);
                }
            }

            _approvedOnce.Add(document);
            retryClose();
        }
        finally
        {
            _pending.Remove(document);
        }
    }

    private static string GetDisplayName(Document document) =>
        string.IsNullOrWhiteSpace(document.Title) ? "未命名 Document" : document.Title;
}
