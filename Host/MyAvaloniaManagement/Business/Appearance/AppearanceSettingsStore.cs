using System;
using System.IO;
using System.Text.Json;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Appearance;

/// <summary>
/// 负责应用外观设置的容错读取和原子写入。
/// </summary>
internal sealed class AppearanceSettingsStore
{
    internal const string SettingsFileName = "appearance-v1.json";
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Action<string> _log;

    public AppearanceSettingsStore()
        : this(GetDefaultPath(), LogToStandardError)
    {
    }

    internal AppearanceSettingsStore(
        string settingsPath,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = Path.GetFullPath(settingsPath);
        _log = log ?? LogToStandardError;
    }

    internal string SettingsPath { get; }

    internal ApplicationThemeMode Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return ApplicationThemeMode.System;
        }

        try
        {
            AppearanceSettingsSnapshot? snapshot;
            using (var stream = new FileStream(
                       SettingsPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                snapshot = JsonSerializer.Deserialize<AppearanceSettingsSnapshot>(
                    stream,
                    SerializerOptions);
            }

            if (snapshot is null ||
                snapshot.SchemaVersion != CurrentSchemaVersion ||
                !Enum.TryParse<ApplicationThemeMode>(
                    snapshot.Theme,
                    ignoreCase: false,
                    out var mode) ||
                !Enum.IsDefined(mode))
            {
                Quarantine("APPEARANCE_SETTINGS_INVALID");
                return ApplicationThemeMode.System;
            }

            return mode;
        }
        catch (JsonException)
        {
            Quarantine("APPEARANCE_JSON_INVALID");
            return ApplicationThemeMode.System;
        }
        catch (IOException)
        {
            _log("APPEARANCE_READ_IO_FAILED");
            return ApplicationThemeMode.System;
        }
        catch (UnauthorizedAccessException)
        {
            _log("APPEARANCE_READ_ACCESS_DENIED");
            return ApplicationThemeMode.System;
        }
    }

    internal bool Save(ApplicationThemeMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)
                ?? throw new InvalidOperationException(
                    "外观设置文件没有父目录。");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $"{SettingsFileName}.{Guid.NewGuid():N}.tmp");

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4 * 1024,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    new AppearanceSettingsSnapshot
                    {
                        SchemaVersion = CurrentSchemaVersion,
                        Theme = mode.ToString()
                    },
                    SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(SettingsPath))
            {
                File.Replace(
                    temporaryPath,
                    SettingsPath,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }

            temporaryPath = null;
            return true;
        }
        catch (IOException)
        {
            _log("APPEARANCE_WRITE_IO_FAILED");
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            _log("APPEARANCE_WRITE_ACCESS_DENIED");
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private void Quarantine(string errorCode)
    {
        _log(errorCode);
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(SettingsPath)!;
        var stem = Path.GetFileNameWithoutExtension(SettingsPath);
        var timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMddTHHmmssfffffffZ");
        var backupPath = Path.Combine(
            directory,
            $"{stem}.{timestamp}.invalid.bak");

        try
        {
            File.Move(SettingsPath, backupPath);
        }
        catch (IOException)
        {
            _log("APPEARANCE_QUARANTINE_IO_FAILED");
        }
        catch (UnauthorizedAccessException)
        {
            _log("APPEARANCE_QUARANTINE_ACCESS_DENIED");
        }
    }

    private void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
            _log("APPEARANCE_TEMP_CLEANUP_IO_FAILED");
        }
        catch (UnauthorizedAccessException)
        {
            _log("APPEARANCE_TEMP_CLEANUP_ACCESS_DENIED");
        }
    }

    private static string GetDefaultPath() =>
        Path.Combine(HostDataRootPolicy.ResolveDefault(), SettingsFileName);

    private static void LogToStandardError(string errorCode) =>
        Console.Error.WriteLine($"AppearanceSettings errorCode={errorCode}");

    private sealed record AppearanceSettingsSnapshot
    {
        public int SchemaVersion { get; init; }

        public string Theme { get; init; } = string.Empty;
    }
}
