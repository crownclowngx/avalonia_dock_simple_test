using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Constants;
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
    public TestHostContext(
        IEnumerable<IDocumentCreationStrategy>? documentStrategies = null,
        IEnumerable<IToolCreationStrategy>? toolStrategies = null,
        Action<IServiceCollection>? configureServices = null)
    {
        TempDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyAvaloniaManagement.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);

        Storage = new TestHostStorageService();
        Interactions = new TestDocumentInteractionService();
        Messenger = new TestMessengerService();
        var services = new ServiceCollection();
        var registryBuilder = new PluginRegistryBuilder();
        services.AddApplicationServices(registryBuilder);
        services.AddViewModels();
        services.AddSingleton<IHostStorageService>(Storage);
        services.AddSingleton<IDocumentInteractionService>(Interactions);
        services.AddSingleton<IMessengerService>(Messenger);
        services.AddSingleton(new DockLayoutStore(
            Path.Combine(TempDirectory, DockLayoutStore.LayoutFileName)));
        services.AddSingleton(new AppearanceSettingsStore(
            Path.Combine(
                TempDirectory,
                AppearanceSettingsStore.SettingsFileName)));
        services.AddSingleton(PluginModuleCatalog.Discover([]));
        foreach (var strategy in documentStrategies ?? [])
        {
            registryBuilder.AddDocumentInstance(HostExtensionIds.Owner, strategy);
        }
        foreach (var strategy in toolStrategies ?? [])
        {
            registryBuilder.AddToolInstance(HostExtensionIds.Owner, strategy);
        }
        var customServiceStart = services.Count;
        configureServices?.Invoke(services);
        // 旧测试通过 DI 工厂创建 scoped 策略。生产模块已禁止这种绕行；测试组合根在构建前
        // 将新增的接口描述符显式提升为 Builder 声明，以继续验证 Document Scope 行为。
        for (var index = services.Count - 1; index >= customServiceStart; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IDocumentCreationStrategy))
            {
                services.RemoveAt(index);
                registryBuilder.AddDocumentFactoryForTests(
                    HostExtensionIds.Owner,
                    descriptor.ImplementationType ??
                    descriptor.ImplementationInstance?.GetType() ??
                    typeof(IDocumentCreationStrategy),
                    provider => (IDocumentCreationStrategy)CreateFromDescriptor(provider, descriptor));
            }
            else if (descriptor.ServiceType == typeof(IToolCreationStrategy))
            {
                services.RemoveAt(index);
                registryBuilder.AddToolFactoryForTests(
                    HostExtensionIds.Owner,
                    descriptor.ImplementationType ??
                    descriptor.ImplementationInstance?.GetType() ??
                    typeof(IToolCreationStrategy),
                    provider => (IToolCreationStrategy)CreateFromDescriptor(provider, descriptor));
            }
        }

        Provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        Factory = Provider.GetRequiredService<ManagementFactory>();
    }

    public string TempDirectory { get; }

    public TestHostStorageService Storage { get; }

    public TestDocumentInteractionService Interactions { get; }

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

    private static object CreateFromDescriptor(
        IServiceProvider provider,
        ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance ??
        descriptor.ImplementationFactory?.Invoke(provider) ??
        ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

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
/// 可编排关闭和恢复选择的无 UI 交互替身。
/// </summary>
internal sealed class TestDocumentInteractionService : IDocumentInteractionService
{
    public Queue<DocumentCloseChoice> CloseChoices { get; } = [];
    public Queue<bool> RecoveryChoices { get; } = [];
    public List<(IReadOnlyList<string> Names, bool IsExit)> CloseRequests { get; } = [];
    public List<string> RecoveryRequests { get; } = [];
    public List<string> Errors { get; } = [];
    public TaskCompletionSource<DocumentCloseChoice>? PendingCloseChoice { get; set; }

    public Task<DocumentCloseChoice> ConfirmCloseAsync(
        IReadOnlyList<string> documentNames,
        bool isApplicationExit)
    {
        CloseRequests.Add((documentNames, isApplicationExit));
        if (PendingCloseChoice is { } pending)
        {
            PendingCloseChoice = null;
            return pending.Task;
        }
        return Task.FromResult(
            CloseChoices.Count == 0
                ? DocumentCloseChoice.Cancel
                : CloseChoices.Dequeue());
    }

    public Task<bool> ConfirmRecoveryAsync(string fileName)
    {
        RecoveryRequests.Add(fileName);
        return Task.FromResult(
            RecoveryChoices.Count != 0 && RecoveryChoices.Dequeue());
    }

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        return Task.CompletedTask;
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

    public Exception? ReadException { get; set; }

    public Exception? WriteException { get; set; }

    public Queue<Exception?> WriteOutcomes { get; } = [];

    public int ReadCount { get; private set; }

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
        ReadCount++;
        if (ReadException is not null)
        {
            return Task.FromException<string>(ReadException);
        }

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
        if (WriteOutcomes.Count != 0 && WriteOutcomes.Dequeue() is { } outcome)
        {
            return Task.FromException(outcome);
        }

        if (WriteException is not null)
        {
            return Task.FromException(WriteException);
        }

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
internal sealed class TestSavableDocument : Document, ISavableDocument, IDocumentSaveState, IDocumentSavePathPolicy
{
    public string FilePath { get; set; } = string.Empty;

    public DocumentTypeId SaveDocumentTypeId => TestSavableStrategy.TypeId;

    public string Content { get; set; } = "initial";

    public bool RequiresSaveAs { get; set; }
    public string SaveAsReason { get; set; } = "测试文档需要另存。";
    public int SaveCompletedCount { get; private set; }
    public bool IsDirty => IsModified;
    public int AcceptChangesCount { get; private set; }

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

    public void AcceptChanges()
    {
        IsModified = false;
        AcceptChangesCount++;
    }
}

/// <summary>
/// 创建 <see cref="TestSavableDocument"/> 的测试文档策略。
/// </summary>
internal sealed class TestSavableStrategy(
    DocumentMetadata? metadata = null) : IDocumentCreationStrategy
{
    internal static readonly DocumentTypeId TypeId =
        new("myavalonia.host.document.test");
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
/// 汇总 scoped Document 回滚测试的生命周期观测值。
/// </summary>
internal sealed class DocumentLifecycleProbe
{
    private int _createdCount;
    private int _loadCount;
    private int _cancellationCount;
    private int _documentDisposeCount;
    private int _dependencyDisposeCount;
    private int _documentDisposedBeforeClosing;

    public bool ThrowOnLoad { get; set; }
    public int CreatedCount => Volatile.Read(ref _createdCount);
    public int LoadCount => Volatile.Read(ref _loadCount);
    public int CancellationCount => Volatile.Read(ref _cancellationCount);
    public int DocumentDisposeCount => Volatile.Read(ref _documentDisposeCount);
    public int DependencyDisposeCount => Volatile.Read(ref _dependencyDisposeCount);
    public bool AllDocumentsObservedClosing =>
        Volatile.Read(ref _documentDisposedBeforeClosing) == 0;

    internal void RecordCreated() => Interlocked.Increment(ref _createdCount);
    internal void RecordLoad() => Interlocked.Increment(ref _loadCount);
    internal void RecordCancellation() => Interlocked.Increment(ref _cancellationCount);

    internal void RecordDocumentDispose(bool wasClosing)
    {
        Interlocked.Increment(ref _documentDisposeCount);
        if (!wasClosing)
        {
            Interlocked.Increment(ref _documentDisposedBeforeClosing);
        }
    }

    internal void RecordDependencyDispose() =>
        Interlocked.Increment(ref _dependencyDisposeCount);
}

/// <summary>
/// 用于证明 Document Scope 中的普通 scoped 依赖随回滚一起释放。
/// </summary>
internal sealed class TrackedScopedDependency(DocumentLifecycleProbe probe) : IDisposable
{
    public void Dispose() => probe.RecordDependencyDispose();
}

/// <summary>
/// 可在元数据加载阶段稳定失败的 scoped Savable Document。
/// </summary>
internal sealed class TrackedScopedSavableDocument : Document, ISavableDocument, IDocumentSaveState, IDisposable
{
    private readonly DocumentLifecycleProbe _probe;
    private readonly IDocumentLifetime _lifetime;
    private readonly CancellationTokenRegistration _closingRegistration;

    public TrackedScopedSavableDocument(
        DocumentLifecycleProbe probe,
        TrackedScopedDependency dependency,
        IDocumentLifetime lifetime)
    {
        _probe = probe;
        _lifetime = lifetime;
        _ = dependency;
        _probe.RecordCreated();
        _closingRegistration = lifetime.ClosingToken.Register(
            _probe.RecordCancellation);
    }

    public string FilePath { get; set; } = string.Empty;
    public DocumentTypeId SaveDocumentTypeId => TrackedScopedSavableStrategy.TypeId;
    public bool IsDirty => IsModified;

    public DocumentSaveData CreateSaveDocumentMetaData(string filePath) =>
        new()
        {
            DocumentTypeId = SaveDocumentTypeId,
            Title = Title ?? string.Empty,
            SaveTime = DateTime.UtcNow,
            Content = string.Empty,
            PluginMetadata = string.Empty,
        };

    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        _probe.RecordLoad();
        if (_probe.ThrowOnLoad)
        {
            throw new DocumentLoadException("测试文档内容损坏。");
        }

        Title = saveData.Title;
    }

    public void AcceptChanges() => IsModified = false;

    public void Dispose()
    {
        _probe.RecordDocumentDispose(_lifetime.IsClosing);
        _closingRegistration.Dispose();
    }
}

/// <summary>
/// 创建测试用 scoped Savable Document，生产代码仍只依赖公共 Scope 工厂。
/// </summary>
internal sealed class TrackedScopedSavableStrategy(
    IDocumentScopeFactory scopeFactory) : IDocumentCreationStrategy
{
    internal static readonly DocumentTypeId TypeId =
        new("myavalonia.host.document.scoped-savable-test");

    public Document CreateDocument(DocumentCreationParams @params) =>
        scopeFactory.CreateDocument<TrackedScopedSavableDocument>();

    public DocumentMetadata GetMetadata() =>
        new(TypeId, "Scoped Savable 测试文档")
        {
            MenuCategory = "测试",
        };
}

/// <summary>
/// 故意不实现保存契约，用于验证恢复入口拒绝类型后仍释放 Scope。
/// </summary>
internal sealed class TrackedScopedNonSavableDocument : Document, IDisposable
{
    private readonly DocumentLifecycleProbe _probe;
    private readonly IDocumentLifetime _lifetime;
    private readonly CancellationTokenRegistration _closingRegistration;

    public TrackedScopedNonSavableDocument(
        DocumentLifecycleProbe probe,
        TrackedScopedDependency dependency,
        IDocumentLifetime lifetime)
    {
        _probe = probe;
        _lifetime = lifetime;
        _ = dependency;
        _probe.RecordCreated();
        _closingRegistration = lifetime.ClosingToken.Register(
            _probe.RecordCancellation);
    }

    public void Dispose()
    {
        _probe.RecordDocumentDispose(_lifetime.IsClosing);
        _closingRegistration.Dispose();
    }
}

internal sealed class TrackedScopedNonSavableStrategy(
    IDocumentScopeFactory scopeFactory) : IDocumentCreationStrategy
{
    internal static readonly DocumentTypeId TypeId =
        new("myavalonia.host.document.scoped-non-savable-test");

    public Document CreateDocument(DocumentCreationParams @params) =>
        scopeFactory.CreateDocument<TrackedScopedNonSavableDocument>();

    public DocumentMetadata GetMetadata() =>
        new(TypeId, "Scoped Non-Savable 测试文档")
        {
            MenuCategory = "测试",
        };
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
