using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;

namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>表示 Executor 对一个 Document Adapter 的一次在途使用权。</summary>
/// <remarks>
/// Lease 只延长 Host 对 Adapter/Target 的使用窗口，不创建 DI Scope，也不拥有插件模型。
/// 释放最后一个 Lease 后，等待关闭的 Dock 流程才可以继续释放既有 Document Scope。
/// </remarks>
internal sealed class WorkbenchDocumentCommandLease : IDisposable
{
    private WorkbenchDocumentCommandLeaseStore? _owner;
    private readonly ManagedDocumentDockable _document;

    internal WorkbenchDocumentCommandLease(
        WorkbenchDocumentCommandLeaseStore owner,
        ManagedDocumentDockable document,
        CancellationToken closingToken)
    {
        _owner = owner;
        _document = document;
        ClosingToken = closingToken;
    }

    /// <summary>获取仅在单个 Document 获准关闭时取消的令牌。</summary>
    internal CancellationToken ClosingToken { get; }

    public void Dispose() =>
        Interlocked.Exchange(ref _owner, null)?.Release(_document);
}

/// <summary>按 Adapter 引用跟踪 Document Command 的在途数量和关闭排空。</summary>
/// <remarks>
/// 本类型不等待、不执行 Target，也不释放 Adapter。它只保证“禁止新调用、传播取消、最后一个调用退出”
/// 三个事实的原子性；DocumentCloseCoordinator 仍是唯一决定何时重试 Dock 关闭的对象。
/// </remarks>
internal sealed class WorkbenchDocumentCommandLeaseStore
{
    private readonly object _gate = new();
    private readonly Dictionary<ManagedDocumentDockable, LeaseState> _states =
        new(ReferenceEqualityComparer.Instance);
    private readonly IHostDiagnosticSink? _diagnostics;

    internal WorkbenchDocumentCommandLeaseStore(IHostDiagnosticSink? diagnostics = null) =>
        _diagnostics = diagnostics;

    /// <summary>仅在 Document 尚未进入关闭阶段时取得一次引用计数 Lease。</summary>
    internal bool TryAcquire(
        ManagedDocumentDockable document,
        out WorkbenchDocumentCommandLease? lease)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            if (!_states.TryGetValue(document, out var state))
            {
                state = new LeaseState();
                _states.Add(document, state);
            }
            if (state.IsClosing)
            {
                lease = null;
                return false;
            }

            if (state.ActiveCount == 0)
            {
                state.Drained = NewDrainSource();
            }
            state.ActiveCount++;
            lease = new WorkbenchDocumentCommandLease(
                this,
                document,
                state.CloseCancellation.Token);
            return true;
        }
    }

    /// <summary>禁止新调用、取消已有调用，并返回全部 Lease 退出时完成的任务。</summary>
    internal Task BeginClose(ManagedDocumentDockable document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CancellationTokenSource? cancellation = null;
        Task drain;
        lock (_gate)
        {
            if (!_states.TryGetValue(document, out var state))
            {
                state = new LeaseState();
                _states.Add(document, state);
            }
            if (!state.IsClosing)
            {
                state.IsClosing = true;
                cancellation = state.CloseCancellation;
            }
            drain = state.ActiveCount == 0 ? Task.CompletedTask : state.Drained.Task;
        }

        if (cancellation is not null)
        {
            try
            {
                // CancellationToken 注册回调属于插件边界，必须在 Host 状态锁外运行。
                cancellation.Cancel(throwOnFirstException: false);
            }
            catch (Exception exception)
            {
                _diagnostics?.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.WorkbenchCommandDocumentCloseCancellationFailed,
                    HostDiagnosticPhase.WorkbenchCommand)
                {
                    StableId = document.Registration.Descriptor.DocumentTypeId.Value,
                    Exception = exception,
                });
            }
        }
        return drain;
    }

    /// <summary>Dock 最终拒绝关闭时恢复命令入口。</summary>
    internal void Reopen(ManagedDocumentDockable document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            if (!_states.TryGetValue(document, out var state) || !state.IsClosing)
            {
                return;
            }
            if (state.ActiveCount != 0)
            {
                throw new InvalidOperationException("Document Command 尚未排空，不能恢复关闭状态。");
            }
            _states.Remove(document);
            cancellation = state.CloseCancellation;
        }
        cancellation.Dispose();
    }

    /// <summary>Document 真正关闭后移除不再需要的引用状态。</summary>
    internal void CompleteClose(ManagedDocumentDockable document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            if (!_states.Remove(document, out var state))
            {
                return;
            }
            if (state.ActiveCount != 0)
            {
                _states.Add(document, state);
                throw new InvalidOperationException("Document Command 未排空，不能释放活动 Target。");
            }
            cancellation = state.CloseCancellation;
        }
        cancellation.Dispose();
    }

    internal void Release(ManagedDocumentDockable document)
    {
        TaskCompletionSource? drained = null;
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            if (!_states.TryGetValue(document, out var state) || state.ActiveCount <= 0)
            {
                throw new InvalidOperationException("Document Command Lease 释放次数超过取得次数。");
            }
            state.ActiveCount--;
            if (state.ActiveCount == 0)
            {
                drained = state.Drained;
                if (!state.IsClosing)
                {
                    _states.Remove(document);
                    cancellation = state.CloseCancellation;
                }
            }
        }

        // Continuation 和 CancellationTokenSource.Dispose 均在锁外执行，避免关闭重试重入。
        drained?.TrySetResult();
        cancellation?.Dispose();
    }

    private static TaskCompletionSource NewDrainSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class LeaseState
    {
        internal int ActiveCount { get; set; }
        internal bool IsClosing { get; set; }
        internal CancellationTokenSource CloseCancellation { get; } = new();
        internal TaskCompletionSource Drained { get; set; } = CompletedDrainSource();
    }

    private static TaskCompletionSource CompletedDrainSource()
    {
        var source = NewDrainSource();
        source.TrySetResult();
        return source;
    }
}
