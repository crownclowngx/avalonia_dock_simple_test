namespace MyAvaloniaManagement.Gate;

internal enum GateProfile
{
    Verify,
    Seal,
}

internal enum GateScope
{
    All,
    Host,
}

internal sealed record GateOptions(
    GateProfile Profile,
    GateScope Scope,
    bool Repeat,
    bool ShowHelp)
{
    public static GateOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments.Contains("--help", StringComparer.Ordinal) ||
            arguments.Contains("-h", StringComparer.Ordinal))
        {
            return new(GateProfile.Verify, GateScope.All, false, true);
        }

        var profile = arguments[0] switch
        {
            "verify" => GateProfile.Verify,
            "seal" => GateProfile.Seal,
            _ => throw new GateUsageException($"未知 profile：{arguments[0]}。"),
        };

        var scope = GateScope.All;
        var repeat = false;
        for (var index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--repeat":
                    repeat = true;
                    break;
                case "--scope":
                    scope = ParseScope(RequireValue(arguments, ref index, "--scope"));
                    break;
                case "--workflow-studio":
                case "--classic-game":
                    throw new GateUsageException($"{arguments[index]} 已退役；Gate 只验证本仓 Host 与 MyPlugTest。");
                default:
                    throw new GateUsageException($"未知参数：{arguments[index]}。");
            }
        }

        if (repeat && profile != GateProfile.Seal)
        {
            throw new GateUsageException("--repeat 只能与 seal 一起使用。");
        }

        if (profile == GateProfile.Seal && scope != GateScope.All)
        {
            throw new GateUsageException("seal 始终执行完整门禁；--scope 只用于 verify 排错。");
        }

        return new(profile, scope, repeat, false);
    }

    private static GateScope ParseScope(string value) => value switch
    {
        "all" => GateScope.All,
        "host" => GateScope.Host,
        "workflow" or "workbench" => throw new GateUsageException(
            $"scope {value} 已退役；host 与 all 均执行完整本仓验证。"),
        _ => throw new GateUsageException($"未知 scope：{value}。"),
    };

    private static string RequireValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (++index >= arguments.Count || arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new GateUsageException($"{option} 缺少值。");
        }

        return arguments[index];
    }
}

internal sealed class GateUsageException(string message) : Exception(message);
