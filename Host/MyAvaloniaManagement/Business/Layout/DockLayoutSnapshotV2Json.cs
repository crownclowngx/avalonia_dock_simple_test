using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// Dock Layout V2 的唯一严格 JSON 编解码器。它只理解线格式，不访问路径、不执行隔离，
/// 也不解释运行时 Dock 结构；文件所有权仍由 <see cref="DockLayoutStore"/> 承担。
/// </summary>
internal static class DockLayoutSnapshotV2Json
{
    private const int MaximumJsonDepth = 8;
    private static readonly string[] RootProperties =
        ["schemaVersion", "panes", "tools", "activeToolId"];
    private static readonly string[] PaneProperties = ["id", "proportion"];
    private static readonly string[] ToolProperties =
        ["id", "dockId", "order", "isVisible", "isPinned"];

    /// <summary>
    /// 严格读取唯一 V2 字段集合。逐层检查属性名称可同时拒绝未知字段、大小写漂移和重复字段，
    /// 避免普通反序列化器“最后一个字段获胜”的宽松行为掩盖损坏快照。
    /// </summary>
    internal static DockLayoutSnapshotV2 Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumJsonDepth,
        });
        var root = document.RootElement;
        ValidatePropertySet(root, RootProperties, "LAYOUT_ROOT_FIELDS_INVALID");
        var schemaVersion = ReadInt32(root, "schemaVersion");
        if (schemaVersion != DockLayoutSnapshotV2.CurrentSchemaVersion)
        {
            throw new DockLayoutFormatException("LAYOUT_SCHEMA_UNSUPPORTED");
        }

        var panesElement = root.GetProperty("panes");
        if (panesElement.ValueKind != JsonValueKind.Array)
        {
            throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID");
        }

        var panes = new List<DockPaneSnapshotV2>();
        foreach (var element in panesElement.EnumerateArray())
        {
            ValidatePropertySet(element, PaneProperties, "LAYOUT_PANE_FIELDS_INVALID");
            panes.Add(new DockPaneSnapshotV2
            {
                Id = ReadString(element, "id"),
                Proportion = ReadDouble(element, "proportion"),
            });
        }

        var toolsElement = root.GetProperty("tools");
        if (toolsElement.ValueKind != JsonValueKind.Array)
        {
            throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID");
        }

        var tools = new List<DockToolSnapshotV2>();
        foreach (var element in toolsElement.EnumerateArray())
        {
            ValidatePropertySet(element, ToolProperties, "LAYOUT_TOOL_FIELDS_INVALID");
            tools.Add(new DockToolSnapshotV2
            {
                Id = ReadString(element, "id"),
                DockId = ReadString(element, "dockId"),
                Order = ReadInt32(element, "order"),
                IsVisible = ReadBoolean(element, "isVisible"),
                IsPinned = ReadBoolean(element, "isPinned"),
            });
        }

        var activeElement = root.GetProperty("activeToolId");
        var activeToolId = activeElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => activeElement.GetString(),
            _ => throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID"),
        };

        return new DockLayoutSnapshotV2
        {
            SchemaVersion = schemaVersion,
            Panes = panes,
            Tools = tools,
            ActiveToolId = activeToolId,
        };
    }

    /// <summary>按固定顺序写出唯一 V2 线格式；读取端不把字段顺序作为兼容承诺。</summary>
    internal static void Write(Stream stream, DockLayoutSnapshotV2 snapshot)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(snapshot);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            SkipValidation = false,
            MaxDepth = MaximumJsonDepth,
        });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", snapshot.SchemaVersion);
        writer.WriteStartArray("panes");
        foreach (var pane in snapshot.Panes)
        {
            writer.WriteStartObject();
            writer.WriteString("id", pane.Id);
            writer.WriteNumber("proportion", pane.Proportion);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("tools");
        foreach (var tool in snapshot.Tools)
        {
            writer.WriteStartObject();
            writer.WriteString("id", tool.Id);
            writer.WriteString("dockId", tool.DockId);
            writer.WriteNumber("order", tool.Order);
            writer.WriteBoolean("isVisible", tool.IsVisible);
            writer.WriteBoolean("isPinned", tool.IsPinned);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        if (snapshot.ActiveToolId is { } activeToolId)
        {
            writer.WriteString("activeToolId", activeToolId);
        }
        else
        {
            writer.WriteNull("activeToolId");
        }
        writer.WriteEndObject();
    }

    private static void ValidatePropertySet(
        JsonElement element,
        IReadOnlyList<string> requiredProperties,
        string errorCode)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID");
        }

        var required = new HashSet<string>(requiredProperties, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !required.Contains(property.Name))
            {
                throw new DockLayoutFormatException(errorCode);
            }
        }

        if (seen.Count != required.Count)
        {
            throw new DockLayoutFormatException(errorCode);
        }
    }

    private static int ReadInt32(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID");
    }

    private static double ReadDouble(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID");
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID"),
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.String && property.GetString() is { } value
            ? value
            : throw new DockLayoutFormatException("LAYOUT_FIELD_TYPE_INVALID");
    }
}

/// <summary>表示布局 JSON 没有满足唯一 V2 线格式；正文不携带原始文件内容。</summary>
internal sealed class DockLayoutFormatException(
    string code,
    string? stableId = null) : Exception(code)
{
    internal string Code { get; } = code;
    internal string? StableId { get; } = stableId;
}
