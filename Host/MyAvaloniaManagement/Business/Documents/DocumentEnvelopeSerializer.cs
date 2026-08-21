using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>表示已经通过 Host 严格校验的 Document V2 信封。</summary>
/// <remarks>
/// 信封身份、标题和保存时间属于 Host；插件只能拥有 <see cref="Content"/> 内部的 schema 与
/// JSON payload。本类型保持 internal，防止磁盘协议反向扩张 Plugin SDK public API。
/// </remarks>
internal sealed record DocumentEnvelopeV2(
    PluginId PluginId,
    DocumentTypeId DocumentTypeId,
    string Title,
    DateTimeOffset SavedAtUtc,
    DocumentContent Content);

/// <summary>表示 Document 信封不满足唯一 V2 线格式。</summary>
/// <remarks>
/// 该异常只在 Host 持久化边界内传播。异常正文不得携带 payload、路径或插件异常正文，用户提示由
/// <see cref="DocumentPersistenceErrorMapper"/> 按异常类型映射为固定文本。
/// </remarks>
internal sealed class DocumentEnvelopeException : Exception
{
    internal DocumentEnvelopeException(string message) : base(message) { }
    internal DocumentEnvelopeException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>独占 Document V2 线格式、严格字段集合和资源上限。</summary>
/// <remarks>
/// Serializer 不解释插件 payload，也不负责文件事务。读取器只接受一个 V2 结构，不探测 V1、
/// 不迁移历史字段；这样格式判断、业务初始化和磁盘提交分别保持单一职责。
/// </remarks>
internal sealed class DocumentEnvelopeSerializer
{
    internal const int CurrentSchemaVersion = 2;
    internal const int MaximumEnvelopeBytes = 8 * 1024 * 1024;
    internal const int MaximumJsonDepth = 8;

    private static readonly string[] RootProperties =
    [
        "schemaVersion", "pluginId", "documentTypeId", "title", "savedAtUtc", "content",
    ];

    private static readonly string[] ContentProperties = ["schemaVersion", "payload"];

    /// <summary>使用 Host 已验证的身份事实和插件内容生成唯一 V2 信封。</summary>
    internal string Serialize(
        PluginId pluginId,
        DocumentTypeId documentTypeId,
        string title,
        DateTimeOffset savedAtUtc,
        DocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(documentTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        if (savedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Document 保存时间必须使用 UTC。", nameof(savedAtUtc));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = true,
                   SkipValidation = false,
                   MaxDepth = MaximumJsonDepth,
               }))
        {
            // 字段顺序仅用于形成确定、可读的输出；reader 仍按名称验证，避免顺序成为兼容承诺。
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("pluginId", pluginId.Value);
            writer.WriteString("documentTypeId", documentTypeId.Value);
            writer.WriteString("title", title);
            writer.WriteString("savedAtUtc", savedAtUtc);
            writer.WriteStartObject("content");
            writer.WriteNumber("schemaVersion", content.SchemaVersion);
            writer.WritePropertyName("payload");
            content.Payload.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        EnsureByteLength(buffer.WrittenCount, isReading: false);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>严格读取唯一受支持的 V2 信封，并克隆插件 JSON 内容。</summary>
    internal DocumentEnvelopeV2 Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        EnsureByteLength(Encoding.UTF8.GetByteCount(json), isReading: true);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumJsonDepth,
            });
            var root = document.RootElement;
            ValidatePropertySet(root, RootProperties, "根对象");

            if (ReadInt32(root, "schemaVersion") != CurrentSchemaVersion)
            {
                throw Invalid("文档信封 schemaVersion 不受支持。只接受 V2，不读取或迁移 V1。");
            }

            var pluginIdText = ReadString(root, "pluginId");
            if (!PluginId.TryParse(pluginIdText, out var pluginId) || pluginId is null)
            {
                throw Invalid("文档信封中的插件身份格式无效。");
            }

            var documentTypeIdText = ReadString(root, "documentTypeId");
            if (!DocumentTypeId.TryParse(documentTypeIdText, out var documentTypeId) ||
                documentTypeId is null)
            {
                throw Invalid("文档信封中的 Document 类型身份格式无效。");
            }

            var title = ReadString(root, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                throw Invalid("文档信封标题不能为空。");
            }

            var savedAtElement = root.GetProperty("savedAtUtc");
            if (savedAtElement.ValueKind != JsonValueKind.String ||
                !savedAtElement.TryGetDateTimeOffset(out var savedAtUtc) ||
                savedAtUtc.Offset != TimeSpan.Zero)
            {
                throw Invalid("文档保存时间必须是有效的 UTC DateTimeOffset。");
            }

            var contentElement = root.GetProperty("content");
            ValidatePropertySet(contentElement, ContentProperties, "content 对象");
            var contentSchemaVersion = ReadInt32(contentElement, "schemaVersion");
            if (contentSchemaVersion <= 0)
            {
                throw Invalid("文档内容 schema 必须是正整数。");
            }

            // DocumentContent 在构造时再次 Clone，使结果不依赖本方法内 JsonDocument 的生命周期。
            var content = new DocumentContent(
                contentSchemaVersion,
                contentElement.GetProperty("payload"));
            return new DocumentEnvelopeV2(
                pluginId,
                documentTypeId,
                title,
                savedAtUtc,
                content);
        }
        catch (DocumentEnvelopeException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new DocumentEnvelopeException(
                "文档信封结构损坏或超过允许的 JSON 深度。",
                exception);
        }
    }

    /// <summary>在分配整份文本前拒绝文件系统已经报告为非法大小的文件。</summary>
    internal void ValidateFileLength(long byteLength) =>
        EnsureByteLength(byteLength, isReading: true);

    private static void ValidatePropertySet(
        JsonElement element,
        IReadOnlyList<string> requiredProperties,
        string objectName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"文档信封的 {objectName} 必须是 JSON 对象。");
        }

        var required = new HashSet<string>(requiredProperties, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Invalid($"文档信封的 {objectName} 包含重复字段。");
            }

            if (!required.Contains(property.Name))
            {
                throw Invalid($"文档信封的 {objectName} 包含未知字段或字段大小写错误。");
            }
        }

        if (seen.Count != required.Count || required.Any(name => !seen.Contains(name)))
        {
            throw Invalid($"文档信封的 {objectName} 缺少必填字段。");
        }
    }

    private static int ReadInt32(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw Invalid("文档信封整数栏位类型无效。");
        }

        return value;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.String || property.GetString() is not { } value)
        {
            throw Invalid("文档信封字符串栏位类型无效。");
        }

        return value;
    }

    private static void EnsureByteLength(long byteLength, bool isReading)
    {
        if (byteLength > 0 && byteLength <= MaximumEnvelopeBytes)
        {
            return;
        }

        if (isReading)
        {
            throw new DocumentEnvelopeException(
                byteLength <= 0
                    ? "文档信封不能为空。"
                    : $"文档信封超过 {MaximumEnvelopeBytes} 字节限制。");
        }

        throw new JsonException($"Document 信封超过 {MaximumEnvelopeBytes} 字节限制。");
    }

    private static DocumentEnvelopeException Invalid(string message) => new(message);
}
