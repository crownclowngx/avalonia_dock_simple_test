using System;
using System.IO;
using System.Text.Json;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Navigation;

/// <summary>稳定模式值只表达行为，不使用“旧版/新版”等允许更改的显示文字作为保存身份。</summary>
internal enum PluginMenuMode { Legacy, Tree }

/// <summary>一次导航偏好快照，名称可自定义，未配置时保持现有菜单习惯。</summary>
internal sealed record PluginNavigationSettings(
    PluginMenuMode Mode = PluginMenuMode.Legacy,
    string LegacyName = "旧版（平铺）",
    string TreeName = "新版（树形）");

/// <summary>只负责导航偏好的读取和原子保存，不创建 UI，也不操作工作区布局。</summary>
/// <remarks>
/// 独立文件避免为一个 Tool 扩展布局格式。沿用 Host 设置的临时写入和损坏文件保留策略，
/// 失败只影响跨启动记忆；当前界面仍使用内存状态，不因磁盘权限问题中断业务。
/// </remarks>
internal sealed class PluginNavigationSettingsStore
{
    internal const string FileName = "plugin-navigation-v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly Action<string> _log;
    internal string SettingsPath { get; }

    public PluginNavigationSettingsStore() : this(Path.Combine(HostDataRootPolicy.ResolveDefault(), FileName)) { }

    internal PluginNavigationSettingsStore(string path, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SettingsPath = Path.GetFullPath(path);
        _log = log ?? (code => Console.Error.WriteLine($"PluginNavigation errorCode={code}"));
    }

    internal PluginNavigationSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            using var stream = File.OpenRead(SettingsPath);
            var saved = JsonSerializer.Deserialize<Snapshot>(stream, JsonOptions);
            if (saved is null || saved.SchemaVersion != 1)
            {
                stream.Dispose();
                Quarantine();
                return new();
            }
            var defaults = new PluginNavigationSettings();
            return new(saved.PluginMenuMode == "tree" ? PluginMenuMode.Tree : PluginMenuMode.Legacy,
                DisplayName(saved.ModeDisplayNames?.Legacy, defaults.LegacyName),
                DisplayName(saved.ModeDisplayNames?.Tree, defaults.TreeName));
        }
        catch (JsonException) { Quarantine(); return new(); }
        catch (IOException) { _log("NAVIGATION_READ_FAILED"); return new(); }
        catch (UnauthorizedAccessException) { _log("NAVIGATION_READ_DENIED"); return new(); }
    }

    internal bool Save(PluginNavigationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Mode)) throw new ArgumentOutOfRangeException(nameof(settings));
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            temporary = SettingsPath + $".{Guid.NewGuid():N}.tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, new Snapshot
                {
                    SchemaVersion = 1,
                    PluginMenuMode = settings.Mode == PluginMenuMode.Tree ? "tree" : "legacy",
                    ModeDisplayNames = new Names { Legacy = settings.LegacyName, Tree = settings.TreeName },
                }, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
            temporary = null;
            return true;
        }
        catch (IOException) { _log("NAVIGATION_WRITE_FAILED"); return false; }
        catch (UnauthorizedAccessException) { _log("NAVIGATION_WRITE_DENIED"); return false; }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { _log("NAVIGATION_TEMP_CLEANUP_FAILED"); }
                catch (UnauthorizedAccessException) { _log("NAVIGATION_TEMP_CLEANUP_DENIED"); }
            }
        }
    }

    private void Quarantine()
    {
        _log("NAVIGATION_SETTINGS_INVALID");
        try
        {
            File.Move(SettingsPath, SettingsPath + $".{Guid.NewGuid():N}.invalid.bak");
        }
        catch (IOException) { _log("NAVIGATION_BACKUP_FAILED"); }
        catch (UnauthorizedAccessException) { _log("NAVIGATION_BACKUP_DENIED"); }
    }

    private static string DisplayName(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    private sealed record Snapshot
    {
        public int SchemaVersion { get; init; }
        public string? PluginMenuMode { get; init; }
        public Names? ModeDisplayNames { get; init; }
    }

    private sealed record Names
    {
        public string? Legacy { get; init; }
        public string? Tree { get; init; }
    }
}
