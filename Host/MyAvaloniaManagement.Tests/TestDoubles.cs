using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 为单元和组件测试创建隔离的依赖注入容器、内存存储及临时布局目录。
/// </summary>
/// <remarks>
/// 每个测试上下文拥有独立目录和替身实例，避免测试顺序、用户文件和静态状态相互影响。
/// </remarks>
internal sealed class TestHostContext : IDisposable
{
    public TestHostContext()
    {
        TempDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyAvaloniaManagement.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);

        Storage = new TestHostStorageService();
        Messenger = new TestMessengerService();
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();
        services.AddSingleton<IHostStorageService>(Storage);
        services.AddSingleton<IMessengerService>(Messenger);
        services.AddSingleton(new DockLayoutStore(
            Path.Combine(TempDirectory, DockLayoutStore.LayoutFileName)));
        services.AddSingleton(new AppearanceSettingsStore(
            Path.Combine(
                TempDirectory,
                AppearanceSettingsStore.SettingsFileName)));
        services.AddSingleton(PluginModuleCatalog.Discover([]));

        Provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        Factory = Provider.GetRequiredService<ManagementFactory>();
    }

    public string TempDirectory { get; }

    public TestHostStorageService Storage { get; }

    public TestMessengerService Messenger { get; }

    public Microsoft.Extensions.DependencyInjection.ServiceProvider Provider { get; }

    public ManagementFactory Factory { get; }

    public ApplicationThemeService ThemeService =>
        Provider.GetRequiredService<ApplicationThemeService>();

    public string AppearanceSettingsPath =>
        Path.Combine(
            TempDirectory,
            AppearanceSettingsStore.SettingsFileName);

    /// <summary>
    /// 从经过 ValidateOnBuild/ValidateScopes 校验的容器创建主 ViewModel。
    /// </summary>
    public MainWindowViewModel CreateMainWindowViewModel() =>
        Provider.GetRequiredService<MainWindowViewModel>();

    /// <summary>
    /// 释放服务容器并删除本测试创建的临时目录。
    /// </summary>
    public void Dispose()
    {
        Provider.Dispose();
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }
}

/// <summary>
/// 可编排选择结果并在内存中读写文件的宿主存储替身。
/// </summary>
internal sealed class TestHostStorageService : IHostStorageService
{
    public IReadOnlyList<string> OpenPaths { get; set; } = [];

    public string? SavePath { get; set; }

    public string? FolderPath { get; set; }

    public DocumentMetadata? LastSaveMetadata { get; private set; }

    public Dictionary<string, string> Files { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<(string Path, string Content)> Writes { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> PickOpenFilesAsync() =>
        Task.FromResult(OpenPaths);

    /// <inheritdoc />
    public Task<string?> PickSaveFileAsync(DocumentMetadata? metadata)
    {
        LastSaveMetadata = metadata;
        return Task.FromResult(SavePath);
    }

    /// <inheritdoc />
    public Task<string?> PickFolderAsync() => Task.FromResult(FolderPath);

    /// <inheritdoc />
    public bool FileExists(string path) =>
        Files.ContainsKey(Path.GetFullPath(path)) || File.Exists(path);

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (Files.TryGetValue(normalized, out var content))
        {
            return Task.FromResult(content);
        }

        return File.ReadAllTextAsync(normalized);
    }

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string content)
    {
        var normalized = Path.GetFullPath(path);
        Files[normalized] = content;
        Writes.Add((normalized, content));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 向内存文件集合加入一个经过规范化的路径。
    /// </summary>
    public void AddFile(string path, string content) =>
        Files[Path.GetFullPath(path)] = content;
}

/// <summary>
/// 同步派发消息的测试实现，便于立即断言消息副作用。
/// </summary>
internal sealed class TestMessengerService : IMessengerService
{
    private readonly List<Registration> _registrations = [];

    public IMessenger Messenger => WeakReferenceMessenger.Default;

    public void Send<TMessage>(TMessage message)
        where TMessage : class
    {
        foreach (var registration in _registrations
                     .Where(item => item.MessageType == typeof(TMessage))
                     .ToArray())
        {
            registration.Handler(registration.Receiver, message);
        }
    }

    public void Register<TReceiver, TMessage>(
        TReceiver receiver,
        MyAvaloniaManagementCommon.Message.MessageHandler<TReceiver, TMessage> handler)
        where TReceiver : class
        where TMessage : class =>
        _registrations.Add(new Registration(
            typeof(TMessage),
            receiver,
            (target, message) =>
                handler((TReceiver)target, (TMessage)message)));

    public void Unregister<TMessage>(object receiver)
        where TMessage : class =>
        _registrations.RemoveAll(item =>
            item.MessageType == typeof(TMessage) &&
            ReferenceEquals(item.Receiver, receiver));

    public void UnregisterAll(object receiver) =>
        _registrations.RemoveAll(item =>
            ReferenceEquals(item.Receiver, receiver));

    private sealed record Registration(
        Type MessageType,
        object Receiver,
        Action<object, object> Handler);
}

/// <summary>
/// 用于验证保存、加载和标题路径同步的最小可保存文档。
/// </summary>
internal sealed class TestSavableDocument : Document, ISavableDocument, IDocumentSavePathPolicy
{
    public string FilePath { get; set; } = string.Empty;

    public string SaveDocumentTypeId => TestSavableStrategy.TypeId;

    public string Content { get; set; } = "initial";

    public bool RequiresSaveAs { get; set; }
    public string SaveAsReason { get; set; } = "测试文档需要另存。";
    public int SaveCompletedCount { get; private set; }

    public DocumentSaveData CreateSaveDocumentMetaData(string filePath) =>
        new()
        {
            DocumentTypeId = SaveDocumentTypeId,
            Title = Title ?? string.Empty,
            SaveTime = DateTime.UtcNow,
            Content = Content,
            PluginMetadata = """{"source":"test"}"""
        };

    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        Title = saveData.Title;
        Content = saveData.Content;
    }

    public void NotifySaveCompleted(string filePath)
    {
        FilePath = filePath;
        RequiresSaveAs = false;
        SaveCompletedCount++;
    }
}

/// <summary>
/// 创建 <see cref="TestSavableDocument"/> 的测试文档策略。
/// </summary>
internal sealed class TestSavableStrategy(
    DocumentMetadata? metadata = null) : IDocumentCreationStrategy
{
    internal const string TypeId = "testdoc";
    private readonly DocumentMetadata _metadata = metadata ??
        new DocumentMetadata(TypeId, "测试文档")
        {
            MenuCategory = "测试"
        };

    public Document CreateDocument(DocumentCreationParams @params) =>
        new TestSavableDocument
        {
            Title = string.IsNullOrWhiteSpace(@params.Title)
                ? "未命名"
                : @params.Title
        };

    public DocumentMetadata GetMetadata() => _metadata;
}

/// <summary>
/// 返回固定元数据的轻量文档策略，用于菜单分组测试。
/// </summary>
internal sealed class StubDocumentStrategy(
    DocumentMetadata metadata) : IDocumentCreationStrategy
{
    public Document CreateDocument(DocumentCreationParams @params) =>
        new() { Title = @params.Title };

    public DocumentMetadata GetMetadata() => metadata;
}

/// <summary>
/// 返回固定工具和元数据的轻量工具策略。
/// </summary>
internal sealed class StubToolStrategy(
    Tool tool,
    ToolMetadata metadata) : IToolCreationStrategy
{
    public Tool CreateTool() => tool;

    public ToolMetadata GetMetadata() => metadata;
}
