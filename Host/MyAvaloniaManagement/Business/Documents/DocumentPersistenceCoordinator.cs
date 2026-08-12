using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    IHostStorageService storageService)
{
    private readonly DocumentWorkspace _workspace = new(factory);
    private readonly DocumentEnvelopeSerializer _serializer = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    internal void CreateDocument(string documentType)
    {
        var document = factory.CreateManagementNewDocument(
            new DocumentCreationParams(DocumentTypeId.Parse(documentType)));
        _workspace.Add(document);
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
        await _operationGate.WaitAsync();
        try
        {
            var activeDocument = _workspace.GetActiveDocument();
            if (activeDocument is not ISavableDocument savableDocument)
            {
                return DocumentOperationResult.NoChange;
            }

            var savePathPolicy = activeDocument as IDocumentSavePathPolicy;
            var originalPath = savableDocument.FilePath;
            string? filePath;
            if (string.IsNullOrEmpty(originalPath) ||
                savePathPolicy?.RequiresSaveAs == true)
            {
                var metadata = factory.GetAllDocumentMetadata()
                    .FirstOrDefault(item =>
                        item.DocumentTypeId == savableDocument.SaveDocumentTypeId);
                filePath = await storageService.PickSaveFileAsync(metadata);
            }
            else
            {
                filePath = originalPath;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return DocumentOperationResult.NoChange;
            }

            filePath = DocumentPathIdentity.Normalize(filePath);
            if (savePathPolicy?.RequiresSaveAs == true &&
                !string.IsNullOrWhiteSpace(originalPath) &&
                DocumentPathIdentity.Equals(originalPath, filePath))
            {
                return DocumentOperationResult.Failure(
                    $"{savePathPolicy.SaveAsReason} 请选择不同的文件路径。");
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var saveData = savableDocument.CreateSaveDocumentMetaData(filePath);
            saveData.Title = fileName;
            await storageService.WriteAllTextAsync(
                filePath,
                _serializer.Serialize(saveData));

            if (activeDocument is Document document)
            {
                document.Title = fileName;
            }

            savableDocument.FilePath = filePath;
            savePathPolicy?.NotifySaveCompleted(filePath);
            return DocumentOperationResult.ClearError;
        }
        catch (Exception exception) when (IsExpectedPersistenceFailure(exception))
        {
            Console.Error.WriteLine(
                $"DocumentPersistence errorCode=DOCUMENT_SAVE_FAILED type={exception.GetType().Name}");
            return DocumentOperationResult.Failure(
                "保存文档失败，请检查目标路径是否可写。文档状态未被修改。");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<DocumentOperationResult> OpenAllAsync(
        IReadOnlyList<string> paths,
        IRootDock? root)
    {
        await _operationGate.WaitAsync();
        try
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

                    await LoadAndAddAsync(normalizedPath);
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
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task LoadAndAddAsync(string filePath)
    {
        var content = await storageService.ReadAllTextAsync(filePath);
        var data = _serializer.Deserialize(content);
        // 插件只观察规范 ID；历史别名的兼容责任留在宿主边界，下一次保存自然写回新值。
        data.DocumentTypeId = factory.NormalizePersistedDocumentTypeId(data.DocumentTypeId);
        var document = factory.CreateManagementNewDocument(
            new DocumentCreationParams(data.DocumentTypeId)
            {
                Title = data.Title
            });

        if (document is ISavableDocument savableDocument)
        {
            savableDocument.FilePath = filePath;
            savableDocument.LoadDocumentByMetaData(data);
            _workspace.Add(document);
        }
    }

    private static bool IsExpectedPersistenceFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            ArgumentException or
            NotSupportedException;
}
