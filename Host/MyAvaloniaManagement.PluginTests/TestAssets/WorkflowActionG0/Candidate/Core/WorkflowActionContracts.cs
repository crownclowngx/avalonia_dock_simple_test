using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MyAvaloniaManagement.PluginSdk;

/// <summary>表示由插件声明、由 Host 治理和调用的稳定工作流动作身份。</summary>
/// <remarks>
/// 值对象只处理与现有 Plugin、Document、Tool 相同的词法规则。动作是否属于声明插件的
/// <c>myavalonia.plugin.&lt;name&gt;.workflow.</c> 命名空间，由 Host 在汇总目录时统一判断，
/// 避免 SDK 值对象反向依赖运行期所有权。
/// </remarks>
public sealed record WorkflowActionId
{
    /// <summary>使用规范的小写点分/kebab-case 字符串创建动作身份。</summary>
    public WorkflowActionId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value), allowDots: true);

    /// <summary>获取可稳定写入目录、诊断和工作流定义的动作身份。</summary>
    public string Value { get; }

    /// <summary>解析动作身份，非法输入通过异常明确拒绝。</summary>
    public static WorkflowActionId Parse(string value) => new(value);

    /// <summary>尝试解析动作身份，不把预期输入错误转换为异常。</summary>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out WorkflowActionId? actionId)
    {
        actionId = StableIdentifierRules.TryValidate(value, true, out var validated)
            ? new WorkflowActionId(validated)
            : null;
        return actionId is not null;
    }

    /// <summary>返回动作身份的规范字符串。</summary>
    public override string ToString() => Value;
}

/// <summary>描述动作可能涉及的风险；多个风险可以组合。</summary>
[Flags]
public enum WorkflowActionRiskFlags
{
    /// <summary>动作不产生副作用、不读取敏感信息且不属于长任务。</summary>
    None = 0,

    /// <summary>动作会访问网络。</summary>
    UsesNetwork = 1,

    /// <summary>动作会读取本地文件。</summary>
    ReadsLocalFiles = 2,

    /// <summary>动作会写入本地文件。</summary>
    WritesLocalFiles = 4,

    /// <summary>动作会删除本地文件或执行同等不可恢复操作。</summary>
    DeletesLocalFiles = 8,

    /// <summary>动作会接收或使用会话 Secret。</summary>
    HandlesSecret = 16,

    /// <summary>动作预期长时间占用资源并持续报告进度。</summary>
    LongRunning = 32,
}

/// <summary>定义动作声明的最低用户确认频率。</summary>
public enum WorkflowActionConfirmationPolicy
{
    /// <summary>无需确认；只允许与 <see cref="WorkflowActionRiskFlags.None"/> 配合。</summary>
    Never = 0,

    /// <summary>同一次工作流运行中，对确定的动作、目标和参数摘要确认一次。</summary>
    OncePerRun = 1,

    /// <summary>每次调用都确认；删除等不可恢复操作必须使用此策略。</summary>
    EveryInvocation = 2,
}

/// <summary>描述一个可列举但尚未执行的工作流动作。</summary>
/// <remarks>
/// 构造函数克隆两个 Schema，避免调用方随后释放原始 <see cref="JsonDocument"/> 或改变
/// 可观察生命周期。Schema Profile、所有权、预算与确认组合仍由 Host 在注册阶段统一验证。
/// </remarks>
public sealed class WorkflowActionDescriptor
{
    /// <summary>创建不可变动作描述符。</summary>
    public WorkflowActionDescriptor(
        WorkflowActionId id,
        string displayName,
        string description,
        JsonElement inputSchema,
        JsonElement outputSchema,
        WorkflowActionRiskFlags risks,
        WorkflowActionConfirmationPolicy confirmationPolicy,
        IReadOnlyList<string>? sensitiveInputPointers = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        DisplayName = displayName;
        Description = description;
        InputSchema = inputSchema.Clone();
        OutputSchema = outputSchema.Clone();
        Risks = risks;
        ConfirmationPolicy = confirmationPolicy;
        SensitiveInputPointers = new ReadOnlyCollection<string>(
            (sensitiveInputPointers ?? []).ToArray());
    }

    /// <summary>获取稳定动作身份。</summary>
    public WorkflowActionId Id { get; }

    /// <summary>获取供用户识别动作的本地化名称。</summary>
    public string DisplayName { get; }

    /// <summary>获取供编辑器展示的动作说明。</summary>
    public string Description { get; }

    /// <summary>获取 Host 冻结后的输入 Schema 快照。</summary>
    public JsonElement InputSchema { get; }

    /// <summary>获取 Host 冻结后的输出 Schema 快照。</summary>
    public JsonElement OutputSchema { get; }

    /// <summary>获取动作声明的完整风险组合。</summary>
    public WorkflowActionRiskFlags Risks { get; }

    /// <summary>获取动作声明的最低确认频率。</summary>
    public WorkflowActionConfirmationPolicy ConfirmationPolicy { get; }

    /// <summary>获取需要在日志、摘要和诊断中遮蔽的规范输入 JSON Pointer。</summary>
    public IReadOnlyList<string> SensitiveInputPointers { get; }
}

/// <summary>表示 Handler 向 Host 报告的一条受限进度。</summary>
public sealed class WorkflowActionProgress
{
    /// <summary>创建进度快照；百分比为空表示无法确定总量。</summary>
    public WorkflowActionProgress(string stage, int? percent, string? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent));
        }

        Stage = stage;
        Percent = percent;
        Message = message;
    }

    /// <summary>获取稳定、非本地化的当前阶段。</summary>
    public string Stage { get; }

    /// <summary>获取 0–100 的完成百分比；无法确定时为空。</summary>
    public int? Percent { get; }

    /// <summary>获取可选的用户可读短消息；Host 会应用长度与脱敏限制。</summary>
    public string? Message { get; }
}

/// <summary>提供一次调用中由 Host 生成且不能由调用者伪造的上下文。</summary>
public sealed class WorkflowActionContext
{
    /// <summary>创建调用上下文。</summary>
    public WorkflowActionContext(
        Guid invocationId,
        PluginId callerId,
        IProgress<WorkflowActionProgress> progress)
    {
        if (invocationId == Guid.Empty)
        {
            throw new ArgumentException("调用身份不能为空。", nameof(invocationId));
        }

        InvocationId = invocationId;
        CallerId = callerId ?? throw new ArgumentNullException(nameof(callerId));
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    /// <summary>获取 Host 为本次调用生成的唯一身份。</summary>
    public Guid InvocationId { get; }

    /// <summary>获取 Host 在注入 Gateway 时绑定的调用插件身份。</summary>
    public PluginId CallerId { get; }

    /// <summary>获取受 Host 限流和脱敏的进度端口。</summary>
    public IProgress<WorkflowActionProgress> Progress { get; }
}

/// <summary>定义所有 Provider 插件共同实现的非泛型 JSON 调用边界。</summary>
public interface IWorkflowActionHandler
{
    /// <summary>在动作所有者的独立调用 Scope 中执行一次动作。</summary>
    ValueTask<JsonElement> InvokeAsync(
        JsonElement arguments,
        WorkflowActionContext context,
        CancellationToken cancellationToken);
}

/// <summary>表示调用者提交给已绑定 Gateway 的最小请求。</summary>
/// <remarks>请求有意不包含 CallerId、OwnerId、授权结果或 Provider 类型，防止调用者伪造治理事实。</remarks>
public sealed class WorkflowActionInvocationRequest
{
    /// <summary>创建请求并克隆参数 JSON。</summary>
    public WorkflowActionInvocationRequest(WorkflowActionId actionId, JsonElement arguments)
    {
        ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
        Arguments = arguments.Clone();
    }

    /// <summary>获取需要调用的稳定动作身份。</summary>
    public WorkflowActionId ActionId { get; }

    /// <summary>获取调用参数的独立 JSON 快照。</summary>
    public JsonElement Arguments { get; }
}

/// <summary>定义不会泄漏插件异常正文的结构化失败。</summary>
public sealed class WorkflowActionFailure
{
    /// <summary>创建由 Host 白名单映射的失败。</summary>
    public WorkflowActionFailure(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
    }

    /// <summary>获取稳定、非本地化的失败码。</summary>
    public string Code { get; }

    /// <summary>获取已脱敏的用户可读失败消息。</summary>
    public string Message { get; }
}

/// <summary>表示 Gateway 已收口的一次调用终态。</summary>
public enum WorkflowActionInvocationStatus
{
    /// <summary>动作成功并产生符合输出 Schema 的结果。</summary>
    Succeeded = 0,

    /// <summary>动作失败或返回了非法结果。</summary>
    Failed = 1,

    /// <summary>调用被协作取消。</summary>
    Cancelled = 2,

    /// <summary>调用超过 Host 预算并已请求协作取消。</summary>
    TimedOut = 3,

    /// <summary>调用在 Schema、授权或治理阶段被拒绝。</summary>
    Rejected = 4,

    /// <summary>动作所有者当前不可用。</summary>
    Unavailable = 5,
}

/// <summary>表示一次动作调用的结构化结果。</summary>
public sealed class WorkflowActionInvocationResult
{
    /// <summary>创建终态并取得输出 JSON 的独立快照。</summary>
    public WorkflowActionInvocationResult(
        Guid invocationId,
        WorkflowActionInvocationStatus status,
        JsonElement? output,
        WorkflowActionFailure? failure)
    {
        if (invocationId == Guid.Empty)
        {
            throw new ArgumentException("调用身份不能为空。", nameof(invocationId));
        }

        InvocationId = invocationId;
        Status = status;
        Output = output?.Clone();
        Failure = failure;
    }

    /// <summary>获取与 Handler Context 对应的调用身份。</summary>
    public Guid InvocationId { get; }

    /// <summary>获取调用终态。</summary>
    public WorkflowActionInvocationStatus Status { get; }

    /// <summary>获取成功时的输出快照；其他终态可以为空。</summary>
    public JsonElement? Output { get; }

    /// <summary>获取失败、拒绝或不可用时的结构化原因。</summary>
    public WorkflowActionFailure? Failure { get; }
}

/// <summary>表示由 Host 签发并绑定可信 CallerId 的一次工作流运行边界。</summary>
/// <remarks>
/// Run 独占授权缓存、取消信号和并发预算。调用者不能提交或复用 Host internal RunId；
/// 每次真实工作流运行应创建并释放一个新实例。
/// </remarks>
public interface IWorkflowActionRun : IAsyncDisposable
{
    /// <summary>在当前运行边界内提交一次受 Host 治理的动作调用。</summary>
    Task<WorkflowActionInvocationResult> InvokeAsync(
        WorkflowActionInvocationRequest request,
        IProgress<WorkflowActionProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>定义 Host 注入给显式 Consumer 的 caller-bound 动作端口。</summary>
public interface IWorkflowActionGateway
{
    /// <summary>列举当前调用者有权看到且所有者可用的动作快照。</summary>
    IReadOnlyList<WorkflowActionDescriptor> GetAvailableActions();

    /// <summary>创建绑定当前 Consumer 身份和目录 revision 的新运行边界。</summary>
    IWorkflowActionRun CreateRun();
}
