using System.Text.Json;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>集中建立 G3 当前实例路由测试使用的真实 Registry、Scope 和 Target 模型。</summary>
internal static class WorkbenchCommandG3TestContext
{
    internal static readonly DocumentTypeId DocumentType =
        new("myavalonia.plugin.host-tests.document.command-target");
    internal static readonly CommandId Command =
        new("myavalonia.plugin.host-tests.command.run-current");
    internal static readonly CommandId UnknownCommand =
        new("myavalonia.plugin.host-tests.command.unknown");

    internal static TestHostContext Create(
        RecordingWorkbenchCommandDiagnosticSink? diagnostics = null,
        bool persistable = false,
        bool targetImplemented = true) =>
        new(
            configureServices: services =>
            {
                services.AddSingleton<WorkbenchCommandG3Probe>();
                if (targetImplemented)
                {
                    services.AddScoped<WorkbenchCommandG3Document>();
                }
                else
                {
                    services.AddScoped<WorkbenchCommandG3PlainDocument>();
                }
                if (diagnostics is not null)
                {
                    services.AddSingleton<IHostDiagnosticSink>(diagnostics);
                }
            },
            configureContributions: (_, builder) =>
            {
                var modelType = targetImplemented
                    ? typeof(WorkbenchCommandG3Document)
                    : typeof(WorkbenchCommandG3PlainDocument);
                builder.AddDocument(
                    TestPluginIds.Owner,
                    new DocumentDescriptor(
                        DocumentType,
                        "G3 命令文档",
                        "验证活动实例命令路由",
                        "测试"),
                    modelType,
                    typeof(UserControl),
                    static () => new UserControl(),
                    persistable);
                builder.AddDocumentCommand(
                    TestPluginIds.Owner,
                    new CommandDescriptor(Command, "运行当前实例", "验证 G3 Target 路由"),
                    DocumentType);
            });

    internal static async ValueTask<ManagedDocumentDockable> CreateDocumentAsync(
        TestHostContext context,
        string title)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = context.CreateMainWindowViewModel();
        return await context.Workspace.CreateAndPublishDocumentAsync(
            DocumentType,
            new NewDocumentActivation(title));
    }
}

/// <summary>记录每个 scoped Target 实例和资源释放时点。</summary>
internal sealed class WorkbenchCommandG3Probe
{
    private readonly object _gate = new();
    private readonly List<WorkbenchCommandG3Document> _documents = [];

    internal IReadOnlyList<WorkbenchCommandG3Document> Documents
    {
        get
        {
            lock (_gate)
            {
                return _documents.ToArray();
            }
        }
    }

    internal void Register(WorkbenchCommandG3Document document)
    {
        lock (_gate)
        {
            _documents.Add(document);
        }
    }
}

/// <summary>可编排状态、事件、取消和异常的真实 scoped Document Target。</summary>
internal sealed class WorkbenchCommandG3Document :
    IPersistablePluginDocument,
    IWorkbenchDocumentCommandTarget,
    IDisposable
{
    private readonly WorkbenchCommandG3Probe _probe;
    private EventHandler<WorkbenchCommandStateChangedEventArgs>? _stateChanged;
    private EventHandler<WorkbenchCommandStateChangedEventArgs>? _lastRemovedHandler;
    private int _subscriberCount;
    private int _disposed;

    public WorkbenchCommandG3Document(WorkbenchCommandG3Probe probe)
    {
        _probe = probe;
        probe.Register(this);
    }

    internal bool AllowExecute { get; set; } = true;
    internal bool ThrowOnCanExecute { get; set; }
    internal bool ThrowOnEventAdd { get; set; }
    internal bool ThrowOnEventRemove { get; set; }
    internal Exception? ExecuteException { get; set; }
    internal bool BlockUntilCanceled { get; set; }
    internal int ExecutionCount { get; private set; }
    internal CommandId? LastExecutedCommand { get; private set; }
    internal int SubscriberCount => Volatile.Read(ref _subscriberCount);
    internal bool CancellationObservedBeforeDispose { get; private set; }
    internal TaskCompletionSource Entered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CancellationObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ReleaseAfterCancellation { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Disposed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DocumentPresentationState Presentation => new("G3 Target");
    public event EventHandler? PresentationChanged { add { } remove { } }
    public bool IsDirty => false;
    public event EventHandler? IsDirtyChanged { add { } remove { } }

    public event EventHandler<WorkbenchCommandStateChangedEventArgs>? CommandStateChanged
    {
        add
        {
            if (ThrowOnEventAdd)
            {
                throw new InvalidOperationException("secret-event-add");
            }
            _stateChanged += value;
            Interlocked.Increment(ref _subscriberCount);
        }
        remove
        {
            if (ThrowOnEventRemove)
            {
                throw new InvalidOperationException("secret-event-remove");
            }
            _stateChanged -= value;
            _lastRemovedHandler = value;
            Interlocked.Decrement(ref _subscriberCount);
        }
    }

    public ValueTask InitializeAsync(
        DocumentActivation context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public bool CanExecute(CommandId commandId)
    {
        if (ThrowOnCanExecute)
        {
            throw new InvalidOperationException("secret-state-path");
        }
        return commandId == WorkbenchCommandG3TestContext.Command && AllowExecute;
    }

    public async ValueTask ExecuteAsync(
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        ExecutionCount++;
        LastExecutedCommand = commandId;
        Entered.TrySetResult();
        if (ExecuteException is not null)
        {
            throw ExecuteException;
        }
        if (!BlockUntilCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CancellationObservedBeforeDispose = Volatile.Read(ref _disposed) == 0;
            CancellationObserved.TrySetResult();
            await ReleaseAfterCancellation.Task;
            throw;
        }
    }

    internal void RaiseStateChanged(CommandId commandId) =>
        _stateChanged?.Invoke(this, new WorkbenchCommandStateChangedEventArgs(commandId));

    internal void RaiseLateStateChanged(CommandId commandId) =>
        _lastRemovedHandler?.Invoke(this, new WorkbenchCommandStateChangedEventArgs(commandId));

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var json = JsonDocument.Parse("{}");
        return ValueTask.FromResult(new DocumentSaveSnapshot(
            new DocumentRevision(0),
            new DocumentContent(1, json.RootElement)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Disposed.TrySetResult();
    }
}

/// <summary>故意不实现 Target 的普通 Document，用于验证能力缺失映射。</summary>
internal sealed class WorkbenchCommandG3PlainDocument : IPluginDocument
{
    public DocumentPresentationState Presentation => new("G3 Plain");
    public event EventHandler? PresentationChanged { add { } remove { } }

    public ValueTask InitializeAsync(
        DocumentActivation context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

/// <summary>保存 G3 测试观察到的脱敏诊断草稿。</summary>
internal sealed class RecordingWorkbenchCommandDiagnosticSink : IHostDiagnosticSink
{
    internal List<HostDiagnosticDraft> Drafts { get; } = [];

    public HostDiagnosticRecord Report(HostDiagnosticDraft diagnostic)
    {
        Drafts.Add(diagnostic);
        return new HostDiagnosticRecord
        {
            SessionId = Guid.Empty,
            Sequence = Drafts.Count,
            TimestampUtc = DateTimeOffset.UnixEpoch,
            Code = diagnostic.Code,
            Severity = HostDiagnosticSeverity.Error,
            Phase = diagnostic.Phase,
            Disposition = HostDiagnosticDisposition.Continue,
            UserMessage = "测试诊断",
        };
    }
}
