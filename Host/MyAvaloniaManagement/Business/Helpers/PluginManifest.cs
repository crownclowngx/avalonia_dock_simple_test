using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 表示插件声明的一个左闭右开版本区间。
/// </summary>
/// <remarks>
/// 设计意图：宿主只理解“当前版本是否落在区间内”，不推测语义化版本的兼容关系。
/// 兼容承诺由插件作者显式给出，破坏性升级也因此能够在执行插件代码前被拒绝。
/// </remarks>
internal sealed record PluginVersionRange(Version MinInclusive, Version MaxExclusive)
{
    internal bool Contains(Version current) =>
        current >= MinInclusive && current < MaxExclusive;

    public override string ToString() =>
        $"[{PluginVersionText.Format(MinInclusive)}, {PluginVersionText.Format(MaxExclusive)})";
}

/// <summary>
/// 描述宿主在加载插件前需要掌握的不可变清单事实。
/// </summary>
internal sealed record PluginManifest(
    int SchemaVersion,
    PluginId PluginId,
    Version PluginVersion,
    string EntryAssembly,
    PluginVersionRange HostApi,
    PluginVersionRange CommonContract);

/// <summary>
/// 保存当前宿主 API 与公共契约的实际程序集版本。
/// </summary>
/// <remarks>
/// 设计意图：版本事实由真正参与运行的程序集提供，而不是复制到加载器常量中。
/// 项目文件显式固定 AssemblyVersion 后，清单检查和 CLR 共享程序集检查能够指向同一发布事实。
/// </remarks>
internal sealed record HostCompatibilityProfile(
    Version HostApiVersion,
    Version CommonContractVersion)
{
    internal static HostCompatibilityProfile Current { get; } = new(
        PluginVersionText.Normalize(
            typeof(HostCompatibilityProfile).Assembly.GetName().Version
            ?? throw new InvalidOperationException("宿主程序集没有版本。")),
        PluginVersionText.Normalize(
            typeof(IPluginModule).Assembly.GetName().Version
            ?? throw new InvalidOperationException("公共契约程序集没有版本。")));
}

/// <summary>
/// 严格读取插件根目录中的 <c>plugin.manifest.json</c>。
/// </summary>
/// <remarks>
/// 清单属于执行代码前的信任边界，因此这里拒绝未知字段、重复字段、注释和尾随逗号，
/// 并限制文件大小。严格失败比静默忽略拼写错误更安全，也能避免作者以为某项约束已经生效。
/// </remarks>
internal static partial class PluginManifestReader
{
    internal const string FileName = "plugin.manifest.json";
    internal const int CurrentSchemaVersion = 1;
    private const int MaximumManifestBytes = 64 * 1024;

    private static readonly string[] RootProperties =
        ["schemaVersion", "pluginId", "pluginVersion", "entryAssembly", "compatibility"];
    private static readonly string[] CompatibilityProperties =
        ["hostApi", "commonContract"];
    private static readonly string[] RangeProperties =
        ["minInclusive", "maxExclusive"];

    /// <summary>
    /// 尝试读取并验证一个插件清单；失败结果只返回稳定错误码和受控中文原因。
    /// </summary>
    internal static bool TryRead(
        string pluginDirectory,
        out PluginManifest? manifest,
        out string? errorCode,
        out string? errorDetail)
    {
        manifest = null;
        errorCode = null;
        errorDetail = null;
        var manifestPath = Path.Combine(pluginDirectory, FileName);

        try
        {
            if (!File.Exists(manifestPath))
            {
                errorCode = HostDiagnosticCodes.PluginManifestMissing;
                errorDetail = $"插件根目录缺少 {FileName}。";
                return false;
            }

            var fileInfo = new FileInfo(manifestPath);
            if (fileInfo.Length == 0 || fileInfo.Length > MaximumManifestBytes)
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                errorDetail = fileInfo.Length == 0
                    ? "插件清单不能为空。"
                    : $"插件清单超过 {MaximumManifestBytes} 字节限制。";
                return false;
            }

            using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });

            var root = document.RootElement;
            if (!ValidateObject(root, RootProperties, "根对象", out errorDetail))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                return false;
            }

            if (!TryReadInt32(root, "schemaVersion", out var schemaVersion, out errorDetail))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                return false;
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                errorCode = HostDiagnosticCodes.PluginManifestSchemaUnsupported;
                errorDetail = $"不支持清单 schemaVersion={schemaVersion}，当前只支持 {CurrentSchemaVersion}。";
                return false;
            }

            if (!TryReadString(root, "pluginId", out var pluginIdText, out errorDetail) ||
                !PluginId.TryParse(pluginIdText, out var pluginId) ||
                !pluginId!.IsCanonical ||
                !pluginId.Value.StartsWith("myavalonia.plugin.", StringComparison.Ordinal))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                errorDetail ??= "pluginId 必须是以 myavalonia.plugin. 开头的规范稳定标识。";
                return false;
            }

            if (!TryReadVersion(root, "pluginVersion", out var pluginVersion, out errorDetail) ||
                !TryReadString(root, "entryAssembly", out var entryAssembly, out errorDetail) ||
                !ValidateEntryAssembly(entryAssembly!, out errorDetail))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                return false;
            }

            if (!root.TryGetProperty("compatibility", out var compatibility) ||
                !ValidateObject(
                    compatibility,
                    CompatibilityProperties,
                    "compatibility",
                    out errorDetail) ||
                !TryReadRange(compatibility, "hostApi", out var hostApi, out errorDetail) ||
                !TryReadRange(
                    compatibility,
                    "commonContract",
                    out var commonContract,
                    out errorDetail))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                return false;
            }

            manifest = new PluginManifest(
                schemaVersion,
                pluginId,
                pluginVersion!,
                entryAssembly!,
                hostApi!,
                commonContract!);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            errorCode = HostDiagnosticCodes.PluginManifestInvalid;
            errorDetail = $"无法安全读取插件清单（{exception.GetType().Name}）。";
            return false;
        }
    }

    private static bool TryReadRange(
        JsonElement compatibility,
        string propertyName,
        out PluginVersionRange? range,
        out string? errorDetail)
    {
        range = null;
        if (!compatibility.TryGetProperty(propertyName, out var element))
        {
            errorDetail = $"compatibility 缺少必填字段 {propertyName}。";
            return false;
        }

        if (!ValidateObject(element, RangeProperties, propertyName, out errorDetail) ||
            !TryReadVersion(element, "minInclusive", out var minimum, out errorDetail) ||
            !TryReadVersion(element, "maxExclusive", out var maximum, out errorDetail))
        {
            return false;
        }

        if (minimum! >= maximum!)
        {
            errorDetail = $"{propertyName} 必须满足 minInclusive < maxExclusive。";
            return false;
        }

        range = new PluginVersionRange(minimum!, maximum!);
        errorDetail = null;
        return true;
    }

    private static bool ValidateObject(
        JsonElement element,
        IReadOnlyCollection<string> expectedProperties,
        string objectName,
        out string? errorDetail)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errorDetail = $"{objectName} 必须是 JSON 对象。";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                errorDetail = $"{objectName} 包含重复字段 {property.Name}。";
                return false;
            }

            if (!expectedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                errorDetail = $"{objectName} 包含未知字段 {property.Name}。";
                return false;
            }
        }

        var missing = expectedProperties.FirstOrDefault(property => !seen.Contains(property));
        if (missing is not null)
        {
            errorDetail = $"{objectName} 缺少必填字段 {missing}。";
            return false;
        }

        errorDetail = null;
        return true;
    }

    private static bool TryReadInt32(
        JsonElement parent,
        string propertyName,
        out int value,
        out string? errorDetail)
    {
        if (parent.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value))
        {
            errorDetail = null;
            return true;
        }

        value = default;
        errorDetail = $"{propertyName} 必须是 32 位整数。";
        return false;
    }

    private static bool TryReadString(
        JsonElement parent,
        string propertyName,
        out string? value,
        out string? errorDetail)
    {
        if (parent.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            element.GetString() is { Length: > 0 } text &&
            string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            value = text;
            errorDetail = null;
            return true;
        }

        value = null;
        errorDetail = $"{propertyName} 必须是非空且没有首尾空白的字符串。";
        return false;
    }

    private static bool TryReadVersion(
        JsonElement parent,
        string propertyName,
        out Version? version,
        out string? errorDetail)
    {
        version = null;
        if (!TryReadString(parent, propertyName, out var text, out errorDetail))
        {
            return false;
        }

        if (!NumericVersionPattern().IsMatch(text!) ||
            !Version.TryParse(text, out var parsed))
        {
            errorDetail = $"{propertyName} 必须是 major.minor.patch[.revision] 数字版本。";
            return false;
        }

        version = PluginVersionText.Normalize(parsed);
        return true;
    }

    private static bool ValidateEntryAssembly(string entryAssembly, out string? errorDetail)
    {
        if (Path.IsPathRooted(entryAssembly) ||
            entryAssembly.IndexOfAny(['/', '\\']) >= 0 ||
            entryAssembly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(entryAssembly), entryAssembly, StringComparison.Ordinal) ||
            !entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            entryAssembly.Length <= ".dll".Length)
        {
            errorDetail = "entryAssembly 只能是插件根目录中的单个 DLL 文件名。";
            return false;
        }

        errorDetail = null;
        return true;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericVersionPattern();
}

/// <summary>
/// 对插件清单和当前运行时版本执行无副作用兼容评估。
/// </summary>
internal static class PluginCompatibilityEvaluator
{
    /// <summary>
    /// 判断清单插件版本是否与入口程序集版本完全一致。
    /// </summary>
    /// <remarks>
    /// 版本区间用于宿主兼容；插件自身版本则必须精确匹配，避免清单展示、诊断和实际代码来自不同发布物。
    /// </remarks>
    internal static bool HasMatchingPluginVersion(
        PluginManifest manifest,
        Version? assemblyVersion) =>
        assemblyVersion is not null &&
        PluginVersionText.Normalize(assemblyVersion) == manifest.PluginVersion;

    internal static bool TryEvaluate(
        PluginManifest manifest,
        HostCompatibilityProfile host,
        out string? errorCode,
        out string? errorDetail)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(host);

        if (!manifest.HostApi.Contains(host.HostApiVersion))
        {
            errorCode = HostDiagnosticCodes.PluginHostApiIncompatible;
            errorDetail =
                $"插件要求 Host API {manifest.HostApi}，当前为 {PluginVersionText.Format(host.HostApiVersion)}。";
            return false;
        }

        if (!manifest.CommonContract.Contains(host.CommonContractVersion))
        {
            errorCode = HostDiagnosticCodes.PluginCommonContractIncompatible;
            errorDetail =
                $"插件要求公共契约 {manifest.CommonContract}，当前为 {PluginVersionText.Format(host.CommonContractVersion)}。";
            return false;
        }

        errorCode = null;
        errorDetail = null;
        return true;
    }
}

/// <summary>
/// 集中规范化和格式化清单版本，避免三段与四段版本在不同阶段产生不同结论。
/// </summary>
internal static class PluginVersionText
{
    internal static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    internal static string Format(Version version) =>
        Normalize(version).ToString(4);
}
