namespace MyAvaloniaManagement.Gate;

internal sealed class GateExecutionGraph
{
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private readonly List<(string Id, Func<Task> Action)> nodes = [];

    public GateExecutionGraph Add(string id, Func<Task> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(action);
        if (ids.Add(id))
        {
            nodes.Add((id, action));
        }

        return this;
    }

    public async Task ExecuteAsync(Func<string, Func<Task>, Task> stageRunner)
    {
        ArgumentNullException.ThrowIfNull(stageRunner);
        foreach (var node in nodes)
        {
            await stageRunner(node.Id, node.Action);
        }
    }
}
