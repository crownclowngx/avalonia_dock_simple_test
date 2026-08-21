using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 表示文档操作对界面错误状态的更新意图，而不是把异常直接泄漏给 ViewModel。
/// “不变”“清空”和“失败”三种结果可保留用户取消等既有交互语义。
/// </summary>
internal readonly record struct DocumentOperationResult(
    bool ShouldUpdateError,
    string Error)
{
    internal static DocumentOperationResult NoChange => new(false, string.Empty);
    internal static DocumentOperationResult ClearError => new(true, string.Empty);
    internal static DocumentOperationResult Failure(string error) => new(true, error);
}

/// <summary>
/// 串行编排文档的打开与保存，并将预期的文件系统故障转换为稳定的操作结果。
/// 这样窗口 ViewModel 只处理绑定和定向协调，且并发请求不会重复打开或互相覆盖状态。
/// </summary>
internal sealed class DocumentPersistenceCoordinator(
    ManagementFactory factory,
    IHostStorageService storageService,
    DocumentSaveService saveService,
    DocumentOperationGate operationGate,
    DocumentPersistenceStateStore persistenceStates,
    DocumentRecoveryRegistry recoveryRegistry,
    IDocumentInteractionService interactionService,
    DocumentEnvelopeSerializer serializer,
    DocumentOperationState operationState) : IHostDocumentOpenService
{
    private readonly DocumentWorkspace _workspace = new(
        factory,
        persistenceStates,
        recoveryRegistry);

    internal void CreateDocument(string documentType)
    {
        factory.CreateAndPublishDocument(
            new DocumentCreationParams(DocumentTypeId.Parse(documentType)));
    }

    internal async Task<DocumentOperationResult> OpenSelectedAsync(IRootDock? root)
    {
        var paths = await storageService.PickOpenFilesAsync();
        return await OpenAllAsync(paths, root);
    }

    internal async Task<DocumentOperationResult> OpenPathAsync(
        string filePath,
        IRootDock? root)
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            !storageService.FileExists(filePath))
        {
            DocumentPersistenceErrorMapper.Report("DOCUMENT_OPEN_FILE_NOT_FOUND");
            return DocumentOperationResult.NoChange;
        }

        return await OpenAllAsync([filePath], root);
    }

    /// <summary>
    /// 处理来自文件树的直接打开请求，并把用户可见结果提交到共享状态。
    /// </summary>
    /// <remarks>
    /// 预期的持久化错误仍由内部打开流程转换为稳定结果；只有编程错误或第三方策略意外异常
    /// 会进入兜底分支。这个边界替代了原先主窗口对广播消息的 fire-and-forget 处理，因此
    /// 必须在异步命令内部观察异常，避免产生未观察任务，同时不能向界面泄漏异常正文。
    /// </remarks>
    async Task IHostDocumentOpenService.OpenPathAsync(string filePath)
    {
        try
        {
            operationState.Apply(await OpenPathAsync(filePath, factory.RootDock));
        }
        catch (Exception exception)
        {
            operationState.ReportUnexpectedOpenFailure(exception);
        }
    }

    internal async Task<DocumentOperationResult> SaveActiveAsync()
    {
        var activeDocument = _workspace.GetActiveDocument();
        if (activeDocument is not Document document)
        {
            return DocumentOperationResult.NoChange;
        }

        var result = await saveService.SaveAsync(document);
        return result.Status switch
        {
            DocumentSaveStatus.Saved => DocumentOperationResult.ClearError,
            DocumentSaveStatus.SavedWithBackupWarning =>
                DocumentOperationResult.Failure(result.Message),
            DocumentSaveStatus.Canceled or DocumentSaveStatus.NotSavable =>
                DocumentOperationResult.NoChange,
            _ => DocumentOperationResult.Failure(result.Message),
        };
    }

    private async Task<DocumentOperationResult> OpenAllAsync(
        IReadOnlyList<string> paths,
        IRootDock? root)
    {
        return await operationGate.RunAsync(async () =>
        {
            DocumentOperationResult result = DocumentOperationResult.NoChange;
            foreach (var path in paths)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) ||
                        !storageService.FileExists(path))
                    {
                        continue;
                    }

                    var normalizedPath = DocumentPathIdentity.Normalize(path);
                    if (_workspace.TryActivate(root, normalizedPath))
                    {
                        continue;
                    }

                    await LoadPrimaryOrRecoveryAsync(normalizedPath);
                }
                catch (Exception exception) when (
                    exception is DocumentLoadException ||
                    IsExpectedPersistenceFailure(exception))
                {
                    result = DocumentOperationResult.Failure(
                        DocumentPersistenceErrorMapper.ToOpenFailureMessage(exception));
                    DocumentPersistenceErrorMapper.Report(
                        "DOCUMENT_OPEN_FAILED",
                        exception);
                }
            }

            return result;
        });
    }

    private async Task LoadPrimaryOrRecoveryAsync(string primaryPath)
    {
        try
        {
            var primary = await CreateLoadedDocumentAsync(primaryPath);
            PublishLoadedDocument(primary, recoverySourcePath: null);
            return;
        }
        catch (Exception primaryException) when (
            primaryException is DocumentLoadException or JsonException)
        {
            var backupPath = DocumentRecoveryRegistry.GetBackupPath(primaryPath);
            if (!storageService.FileExists(backupPath))
            {
                throw;
            }

            Document backup;
            try
            {
                // 先在未发布的新 Scope 中完整加载备份，再询问用户。这样“发现恢复备份”
                // 明确表示备份已通过宿主信封和插件内容校验，不会先给出一个虚假的恢复入口。
                backup = await CreateLoadedDocumentAsync(backupPath);
            }
            catch (Exception backupException) when (
                backupException is DocumentLoadException or JsonException)
            {
                throw new DocumentLoadException(
                    "主文件及恢复备份均已损坏，无法安全恢复。",
                    backupException);
            }

            if (!await interactionService.ConfirmRecoveryAsync(Path.GetFileName(primaryPath)))
            {
                factory.ReleaseDocument(backup);
                throw;
            }

            PublishLoadedDocument(backup, primaryPath);
        }
    }

    private async Task<Document> CreateLoadedDocumentAsync(string filePath)
    {
        // 长度预检发生在整文件读取前；读取后的反序列化仍会按实际 UTF-8 字节复核，
        // 避免文件在检查与读取之间变化时绕过 8 MiB 门限。
        serializer.ValidateFileLength(storageService.GetFileLength(filePath));
        var content = await storageService.ReadAllTextAsync(filePath);
        var envelope = serializer.Deserialize(content);
        var canonicalTypeId = factory.NormalizePersistedDocumentTypeId(envelope.DocumentTypeId);
        if (canonicalTypeId != envelope.DocumentTypeId)
        {
            // Document 文件只接受当前契约写出的规范类型 ID。策略注册中的别名仍可服务于
            // 运行期创建意图，但不能悄悄把历史文件迁移为当前格式，否则插件会在不知情时
            // 接收到一个宿主改写过身份的文件，违背“无旧文件兼容”的明确产品边界。
            throw new DocumentLoadException(
                "文档类型标识不是当前规范值，宿主不会迁移历史 Document 文件。");
        }

        if (!factory.TryGetPersistedDocumentRegistration(
                envelope.DocumentTypeId,
                out var registration))
        {
            throw new NotSupportedException("当前宿主没有注册该 Document 类型。");
        }

        if (!string.Equals(
                registration.OwnerId.Value,
                envelope.PluginId.Value,
                StringComparison.Ordinal))
        {
            throw new DocumentLoadException(
                "文档声明的插件所有者与当前 Document 注册不匹配。");
        }

        Document? pendingDocument = factory.CreateManagementNewDocument(
            new DocumentCreationParams(envelope.DocumentTypeId)
            {
                Title = envelope.Title
            });
        try
        {
            // 创建策略可以决定新建文档的默认标题，但从磁盘恢复时标题属于已经验证的宿主
            // 信封。宿主在插件加载正文前再次应用它，确保策略是否消费 Title 参数不会改变
            // 持久化语义，也避免插件通过 payload 维护第二份标题。
            pendingDocument.Title = envelope.Title;
            if (pendingDocument is not ISavableDocument savableDocument)
            {
                throw new DocumentLoadException(
                    "该文档类型不支持从文件恢复。");
            }

            if (pendingDocument is not IDocumentSaveState)
            {
                throw new DocumentLoadException(
                    "该文档类型未实现公共保存状态契约。");
            }

            savableDocument.RestoreContent(envelope.Content);
            // 只有插件内容完整恢复后才把路径提交到宿主状态。若 RestoreContent 失败，
            // finally 会释放 Scope 和临时状态，不会留下“已打开但正文无效”的路径登记。
            persistenceStates.CommitFilePath(pendingDocument, filePath);
            var loadedDocument = pendingDocument;
            pendingDocument = null;
            return loadedDocument;
        }
        finally
        {
            if (pendingDocument is not null)
            {
                recoveryRegistry.Clear(pendingDocument);
                factory.ReleaseDocument(pendingDocument);
            }
        }
    }

    private void PublishLoadedDocument(
        Document document,
        string? recoverySourcePath)
    {
        var pendingDocument = document;
        try
        {
            if (recoverySourcePath is not null)
            {
                // 备份只用于构造一个脱离损坏原件的新工作副本。清空主路径并在宿主注册
                // 恢复来源，确保后续普通保存也必须经过另存选择，且不能覆盖原件或备份。
                persistenceStates.ClearFilePath(pendingDocument);
                pendingDocument.Title = $"{pendingDocument.Title}（已恢复）";
                pendingDocument.IsModified = true;
                recoveryRegistry.Register(pendingDocument, recoverySourcePath);
            }

            factory.PublishDocument(pendingDocument);
            pendingDocument = null!;
        }
        finally
        {
            if (pendingDocument is not null)
            {
                recoveryRegistry.Clear(pendingDocument);
                factory.ReleaseDocument(pendingDocument);
            }
        }
    }

    private static bool IsExpectedPersistenceFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            ArgumentException or
            NotSupportedException;
}
