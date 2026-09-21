namespace MyAvaloniaManagement.Gate;

/// <summary>固定的顺序计划。开发契约前置以尽早失败；发布阶段保持原顺序，不引入依赖图或动态注册。</summary>
internal sealed class GateExecutionPlan
{
    private readonly Func<string, Task> execute;
    internal IReadOnlyList<string> Stages { get; }

    private GateExecutionPlan(GateProfile profile, Func<string, Task> execute)
    {
        this.execute = execute;
        Stages = profile switch
        {
            GateProfile.Verify => ["avalonia-layout-patch", "dock-patch", "restore", "build", "contracts", "tests", "packages", "package-acceptance"],
            GateProfile.Seal => ["avalonia-layout-patch", "dock-patch", "restore", "build", "tests", "contracts", "packages", "package-acceptance", "coverage", "windows-smoke"],
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
    }

    internal static GateExecutionPlan ForProfile(GateProfile profile, Func<string, Task> execute) => new(profile, execute);

    internal async Task ExecuteAsync(Func<string, Func<Task>, Task> stageRunner)
    {
        foreach (var id in Stages) await stageRunner(id, () => execute(id));
    }
}
