using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// V3 严格编解码。构造参数必需和未知字段拒绝由 System.Text.Json 执行；重复字段单独递归检查，
/// 防止默认的最后字段覆盖语义。文件大小在分配完整文档前受限，所有错误只携带稳定错误码。
/// </summary>
internal static class DockLayoutV3Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 100,
        WriteIndented = true,
    };

    internal static DockLayoutSnapshotV3 Read(Stream stream)
    {
        try { return ReadCore(stream); }
        catch (JsonException) { throw new DockLayoutFormatException("LAYOUT_JSON_INVALID"); }
    }

    private static DockLayoutSnapshotV3 ReadCore(Stream stream)
    {
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int count;
        while ((count = stream.Read(block, 0, block.Length)) != 0)
        {
            if (buffer.Length + count > DockLayoutV3Validator.MaximumFileBytes)
                throw new DockLayoutFormatException("LAYOUT_SIZE_EXCEEDED");
            buffer.Write(block, 0, count);
        }
        using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 100 });
        CheckDuplicates(json.RootElement);
        if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("schemaVersion", out var version) &&
            version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var schema) && schema > 3)
            throw new DockLayoutFormatException("LAYOUT_SCHEMA_FUTURE");
        try
        {
            var result = json.RootElement.Deserialize<DockLayoutSnapshotV3>(Options) ??
                throw new DockLayoutFormatException("LAYOUT_V3_STRUCTURE_INVALID");
            DockLayoutV3Validator.Validate(result);
            return result;
        }
        catch (JsonException) { throw new DockLayoutFormatException("LAYOUT_JSON_INVALID"); }
    }

    internal static void Write(Stream stream, DockLayoutSnapshotV3 snapshot)
    {
        DockLayoutV3Validator.Validate(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
        if (bytes.Length > DockLayoutV3Validator.MaximumFileBytes) throw new DockLayoutFormatException("LAYOUT_SIZE_EXCEEDED");
        stream.Write(bytes);
    }

    private static void CheckDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new DockLayoutFormatException("LAYOUT_FIELDS_DUPLICATE");
                CheckDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckDuplicates(item);
    }
}
