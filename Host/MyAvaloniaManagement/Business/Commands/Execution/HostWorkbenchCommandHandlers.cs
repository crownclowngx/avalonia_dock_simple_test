using System;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Documents;

namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>Host Catalog 调用内建命令所需的最小执行端口。</summary>
/// <remarks>
/// 当前存在打开和保存两个真实实现，因此该接口是实际替换边界，不是为单个类制造的形式抽象。
/// 它不暴露 Provider、Context 或任意参数对象。
/// </remarks>
internal interface IHostWorkbenchCommandHandler
{
    /// <summary>依据不可变 Context 快照判断本次 Host 用例是否可执行。</summary>
    bool CanExecute(WorkbenchContextSnapshot context);

    ValueTask ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>把 Host 打开命令适配到既有 Document 持久化用例。</summary>
internal sealed class HostOpenDocumentCommandHandler(
    DocumentPersistenceCoordinator documents,
    DocumentOperationState operationState) : IHostWorkbenchCommandHandler
{
    private readonly DocumentPersistenceCoordinator _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));
    private readonly DocumentOperationState _operationState =
        operationState ?? throw new ArgumentNullException(nameof(operationState));

    /// <summary>打开文件不依赖活动 Document，Host 接受命令时始终可执行。</summary>
    public bool CanExecute(WorkbenchContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return true;
    }

    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        // 现有文件选择端口尚未接收 CancellationToken。调用开始前先拒绝已经取消的请求；
        // 一旦真实文件事务开始，Executor 会等待它完成，不能在副作用已经提交后伪称取消成功。
        cancellationToken.ThrowIfCancellationRequested();
        _operationState.Apply(await _documents.OpenSelectedAsync());
    }
}

/// <summary>把 Host 保存命令适配到既有活动 Document 保存用例。</summary>
internal sealed class HostSaveDocumentCommandHandler(
    DocumentPersistenceCoordinator documents,
    DocumentOperationState operationState) : IHostWorkbenchCommandHandler
{
    private readonly DocumentPersistenceCoordinator _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));
    private readonly DocumentOperationState _operationState =
        operationState ?? throw new ArgumentNullException(nameof(operationState));

    /// <summary>只有存在声明了 Host 持久化能力的活动 Document 时才允许保存。</summary>
    public bool CanExecute(WorkbenchContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsActiveDocumentPersistable;
    }

    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        // 保存事务与打开事务采用相同语义：开始前观察取消，开始后等待现有协调器给出真实结果。
        cancellationToken.ThrowIfCancellationRequested();
        _operationState.Apply(await _documents.SaveActiveAsync());
    }
}
