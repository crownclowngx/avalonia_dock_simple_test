using System.Text.Json;

namespace BiliDownloader.ReleaseAcceptance;

/// <summary>
/// 单项发布门禁。每个实现只验证一种外部事实，组合器负责顺序、异常收敛和报告，
/// 避免真实网络、ffmpeg、敏感扫描再次聚合成难以测试的“大验收类”。
/// </summary>
internal interface IReleaseGate
{
    string Name { get; }
    Task<ReleaseGateResult> ExecuteAsync(ReleaseGateContext context, CancellationToken cancellationToken);
}

/// <summary>门禁共享的只读输入，以及仅在本次进程内传递的验收状态。</summary>
internal sealed class ReleaseGateContext
{
    public ReleaseGateContext(string sandboxRoot, string? bvid, string? cookie)
    {
        SandboxRoot = Path.GetFullPath(sandboxRoot);
        Bvid = bvid;
        Cookie = cookie;
    }

    public string SandboxRoot { get; }
    public string? Bvid { get; }

    /// <summary>
    /// Cookie 只允许在进程内存中流转。任何报告 DTO 都不能引用此属性，
    /// 门禁返回的消息也必须只描述规则名称，不能拼接原始异常请求或凭据。
    /// </summary>
    public string? Cookie { get; }
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
}

/// <summary>机器可读且不包含敏感原文的单项门禁结果。</summary>
internal sealed record ReleaseGateResult(
    string Name,
    bool Passed,
    string Summary,
    IReadOnlyDictionary<string, object?>? Metrics = null)
{
    public static ReleaseGateResult Pass(
        string name,
        string summary,
        IReadOnlyDictionary<string, object?>? metrics = null)
        => new(name, true, summary, metrics);

    public static ReleaseGateResult Fail(
        string name,
        string summary,
        IReadOnlyDictionary<string, object?>? metrics = null)
        => new(name, false, summary, metrics);
}

/// <summary>完整运行报告；Publishable 由外层发布脚本结合 clean worktree 再做最终判定。</summary>
internal sealed record ReleaseAcceptanceReport(
    int SchemaVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool Passed,
    IReadOnlyList<ReleaseGateResult> Gates);

/// <summary>顺序执行门禁并把意外异常转换为安全失败结果。</summary>
internal sealed class ReleaseGatePipeline(IEnumerable<IReleaseGate> gates)
{
    private readonly IReadOnlyList<IReleaseGate> _gates = gates.ToArray();

    public async Task<ReleaseAcceptanceReport> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var results = new List<ReleaseGateResult>(_gates.Count);
        foreach (var gate in _gates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await gate.ExecuteAsync(context, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 外部服务异常可能带 URL 查询参数；这里只报告异常类型，详细原文不进入证据文件。
                results.Add(ReleaseGateResult.Fail(
                    gate.Name,
                    $"门禁发生未处理异常：{ex.GetType().Name}"));
            }
        }

        return new ReleaseAcceptanceReport(
            1,
            started,
            DateTimeOffset.UtcNow,
            results.All(result => result.Passed),
            results);
    }

    public static async Task WriteReportAsync(
        string path,
        ReleaseAcceptanceReport report,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);
    }
}
