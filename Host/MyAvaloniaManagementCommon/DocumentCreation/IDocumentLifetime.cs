namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 表示当前 Managed Document 由宿主管理的关闭生命周期。
/// </summary>
/// <remarks>
/// <para>
/// 每个 Document Scope 都拥有独立实例。Dock 只有在确认标签已经永久关闭后，才由宿主
/// 取消 <see cref="ClosingToken"/>；关闭请求被用户或保存流程否决时不会提前发出信号。
/// </para>
/// <para>
/// 该接口只暴露观察能力，不暴露主动取消方法，设计意图是保持生命周期所有权单一：
/// 插件可以响应关闭，但不能绕过 Dock 擅自结束 Document，也不能取消其他 Document。
/// </para>
/// <para>
/// 令牌适用于 Document 拥有的 HTTP 请求、解析、临时计算和界面投影。收到取消后应
/// 协作退出并禁止迟到结果回写，不得在 Dispose 中同步等待；已经转交给插件级后台
/// Coordinator 的任务不再属于 Document，不应绑定此令牌。
/// </para>
/// </remarks>
public interface IDocumentLifetime
{
    /// <summary>
    /// 当所属 Document 被 Dock 确认永久关闭时取消的令牌。
    /// </summary>
    CancellationToken ClosingToken { get; }

    /// <summary>
    /// 获取宿主是否已经发出永久关闭信号；用于在 Dispatcher 回调或不可取消 API 返回后，
    /// 再次判断结果是否仍允许提交到界面状态。
    /// </summary>
    bool IsClosing { get; }
}
