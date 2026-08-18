using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Save;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 已完成严格校验的 Document 信封 v1。
/// </summary>
/// <remarks>
/// 该模型留在 Host 内部，因为 PluginId、DocumentTypeId、标题和时间均由宿主拥有。
/// 插件只能接收其中的 <see cref="Content"/>，不会因此依赖宿主磁盘协议。
/// </remarks>
internal sealed record DocumentEnvelopeV1(
    PluginId PluginId,
    DocumentTypeId DocumentTypeId,
    string Title,
    DateTimeOffset SavedAtUtc,
    DocumentSaveData Content);

/// <summary>
/// 独占 Document 信封 v1 的磁盘格式、资源限制和结构校验。
/// </summary>
/// <remarks>
/// 本类型只处理宿主信封，不解释 payload。严格字段集合可以立即暴露拼写错误和协议漂移，
/// 而不是让默认反序列化器静默忽略未知字段。当前不存在旧信封，因此这里没有格式探测、
/// 兼容读取或迁移分支。
/// </remarks>
internal sealed class DocumentEnvelopeSerializer
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumEnvelopeBytes = 8 * 1024 * 1024;
    internal const int MaximumJsonDepth = 8;

    private static readonly string[] RequiredProperties =
    [
        "schemaVersion",
        "pluginId",
        "documentTypeId",
        "contentSchemaVersion",
        "title",
        "savedAtUtc",
        "payload",
    ];

    private static readonly HashSet<string> RequiredPropertySet =
        new(RequiredProperties, StringComparer.Ordinal);

    /// <summary>
    /// 使用宿主已经验证的所有权事实和插件内容快照生成唯一 v1 格式。
    /// </summary>
    internal string Serialize(
        PluginId pluginId,
        DocumentTypeId documentTypeId,
        string title,
        DateTimeOffset savedAtUtc,
        DocumentSaveData content)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(documentTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        if (!pluginId.IsCanonical)
        {
            // 强类型 ID 仍可能表示 Registry 中用于运行期兼容的历史别名。落盘边界再次
            // 要求主 ID，避免内部调用者绕过 Registry 后写出 reader 明确拒绝的文件。
            throw new ArgumentException("Document 信封只能写入规范插件身份。", nameof(pluginId));
        }

        if (!documentTypeId.IsCanonical)
        {
            throw new ArgumentException(
                "Document 信封只能写入规范 Document 类型身份。",
                nameof(documentTypeId));
        }

        if (savedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Document 保存时间必须使用 UTC。",
                nameof(savedAtUtc));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = true,
                   SkipValidation = false,
               }))
        {
            // 字段顺序属于可读性约定。读取器仍按名称校验，不让顺序成为兼容负担。
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("pluginId", pluginId.Value);
            writer.WriteString("documentTypeId", documentTypeId.Value);
            writer.WriteNumber("contentSchemaVersion", content.ContentSchemaVersion);
            writer.WriteString("title", title);
            writer.WriteString("savedAtUtc", savedAtUtc);
            writer.WriteString("payload", content.Payload);
            writer.WriteEndObject();
        }

        EnsureByteLength(buffer.WrittenCount, isReading: false);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// 严格读取唯一受支持的 v1 信封，并把宿主字段与插件内容重新分离。
    /// </summary>
    internal DocumentEnvelopeV1 Deserialize(string json)
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
            ValidatePropertySet(root);

            var schemaVersion = ReadInt32(root, "schemaVersion");
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw Invalid("文档信封 schemaVersion 不受支持。");
            }

            var pluginIdText = ReadString(root, "pluginId");
            if (!PluginId.TryParse(pluginIdText, out var pluginId) || !pluginId!.IsCanonical)
            {
                throw Invalid("文档信封中的插件身份格式无效。");
            }

            var documentTypeIdText = ReadString(root, "documentTypeId");
            if (!DocumentTypeId.TryParse(documentTypeIdText, out var documentTypeId) ||
                !documentTypeId!.IsCanonical)
            {
                throw Invalid("文档信封中的 Document 类型身份格式无效。");
            }

            var contentSchemaVersion = ReadInt32(root, "contentSchemaVersion");
            if (contentSchemaVersion <= 0)
            {
                throw Invalid("文档内容 schema 必须是正整数。");
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

            var payload = ReadString(root, "payload");
            return new DocumentEnvelopeV1(
                pluginId,
                documentTypeId,
                title,
                savedAtUtc,
                new DocumentSaveData(contentSchemaVersion, payload));
        }
        catch (DocumentLoadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new DocumentLoadException(
                "文档信封结构损坏或超过允许的 JSON 深度。",
                exception);
        }
    }

    /// <summary>
    /// 在读取整份文本前检查文件系统报告的长度，避免明显超限文件先造成大额内存分配。
    /// </summary>
    internal void ValidateFileLength(long byteLength) =>
        EnsureByteLength(byteLength, isReading: true);

    private static void ValidatePropertySet(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("文档信封根节点必须是 JSON 对象。");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Invalid("文档信封包含重复字段。");
            }

            if (!RequiredPropertySet.Contains(property.Name))
            {
                throw Invalid("文档信封包含未知字段或字段大小写错误。");
            }
        }

        if (seen.Count != RequiredProperties.Length ||
            RequiredProperties.Any(property => !seen.Contains(property)))
        {
            throw Invalid("文档信封缺少必填字段。");
        }
    }

    private static int ReadInt32(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Invalid("文档信封整数栏位类型无效。");
        }

        return value;
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value)
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
            throw new DocumentLoadException(
                byteLength <= 0
                    ? "文档信封不能为空。"
                    : $"文档信封超过 {MaximumEnvelopeBytes} 字节限制。");
        }

        throw new JsonException(
            $"Document 信封超过 {MaximumEnvelopeBytes} 字节限制。");
    }

    private static DocumentLoadException Invalid(string message) => new(message);
}
