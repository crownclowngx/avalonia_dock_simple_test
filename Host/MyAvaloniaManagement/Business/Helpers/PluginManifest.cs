using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;

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
/// 保存 manifest v2 明确声明的入口程序集与入口类型。
/// </summary>
/// <remarks>
/// 入口类型使用程序集内完整名称而不是程序集限定名。这样清单只拥有“从哪个插件程序集取哪个类型”
/// 这一项职责，不允许作者在类型文本中再次嵌入版本、公钥或另一个程序集身份。
/// </remarks>
internal sealed record PluginEntryPoint(string Assembly, string Type);

/// <summary>
/// 描述宿主在加载插件前需要掌握的不可变清单事实。
/// </summary>
internal sealed record PluginManifest(
    int SchemaVersion,
    PluginId PluginId,
    Version PluginVersion,
    PluginEntryPoint EntryPoint,
    PluginVersionRange Sdk);

/// <summary>
/// 保存当前 Host 实际装载的 Core/UI Plugin SDK 版本。
/// </summary>
/// <remarks>
/// 设计意图：版本事实由真正参与运行的程序集提供，而不是复制到加载器常量中。
/// 项目文件显式固定 AssemblyVersion 后，清单检查和 CLR 共享程序集检查能够指向同一发布事实。
/// </remarks>
internal sealed record PluginSdkCompatibilityProfile(Version SdkVersion)
{
    internal static PluginSdkCompatibilityProfile Current { get; } = CreateCurrent();

    private static PluginSdkCompatibilityProfile CreateCurrent()
    {
        var coreVersion = PluginVersionText.Normalize(
            typeof(global::MyAvaloniaManagement.PluginSdk.PluginId).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Core Plugin SDK 程序集没有版本。"));
        var uiVersion = PluginVersionText.Normalize(
            typeof(global::MyAvaloniaManagement.PluginSdk.UI.IPluginModule).Assembly.GetName().Version
            ?? throw new InvalidOperationException("UI Plugin SDK 程序集没有版本。"));

        // manifest v2 只有一个 SDK 区间，因此 Host 自身也必须只提供一个 SDK 版本事实。
        // 若 Core/UI 漂移，继续评估插件会让同一清单产生两种相反结论，必须在执行插件代码前终止。
        if (coreVersion != uiVersion)
        {
            throw new InvalidOperationException(
                $"Core/UI Plugin SDK 版本不一致：Core={PluginVersionText.Format(coreVersion)}，" +
                $"UI={PluginVersionText.Format(uiVersion)}。");
        }

        return new PluginSdkCompatibilityProfile(coreVersion);
    }
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
    internal const int CurrentSchemaVersion = 2;
    private const int MaximumManifestBytes = 64 * 1024;

    private static readonly string[] RootProperties =
        ["schemaVersion", "pluginId", "pluginVersion", "entryPoint", "sdk"];
    private static readonly string[] EntryPointProperties = ["assembly", "type"];
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
                pluginId is null ||
                !pluginId.Value.StartsWith("myavalonia.plugin.", StringComparison.Ordinal))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                errorDetail ??= "pluginId 必须是以 myavalonia.plugin. 开头的规范稳定标识。";
                return false;
            }

            if (!TryReadVersion(root, "pluginVersion", out var pluginVersion, out errorDetail) ||
                !root.TryGetProperty("entryPoint", out var entryPoint) ||
                !ValidateObject(entryPoint, EntryPointProperties, "entryPoint", out errorDetail) ||
                !TryReadString(entryPoint, "assembly", out var entryAssembly, out errorDetail) ||
                !ValidateEntryAssembly(entryAssembly!, out errorDetail) ||
                !TryReadString(entryPoint, "type", out var entryType, out errorDetail) ||
                !ValidateEntryType(entryType!, out errorDetail))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                return false;
            }

            if (!root.TryGetProperty("sdk", out var sdkElement) ||
                !ValidateObject(sdkElement, RangeProperties, "sdk", out errorDetail) ||
                !TryReadRange(sdkElement, "sdk", out var sdk, out errorDetail))
            {
                errorCode = HostDiagnosticCodes.PluginManifestInvalid;
                return false;
            }

            manifest = new PluginManifest(
                schemaVersion,
                pluginId,
                pluginVersion!,
                new PluginEntryPoint(entryAssembly!, entryType!),
                sdk!);
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
        JsonElement element,
        string rangeName,
        out PluginVersionRange? range,
        out string? errorDetail)
    {
        range = null;
        if (!TryReadVersion(element, "minInclusive", out var minimum, out errorDetail) ||
            !TryReadVersion(element, "maxExclusive", out var maximum, out errorDetail))
        {
            return false;
        }

        if (minimum! >= maximum!)
        {
            errorDetail = $"{rangeName} 必须满足 minInclusive < maxExclusive。";
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
            errorDetail = $"{propertyName} 必须是 major.minor.patch 三段数字版本。";
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
            errorDetail = "entryPoint.assembly 只能是插件根目录中的单个 DLL 文件名。";
            return false;
        }

        errorDetail = null;
        return true;
    }

    private static bool ValidateEntryType(string entryType, out string? errorDetail)
    {
        if (!EntryTypePattern().IsMatch(entryType))
        {
            errorDetail =
                "entryPoint.type 必须是区分大小写的命名空间限定类型名，不能包含泛型、嵌套类型或程序集限定信息。";
            return false;
        }

        errorDetail = null;
        return true;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericVersionPattern();

    [GeneratedRegex(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EntryTypePattern();
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
        PluginSdkCompatibilityProfile host,
        out string? errorCode,
        out string? errorDetail)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(host);

        if (!manifest.Sdk.Contains(host.SdkVersion))
        {
            errorCode = HostDiagnosticCodes.PluginSdkIncompatible;
            errorDetail =
                $"插件要求 Plugin SDK {manifest.Sdk}，当前为 {PluginVersionText.Format(host.SdkVersion)}。";
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
