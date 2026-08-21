using System;
using System.Threading;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 宿主拥有的每 Document 关闭信号实现。
/// </summary>
/// <remarks>
/// 本类型注册为 scoped，并由 <see cref="DocumentScopeManager"/> 在释放 Scope 之前调用
/// <see cref="RequestClose"/>。先取消、后 Dispose 的顺序让异步操作尽早开始退出，同时
/// 保证 Document.Dispose 观察到的 <see cref="IsClosing"/> 已为 true。
/// </remarks>
internal sealed class DocumentLifetime :
    IDocumentLifetime,
    MyAvaloniaManagement.PluginSdk.IDocumentLifetime,
    IDisposable
{
    private readonly CancellationTokenSource _closing = new();
    private int _isClosing;
    private int _disposed;

    public CancellationToken ClosingToken => _closing.Token;

    public bool IsClosing => Volatile.Read(ref _isClosing) != 0;

    internal void RequestClose()
    {
        // 使用原子交换同时承担“关闭状态发布”和“幂等门禁”两个职责。Dock 重复通知、
        // 宿主退出兜底以及 Scope 自身 Dispose 可能汇合到这里，但取消只能发出一次。
        if (Interlocked.Exchange(ref _isClosing, 1) != 0)
        {
            return;
        }

        try
        {
            _closing.Cancel();
        }
        catch (AggregateException ex)
        {
            // CancellationToken 回调属于插件代码，理论上可能抛出异常。生命周期释放不能
            // 因单个回调失败而中断，否则整个 Scope 及其资源会泄漏；因此记录诊断后继续，
            // 由 DocumentScopeLease 的 finally 保证执行 Scope.Dispose。
            DocumentPersistenceErrorMapper.Report(
                "DOCUMENT_CLOSE_CANCELLATION_CALLBACK_FAILED",
                ex);
        }
    }

    public void Dispose()
    {
        // 正式 Lifetime 会先由租约 RequestClose，再随 DI Scope Dispose；兼容回退实例则
        // 由租约显式 Dispose。幂等门禁使两条路径可以安全汇合，不依赖容器释放顺序。
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RequestClose();
        _closing.Dispose();
    }
}
