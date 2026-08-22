using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>表示 Document 操作对共享用户提示的更新意图。</summary>
internal readonly record struct DocumentOperationResult(
    bool ShouldUpdateError,
    string Error)
{
    internal static DocumentOperationResult NoChange => new(false, string.Empty);
    internal static DocumentOperationResult ClearError => new(true, string.Empty);
    internal static DocumentOperationResult Failure(string error) => new(true, error);
}

/// <summary>编排当前 V3 Document 的新建、打开、恢复和活动项保存。</summary>
/// <remarks>
/// 本类型只负责编排用例，不解释 JSON、不写原子文件，也不拥有 Scope。所有入口共享同一个串行门，
/// 因而并发打开同一路径时，后一个请求必定能观察到前一个已经提交的工作区状态。
/// </remarks>
internal sealed class DocumentPersistenceCoordinator(
    WorkspaceSession workspace,
    IHostStorageService storageService,
    DocumentSaveService saveService,
    DocumentOperationGate operationGate,
    DocumentPersistenceStateStore persistenceStates,
    DocumentRecoveryRegistry recoveryRegistry,
    IDocumentInteractionService interactionService,
    DocumentEnvelopeSerializer serializer,
    DocumentOperationState operationState) : IHostDocumentOpenService
{
    internal Task<DocumentOperationResult> CreateDocumentAsync(
        DocumentTypeId documentTypeId,
        CreationIntentId? creationIntentId = null) =>
        operationGate.RunAsync(async () =>
        {
            try
            {
                await workspace.CreateAndPublishDocumentAsync(
                    documentTypeId,
                    new NewDocumentActivation(
                        title: string.Empty,
                        creationIntentId));
                return DocumentOperationResult.ClearError;
            }
            catch (Exception exception)
            {
                DocumentPersistenceErrorMapper.Report(
                    "DOCUMENT_INITIALIZATION_FAILED",
                    exception);
                return DocumentOperationResult.Failure(
                    "无法创建 Document：插件初始化未完成。未发布任何标签。");
            }
        });

    internal async Task<DocumentOperationResult> OpenSelectedAsync()
    {
        var paths = await storageService.PickOpenFilesAsync();
        return await OpenAllAsync(paths);
    }

    internal async Task<DocumentOperationResult> OpenPathAsync(
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !storageService.FileExists(filePath))
        {
            DocumentPersistenceErrorMapper.Report("DOCUMENT_OPEN_FILE_NOT_FOUND");
            return DocumentOperationResult.NoChange;
        }

        return await OpenAllAsync([filePath]);
    }

    async Task IHostDocumentOpenService.OpenPathAsync(string filePath)
    {
        try
        {
            operationState.Apply(await OpenPathAsync(filePath));
        }
        catch (Exception exception)
        {
            operationState.ReportUnexpectedOpenFailure(exception);
        }
    }

    internal async Task<DocumentOperationResult> SaveActiveAsync()
    {
        if (workspace.GetActiveDocument() is not ManagedDocumentDockable document)
        {
            return DocumentOperationResult.NoChange;
        }

        var result = await saveService.SaveAsync(document);
        return result.Status switch
        {
            DocumentSaveStatus.Saved => DocumentOperationResult.ClearError,
            DocumentSaveStatus.SavedWithWarning =>
                DocumentOperationResult.Failure(result.Message),
            DocumentSaveStatus.Canceled or DocumentSaveStatus.NotPersistable =>
                DocumentOperationResult.NoChange,
            _ => DocumentOperationResult.Failure(result.Message),
        };
    }

    private Task<DocumentOperationResult> OpenAllAsync(
        IReadOnlyList<string> paths) =>
        operationGate.RunAsync(async () =>
        {
            var result = DocumentOperationResult.NoChange;
            foreach (var path in paths)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !storageService.FileExists(path))
                    {
                        continue;
                    }

                    var normalizedPath = DocumentPathIdentity.Normalize(path);
                    if (workspace.TryActivateDocument(normalizedPath))
                    {
                        continue;
                    }

                    await LoadPrimaryOrRecoveryAsync(normalizedPath);
                    result = DocumentOperationResult.ClearError;
                }
                catch (Exception exception) when (IsExpectedPersistenceFailure(exception))
                {
                    result = DocumentOperationResult.Failure(
                        DocumentPersistenceErrorMapper.ToOpenFailureMessage(exception));
                    DocumentPersistenceErrorMapper.Report("DOCUMENT_OPEN_FAILED", exception);
                }
            }

            return result;
        });

    private async Task LoadPrimaryOrRecoveryAsync(string primaryPath)
    {
        ExceptionDispatchInfo primaryFailure;
        try
        {
            var primary = await CreateLoadedDocumentAsync(primaryPath);
            PublishLoadedDocument(primary, recoverySourcePath: null);
            return;
        }
        catch (Exception exception) when (IsRecoverableOpenFailure(exception))
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }

        var backupPath = DocumentRecoveryRegistry.GetBackupPath(primaryPath);
        if (!storageService.FileExists(backupPath))
        {
            primaryFailure.Throw();
        }

        ManagedDocumentDockable backup;
        try
        {
            // 只有备份已经通过严格信封、Registry、模型初始化和 View 预构建后才询问用户，
            // 避免把不可用备份展示成虚假的恢复机会。
            backup = await CreateLoadedDocumentAsync(backupPath);
        }
        catch (Exception exception) when (IsRecoverableOpenFailure(exception))
        {
            throw new DocumentEnvelopeException(
                "主文件及恢复备份均无法安全初始化。",
                exception);
        }

        if (!await interactionService.ConfirmRecoveryAsync(Path.GetFileName(primaryPath)))
        {
            workspace.ReleaseDocument(backup);
            primaryFailure.Throw();
        }

        PublishLoadedDocument(backup, primaryPath);
    }

    private async Task<ManagedDocumentDockable> CreateLoadedDocumentAsync(string filePath)
    {
        serializer.ValidateFileLength(storageService.GetFileLength(filePath));
        var json = await storageService.ReadAllTextAsync(filePath);
        var envelope = serializer.Deserialize(json);
        if (!workspace.TryGetPersistablePluginDocumentRegistration(
                envelope.DocumentTypeId,
                out var registration))
        {
            throw new NotSupportedException("当前 Host 没有注册该可持久化 Document 类型。");
        }

        if (registration.OwnerId != envelope.PluginId)
        {
            throw new DocumentEnvelopeException(
                "Document 声明的插件所有者与当前 Registry 不匹配。");
        }

        ManagedDocumentDockable? pending = null;
        try
        {
            try
            {
                pending = await workspace.CreateDocumentAsync(
                    envelope.DocumentTypeId,
                    new RestoreDocumentActivation(
                        envelope.Title,
                        envelope.Content));
            }
            catch (Exception exception)
            {
                throw new DocumentEnvelopeException(
                    "插件未能初始化 Document 内容。",
                    exception);
            }

            persistenceStates.CommitFile(pending, filePath, envelope.Title);
            var loaded = pending;
            pending = null;
            return loaded;
        }
        finally
        {
            if (pending is not null)
            {
                workspace.ReleaseDocument(pending);
            }
        }
    }

    private void PublishLoadedDocument(
        ManagedDocumentDockable document,
        string? recoverySourcePath)
    {
        ManagedDocumentDockable? pending = document;
        try
        {
            if (recoverySourcePath is not null)
            {
                var recoveredTitle = $"{document.HostTitle}（已恢复）";
                persistenceStates.MarkRecovered(document, recoveredTitle);
                recoveryRegistry.Register(document, recoverySourcePath);
            }

            workspace.PublishDocument(document);
            pending = null;
        }
        finally
        {
            if (pending is not null)
            {
                recoveryRegistry.Clear(pending);
                workspace.ReleaseDocument(pending);
            }
        }
    }

    private static bool IsRecoverableOpenFailure(Exception exception) =>
        exception is DocumentEnvelopeException or JsonException;

    private static bool IsExpectedPersistenceFailure(Exception exception) =>
        exception is DocumentEnvelopeException or
            IOException or
            UnauthorizedAccessException or
            JsonException or
            ArgumentException or
            NotSupportedException;
}
