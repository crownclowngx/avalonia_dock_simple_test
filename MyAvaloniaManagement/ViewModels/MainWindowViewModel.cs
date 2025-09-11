using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDropTarget
{
    private readonly ManagementFactory? _factory;
    private IRootDock? _layout;
    private readonly PluginMenuService? _pluginMenuService;

    public IRootDock? Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    // 按分类分组的文档元数据，用于绑定菜单
    public Dictionary<string, List<DocumentMetadata>> DocumentMetadataByCategory =>
        _pluginMenuService?.GetDocumentMetadataByCategory() ?? new Dictionary<string, List<DocumentMetadata>>();

    public MainWindowViewModel()
    {
        _factory = new ManagementFactory();
        _pluginMenuService = new PluginMenuService(_factory);
        // 初始化AppServices，使其他组件可以访问这些实例
        AppServices.Initialize(_factory, _pluginMenuService);
        Layout = _factory?.CreateLayout();
        if (Layout is { })
        {
            _factory?.InitLayout(Layout);
        }
    }

    /// <summary>
    /// 创建文档
    /// </summary>
    /// <param name="documentType"></param>
    [RelayCommand]
    public void CreateDocument(String documentType)
    {
        var document = _factory?.CreateManagementNewDocument(new DocumentCreationParams(documentType));
        var files = _factory?.GetDockable<IDocumentDock>("Files") as DocumentDock;
        if (document != null)
        {
            files?.AddDocument(document);
        }
    }

    /// <summary>
    /// 打开文档
    /// </summary>
    [RelayCommand]
    public async void OpenDocument()
    {
        // 使用正确的方式获取主窗口
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (mainWindow == null) return;

        var options = new FilePickerOpenOptions
        {
            Title = "打开文档",
            AllowMultiple = true,
            FileTypeFilter = new[] { FilePickerFileTypes.TextPlain }
        };

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            foreach (var storageFile in files)
            {
                try
                {
                    if (LoadAndActiveIfNowTabHasThisDocument(storageFile))
                    {
                        return;
                    }
                    await LoadAndCreateNewDocument(storageFile);
                }
                catch (Exception ex)
                {
                    // 实际应用中应使用更友好的错误处理
                    Console.WriteLine($"打开文档错误: {ex.Message}");
                }
            }
        }
    }

    private bool LoadAndActiveIfNowTabHasThisDocument(IStorageFile file)
    {
        if (Layout is IDock rootDock)
        {
            foreach (var dockable in FindAllDocuments(rootDock))
            {
                if (dockable is ISavableDocument doc && doc.FilePath == file.Path.LocalPath)
                {
                    // 找到已打开的文档，高亮对应的选项卡
                    if (Layout is IRootDock root)
                    {
                        // 获取包含此文档的文档停靠容器
                        var documentDock = FindDocumentDock(root, dockable);
                        if (documentDock != null)
                        {
                            // 激活此文档
                            documentDock.ActiveDockable = dockable;
                            // 通知UI更新
                            OnPropertyChanged(nameof(Layout));
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private async Task LoadAndCreateNewDocument(IStorageFile file)
    {
        // 读取文件内容
        using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // 反序列化文档数据
        var documentData = JsonConvert.DeserializeObject<DocumentSaveData>(content);
        if (documentData != null)
        {
            // 根据DocumentTypeId创建对应的文档
            var document = _factory?.CreateManagementNewDocument(
                new DocumentCreationParams(documentData.DocumentTypeId) { Title = documentData.Title });

            if (document is ISavableDocument savableDocument)
            {
                savableDocument.FilePath = file.Path.LocalPath;
                // 传递文件内容给文档加载
                using (var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
                {
                    var fileContent = File.ReadAllText(file.Path.LocalPath);
                    var saveData = JsonConvert.DeserializeObject<DocumentSaveData>(fileContent);
                    savableDocument.LoadDocumentByMetaData(saveData);
                }

                var filesDock = _factory?.GetDockable<IDocumentDock>("Files") as DocumentDock;
                if (document != null)
                {
                    filesDock?.AddDocument(document);
                }
            }
        }
    }

    /// <summary>
    /// 保存当前激活的文档
    /// </summary>
    [RelayCommand]
    public async void SaveDocument()
    {
        var activeDocument = GetActiveDocument();
        if (activeDocument is ISavableDocument savableDocument)
        {
            // 使用正确的方式获取主窗口
            var mainWindow =
                (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
            if (mainWindow == null) return;
            IStorageFile? file;
            // 如果已有文件路径，则直接保存；否则显示保存对话框
            if (string.IsNullOrEmpty(savableDocument.FilePath))
            {
                // 获取文档类型的元数据，用于设置默认文件扩展名
                var allMetadata = _factory?.GetAllDocumentMetadata();
                var metadata = allMetadata?.FirstOrDefault(m => m.DocumentTypeId == savableDocument.SaveDocumentTypeId);

                var fileType = metadata != null
                    ? new FilePickerFileType(metadata.DisplayName)
                        { Patterns = new[] { $"*.{metadata.DocumentTypeId.ToLower()}" } }
                    : FilePickerFileTypes.TextPlain;

                var options = new FilePickerSaveOptions
                {
                    Title = "保存文档",
                    DefaultExtension = "txt",
                    FileTypeChoices = new[] { FilePickerFileTypes.TextPlain }
                };

                file = await mainWindow.StorageProvider.SaveFilePickerAsync(options);
            }
            else
            {
                file = await mainWindow.StorageProvider.TryGetFileFromPathAsync(savableDocument.FilePath);
            }
            if (file != null)
            {
                var filePath = file.Path.LocalPath;
                savableDocument.FilePath = filePath;
                // 从文件路径中提取文件名并设置为文档标题
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var saveData = savableDocument.CreateSaveDocumentMetaData(filePath);
                saveData.Title = fileName;
                if (activeDocument is Document document)
                {
                    document.Title = fileName;
                }
                if (activeDocument is ISavableDocument savableDocumentDocument)
                {
                    savableDocumentDocument.FilePath = filePath;
                }
                File.WriteAllText(filePath, JsonConvert.SerializeObject(saveData, Formatting.Indented));
            }
        }
    }

    /// <summary>
    /// 获取当前激活的文档
    /// </summary>
    /// <returns>当前激活的文档，如果没有则返回null</returns>
    private IDockable? GetActiveDocument()
    {
        var filesDock = _factory?.GetDockable<IDocumentDock>("Files") as DocumentDock;
        return filesDock?.ActiveDockable;
    }

    // 辅助方法：查找所有文档
    private IEnumerable<IDockable> FindAllDocuments(IDock dock)
    {
        var results = new List<IDockable>();

        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    results.AddRange(FindAllDocuments(childDock));
                }
                else if (dockable is Document)
                {
                    results.Add(dockable);
                }
            }
        }

        return results;
    }
    
    // 辅助方法：查找包含特定文档的DocumentDock
    private IDocumentDock? FindDocumentDock(IDock dock, IDockable document)
    {
        if (dock is IDocumentDock docDock && 
            docDock.VisibleDockables != null && 
            docDock.VisibleDockables.Contains(document))
        {
            return docDock;
        }
    
        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    var result = FindDocumentDock(childDock, document);
                    if (result != null)
                        return result;
                }
            }
        }
    
        return null;
    }
    public void DragOver(object? sender, DragEventArgs e)
    {
    }

    public void Drop(object? sender, DragEventArgs e)
    {
    }
}