using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 宿主与插件共享的 Document 保存信封。
/// </summary>
public sealed class DocumentSaveData
{
    /// <summary>获取或设置声明的 Document 类型身份。</summary>
    public required DocumentTypeId DocumentTypeId { get; set; }
    /// <summary>获取或设置保存时的展示标题。</summary>
    public required string Title { get; set; }
    /// <summary>获取或设置旧信封使用的本地保存时间。</summary>
    /// <remarks>G7 将以宿主信封中的 UTC <see cref="DateTimeOffset"/> 替换该字段。</remarks>
    public DateTime SaveTime { get; set; }
    /// <summary>获取或设置由插件解释的业务正文。</summary>
    public required string Content { get; set; }
    /// <summary>获取或设置旧契约中的自由格式插件元数据。</summary>
    /// <remarks>该字段不是可靠版本边界；G7 会以整数内容 schema 替换。</remarks>
    public required string PluginMetadata { get; set; }
}
