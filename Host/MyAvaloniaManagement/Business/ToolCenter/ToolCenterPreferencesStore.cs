using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.ToolCenter;

internal sealed record ToolCategory(string Id, string DisplayName);

/// <summary>只保存工具中心偏好，绝不复制 Dock 状态或插件业务数据。</summary>
internal sealed record ToolCenterSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string[] FavoriteToolIds { get; init; } = [];
    public string[] RecentToolIds { get; init; } = [];
    public ToolCategory[] Categories { get; init; } = [];
    public Dictionary<string, string> ToolCategoryAssignments { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> LastKnownToolNames { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>独立文件、原子写入；未知新版本只读，避免旧程序覆盖新配置。</summary>
internal sealed class ToolCenterPreferencesStore
{
    internal const string FileName = "tool-center-v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private bool _readOnly;
    internal string SettingsPath { get; }
    internal string Error { get; private set; } = string.Empty;
    public ToolCenterPreferencesStore() : this(Path.Combine(HostDataRootPolicy.ResolveDefault(), FileName)) { }
    internal ToolCenterPreferencesStore(string path) => SettingsPath = Path.GetFullPath(path);

    internal ToolCenterSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            ToolCenterSettings? settings;
            using (var stream = File.OpenRead(SettingsPath)) settings = JsonSerializer.Deserialize<ToolCenterSettings>(stream, JsonOptions);
            if (settings is null) throw new JsonException("Empty preferences");
            if (settings.SchemaVersion != 1)
            {
                _readOnly = true;
                Error = "工具偏好版本不受支持；原文件已保留，本次修改只在内存中生效。";
                return new();
            }
            return Normalize(settings);
        }
        catch (JsonException)
        {
            try { File.Move(SettingsPath, SettingsPath + $".{Guid.NewGuid():N}.invalid.bak"); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { _readOnly = true; }
            Error = "工具偏好损坏，已使用默认设置；原文件保留。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _readOnly = true;
            Error = "无法读取工具偏好，本次修改只在内存中生效。";
        }
        return new();
    }

    internal bool Save(ToolCenterSettings settings)
    {
        if (_readOnly) return false;
        try
        {
            AtomicFileTransaction.Write(SettingsPath, stream => JsonSerializer.Serialize(stream, Normalize(settings), JsonOptions));
            Error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = "工具偏好未能保存，当前修改仍有效，但重启后可能丢失。";
            Console.Error.WriteLine("ToolCenter errorCode=TOOL_PREFERENCES_WRITE_FAILED");
            return false;
        }
    }

    internal static ToolCenterSettings Normalize(ToolCenterSettings value)
    {
        static string[] Ids(string[]? source) => (source ?? []).Where(ValidToolId).Distinct(StringComparer.Ordinal).ToArray();
        var names = new HashSet<string>(ToolCenterPreferences.BuiltInCategories.Select(item => item.DisplayName), StringComparer.OrdinalIgnoreCase);
        var categoryIds = new HashSet<string>(StringComparer.Ordinal);
        var categories = (value.Categories ?? []).Where(item => item is not null &&
                item.Id is not null && item.Id.StartsWith("user:", StringComparison.Ordinal) && item.Id.Length <= 100 &&
                !string.IsNullOrWhiteSpace(item.DisplayName) && item.DisplayName.Trim().Length <= 60 &&
                categoryIds.Add(item.Id) && names.Add(item.DisplayName.Trim()))
            .Select(item => item with { DisplayName = item.DisplayName.Trim() }).ToArray();
        var validCategories = categories.Concat(ToolCenterPreferences.BuiltInCategories).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return new ToolCenterSettings
        {
            FavoriteToolIds = Ids(value.FavoriteToolIds),
            RecentToolIds = Ids(value.RecentToolIds).Take(20).ToArray(),
            Categories = categories,
            ToolCategoryAssignments = (value.ToolCategoryAssignments ?? []).Where(pair => ValidToolId(pair.Key))
                .ToDictionary(pair => pair.Key, pair => validCategories.Contains(pair.Value ?? "") ? pair.Value! : ToolCenterPreferences.OtherCategoryId, StringComparer.Ordinal),
            LastKnownToolNames = (value.LastKnownToolNames ?? []).Where(pair => ValidToolId(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
    }

    internal static bool ValidToolId(string? id) => MyAvaloniaManagement.PluginSdk.ToolTypeId.TryParse(id, out _);
}
