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

/// <summary>表示插件某一份持久化内容所对应的不透明修订号。</summary>
/// <param name="Value">由插件拥有并在持久化内容发生实际变化后单调推进的值。</param>
/// <remarks>
/// 默认值零表示 Document 初始化完成后的首个干净修订。Host 不比较、不排序也不把该值写入
/// Document 信封；它只在主文件原子提交成功后，把捕获时的原值交还给同一个插件模型。
/// </remarks>
public readonly record struct DocumentRevision(long Value);

/// <summary>表示插件在同一个稳定观察区间捕获的修订号与不可变业务内容。</summary>
/// <remarks>
/// 本对象只解决“写入了哪一版内容”的确认问题，不携带路径、标题、插件身份或文件事务信息。
/// <see cref="DocumentContent"/> 已拥有并克隆 JSON；本类型只保存该不可变内容引用。
/// </remarks>
public sealed class DocumentSaveSnapshot
{
    /// <summary>创建一份由指定内容修订拥有的保存快照。</summary>
    /// <param name="revision">捕获内容时对应的插件修订号。</param>
    /// <param name="content">非 null 的不可变业务内容。</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> 为 null。</exception>
    public DocumentSaveSnapshot(DocumentRevision revision, DocumentContent content)
    {
        Revision = revision;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>获取捕获内容时的插件修订号。</summary>
    public DocumentRevision Revision { get; }

    /// <summary>获取要由 Host 写入信封的不可变业务内容。</summary>
    public DocumentContent Content { get; }
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

    /// <summary>当 <see cref="IsDirty"/> 的值实际发生变化时通知 Host。</summary>
    /// <remarks>
    /// 事件可以从插件工作线程发出；Host 负责切换到 UI 线程并把状态投影到 Dock。
    /// 重复设置相同值时不应发出通知。
    /// </remarks>
    event EventHandler? IsDirtyChanged;

    /// <summary>捕获同一个稳定观察区间内的内容与修订号；路径和信封元数据仍由 Host 独占。</summary>
    /// <param name="cancellationToken">保存被取消或 Document 关闭时由 Host 触发的协作取消令牌。</param>
    /// <returns>由插件拥有 Revision 和内容 schema、由 Host 原样提交的不可变快照。</returns>
    /// <remarks>
    /// 方法不得自行写文件或提前清除脏状态。若捕获期间持久化内容发生变化，实现必须重新捕获或
    /// 以其他方式保证返回的 Revision 与 Content 对应；只有 Host 完成主文件原子提交后才会调用
    /// <see cref="AcceptChanges"/>。
    /// </remarks>
    ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>确认 Host 已经原子写入指定修订；只有当前修订仍匹配时才可清除脏状态。</summary>
    /// <param name="savedRevision">此前由同一模型捕获、现已写入主文件的修订号。</param>
    /// <remarks>
    /// 调用只确认内容修订，不取得路径或事务所有权。旧修订表示捕获后又发生了修改，必须保持
    /// <see cref="IsDirty"/>；重复确认同一修订应保持幂等。
    /// </remarks>
    void AcceptChanges(DocumentRevision savedRevision);
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
