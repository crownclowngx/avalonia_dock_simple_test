using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Plugins.Enablement;

/// <summary>负责一个开关文件的严格读取及原子保存，不拥有插件、UI 或运行状态。</summary>
/// <remarks>
/// 设计思路：缺失与读取失败必须区分；坏配置不能意外启用插件。保存持有短时跨实例锁，
/// 在锁内比较读取时的摘要，再替换文件；提交点以前失败只返回旧快照，不覆盖外部修改。
/// </remarks>
internal sealed class PluginEnablementSettingsStore(string path, Action? beforeCommit = null)
{
    internal const string FileName = "plugin-enablement-v1.json";
    private const int MaximumBytes = 1024 * 1024;
    private const int MaximumIds = 10000;
    internal string SettingsPath { get; } = Path.GetFullPath(path);

    /// <summary>只读取。原件损坏时可采用有效备份，但保持只读；未来 schema 不能退回旧备份。</summary>
    internal PluginEnablementReadResult Load()
    {
        try
        {
            var bytes = ReadOptional(SettingsPath);
            if (bytes is not null)
            {
                try { return Parse(bytes); }
                catch (UnsupportedSchemaException) { return new(null, Hash(bytes), "PLUGIN_ENABLEMENT_SCHEMA_UNSUPPORTED"); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException) { return Recover(); }
            }
            return Recover(missingMain: true);
        }
        catch (InvalidDataException) { return Recover(); }
        catch (Exception ex) when (IsFileFailure(ex)) { return new(null, "unreadable", "PLUGIN_ENABLEMENT_READ_FAILED"); }
    }

    /// <summary>
    /// 在文件锁内核对旧版本并落盘，成功替换后才返回新快照。beforeCommit 仅为实际提交前的
    /// 受控故障测试点；生产不提供回调，不引入可替换的通用文件系统或事务框架。
    /// </summary>
    internal PluginEnablementSaveResult TrySave(PluginEnablementReadResult expected, PluginEnablementSettings next)
    {
        if (!expected.CanWrite) return new(false, expected, "PLUGIN_ENABLEMENT_READ_ONLY");
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            using var lease = new FileStream(SettingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var actual = Load();
            if (!actual.CanWrite || actual.Revision != expected.Revision)
                return new(false, expected, "PLUGIN_ENABLEMENT_CONFLICT");
            if (next.DisabledPluginIds.Count > MaximumIds) return new(false, expected, "PLUGIN_ENABLEMENT_WRITE_FAILED");
            if (actual.Settings!.DisabledPluginIds.SetEquals(next.DisabledPluginIds)) return new(true, actual);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                disabledPluginIds = next.DisabledPluginIds.Select(id => id.Value).Order(StringComparer.Ordinal).ToArray(),
            }, new JsonSerializerOptions { WriteIndented = true });
            if (bytes.Length > MaximumBytes) return new(false, expected, "PLUGIN_ENABLEMENT_WRITE_FAILED");
            var committed = new PluginEnablementReadResult(next, Hash(bytes));
            temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            beforeCommit?.Invoke();
            if (actual.Revision == "missing") File.Move(temporary, SettingsPath);
            else File.Replace(temporary, SettingsPath, SettingsPath + ".bak");
            // 唯一提交点已越过；后续不能再执行可能把已保存结果报告为失败的文件操作。
            temporary = null;
            return new(true, committed);
        }
        catch (Exception ex) when (IsFileFailure(ex)) { return new(false, expected, "PLUGIN_ENABLEMENT_WRITE_FAILED"); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (IsFileFailure(ex))
                { System.Diagnostics.Trace.TraceWarning("PLUGIN_ENABLEMENT_TEMP_CLEANUP_FAILED"); }
            }
        }
    }

    private PluginEnablementReadResult Recover(bool missingMain = false)
    {
        try
        {
            var backup = ReadOptional(SettingsPath + ".bak");
            if (backup is null && missingMain) return PluginEnablementReadResult.Default;
            if (backup is not null) return Parse(backup) with { ErrorCode = "PLUGIN_ENABLEMENT_RECOVERED" };
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or UnsupportedSchemaException || IsFileFailure(ex)) { }
        return new(null, "invalid", "PLUGIN_ENABLEMENT_INVALID");
    }

    /// <summary>直接打开文件才能区分“不存在”和“无权限”；File.Exists 的 false 不足以决定全启用。</summary>
    private static byte[]? ReadOptional(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes) throw new InvalidDataException();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (buffer.Length > MaximumBytes) throw new InvalidDataException();
            return buffer.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static PluginEnablementReadResult Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var fields = root.EnumerateObject().ToArray();
        if (fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw new InvalidDataException();
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema))
            throw new InvalidDataException();
        if (schema != 1) throw new UnsupportedSchemaException();
        if (fields.Length != 2 || !root.TryGetProperty("disabledPluginIds", out var ids) ||
            ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() > MaximumIds)
            throw new InvalidDataException();
        var parsed = ids.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String || !PluginId.TryParse(item.GetString(), out var id))
                throw new InvalidDataException();
            return id;
        }).ToArray();
        return new(new(parsed), Hash(bytes));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsFileFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private sealed class UnsupportedSchemaException : Exception;
}
