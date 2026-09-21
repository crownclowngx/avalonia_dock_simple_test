namespace MyAvaloniaManagement.Testing;

/// <summary>
/// 测试附属文件的目录约定：Gate 传入本套件专属根，独立测试使用本进程唯一目录。
/// 只负责定位和创建目录，不判定测试结果；各夹具仍拥有自己的文件与清理流程。
/// </summary>
internal static class TestEvidenceDirectory
{
    internal const string Variable = "MYAVALONIA_TEST_EVIDENCE_DIRECTORY";
    private static readonly string Root = Path.GetFullPath(
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "TestResults", $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"));

    internal static string Create(string scenario)
    {
        var path = Path.Combine(Root, scenario);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>历史截图开关继续可用；Gate 目录优先。未启用导出只影响辅助 PNG，不影响行为断言。</summary>
    internal static string? Optional(string scenario, string legacyVariable)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable))) return Create(scenario);
        var path = Environment.GetEnvironmentVariable(legacyVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        Directory.CreateDirectory(path);
        return path;
    }
}
