using System.Text.Json;

namespace MyAvaloniaManagement.PluginSdk;

/// <summary>表示插件在某一时刻拥有的不可变 Document 业务内容。</summary>
/// <remarks>
/// Host 只保存和转交内容，不解释插件 schema。构造函数克隆 JSON，避免调用方释放原始
/// <see cref="JsonDocument"/> 或改变其所有权后留下悬空的 <see cref="JsonElement"/>。
/// </remarks>
public sealed class DocumentContent
{
    /// <summary>创建由插件独立版本化的 Document 内容。</summary>
    /// <param name="schemaVersion">由插件解释的正整数内容 schema。</param>
    /// <param name="payload">任意合法 JSON 值；不能是未初始化的 Undefined。</param>
    /// <exception cref="ArgumentOutOfRangeException">schema 不是正整数。</exception>
    /// <exception cref="ArgumentException">payload 为 Undefined。</exception>
    public DocumentContent(int schemaVersion, JsonElement payload)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Document 内容 schema 必须是正整数。");
        }

        if (payload.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Document payload 必须是已经解析的合法 JSON 值。", nameof(payload));
        }

        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
    }

    /// <summary>获取由插件独立解释的内容 schema。</summary>
    public int SchemaVersion { get; }

    /// <summary>获取构造时克隆的不可变 JSON 内容。</summary>
    public JsonElement Payload { get; }
}

/// <summary>表示 Host 启动或恢复一个插件 Document 时传入的已验证上下文。</summary>
/// <remarks>
/// 本对象刻意不携带路径、PluginId、DocumentTypeId、Dock 对象或服务集合，防止插件获得不属于
/// 自身的宿主状态所有权。空标题允许插件采用自己的默认标题，但标题本身不能为 null。
/// </remarks>
public sealed class DocumentActivationContext
{
    /// <summary>创建 Document 激活上下文。</summary>
    /// <param name="title">Host 已验证的非 null 初始标题；允许空字符串。</param>
    /// <param name="creationIntentId">新建时选择的可选入口；恢复内容时通常为 null。</param>
    /// <param name="restoredContent">Host 从信封读取并验证后的可选内容快照。</param>
    /// <exception cref="ArgumentNullException">标题为 null。</exception>
    public DocumentActivationContext(
        string title,
        CreationIntentId? creationIntentId = null,
        DocumentContent? restoredContent = null)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        CreationIntentId = creationIntentId;
        RestoredContent = restoredContent;
    }

    /// <summary>获取 Host 已验证的初始标题；空字符串表示由插件决定默认标题。</summary>
    public string Title { get; }

    /// <summary>获取可选的创建入口；恢复已有内容时通常为空。</summary>
    public CreationIntentId? CreationIntentId { get; }

    /// <summary>获取可选的恢复内容；新建 Document 时为空。</summary>
    public DocumentContent? RestoredContent { get; }
}

/// <summary>表示插件希望 Host 投影到 Document 标签的当前展示状态。</summary>
public sealed record DocumentPresentationState
{
    /// <summary>创建展示状态。</summary>
    /// <param name="title">要投影到标签的非 null 标题；是否允许空标题由 Host 展示政策决定。</param>
    public DocumentPresentationState(string title) =>
        Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <summary>获取当前标题。</summary>
    public string Title { get; }
}

/// <summary>定义不认识 Dock 类型的普通插件 Document 模型。</summary>
public interface IPluginDocument
{
    /// <summary>获取当前可展示状态。</summary>
    /// <remarks>返回对象应是当前一致快照；Host 不取得插件模型的修改权。</remarks>
    DocumentPresentationState Presentation { get; }

    /// <summary>当 <see cref="Presentation"/> 发生变化时通知 Host 重新投影。</summary>
    /// <remarks>事件可以从插件工作线程发出；Host 实现负责在读取和更新 UI 时切换到 UI 线程。</remarks>
    event EventHandler? PresentationChanged;

    /// <summary>使用 Host 已验证的输入异步初始化当前 Document。</summary>
    /// <param name="context">非 null 的激活输入，其路径、所有权和信封身份已由 Host 剥离。</param>
    /// <param name="cancellationToken">初始化失败、超时或关闭时由 Host 触发的协作取消令牌。</param>
    /// <returns>表示模型完全可发布的初始化操作。</returns>
    /// <remarks>实现不得捕获 UI View 或 Dock 对象；失败时 Host 不发布标签，并释放暂存 Scope。</remarks>
    ValueTask InitializeAsync(DocumentActivationContext context, CancellationToken cancellationToken);
}

/// <summary>定义可以由 Host 捕获和恢复业务内容的插件 Document。</summary>
public interface IPersistablePluginDocument : IPluginDocument
{
    /// <summary>获取当前业务内容是否包含尚未成功提交的修改。</summary>
    bool IsDirty { get; }

    /// <summary>捕获当前不可变业务内容；路径和信封元数据仍由 Host 独占。</summary>
    /// <param name="cancellationToken">保存被取消或 Document 关闭时由 Host 触发的协作取消令牌。</param>
    /// <returns>由插件拥有 schema、由 Host 克隆并写入信封的内容快照。</returns>
    /// <remarks>方法不得自行写文件；只有 Host 完成原子保存后才会调用 <see cref="AcceptChanges"/>。</remarks>
    ValueTask<DocumentContent> CaptureContentAsync(CancellationToken cancellationToken);

    /// <summary>在 Host 完成原子保存后提交当前状态为已保存。</summary>
    /// <remarks>调用只提交脏状态，不取得路径或事务所有权；重复调用应保持幂等。</remarks>
    void AcceptChanges();
}

/// <summary>提供当前 Document 由 Host 管理的只读关闭信号。</summary>
/// <remarks>插件只能观察并协作取消自己的工作，不能主动结束当前或其他 Document。</remarks>
public interface IDocumentLifetime
{
    /// <summary>当 Host 确认 Document 永久关闭时取消的令牌。</summary>
    /// <remarks>插件只能观察令牌；令牌源和取消时机始终由 Host 所有。</remarks>
    CancellationToken ClosingToken { get; }

    /// <summary>获取 Host 是否已发出永久关闭信号。</summary>
    /// <remarks>用于抑制迟到副作用，不替代对 <see cref="ClosingToken"/> 的协作取消观察。</remarks>
    bool IsClosing { get; }
}
