using System;
using System.Threading;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>一份已初始化文档在发布前的局部回滚义务。</summary>
/// <remarks>
/// Session/Scope 始终是资源所有者，本对象只保证调用链跨越文件登记、恢复确认等 await 时，
/// 不会忘记撤销候选。发布成功才解除义务；Dispose 只回调 Session，既不直接释放模型/View，
/// 也不创建通用事务协议。按 UI 调用链使用；交换引用仅保证重复 Dispose 不重复回滚。
/// </remarks>
internal sealed class PendingWorkspaceDocument(
    WorkspaceSession workspace,
    ManagedDocumentDockable document) : IDisposable
{
    private WorkspaceSession? _workspace = workspace;
    internal ManagedDocumentDockable Document { get; } = document;

    /// <summary>交给原工作区发布；插入或通知失败时保留义务，让 using 的退出统一回滚。</summary>
    internal ManagedDocumentDockable Publish(DocumentCreationTarget? target = null)
    {
        var owner = _workspace ?? throw new InvalidOperationException("候选文档已发布或回滚。");
        owner.PublishDocument(Document, target);
        _workspace = null;
        return Document;
    }

    public void Dispose() => Interlocked.Exchange(ref _workspace, null)?.RollbackPendingDocument(Document);
}
