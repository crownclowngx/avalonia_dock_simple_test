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

/// <summary>表示 Host 启动一个插件 Document 时传入的互斥激活输入。</summary>
/// <remarks>
/// 基类型只保存两种激活方式共同拥有的标题。创建意图只存在于
/// <see cref="NewDocumentActivation"/>，恢复内容只存在于
/// <see cref="RestoreDocumentActivation"/>，调用方因此不能再构造“既新建又恢复”或
/// “没有明确分支”的非法组合。
///
/// 构造函数使用 <c>private protected</c>，使类型层次只能由当前 SDK 程序集扩展。Host 和插件可以
/// 穷尽处理两个密封子类型，但不能在外部程序集私自增加第三种激活语义。路径、PluginId、
/// DocumentTypeId、Dock 对象和服务集合仍不进入本协议。
/// </remarks>
public abstract record DocumentActivation
{
    /// <summary>初始化两种激活方式共享的标题。</summary>
    /// <param name="title">Host 已验证的非 null 初始标题；允许空字符串。</param>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> 为 null。</exception>
    private protected DocumentActivation(string title) =>
        Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <summary>由 SDK 内建具体类型实现的封闭层次标记。</summary>
    /// <remarks>
    /// C# 会为 record 自动生成受保护的复制构造函数；仅限制普通构造函数仍可能让外部程序集
    /// 借助该复制构造函数派生第三种 record。本抽象成员只在 SDK 程序集内可见，外部程序集既
    /// 无法实现它，也就无法声明可实例化的第三种激活类型。该标记不承载运行时分支或业务状态，
    /// Host 与插件仍直接按照两个公开密封类型进行朴素分支。
    /// </remarks>
    internal abstract bool IsSdkDefinedActivation { get; }

    /// <summary>获取 Host 已验证的初始标题；空字符串表示由插件决定默认标题。</summary>
    public string Title { get; }
}

/// <summary>表示创建一个全新插件 Document。</summary>
/// <remarks>
/// Creation Intent 只是新建入口的可选细分，由 Host 先按 Descriptor 验证，再由拥有该语义的插件解释。
/// 本类型不含恢复内容，因此插件无需再通过可空字段推断当前是否为新建流程。
/// </remarks>
public sealed record NewDocumentActivation : DocumentActivation
{
    /// <summary>创建一个新建激活输入。</summary>
    /// <param name="title">Host 已验证的非 null 初始标题；允许空字符串。</param>
    /// <param name="creationIntentId">可选创建入口；null 表示该 Document 的默认新建方式。</param>
    public NewDocumentActivation(
        string title,
        CreationIntentId? creationIntentId = null)
        : base(title) =>
        CreationIntentId = creationIntentId;

    /// <summary>获取可选创建入口；该属性只存在于新建分支。</summary>
    public CreationIntentId? CreationIntentId { get; }

    /// <inheritdoc />
    internal override bool IsSdkDefinedActivation => true;
}

/// <summary>表示使用 Host 已严格读取的业务内容恢复一个插件 Document。</summary>
/// <remarks>
/// 恢复内容在构造边界即为必需值，不能再与 Creation Intent 同时出现。Host 仍拥有文件路径、信封身份、
/// 标题提交和恢复副本策略；插件只负责解释自己的 <see cref="DocumentContent"/>。
/// </remarks>
public sealed record RestoreDocumentActivation : DocumentActivation
{
    /// <summary>创建一个恢复激活输入。</summary>
    /// <param name="title">Host 从严格信封取得的非 null 标题；允许空字符串。</param>
    /// <param name="restoredContent">Host 已验证并冻结的非 null 插件业务内容。</param>
    /// <exception cref="ArgumentNullException"><paramref name="restoredContent"/> 为 null。</exception>
    public RestoreDocumentActivation(
        string title,
        DocumentContent restoredContent)
        : base(title) =>
        RestoredContent = restoredContent ?? throw new ArgumentNullException(nameof(restoredContent));

    /// <summary>获取必须由插件解释的恢复内容；该属性只存在于恢复分支。</summary>
    public DocumentContent RestoredContent { get; }

    /// <inheritdoc />
    internal override bool IsSdkDefinedActivation => true;
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
    /// <param name="activation">非 null 且类型互斥的激活输入，其路径、所有权和信封身份已由 Host 剥离。</param>
    /// <param name="cancellationToken">初始化失败、超时或关闭时由 Host 触发的协作取消令牌。</param>
    /// <returns>表示模型完全可发布的初始化操作。</returns>
    /// <remarks>实现不得捕获 UI View 或 Dock 对象；失败时 Host 不发布标签，并释放暂存 Scope。</remarks>
    ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken);
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
