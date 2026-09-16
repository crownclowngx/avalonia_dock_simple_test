namespace MyAvaloniaManagement.CompatibilityTool;

/// <summary>显式输入产物和执行范围，不猜测用户安装位置，也不默认启动业务模型。</summary>
internal sealed record CompatibilityOptions(string Input, string Host, string Output, string? UiTests,
    IReadOnlySet<string> WorkspaceIds, int TimeoutSeconds)
{
    internal static CompatibilityOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--input" or "--host" or "--output" or "--ui-tests" or "--workspace" or "--timeout") ||
                !values.TryAdd(args[i], args[i + 1]) || string.IsNullOrWhiteSpace(args[i + 1]))
                throw new ArgumentException("参数无效或重复；使用 --help 查看说明。");
        }
        string Required(string key) => Path.GetFullPath(values.GetValueOrDefault(key) ?? throw new ArgumentException($"缺少 {key}"));
        var ids = (values.GetValueOrDefault("--workspace") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (ids.Count > 0 && !values.ContainsKey("--ui-tests")) throw new ArgumentException("Workspace 验收需要 --ui-tests。");
        var timeout = values.TryGetValue("--timeout", out var raw) ? int.Parse(raw) : 120;
        if (timeout is < 1 or > 3600) throw new ArgumentException("超时需为 1–3600 秒。");
        return new(Required("--input"), Required("--host"), Required("--output"),
            values.TryGetValue("--ui-tests", out var tests) ? Path.GetFullPath(tests) : null, ids, timeout);
    }
}
