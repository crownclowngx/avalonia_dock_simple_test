using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// 这样窗口 ViewModel 只处理绑定和消息，且并发请求不会重复打开或互相覆盖状态。
/// </summary>
internal sealed class DocumentPersistenceCoordinator(
    ManagementFactory factory,
    IHostStorageService storageService,
    DocumentSaveService saveService,
    DocumentOperationGate operationGate,
    DocumentRecoveryRegistry recoveryRegistry,
    IDocumentInteractionService interactionService,
    DocumentEnvelopeSerializer serializer)
{
    private readonly DocumentWorkspace _workspace = new(factory, recoveryRegistry);

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
            Console.WriteLine($"文件不存在: {filePath}");
            return DocumentOperationResult.NoChange;
        }

        return await OpenAllAsync([filePath], root);
    }

    internal async Task<DocumentOperationResult> SaveActiveAsync()
    {
        var activeDocument = _workspace.GetActiveDocument();
        if (activeDocument is not Document document)
        {
            return DocumentOperationResult.NoChange;
        }

        var metadata = document is ISavableDocument savable
            ? factory.GetAllDocumentMetadata().FirstOrDefault(item =>
                item.DocumentTypeId == savable.SaveDocumentTypeId)
            : null;
        var result = await saveService.SaveAsync(document, metadata);
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
                    var fileName = Path.GetFileName(path);
                    var reason = exception switch
                    {
                        DocumentLoadException => exception.Message,
                        JsonException => "文件结构损坏或不是受支持的 Document。",
                        _ => "读取文件失败，请检查文件是否仍然存在且可访问。"
                    };
                    result = DocumentOperationResult.Failure(
                        $"无法打开“{fileName}”：{reason} 原文件未被修改。");
                    Console.Error.WriteLine(
                        $"DocumentPersistence errorCode=DOCUMENT_OPEN_FAILED type={exception.GetType().Name}");
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
        var content = await storageService.ReadAllTextAsync(filePath);
        var data = serializer.Deserialize(content);
        var canonicalTypeId = factory.NormalizePersistedDocumentTypeId(data.DocumentTypeId);
        if (canonicalTypeId != data.DocumentTypeId)
        {
            // Document 文件只接受当前契约写出的规范类型 ID。策略注册中的别名仍可服务于
            // 运行期创建意图，但不能悄悄把历史文件迁移为当前格式，否则插件会在不知情时
            // 接收到一个宿主改写过身份的文件，违背“无旧文件兼容”的明确产品边界。
            throw new DocumentLoadException(
                "文档类型标识不是当前规范值，宿主不会迁移历史 Document 文件。");
        }
        Document? pendingDocument = factory.CreateManagementNewDocument(
            new DocumentCreationParams(data.DocumentTypeId)
            {
                Title = data.Title
            });
        try
        {
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

            savableDocument.FilePath = filePath;
            savableDocument.LoadDocumentByMetaData(data);
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
                ((ISavableDocument)pendingDocument).FilePath = string.Empty;
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
