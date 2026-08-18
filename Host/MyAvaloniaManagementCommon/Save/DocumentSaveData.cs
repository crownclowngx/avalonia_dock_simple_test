namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 插件拥有的 Document 内容快照。
/// </summary>
/// <remarks>
/// 本类型有意只包含插件能够决定的内容 schema 和正文。PluginId、DocumentTypeId、标题与
/// 保存时间属于宿主信封，插件既不需要重复声明，也不能覆盖宿主已经验证的身份事实。
/// 将两类所有权分开后，插件发布版本变化不会被误当成内容格式变化，宿主信封升级也不会
/// 迫使插件理解磁盘事务字段。
/// </remarks>
public sealed class DocumentSaveData
{
    /// <summary>创建一个不可变的插件内容快照。</summary>
    /// <param name="contentSchemaVersion">由插件解释的正整数内容 schema。</param>
    /// <param name="payload">由插件拥有的正文；宿主只把它作为字符串保存和转交。</param>
    /// <exception cref="ArgumentOutOfRangeException">内容 schema 不是正整数。</exception>
    /// <exception cref="ArgumentNullException">正文为 <see langword="null"/>。</exception>
    public DocumentSaveData(int contentSchemaVersion, string payload)
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

    /// <summary>获取由插件解释的内容 schema；它与插件发布版本相互独立。</summary>
    public int ContentSchemaVersion { get; }

    /// <summary>获取插件拥有的正文；宿主不会猜测、迁移或解释其中的业务字段。</summary>
    public string Payload { get; }
}
