using System.Diagnostics.CodeAnalysis;

namespace MyAvaloniaManagementCommon.Identity;

/// <summary>
/// 为插件扩展点的稳定标识提供统一词法规则。
/// </summary>
/// <remarks>
/// 设计意图：值对象只负责保证“这是一个可安全比较和持久化的标识”，而主标识的
/// 命名空间归属（例如必须位于某个 PluginId 下面）由宿主组合阶段校验。这样既能读取
/// 历史 GUID 风格别名，又不会把插件所有权规则散落到各个值对象中。
/// </remarks>
internal static class StableIdentifierRules
{
    internal const int MaximumLength = 128;

    internal static string Validate(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0 || value.Length > MaximumLength ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                $"稳定标识必须为 1-{MaximumLength} 个 ASCII 字母、数字、点、短横线或下划线。",
                parameterName);
        }

        return value;
    }

    internal static bool TryValidate(
        string? value,
        [NotNullWhen(true)] out string? validated)
    {
        validated = null;
        if (value is null || value.Length == 0 || value.Length > MaximumLength ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
        {
            return false;
        }

        validated = value;
        return true;
    }

    /// <summary>
    /// 判断标识是否符合新契约的小写点分层格式。
    /// 每个片段采用 kebab-case；历史别名只需通过 <see cref="Validate"/>。
    /// </summary>
    internal static bool IsCanonical(string value) =>
        value.Split('.').All(segment =>
            segment.Length > 0 &&
            segment[0] != '-' &&
            segment[^1] != '-' &&
            segment.All(character =>
                char.IsAsciiLetterOrDigit(character) && !char.IsAsciiLetterUpper(character) ||
                character == '-'));
}
