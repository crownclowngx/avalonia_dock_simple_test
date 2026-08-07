using BiliDownloader.Models;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Services.History;

/// <summary>
/// 历史中心只读应用服务。该接口不暴露仓储写方法，确保搜索、导出和文件检查
/// 无法借由依赖对象修改任务事实，符合接口隔离与最小权限原则。
/// </summary>
public interface ITaskHistoryQueryService
{
    Task<TaskHistoryPage> QueryPageAsync(
        TaskHistoryQuery query,
        TaskHistoryPageRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TaskHistoryEntry> StreamAsync(
        TaskHistoryQuery query,
        IReadOnlyCollection<string>? taskIds = null,
        CancellationToken cancellationToken = default);

    Task<DownloadTaskRecord?> GetTaskByIdAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskHistoryDocumentOption>> GetDocumentOptionsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class TaskHistoryQueryService : ITaskHistoryQueryService
{
    private readonly ITaskHistoryReadRepository _repository;

    public TaskHistoryQueryService(ITaskHistoryReadRepository repository)
    {
        _repository = repository;
    }

    public Task<TaskHistoryPage> QueryPageAsync(
        TaskHistoryQuery query,
        TaskHistoryPageRequest request,
        CancellationToken cancellationToken = default)
        => _repository.QueryHistoryPageAsync(query, request, cancellationToken);

    public IAsyncEnumerable<TaskHistoryEntry> StreamAsync(
        TaskHistoryQuery query,
        IReadOnlyCollection<string>? taskIds = null,
        CancellationToken cancellationToken = default)
        => _repository.StreamHistoryAsync(query, taskIds, cancellationToken);

    public Task<DownloadTaskRecord?> GetTaskByIdAsync(
        string taskId,
        CancellationToken cancellationToken = default)
        => _repository.GetTaskByIdAsync(taskId, cancellationToken);

    public Task<IReadOnlyList<TaskHistoryDocumentOption>> GetDocumentOptionsAsync(
        CancellationToken cancellationToken = default)
        => _repository.GetHistoryDocumentOptionsAsync(cancellationToken);
}
