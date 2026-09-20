using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>
/// 把原生浮窗的同步关闭请求接到文档范围确认。它只暂借本次窗口及内容，不拥有业务实例。
/// 所有目标获准后才重试框架关闭；窗口移除、框架拒绝和迟到任务均撤销本轮许可。
/// </summary>
/// <remarks>
/// post 只负责把重试安排到当前 Closing 回调结束之后，避免同步完成的确认递归关闭窗口。
/// 生产传入 UI Dispatcher，测试传入可控制队列，因此无需用睡眠猜测原生事件顺序。
/// </remarks>
internal sealed class DockWindowCloseCoordinator(
    DocumentCloseCoordinator documents,
    Action<Action> post,
    Func<IDockWindow, IDisposable?>? ownerScope = null) : IDisposable
{
    private readonly Dictionary<IDockWindow, Request> _requests = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    /// <summary>创建提交必须避开正在确认或等待最终关闭的窗口，不能在旧关闭范围内插入新页。</summary>
    internal bool IsClosing(IDockWindow window) => _requests.ContainsKey(window);

    /// <summary>首次请求先取消；重试只对仍然相同的窗口内容集合开放。</summary>
    internal bool TryBeginClose(IDockWindow window, Action retry)
    {
        if (_disposed) return false;
        if (_requests.TryGetValue(window, out var request))
        {
            if (request.Approval is null) return false;
            if (SameContents(window, request)) return true;
            Complete(window);
            return false;
        }
        request = new Request(GetContents(window));
        _requests.Add(window, request);
        _ = PrepareAndRetryAsync(window, request, retry);
        return false;
    }

    /// <summary>窗口真正关闭、被合并移除或最终拒绝后，解除这一轮关闭许可。</summary>
    internal void Complete(IDockWindow window)
    {
        if (_requests.Remove(window, out var request)) request.Approval?.Dispose();
    }

    private async Task PrepareAndRetryAsync(IDockWindow window, Request request, Action retry)
    {
        DocumentCloseApproval? approval = null;
        try
        {
            using var interaction = ownerScope?.Invoke(window);
            approval = await documents.PrepareRangeCloseAsync(request.Contents.OfType<ManagedDocumentDockable>().ToArray());
            if (approval is null || !IsCurrent(window, request) || !SameContents(window, request)) return;
            request.Approval = approval;
            approval = null;
            post(() =>
            {
                if (!IsCurrent(window, request)) return;
                try
                {
                    if (!SameContents(window, request)) { Complete(window); return; }
                    retry();
                }
                catch (Exception exception)
                {
                    Complete(window);
                    DocumentPersistenceErrorMapper.Report("DOCK_WINDOW_CLOSE_RETRY_FAILED", exception);
                }
            });
        }
        catch (Exception exception)
        {
            Complete(window);
            DocumentPersistenceErrorMapper.Report("DOCK_WINDOW_CLOSE_PREPARE_FAILED", exception);
        }
        finally
        {
            approval?.Dispose();
            if (IsCurrent(window, request) && request.Approval is null) Complete(window);
        }
    }

    private bool IsCurrent(IDockWindow window, Request request) => !_disposed &&
        _requests.TryGetValue(window, out var current) && ReferenceEquals(current, request);

    private static IDockable[] GetContents(IDockWindow window) => window.Layout is { } root
        ? DockTreeNavigator.Enumerate(root).Where(item => item is ManagedDocumentDockable or Tool).ToArray()
        : [];

    private static bool SameContents(IDockWindow window, Request request) =>
        new HashSet<IDockable>(GetContents(window), ReferenceEqualityComparer.Instance).SetEquals(request.Contents);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var request in _requests.Values) request.Approval?.Dispose();
        _requests.Clear();
    }

    /// <summary>按引用识别一轮请求；旧任务不能完成或撤销同窗口上后来产生的新请求。</summary>
    private sealed class Request(IDockable[] contents)
    {
        internal IDockable[] Contents { get; } = contents;
        internal DocumentCloseApproval? Approval { get; set; }
    }
}
