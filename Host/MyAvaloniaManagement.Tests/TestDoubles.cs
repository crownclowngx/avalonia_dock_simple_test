using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.ViewModels;
using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

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
        IEnumerable<StubToolContribution>? toolContributions = null,
        Action<IServiceCollection>? configureServices = null,
        Action<IServiceCollection, PluginRegistryBuilder>? configureContributions = null)
    {
        TempDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyAvaloniaManagement.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);

        Storage = new TestHostStorageService();
        Interactions = new TestDocumentInteractionService();
        var services = new ServiceCollection();
        var registryBuilder = new PluginRegistryBuilder();
        services.AddApplicationServices(registryBuilder);
        services.AddViewModels();
        services.AddSingleton<IHostStorageService>(Storage);
        services.AddSingleton<IDocumentInteractionService>(Interactions);
        services.AddSingleton<DocumentV2TestProbe>();
        services.AddSingleton(new DockLayoutStore(
            Path.Combine(TempDirectory, DockLayoutStore.LayoutFileName)));
        services.AddSingleton(new AppearanceSettingsStore(
            Path.Combine(
                TempDirectory,
                AppearanceSettingsStore.SettingsFileName)));
        services.AddSingleton(PluginModuleCatalog.Discover(PluginDiscoverySnapshot.Empty));
        foreach (var contribution in toolContributions ?? [])
        {
            RegisterToolContribution(services, registryBuilder, contribution);
        }
        configureContributions?.Invoke(services, registryBuilder);
        configureServices?.Invoke(services);

        // 纯单元测试没有 Avalonia 平台，使用不构造 Control 的 V2 工厂替身；真实 View
        // 预构建与失败回滚由 Headless UI 测试覆盖。
        services.AddSingleton<IHostDockableFactory>(provider =>
            new UnitTestDockableFactory(
                provider.GetRequiredService<PluginRegistry>(),
                provider.GetRequiredService<DocumentScopeManager>(),
                provider));

        Provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        Workspace = Provider.GetRequiredService<WorkspaceSession>();
    }

    public string TempDirectory { get; }

    public TestHostStorageService Storage { get; }

    public TestDocumentInteractionService Interactions { get; }

    public Microsoft.Extensions.DependencyInjection.ServiceProvider Provider { get; }

    public WorkspaceSession Workspace { get; }

    public DocumentPersistenceStateStore PersistenceStates =>
        Provider.GetRequiredService<DocumentPersistenceStateStore>();

    public string GetDocumentFilePath(ManagedDocumentDockable document) =>
        PersistenceStates.TryGet(document, out var state)
            ? state.FilePath
            : throw new InvalidOperationException("测试 Document 没有宿主持久化状态。");

    public void SetDocumentFilePath(ManagedDocumentDockable document, string filePath) =>
        PersistenceStates.CommitFile(document, filePath, document.HostTitle);

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

    private static void RegisterToolContribution(
        IServiceCollection services,
        PluginRegistryBuilder builder,
        StubToolContribution contribution)
    {
        var modelType = contribution.Model.GetType();
        services.AddSingleton(modelType, contribution.Model);
        builder.AddTool(
            HostExtensionIds.V2Owner,
            contribution.Descriptor,
            modelType,
            typeof(TestContributionView),
            static () => new TestContributionView());
    }

    private sealed class TestContributionView : UserControl;

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

/// <summary>仅供非 Avalonia 单元测试使用的 V2 Document 与 Tool 窄工厂。</summary>
internal sealed class UnitTestDockableFactory(
    PluginRegistry registry,
    DocumentScopeManager documentScopes,
    IServiceProvider provider) : IHostDockableFactory
{
    public async ValueTask<Document> CreateDocumentAsync(
        MyAvaloniaManagement.PluginSdk.DocumentTypeId documentTypeId,
        MyAvaloniaManagement.PluginSdk.DocumentActivation context)
    {
        if (!registry.TryGetDocumentRegistration(documentTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Document 类型：{documentTypeId.Value}。");
        }

        if (typeof(MyAvaloniaManagement.PluginSdk.IPluginDocument)
            .IsAssignableFrom(registration.ModelType))
        {
            var lease = documentScopes.CreatePluginDocument(registration.ModelType);
            try
            {
                await lease.Model.InitializeAsync(context, lease.ClosingToken);
                return new ManagedDocumentDockable(
                    new ActivatedPluginDocument(registration, lease),
                    context.Title);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException("G7 单元测试只允许普通 IPluginDocument 模型。");
    }

    public Tool CreateTool(MyAvaloniaManagement.PluginSdk.ToolTypeId toolTypeId)
    {
        if (!registry.TryGetToolRegistration(toolTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Tool 类型：{toolTypeId.Value}。");
        }

        var model = provider.GetRequiredService(registration.ModelType);
        if (model is Tool legacy)
        {
            legacy.Id = registration.Descriptor.ToolTypeId.Value;
            legacy.Title = registration.Descriptor.DisplayName;
            legacy.CanClose = registration.Descriptor.CloseBehavior == ToolCloseBehavior.Hide;
            return legacy;
        }

        return new ManagedToolDockable(new ActivatedPluginTool(registration, model));
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
    public TaskCompletionSource<string>? ErrorShown { get; set; }
    public Exception? ConfirmCloseException { get; set; }
    public Exception? ShowErrorException { get; set; }

    public Task<DocumentCloseChoice> ConfirmCloseAsync(
        IReadOnlyList<string> documentNames,
        bool isApplicationExit)
    {
        CloseRequests.Add((documentNames, isApplicationExit));
        if (ConfirmCloseException is { } confirmCloseException)
        {
            return Task.FromException<DocumentCloseChoice>(confirmCloseException);
        }
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
        ErrorShown?.TrySetResult(message);
        return ShowErrorException is { } showErrorException
            ? Task.FromException(showErrorException)
            : Task.CompletedTask;
    }
}

/// <summary>
/// 可编排选择结果并在内存中读写文件的宿主存储替身。
/// </summary>
internal sealed class TestHostStorageService : IHostStorageService
{
    private (TaskCompletionSource Started, TaskCompletionSource Release)? _nextWritePause;

    public IReadOnlyList<string> OpenPaths { get; set; } = [];

    public string? SavePath { get; set; }

    public string? FolderPath { get; set; }

    public string? LastSaveDisplayName { get; private set; }

    public Exception? ReadException { get; set; }

    public Exception? WriteException { get; set; }

    public Queue<Exception?> WriteOutcomes { get; } = [];

    public int ReadCount { get; private set; }

    public Dictionary<string, string> Files { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<(string Path, string Content)> Writes { get; } = [];

    /// <summary>
    /// 让下一次写入在真正提交到内存文件集合前暂停，并把“已经到达写入点”的事实通知测试。
    /// </summary>
    /// <remarks>
    /// 这是一次性的测试同步点，只用于稳定复现“内容已经捕获，但主文件尚未提交”的时间窗口。
    /// 两个信号必须由调用方成对创建和释放；本替身不会把这种测试编排能力扩散到生产存储接口。
    /// </remarks>
    public void PauseNextWrite(
        TaskCompletionSource started,
        TaskCompletionSource release)
    {
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(release);
        if (_nextWritePause is not null)
        {
            throw new InvalidOperationException("下一次写入已经设置了暂停点。");
        }

        _nextWritePause = (started, release);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> PickOpenFilesAsync() =>
        Task.FromResult(OpenPaths);

    /// <inheritdoc />
    public Task<string?> PickSaveFileAsync(string documentDisplayName)
    {
        LastSaveDisplayName = documentDisplayName;
        return Task.FromResult(SavePath);
    }

    /// <inheritdoc />
    public Task<string?> PickFolderAsync() => Task.FromResult(FolderPath);

    /// <inheritdoc />
    public bool FileExists(string path) =>
        Files.ContainsKey(Path.GetFullPath(path)) || File.Exists(path);

    /// <inheritdoc />
    public long GetFileLength(string path)
    {
        var normalized = Path.GetFullPath(path);
        return Files.TryGetValue(normalized, out var content)
            ? System.Text.Encoding.UTF8.GetByteCount(content)
            : new FileInfo(normalized).Length;
    }

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
    public async Task WriteAllTextAsync(string path, string content)
    {
        if (WriteOutcomes.Count != 0 && WriteOutcomes.Dequeue() is { } outcome)
        {
            throw outcome;
        }

        if (WriteException is not null)
        {
            throw WriteException;
        }

        var pause = _nextWritePause;
        _nextWritePause = null;
        if (pause is not null)
        {
            // 先通知测试捕获已经完成，再等待测试制造第二次编辑。暂停点在读取后立即清空，
            // 因而同一次保存随后写恢复备份时不会再次阻塞，也不会意外影响其他测试。
            pause.Value.Started.TrySetResult();
            await pause.Value.Release.Task;
        }

        var normalized = Path.GetFullPath(path);
        Files[normalized] = content;
        Writes.Add((normalized, content));
    }

    /// <summary>
    /// 向内存文件集合加入一个经过规范化的路径。
    /// </summary>
    public void AddFile(string path, string content) =>
        Files[Path.GetFullPath(path)] = content;
}

/// <summary>
/// 用于验证保存、加载和标题路径同步的最小可保存文档。
/// </summary>
internal sealed class TestSavableDocument(
    DocumentV2TestProbe probe,
    MyAvaloniaManagement.PluginSdk.IDocumentLifetime lifetime) :
    MyAvaloniaManagement.PluginSdk.IPersistablePluginDocument,
    IDisposable
{
    private long _revision;
    private long _acceptedRevision;

    public string Content { get; set; } = "initial";
    public string Title { get; private set; } = "未命名";
    public bool IsModified
    {
        get => _revision != _acceptedRevision;
        set
        {
            var wasDirty = IsModified;
            if (value)
            {
                _revision = checked(_revision + 1);
            }
            else
            {
                _acceptedRevision = _revision;
            }
            if (wasDirty != IsModified)
            {
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsDirty => IsModified;
    public event EventHandler? IsDirtyChanged;
    public int AcceptChangesCount { get; private set; }
    public MyAvaloniaManagement.PluginSdk.DocumentPresentationState Presentation =>
        new(Title);
    public event EventHandler? PresentationChanged;

    public async ValueTask InitializeAsync(
        MyAvaloniaManagement.PluginSdk.DocumentActivation context,
        CancellationToken cancellationToken)
    {
        probe.ActivationContexts.Add(context);
        if (probe.InitializeBlocker is { } blocker)
        {
            await blocker.Task.WaitAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (probe.InitializeException is { } initializeException)
        {
            throw initializeException;
        }
        Title = string.IsNullOrWhiteSpace(context.Title) ? "未命名" : context.Title;
        if (context is NewDocumentActivation)
        {
            return;
        }

        if (context is not RestoreDocumentActivation restore)
        {
            throw new NotSupportedException("测试 Document 收到未知激活类型。");
        }

        var content = restore.RestoredContent;

        if (content.SchemaVersion != 1 || content.Payload.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new InvalidOperationException("测试文档内容版本或 JSON 类型不受支持。");
        }

        Content = content.Payload.GetString()!;
        IsModified = false;
    }

    public ValueTask<MyAvaloniaManagement.PluginSdk.DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (probe.CaptureException is { } captureException)
        {
            throw captureException;
        }
        if (probe.ReturnNullContent)
        {
            return ValueTask.FromResult<MyAvaloniaManagement.PluginSdk.DocumentSaveSnapshot>(null!);
        }
        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(Content));
        var content = new MyAvaloniaManagement.PluginSdk.DocumentContent(1, json.RootElement);
        return ValueTask.FromResult(new MyAvaloniaManagement.PluginSdk.DocumentSaveSnapshot(
            new MyAvaloniaManagement.PluginSdk.DocumentRevision(_revision),
            content));
    }

    public void AcceptChanges(MyAvaloniaManagement.PluginSdk.DocumentRevision savedRevision)
    {
        if (probe.AcceptChangesException is { } acceptChangesException)
        {
            throw acceptChangesException;
        }
        if (_revision == savedRevision.Value)
        {
            IsModified = false;
        }
        AcceptChangesCount++;
    }

    internal void SetTitle(string title)
    {
        Title = title;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        probe.ClosingObservedDuringDispose = lifetime.IsClosing;
        probe.DisposeCount++;
    }
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
    public string LoadFailureMessage { get; set; } = "测试文档内容损坏。";
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
internal sealed class TrackedScopedSavableDocument :
    MyAvaloniaManagement.PluginSdk.IPersistablePluginDocument,
    IDisposable
{
    private readonly DocumentLifecycleProbe _probe;
    private readonly MyAvaloniaManagement.PluginSdk.IDocumentLifetime _lifetime;
    private readonly CancellationTokenRegistration _closingRegistration;

    public TrackedScopedSavableDocument(
        DocumentLifecycleProbe probe,
        TrackedScopedDependency dependency,
        MyAvaloniaManagement.PluginSdk.IDocumentLifetime lifetime)
    {
        _probe = probe;
        _lifetime = lifetime;
        _ = dependency;
        _probe.RecordCreated();
        _closingRegistration = lifetime.ClosingToken.Register(
            _probe.RecordCancellation);
    }

    private long _revision;
    private long _acceptedRevision;

    public bool IsDirty
    {
        get => _revision != _acceptedRevision;
        set
        {
            var wasDirty = IsDirty;
            if (value)
            {
                _revision = checked(_revision + 1);
            }
            else
            {
                _acceptedRevision = _revision;
            }
            if (wasDirty != IsDirty)
            {
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    public event EventHandler? IsDirtyChanged;
    public MyAvaloniaManagement.PluginSdk.DocumentPresentationState Presentation =>
        new("Scoped Savable 测试文档");
    public event EventHandler? PresentationChanged { add { } remove { } }

    public ValueTask InitializeAsync(
        MyAvaloniaManagement.PluginSdk.DocumentActivation context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.RecordLoad();
        if (_probe.ThrowOnLoad)
        {
            throw new InvalidOperationException(_probe.LoadFailureMessage);
        }

        if (context is RestoreDocumentActivation { RestoredContent.SchemaVersion: not 1 })
        {
            throw new InvalidOperationException("测试 scoped Document 内容版本不受支持。");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<MyAvaloniaManagement.PluginSdk.DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var json = System.Text.Json.JsonDocument.Parse("{}");
        var content = new MyAvaloniaManagement.PluginSdk.DocumentContent(1, json.RootElement);
        return ValueTask.FromResult(new MyAvaloniaManagement.PluginSdk.DocumentSaveSnapshot(
            new MyAvaloniaManagement.PluginSdk.DocumentRevision(_revision),
            content));
    }

    public void AcceptChanges(MyAvaloniaManagement.PluginSdk.DocumentRevision savedRevision)
    {
        if (_revision == savedRevision.Value)
        {
            IsDirty = false;
        }
    }

    public void Dispose()
    {
        _probe.RecordDocumentDispose(_lifetime.IsClosing);
        _closingRegistration.Dispose();
    }
}

/// <summary>
/// 故意不实现保存契约，用于验证恢复入口拒绝类型后仍释放 Scope。
/// </summary>
internal sealed class TrackedScopedNonSavableDocument :
    MyAvaloniaManagement.PluginSdk.IPluginDocument,
    IDisposable
{
    private readonly DocumentLifecycleProbe _probe;
    private readonly MyAvaloniaManagement.PluginSdk.IDocumentLifetime _lifetime;
    private readonly CancellationTokenRegistration _closingRegistration;

    public TrackedScopedNonSavableDocument(
        DocumentLifecycleProbe probe,
        TrackedScopedDependency dependency,
        MyAvaloniaManagement.PluginSdk.IDocumentLifetime lifetime)
    {
        _probe = probe;
        _lifetime = lifetime;
        _ = dependency;
        _probe.RecordCreated();
        _closingRegistration = lifetime.ClosingToken.Register(
            _probe.RecordCancellation);
    }

    public MyAvaloniaManagement.PluginSdk.DocumentPresentationState Presentation =>
        new("Scoped Non-Savable 测试文档");
    public event EventHandler? PresentationChanged { add { } remove { } }

    public ValueTask InitializeAsync(
        MyAvaloniaManagement.PluginSdk.DocumentActivation context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is not NewDocumentActivation)
        {
            throw new NotSupportedException("非持久化测试 Document 只支持新建激活。");
        }
        _probe.RecordLoad();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _probe.RecordDocumentDispose(_lifetime.IsClosing);
        _closingRegistration.Dispose();
    }
}

/// <summary>
/// 返回固定工具和元数据的轻量工具策略。
/// </summary>
/// <summary>把一个测试 Tool 模型与最终 V2 描述符绑定为不可变贡献。</summary>
/// <remarks>
/// 测试组合根直接消费这一事实，不再模拟已删除的 Strategy 激活协议；这样单元测试与生产
/// Registry 使用相同的声明式输入，同时仍可注入精确模型实例验证显隐和 Pinned 行为。
/// </remarks>
internal sealed record StubToolContribution(Tool Model, ToolDescriptor Descriptor);
