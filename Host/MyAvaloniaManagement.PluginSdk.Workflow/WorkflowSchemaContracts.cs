using System.Collections.ObjectModel;
using System.Text.Json;

namespace MyAvaloniaManagement.PluginSdk.Workflow;

/// <summary>集中定义 Workflow Action 冻结 JSON Schema Profile 的资源预算。</summary>
/// <remarks>
/// 这些常量同时约束 Host 的安全边界和 Studio 的运行前验证。集中在共享程序集内可以避免
/// 两端各自复制数字后逐渐漂移；本 Profile 有意不声称兼容完整 JSON Schema 标准。
/// </remarks>
public static class WorkflowSchemaProfile
{
    /// <summary>单个输入或输出 Schema 的最大 UTF-8 字节数。</summary>
    public const int MaximumSchemaBytes = 64 * 1024;
    /// <summary>单次 Action 输入 JSON 的最大 UTF-8 字节数。</summary>
    public const int MaximumInputBytes = 256 * 1024;
    /// <summary>单次 Action 输出 JSON 的最大 UTF-8 字节数。</summary>
    public const int MaximumOutputBytes = 1024 * 1024;
    /// <summary>Schema 与实例允许的最大递归深度。</summary>
    public const int MaximumDepth = 16;
    /// <summary>一个对象及其后代累计允许声明的最大属性数。</summary>
    public const int MaximumProperties = 128;
    /// <summary>数组实例允许包含的最大元素数。</summary>
    public const int MaximumArrayItems = 1024;
    /// <summary>字符串实例允许占用的最大 UTF-8 字节数。</summary>
    public const int MaximumStringBytes = 64 * 1024;
}

/// <summary>描述共享 Workflow 协议校验发现的一个确定性问题。</summary>
public sealed record WorkflowSchemaIssue(string Code, string Path, string Message);

/// <summary>保存一次 Schema、实例或兼容性校验的只读结果。</summary>
public sealed class WorkflowSchemaValidationResult
{
    /// <summary>从问题快照创建不可变校验结果。</summary>
    /// <param name="issues">按发现顺序排列的问题；构造函数会复制集合。</param>
    public WorkflowSchemaValidationResult(IReadOnlyList<WorkflowSchemaIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = new ReadOnlyCollection<WorkflowSchemaIssue>(issues.ToArray());
    }

    /// <summary>获得只读的问题快照。</summary>
    public IReadOnlyList<WorkflowSchemaIssue> Issues { get; }
    /// <summary>获得是否未发现任何问题。</summary>
    public bool IsValid => Issues.Count == 0;
}

/// <summary>标识引用路径无法按静态与运行时共同语义继续解析的原因。</summary>
public enum WorkflowReferencePathFailure
{
    /// <summary>路径解析成功。</summary>
    None,
    /// <summary>对象中不存在指定属性。</summary>
    MissingProperty,
    /// <summary>属性存在但未由 required 保证。</summary>
    OptionalProperty,
    /// <summary>数组段不是非负十进制整数。</summary>
    InvalidArrayIndex,
    /// <summary>数组索引未由 minItems 保证存在。</summary>
    ArrayIndexNotGuaranteed,
    /// <summary>运行时数组没有指定索引。</summary>
    ArrayIndexOutOfRange,
    /// <summary>当前值或 Schema 类型不能继续解析路径。</summary>
    NonContainer,
}

/// <summary>返回路径解析结果，并保留稳定失败原因和失败段位置。</summary>
public sealed class WorkflowReferencePathResult
{
    internal WorkflowReferencePathResult(
        JsonElement? value,
        WorkflowReferencePathFailure failure,
        int segmentIndex)
    {
        Value = value?.Clone();
        Failure = failure;
        SegmentIndex = segmentIndex;
    }

    /// <summary>获得路径是否完整解析成功。</summary>
    public bool Succeeded => Failure == WorkflowReferencePathFailure.None;
    /// <summary>获得成功时的 Schema 或运行时 JSON 值快照。</summary>
    public JsonElement? Value { get; }
    /// <summary>获得稳定的失败原因；成功时为 <see cref="WorkflowReferencePathFailure.None"/>。</summary>
    public WorkflowReferencePathFailure Failure { get; }
    /// <summary>获得首个失败段的零基索引；成功时为 -1。</summary>
    public int SegmentIndex { get; }
}

/// <summary>保存同一 Action 目录的执行契约修订与展示修订。</summary>
public sealed record WorkflowCatalogRevisions(
    string ContractRevision,
    string PresentationRevision);
