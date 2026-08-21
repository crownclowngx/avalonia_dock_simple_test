namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 表示插件在某一时刻拥有的不可变 Document 业务内容快照。
/// </summary>
/// <remarks>
/// <para>
/// 本类型只承载插件能够解释的内容 schema 与正文。PluginId、DocumentTypeId、标题、
/// 保存时间和文件路径均属于宿主，不允许插件通过本 DTO 复制或覆盖这些事实。
/// </para>
/// <para>
/// G8 在 Managed Plugin v1 封板前对旧候选保存契约进行过一次有意的破坏式重定基线：
/// 旧 <c>DocumentSaveData</c> 未形成已发布兼容承诺，因此被直接删除而不是保留别名或适配器。
/// 这样最终 v1 只有一个含义明确的内容快照类型，不会长期背负两套名称和所有权模型。
/// </para>
/// </remarks>
public sealed class DocumentContentSnapshot
{
    /// <summary>创建一个不可变的插件内容快照。</summary>
    /// <param name="contentSchemaVersion">
    /// 由插件独立解释的正整数内容 schema；它不得从插件程序集版本推导。
    /// </param>
    /// <param name="payload">由插件拥有的正文；宿主只负责原样保存和转交。</param>
    /// <exception cref="ArgumentOutOfRangeException">内容 schema 不是正整数。</exception>
    /// <exception cref="ArgumentNullException">正文为 <see langword="null"/>。</exception>
    public DocumentContentSnapshot(int contentSchemaVersion, string payload)
    {
        if (contentSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentSchemaVersion),
                contentSchemaVersion,
                "Document 内容 schema 必须是正整数。");
        }

        ArgumentNullException.ThrowIfNull(payload);
        ContentSchemaVersion = contentSchemaVersion;
        Payload = payload;
    }

    /// <summary>获取插件独立解释的内容 schema。</summary>
    public int ContentSchemaVersion { get; }

    /// <summary>获取插件拥有的正文；宿主不会猜测、迁移或解释其中的业务字段。</summary>
    public string Payload { get; }
}
