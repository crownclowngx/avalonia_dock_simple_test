using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Documents;

internal enum DocumentSaveStatus
{
    Saved,
    SavedWithWarning,
    Canceled,
    Failed,
    NotPersistable,
}

/// <summary>描述一次保存是否已经完成唯一主文件提交。</summary>
internal readonly record struct DocumentSaveResult(
    DocumentSaveStatus Status,
    string Message,
    bool HasPendingChanges = false)
{
    internal bool IsSaved =>
        Status is DocumentSaveStatus.Saved or DocumentSaveStatus.SavedWithWarning;
}

/// <summary>负责捕获带修订号的内容、原子写入主文件并执行提交后的状态更新。</summary>
/// <remarks>
/// 本服务不选择活动标签、不发布 Dock，也不处理关闭确认。主文件原子写入是唯一业务提交点；
/// `AcceptChanges` 与恢复备份均属于提交后的动作，失败只能产生“已保存但有警告”，不能篡改磁盘事实。
/// </remarks>
internal sealed class DocumentSaveService(
    IHostStorageService storageService,
    DocumentEnvelopeSerializer serializer,
    DocumentOperationGate operationGate,
    DocumentPersistenceStateStore persistenceStates,
    DocumentRecoveryRegistry recoveryRegistry,
    TimeProvider timeProvider)
{
    internal Task<DocumentSaveResult> SaveAsync(ManagedDocumentDockable document) =>
        operationGate.RunAsync(() => SaveCoreAsync(document));

    private async Task<DocumentSaveResult> SaveCoreAsync(ManagedDocumentDockable document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var persistable = document.PersistableModel;
        if (persistable is null)
        {
            return new(DocumentSaveStatus.NotPersistable, string.Empty);
        }

        if (!persistenceStates.TryGet(document, out var state))
        {
            return new(
                DocumentSaveStatus.Failed,
                "该 Document 没有匹配的 Host 持久化状态，已拒绝保存。");
        }

        var isRecovered = recoveryRegistry.TryGet(document, out var recovery);
        var filePath = state.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || isRecovered)
        {
            filePath = await storageService.PickSaveFileAsync(
                state.Registration.Descriptor.DisplayName);
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new(DocumentSaveStatus.Canceled, string.Empty);
        }

        filePath = DocumentPathIdentity.Normalize(filePath);
        if (isRecovered &&
            (DocumentPathIdentity.Equals(filePath, recovery!.SourcePath) ||
             DocumentPathIdentity.Equals(filePath, recovery.BackupPath)))
        {
            return new(
                DocumentSaveStatus.Failed,
                "恢复出的 Document 必须另存为新文件，不能覆盖损坏原件或恢复备份。");
        }

        var hostTitle = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(hostTitle))
        {
            hostTitle = string.IsNullOrWhiteSpace(state.HostTitle)
                ? state.Registration.Descriptor.DisplayName
                : state.HostTitle;
        }

        string envelopeJson;
        DocumentSaveSnapshot snapshot;
        try
        {
            snapshot = await persistable.CaptureSaveSnapshotAsync(document.ClosingToken);
            if (snapshot is null)
            {
                throw new InvalidOperationException("插件返回了 null DocumentSaveSnapshot。");
            }

            envelopeJson = serializer.Serialize(
                state.Registration.OwnerId,
                state.Registration.Descriptor.DocumentTypeId,
                hostTitle,
                timeProvider.GetUtcNow(),
                snapshot.Content);
            await storageService.WriteAllTextAsync(filePath, envelopeJson);
        }
        catch (OperationCanceledException) when (document.ClosingToken.IsCancellationRequested)
        {
            return new(DocumentSaveStatus.Canceled, string.Empty);
        }
        catch (Exception exception)
        {
            // 插件边界允许第三方实现抛出自定义异常。保存服务不能把该类型或正文泄漏给 UI，
            // 也不能让异常越过关闭协调器形成未观察任务；统一记录内部诊断并返回稳定失败。
            DocumentPersistenceErrorMapper.Report("DOCUMENT_SAVE_FAILED", exception);
            return new(
                DocumentSaveStatus.Failed,
                DocumentPersistenceErrorMapper.SaveFailureMessage);
        }

        // 从这里开始主文件已经提交。Host 状态先于插件回调更新，确保插件回调缺陷不能让
        // 路径查重和恢复保护继续停留在旧事实上。
        persistenceStates.CommitFile(document, filePath, hostTitle);
        recoveryRegistry.Clear(document);

        var warnings = new List<string>();
        var hasPendingChanges = false;
        try
        {
            persistable.AcceptChanges(snapshot.Revision);
            // AcceptChanges 正常返回后仍为 Dirty，表示插件明确拒绝了旧修订确认：保存期间已经
            // 产生了较新的内容。主文件提交仍然成功，但关闭协调器必须保持 Document 打开。
            hasPendingChanges = persistable.IsDirty;
        }
        catch (Exception exception)
        {
            DocumentPersistenceErrorMapper.Report("DOCUMENT_ACCEPT_CHANGES_FAILED", exception);
            warnings.Add(DocumentPersistenceErrorMapper.AcceptChangesFailureMessage);
        }
        finally
        {
            // 插件事件实现可能选择异步投递。保存返回前直接重读最终事实，避免标签短暂或
            // 永久保留旧的修改标记；保存命令与关闭保存都从 Host UI 协调入口调用本服务。
            document.RefreshModifiedState();
        }

        try
        {
            await storageService.WriteAllTextAsync(
                DocumentRecoveryRegistry.GetBackupPath(filePath),
                envelopeJson);
        }
        catch (Exception exception)
        {
            DocumentPersistenceErrorMapper.Report("DOCUMENT_BACKUP_FAILED", exception);
            warnings.Add(DocumentPersistenceErrorMapper.BackupFailureMessage);
        }

        return warnings.Count == 0
            ? new(DocumentSaveStatus.Saved, string.Empty, hasPendingChanges)
            : new(
                DocumentSaveStatus.SavedWithWarning,
                string.Join(" ", warnings),
                hasPendingChanges);
    }

}
