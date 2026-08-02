using BiliDownloader.Models;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>
/// G4: 任务筛选条件（不可变值对象）。
/// 设计思考：使用 record 封装筛选参数，而非在引擎方法中传递多个散参数，
/// 原因是筛选条件可能随产品演进增加字段（如 G5 的预设筛选），
/// record 的 with 表达式可以优雅地支持"基于当前条件微调"的场景。
/// </summary>
/// <param name="TitleContains">标题模糊搜索关键词（null 或空表示不筛选）</param>
/// <param name="StatusGroup">状态分组标识（"all" 表示不筛选，见 TaskFilterSortEngine 中的映射表）</param>
/// <param name="DocumentId">关联 Document 实例 ID（null 或 "all" 表示不筛选）</param>
public sealed record TaskFilterCriteria(
    string? TitleContains,
    string? StatusGroup,
    string? DocumentId,
    TaskDateRange DateRange = TaskDateRange.All);

/// <summary>
/// G4: 排序字段枚举。
/// 设计思考：仅暴露 ROADMAP 要求的三个排序维度（创建时间、状态、标题），
/// 不提前扩展"按大小排序"等未验证需求，遵循 YAGNI。
/// </summary>
public enum TaskSortField
{
    /// <summary>按创建时间排序（默认）</summary>
    CreatedAt,

    /// <summary>按状态排序（同状态聚集，便于批量操作）</summary>
    Status,

    /// <summary>按标题字母序排序</summary>
    Title,
}

/// <summary>
/// G4: 任务筛选排序引擎（纯函数，无状态）。
/// 设计思考：将筛选排序逻辑从 VM 中提取为独立纯函数类，
/// 遵循 SRP——VM 负责状态编排和命令协调，引擎负责数据变换。
/// 纯函数特性使其可被独立单元测试，无需构造 Coordinator、仓储或任何 UI 依赖。
/// 100 条任务规模下 LINQ 开销 < 1ms，无需引入增量排序或数据库分页。
/// </summary>
public static class TaskFilterSortEngine
{
    /// <summary>
    /// 对任务列表应用筛选和排序，返回新列表（不修改源集合）。
    /// </summary>
    /// <param name="source">全量任务列表（事实源）</param>
    /// <param name="criteria">筛选条件</param>
    /// <param name="sortField">排序字段</param>
    /// <param name="sortDescending">是否降序</param>
    /// <returns>筛选排序后的新列表</returns>
    public static List<DownloadTaskRecord> Apply(
        IReadOnlyList<DownloadTaskRecord> source,
        TaskFilterCriteria criteria,
        TaskSortField sortField,
        bool sortDescending)
    {
        // 第一步：筛选（WHERE 语义，多条件为 AND 关系）
        IEnumerable<DownloadTaskRecord> query = source;

        // 标题模糊搜索：不区分大小写的包含匹配
        if (!string.IsNullOrWhiteSpace(criteria.TitleContains))
        {
            var keyword = criteria.TitleContains.Trim();
            query = query.Where(t =>
                t.ItemTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        // 状态分组筛选：将 13 种枚举状态映射为用户可理解的分组
        if (!string.IsNullOrEmpty(criteria.StatusGroup) && criteria.StatusGroup != "all")
        {
            query = query.Where(t => MatchesStatusGroup(t.Status, criteria.StatusGroup));
        }

        // Document 筛选：精确匹配 DocumentId
        if (!string.IsNullOrEmpty(criteria.DocumentId) && criteria.DocumentId != "all")
        {
            query = query.Where(t => t.DocumentId == criteria.DocumentId);
        }

        var threshold = criteria.DateRange switch
        {
            TaskDateRange.Today => DateTime.Today,
            TaskDateRange.Last7Days => DateTime.Now.AddDays(-7),
            TaskDateRange.Last30Days => DateTime.Now.AddDays(-30),
            _ => DateTime.MinValue,
        };
        if (threshold != DateTime.MinValue)
            query = query.Where(t => t.CreatedAt >= threshold);

        // 第二步：排序（ORDER BY 语义）
        query = sortField switch
        {
            TaskSortField.CreatedAt => sortDescending
                ? query.OrderByDescending(t => t.CreatedAt)
                : query.OrderBy(t => t.CreatedAt),
            TaskSortField.Status => sortDescending
                ? query.OrderByDescending(t => t.Status, StringComparer.Ordinal)
                : query.OrderBy(t => t.Status, StringComparer.Ordinal),
            TaskSortField.Title => sortDescending
                ? query.OrderByDescending(t => t.ItemTitle, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(t => t.ItemTitle, StringComparer.OrdinalIgnoreCase),
            _ => query,
        };

        return query.ToList();
    }

    /// <summary>
    /// 判断任务的存储状态字符串是否属于指定的状态分组。
    /// 设计思考：状态分组面向用户认知（"运行中"包含 6 种细分状态），
    /// 而非面向内部枚举的一对一映射，降低用户理解成本。
    /// </summary>
    private static bool MatchesStatusGroup(string storageStatus, string statusGroup)
    {
        var status = DownloadTaskStatusMapper.FromStorageString(storageStatus);

        return statusGroup switch
        {
            // "running" 包含所有正在执行的状态（获取信息/下载视频/下载音频/合并等）
            "running" => DownloadTaskStatusMapper.IsRunning(status),
            "failed" => status == DownloadTaskStatus.Failed,
            "interrupted" => status == DownloadTaskStatus.Interrupted,
            "waiting_login" => status == DownloadTaskStatus.WaitingForLogin,
            "done" => status == DownloadTaskStatus.Completed,
            "paused" => status == DownloadTaskStatus.Paused,
            "canceled" => status == DownloadTaskStatus.Canceled,
            "pending" => status == DownloadTaskStatus.Ready,
            // 未知分组不做筛选（防御性编程）
            _ => true,
        };
    }

    /// <summary>
    /// 解析排序键字符串为排序字段和方向。
    /// 设计思考：View 层的 ComboBox 使用字符串标识排序方式（如 "created_desc"），
    /// 此方法将字符串解析为强类型参数，避免在 VM 中写 switch 解析逻辑。
    /// </summary>
    /// <param name="sortBy">排序键（"created_desc"/"created_asc"/"status"/"title"）</param>
    /// <returns>排序字段和是否降序</returns>
    public static (TaskSortField Field, bool Descending) ParseSortBy(string sortBy)
    {
        return sortBy switch
        {
            "created_asc" => (TaskSortField.CreatedAt, false),
            "status" => (TaskSortField.Status, false),
            "title" => (TaskSortField.Title, false),
            // 默认按创建时间降序（最新任务在前）
            _ => (TaskSortField.CreatedAt, true),
        };
    }
}
