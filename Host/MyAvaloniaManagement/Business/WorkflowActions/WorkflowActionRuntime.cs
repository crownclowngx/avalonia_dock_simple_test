using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.WorkflowActions;

/// <summary>集中保存 G1 的 Host internal 资源上限，测试可以注入更短期限。</summary>
internal sealed record WorkflowActionExecutionLimits(
    int MaximumConcurrentPerRun,
    int MaximumConcurrentPerOwner,
    TimeSpan DefaultTimeout,
    TimeSpan LongRunningTimeout,
    TimeSpan ShutdownGrace,
    TimeSpan MinimumProgressInterval)
{
    internal static WorkflowActionExecutionLimits Default { get; } = new(
        MaximumConcurrentPerRun: 4,
        MaximumConcurrentPerOwner: 4,
        DefaultTimeout: TimeSpan.FromMinutes(5),
        LongRunningTimeout: TimeSpan.FromHours(6),
        ShutdownGrace: TimeSpan.FromSeconds(10),
        MinimumProgressInterval: TimeSpan.FromMilliseconds(100));

    internal void Validate()
    {
        if (MaximumConcurrentPerRun <= 0 || MaximumConcurrentPerOwner <= 0 ||
            DefaultTimeout <= TimeSpan.Zero || LongRunningTimeout <= TimeSpan.Zero ||
            ShutdownGrace <= TimeSpan.Zero || MinimumProgressInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkflowActionExecutionLimits));
        }
    }
}

/// <summary>Host 注入到一个 Consumer Provider 的可信调用者门面。</summary>
internal sealed class CallerBoundWorkflowActionGateway(
    PluginId callerId,
    WorkflowActionRunManager runs) : IWorkflowActionGateway
{
    private readonly PluginId _callerId = callerId ?? throw new ArgumentNullException(nameof(callerId));
    private readonly WorkflowActionRunManager _runs = runs ?? throw new ArgumentNullException(nameof(runs));

    public IReadOnlyList<WorkflowActionDescriptor> GetAvailableActions() =>
        _runs.GetAvailableActions();

    public IWorkflowActionRun CreateRun() => _runs.CreateRun(_callerId);
}

/// <summary>创建和跟踪所有 Run，并在 Host 关闭时统一拒绝、取消和排空调用。</summary>
/// <remarks>
/// Manager 只拥有运行治理状态；目录事实属于 CatalogStore，Handler/Scope 属于 PluginProviderOwner，
/// 用户决策属于 Authorizer。锁内只修改计数和集合，绝不执行插件代码、UI 或 Dispose。
/// </remarks>
internal sealed class WorkflowActionRunManager : IWorkflowActionShutdownParticipant
{
    private readonly object _gate = new();
    private readonly WorkflowActionCatalogStore _catalog;
    private readonly IWorkflowActionScopeFactory _scopeFactory;
    private readonly IWorkflowActionAuthorizer _authorizer;
    private readonly WorkflowActionExecutionLimits _limits;
    private readonly TimeProvider _timeProvider;
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<WorkflowActionRun> _runs = [];
    private readonly Dictionary<PluginId, int> _ownerConcurrency = [];
    private TaskCompletionSource _drained = CompletedDrainSource();
    private int _activeInvocations;
    private bool _accepting = true;

    internal WorkflowActionRunManager(
        WorkflowActionCatalogStore catalog,
        IWorkflowActionScopeFactory scopeFactory,
        IWorkflowActionAuthorizer authorizer,
        WorkflowActionExecutionLimits limits,
        TimeProvider timeProvider,
        IHostDiagnosticSink? diagnostics = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _limits.Validate();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _diagnostics = diagnostics;
    }

    public TimeSpan ShutdownGrace => _limits.ShutdownGrace;

    internal IReadOnlyList<WorkflowActionDescriptor> GetAvailableActions()
    {
        lock (_gate)
        {
            if (!_accepting)
            {
                return [];
            }
        }
        return _catalog.GetAvailableDescriptors();
    }

    internal IWorkflowActionRun CreateRun(PluginId callerId)
    {
        ArgumentNullException.ThrowIfNull(callerId);
        lock (_gate)
        {
            if (!_accepting)
            {
                throw new InvalidOperationException("Host 正在关闭，不能创建新的 Workflow Action Run。");
            }
            if (!_catalog.IsCommitted)
            {
                throw new InvalidOperationException("Workflow Action 目录尚未提交。");
            }
            var run = new WorkflowActionRun(this, callerId, _catalog.Revision);
            _runs.Add(run);
            return run;
        }
    }

    /// <summary>关闭创建入口并取消所有 Run；取消发生在锁外，避免回调重入。</summary>
    public void BeginShutdown()
    {
        WorkflowActionRun[] runs;
        lock (_gate)
        {
            if (!_accepting)
            {
                return;
            }
            _accepting = false;
            _shutdown.Cancel(throwOnFirstException: false);
            runs = _runs.ToArray();
        }
        foreach (var run in runs)
        {
            run.CancelFromHost();
        }
    }

    /// <summary>在给定宽限内等待全部实际 Handler 退出；超时不会假装强杀插件代码。</summary>
    public async Task<bool> WaitForDrainAsync(TimeSpan timeout)
    {
        Task wait;
        lock (_gate)
        {
            wait = _activeInvocations == 0 ? Task.CompletedTask : _drained.Task;
        }
        try
        {
            await wait.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    internal async Task<WorkflowActionInvocationResult> InvokeAsync(
        WorkflowActionRun run,
        WorkflowActionInvocationRequest request,
        IProgress<WorkflowActionProgress>? progress,
        CancellationToken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(request);
        var invocationId = Guid.NewGuid();
        if (!TryBeginInvocation())
        {
            return Result(invocationId, WorkflowActionInvocationStatus.Rejected,
                "WORKFLOW_ACTION_HOST_SHUTTING_DOWN", "Host 正在关闭，已拒绝新调用。");
        }

        var stopwatch = Stopwatch.StartNew();
        PluginWorkflowActionRegistration? registration = null;
        try
        {
            if (run.Revision != _catalog.Revision ||
                !_catalog.TryGet(request.ActionId, out registration))
            {
                return Failure(invocationId, WorkflowActionInvocationStatus.Rejected,
                    "WORKFLOW_ACTION_NOT_FOUND", "未找到指定的 Workflow Action。",
                    registration, stopwatch.Elapsed);
            }
            if (!_catalog.IsOwnerAvailable(registration.OwnerId))
            {
                return Failure(invocationId, WorkflowActionInvocationStatus.Unavailable,
                    "WORKFLOW_ACTION_OWNER_UNAVAILABLE", "Workflow Action 所有者当前不可用。",
                    registration, stopwatch.Elapsed);
            }
            try
            {
                WorkflowActionSchemaValidator.ValidateInstance(
                    registration.Descriptor.InputSchema,
                    request.Arguments,
                    WorkflowActionSchemaValidator.MaximumInputBytes);
            }
            catch (ArgumentException)
            {
                return Failure(invocationId, WorkflowActionInvocationStatus.Rejected,
                    "WORKFLOW_ACTION_INPUT_INVALID", "Workflow Action 输入未通过 Schema 或预算校验。",
                    registration, stopwatch.Elapsed);
            }

            if (!run.TryEnter(_limits.MaximumConcurrentPerRun))
            {
                return Failure(invocationId, WorkflowActionInvocationStatus.Rejected,
                    "WORKFLOW_ACTION_CONCURRENCY_LIMIT", "当前 Run 已达到并发上限。",
                    registration, stopwatch.Elapsed);
            }
            try
            {
                if (!TryEnterOwner(registration.OwnerId))
                {
                    return Failure(invocationId, WorkflowActionInvocationStatus.Rejected,
                        "WORKFLOW_ACTION_CONCURRENCY_LIMIT", "动作所有者已达到并发上限。",
                        registration, stopwatch.Elapsed);
                }
                try
                {
                    using var governanceCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        callerCancellation, run.CancellationToken, _shutdown.Token);
                    if (!await AuthorizeAsync(
                            run, registration, request.Arguments, governanceCancellation.Token)
                        .ConfigureAwait(false))
                    {
                        return Failure(invocationId, WorkflowActionInvocationStatus.Rejected,
                            "WORKFLOW_ACTION_AUTHORIZATION_DENIED", "用户未授权本次 Workflow Action。",
                            registration, stopwatch.Elapsed);
                    }

                    return await InvokeHandlerAsync(
                        run,
                        registration,
                        request.Arguments,
                        progress,
                        invocationId,
                        callerCancellation,
                        stopwatch).ConfigureAwait(false);
                }
                finally
                {
                    ExitOwner(registration.OwnerId);
                }
            }
            finally
            {
                run.Exit();
            }
        }
        catch (OperationCanceledException)
        {
            return Failure(invocationId, WorkflowActionInvocationStatus.Cancelled,
                "WORKFLOW_ACTION_CANCELLED", "Workflow Action 已取消。",
                registration, stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            return Failure(invocationId, WorkflowActionInvocationStatus.Failed,
                "WORKFLOW_ACTION_HOST_FAILURE", "Host 无法完成 Workflow Action 调用。",
                registration, stopwatch.Elapsed, exception);
        }
        finally
        {
            EndInvocation();
        }
    }

    private async Task<WorkflowActionInvocationResult> InvokeHandlerAsync(
        WorkflowActionRun run,
        PluginWorkflowActionRegistration registration,
        JsonElement arguments,
        IProgress<WorkflowActionProgress>? progress,
        Guid invocationId,
        CancellationToken callerCancellation,
        Stopwatch stopwatch)
    {
        var timeoutValue = registration.Descriptor.Risks.HasFlag(WorkflowActionRiskFlags.LongRunning)
            ? _limits.LongRunningTimeout
            : _limits.DefaultTimeout;
        using var timeout = new CancellationTokenSource(timeoutValue, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellation, run.CancellationToken, _shutdown.Token, timeout.Token);
        var relay = new WorkflowActionProgressRelay(
            progress, _limits.MinimumProgressInterval, _timeProvider);
        try
        {
            await using var scope = _scopeFactory.CreateWorkflowActionScope(
                registration.OwnerId, registration.HandlerType);
            var output = await scope.Handler.InvokeAsync(
                arguments.Clone(),
                new WorkflowActionContext(invocationId, run.CallerId, relay),
                linked.Token).ConfigureAwait(false);

            if (timeout.IsCancellationRequested &&
                !callerCancellation.IsCancellationRequested &&
                !run.CancellationToken.IsCancellationRequested &&
                !_shutdown.IsCancellationRequested)
            {
                return Failure(invocationId, WorkflowActionInvocationStatus.TimedOut,
                    "WORKFLOW_ACTION_TIMEOUT", "Workflow Action 已超过 Host 时间预算。",
                    registration, stopwatch.Elapsed);
            }
            try
            {
                WorkflowActionSchemaValidator.ValidateInstance(
                    registration.Descriptor.OutputSchema,
                    output,
                    WorkflowActionSchemaValidator.MaximumOutputBytes);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Failure(invocationId, WorkflowActionInvocationStatus.Failed,
                    "WORKFLOW_ACTION_OUTPUT_INVALID", "Workflow Action 输出未通过 Schema 或预算校验。",
                    registration, stopwatch.Elapsed, exception);
            }
            return new WorkflowActionInvocationResult(
                invocationId,
                WorkflowActionInvocationStatus.Succeeded,
                output.Clone(),
                failure: null);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !callerCancellation.IsCancellationRequested &&
            !run.CancellationToken.IsCancellationRequested &&
            !_shutdown.IsCancellationRequested)
        {
            return Failure(invocationId, WorkflowActionInvocationStatus.TimedOut,
                "WORKFLOW_ACTION_TIMEOUT", "Workflow Action 已超过 Host 时间预算。",
                registration, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return Failure(invocationId, WorkflowActionInvocationStatus.Cancelled,
                "WORKFLOW_ACTION_CANCELLED", "Workflow Action 已取消。",
                registration, stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            return Failure(invocationId, WorkflowActionInvocationStatus.Failed,
                "WORKFLOW_ACTION_HANDLER_FAILED", "Workflow Action Handler 执行失败。",
                registration, stopwatch.Elapsed, exception);
        }
    }

    private Task<bool> AuthorizeAsync(
        WorkflowActionRun run,
        PluginWorkflowActionRegistration registration,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (registration.Descriptor.ConfirmationPolicy == WorkflowActionConfirmationPolicy.Never)
        {
            return Task.FromResult(true);
        }
        var request = new WorkflowActionAuthorizationRequest(
            run.CallerId,
            registration.OwnerId,
            registration.Descriptor,
            WorkflowActionArgumentSummary.Create(
                arguments,
                registration.Descriptor.SensitiveInputPointers));
        if (registration.Descriptor.ConfirmationPolicy ==
            WorkflowActionConfirmationPolicy.EveryInvocation)
        {
            return AuthorizeFailClosedAsync(request, cancellationToken);
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(
            WorkflowActionJsonCanonicalizer.GetUtf8Bytes(arguments)));
        var key = $"{run.Revision}:{registration.Descriptor.Id.Value}:{fingerprint}";
        return run.AuthorizeOnceAsync(
            key,
            () => AuthorizeFailClosedAsync(request, cancellationToken));
    }

    private async Task<bool> AuthorizeFailClosedAsync(
        WorkflowActionAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _authorizer.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private bool TryBeginInvocation()
    {
        lock (_gate)
        {
            if (!_accepting)
            {
                return false;
            }
            if (_activeInvocations++ == 0)
            {
                _drained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return true;
        }
    }

    private void EndInvocation()
    {
        TaskCompletionSource? completed = null;
        lock (_gate)
        {
            if (--_activeInvocations == 0)
            {
                completed = _drained;
            }
        }
        completed?.TrySetResult();
    }

    private bool TryEnterOwner(PluginId ownerId)
    {
        lock (_gate)
        {
            var current = _ownerConcurrency.GetValueOrDefault(ownerId);
            if (current >= _limits.MaximumConcurrentPerOwner)
            {
                return false;
            }
            _ownerConcurrency[ownerId] = current + 1;
            return true;
        }
    }

    private void ExitOwner(PluginId ownerId)
    {
        lock (_gate)
        {
            var remaining = _ownerConcurrency[ownerId] - 1;
            if (remaining == 0)
            {
                _ownerConcurrency.Remove(ownerId);
            }
            else
            {
                _ownerConcurrency[ownerId] = remaining;
            }
        }
    }

    internal void RemoveRun(WorkflowActionRun run)
    {
        lock (_gate)
        {
            _runs.Remove(run);
        }
    }

    private WorkflowActionInvocationResult Failure(
        Guid invocationId,
        WorkflowActionInvocationStatus status,
        string code,
        string message,
        PluginWorkflowActionRegistration? registration,
        TimeSpan duration,
        Exception? exception = null)
    {
        if (registration is not null)
        {
            _diagnostics?.Report(new HostDiagnosticDraft(code, HostDiagnosticPhase.WorkflowAction)
            {
                PluginId = registration.OwnerId,
                StableId = registration.Descriptor.Id.Value,
                Duration = duration,
                Exception = exception,
            });
        }
        return Result(invocationId, status, code, message);
    }

    private static WorkflowActionInvocationResult Result(
        Guid invocationId,
        WorkflowActionInvocationStatus status,
        string code,
        string message) => new(
            invocationId,
            status,
            output: null,
            new WorkflowActionFailure(code, message));

    private static TaskCompletionSource CompletedDrainSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

/// <summary>实现 public IWorkflowActionRun，并独占一次真实运行的取消、授权和并发状态。</summary>
internal sealed class WorkflowActionRun(
    WorkflowActionRunManager manager,
    PluginId callerId,
    string revision) : IWorkflowActionRun
{
    private readonly object _gate = new();
    private readonly WorkflowActionRunManager _manager = manager;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly HashSet<Task> _activeTasks = [];
    private readonly Dictionary<string, Task<bool>> _authorizations = new(StringComparer.Ordinal);
    private int _concurrency;
    private bool _disposed;

    internal PluginId CallerId { get; } = callerId;
    internal string Revision { get; } = revision;
    internal CancellationToken CancellationToken => _cancellation.Token;

    public Task<WorkflowActionInvocationResult> InvokeAsync(
        WorkflowActionInvocationRequest request,
        IProgress<WorkflowActionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Task<WorkflowActionInvocationResult> task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            task = _manager.InvokeAsync(this, request, progress, cancellationToken);
            _activeTasks.Add(task);
        }
        _ = task.ContinueWith(
            completed => RemoveTask(completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _cancellation.Cancel(throwOnFirstException: false);
            tasks = _activeTasks.ToArray();
        }
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            _manager.RemoveRun(this);
            _cancellation.Dispose();
        }
    }

    internal void CancelFromHost() => _cancellation.Cancel(throwOnFirstException: false);

    internal bool TryEnter(int maximum)
    {
        lock (_gate)
        {
            if (_disposed || _concurrency >= maximum)
            {
                return false;
            }
            _concurrency++;
            return true;
        }
    }

    internal void Exit()
    {
        lock (_gate)
        {
            _concurrency--;
        }
    }

    internal Task<bool> AuthorizeOnceAsync(string key, Func<Task<bool>> authorize)
    {
        Task<bool> task;
        lock (_gate)
        {
            if (_authorizations.TryGetValue(key, out task!))
            {
                return task;
            }
            task = authorize();
            _authorizations.Add(key, task);
        }
        return RemoveDeniedAsync(key, task);
    }

    private async Task<bool> RemoveDeniedAsync(string key, Task<bool> task)
    {
        var approved = await task.ConfigureAwait(false);
        if (!approved)
        {
            lock (_gate)
            {
                if (_authorizations.GetValueOrDefault(key) == task)
                {
                    _authorizations.Remove(key);
                }
            }
        }
        return approved;
    }

    private void RemoveTask(Task task)
    {
        lock (_gate)
        {
            _activeTasks.Remove(task);
        }
    }
}

/// <summary>同步验证、限流并隔离 Consumer 进度回调异常。</summary>
internal sealed class WorkflowActionProgressRelay(
    IProgress<WorkflowActionProgress>? consumer,
    TimeSpan minimumInterval,
    TimeProvider timeProvider) : IProgress<WorkflowActionProgress>
{
    private readonly object _gate = new();
    private long? _lastTimestamp;

    public void Report(WorkflowActionProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (consumer is null || value.Stage.Length > 64 ||
            value.Message?.Length > 512 || !IsSafeStage(value.Stage))
        {
            return;
        }

        lock (_gate)
        {
            var now = timeProvider.GetTimestamp();
            if (_lastTimestamp is { } last &&
                timeProvider.GetElapsedTime(last, now) < minimumInterval)
            {
                return;
            }
            _lastTimestamp = now;
        }
        try
        {
            consumer.Report(new WorkflowActionProgress(value.Stage, value.Percent, value.Message));
        }
        catch
        {
            // Consumer 的观察器不是 Handler 业务结果的一部分；回调失败不能反向改变动作终态。
        }
    }

    private static bool IsSafeStage(string stage) => stage.All(character =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
