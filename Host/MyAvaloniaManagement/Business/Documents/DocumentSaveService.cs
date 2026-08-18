using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagementCommon.Save;

namespace MyAvaloniaManagement.Business.Documents;

internal enum DocumentSaveStatus
{
    Saved,
    SavedWithBackupWarning,
    Canceled,
    Failed,
    NotSavable,
}

/// <summary>
/// 保存操作的稳定结果。预期磁盘故障在此转换为结果，编程错误继续向上传播。
/// </summary>
internal readonly record struct DocumentSaveResult(
    DocumentSaveStatus Status,
    string Message)
{
    internal bool IsSaved =>
        Status is DocumentSaveStatus.Saved or DocumentSaveStatus.SavedWithBackupWarning;
}

/// <summary>
/// 对指定 Document 执行路径决策、主文件原子提交和恢复备份更新。
/// </summary>
/// <remarks>
/// 本服务不知道哪个 Dock 发起保存，也不负责关闭标签。把提交点集中于此可保证菜单保存、
/// 标签关闭和窗口退出对失败、取消及状态更新采用完全相同的语义。
/// </remarks>
internal sealed class DocumentSaveService(
    IHostStorageService storageService,
    DocumentEnvelopeSerializer serializer,
    DocumentOperationGate operationGate,
    DocumentPersistenceStateStore persistenceStates,
    DocumentRecoveryRegistry recoveryRegistry,
    TimeProvider timeProvider)
{
    internal Task<DocumentSaveResult> SaveAsync(Document document) =>
        operationGate.RunAsync(() => SaveCoreAsync(document));

    private async Task<DocumentSaveResult> SaveCoreAsync(Document document)
    {
        if (document is not ISavableDocument savableDocument)
        {
            return new(DocumentSaveStatus.NotSavable, string.Empty);
        }

        if (document is not IDocumentSaveState saveState)
        {
            return new(
                DocumentSaveStatus.Failed,
                "该 Document 未实现公共保存状态契约，宿主已拒绝保存。");
        }

        if (!persistenceStates.TryGet(document, out var persistenceState))
        {
            return new(
                DocumentSaveStatus.Failed,
                "该 Document 没有匹配的宿主注册所有权，已拒绝保存。");
        }

        var registration = persistenceState.Registration;
        var savePathPolicy = document as IDocumentSavePathPolicy;
        var originalPath = persistenceState.FilePath;
        var isRecovered = recoveryRegistry.TryGet(document, out var recovery);
        string? filePath;
        if (string.IsNullOrWhiteSpace(originalPath) ||
            isRecovered ||
            savePathPolicy?.RequiresSaveAs == true)
        {
            filePath = await storageService.PickSaveFileAsync(registration.Metadata);
        }
        else
        {
            filePath = originalPath;
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

        if (savePathPolicy?.RequiresSaveAs == true &&
            !string.IsNullOrWhiteSpace(originalPath) &&
            DocumentPathIdentity.Equals(originalPath, filePath))
        {
            return new(
                DocumentSaveStatus.Failed,
                $"{savePathPolicy.SaveAsReason} 请选择不同的文件路径。");
        }

        string fileName;
        string content;
        try
        {
            fileName = Path.GetFileNameWithoutExtension(filePath);
            // 内容快照不接收目标路径，也不能提交标题或脏状态。路径选择与业务序列化完全
            // 分离后，同一内存状态保存到不同位置仍会产生同一份插件 payload。
            var saveData = savableDocument.CreateContentSnapshot();
            content = serializer.Serialize(
                registration.OwnerId,
                registration.Metadata.DocumentTypeId,
                fileName,
                timeProvider.GetUtcNow(),
                saveData);

            // 主文件是唯一业务提交点。只有该原子写入完成后，内存 Document 才允许接受
            // 新基线；备份是恢复能力，不能反过来改变主文件已经成功提交的事实。
            await storageService.WriteAllTextAsync(filePath, content);
        }
        catch (Exception exception) when (IsExpectedPersistenceFailure(exception))
        {
            Console.Error.WriteLine(
                $"DocumentPersistence errorCode=DOCUMENT_SAVE_FAILED type={exception.GetType().Name}");
            return new(
                DocumentSaveStatus.Failed,
                "保存文档失败，请检查目标路径是否可写。文档状态未被修改。");
        }

        // 插件回调位于主文件事务之后，回调异常属于契约/编程错误，必须向上传播，不能被
        // 伪装成“磁盘未修改”的预期失败。否则磁盘已经更新，错误文案却会陈述相反事实。
        document.Title = fileName;
        persistenceStates.CommitFilePath(document, filePath);
        saveState.AcceptChanges();
        savePathPolicy?.NotifySaveCompleted(filePath);
        recoveryRegistry.Clear(document);

        try
        {
            await storageService.WriteAllTextAsync(
                DocumentRecoveryRegistry.GetBackupPath(filePath),
                content);
            return new(DocumentSaveStatus.Saved, string.Empty);
        }
        catch (Exception exception) when (IsExpectedPersistenceFailure(exception))
        {
            Console.Error.WriteLine(
                $"DocumentPersistence errorCode=DOCUMENT_BACKUP_FAILED type={exception.GetType().Name}");
            return new(
                DocumentSaveStatus.SavedWithBackupWarning,
                "文档已保存，但恢复备份更新失败；下次保存前请妥善保管主文件。");
        }
    }

    internal static bool IsExpectedPersistenceFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            ArgumentException or
            NotSupportedException;
}
