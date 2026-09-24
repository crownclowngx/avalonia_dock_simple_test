using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>安装文件的严格 JSON 边界；拒绝重复键后再反序列化，避免不同读取阶段解释同一文件不同。</summary>
internal static class PluginInstallJson
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 20,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    internal static JsonDocument Read(string path, int maximumBytes)
    {
        PluginInstallPaths.AssertNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is 0 || stream.Length > maximumBytes) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装清单大小超限或为空。");
        var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 20 });
        try { Unique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    internal static T Read<T>(string path, int limit)
    {
        using var document = Read(path, limit);
        return document.RootElement.Deserialize<T>(Options) ?? throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装记录为空。");
    }

    internal static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装清单对象无效。");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) found.Add(property.Name);
        if (!found.SetEquals(names)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装清单字段不符合协议。");
    }

    internal static bool Hash(string? text) => text is { Length: 64 } &&
        System.Linq.Enumerable.All(text, Uri.IsHexDigit);

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!found.Add(property.Name)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装清单含重复字段。");
                Unique(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) Unique(child);
    }
}
