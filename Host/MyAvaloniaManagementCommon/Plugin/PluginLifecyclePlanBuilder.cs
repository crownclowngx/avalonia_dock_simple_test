namespace MyAvaloniaManagementCommon.Plugin;

internal sealed record PluginLifecyclePlanNode(
    IPluginLifecycle Lifecycle,
    string PluginId,
    IReadOnlyList<string> RequiredPluginIds);

internal sealed record PluginLifecyclePlan(
    IReadOnlyList<PluginLifecyclePlanNode> OrderedNodes,
    IReadOnlyDictionary<string, PluginLifecycleState> InitialStates);

/// <summary>
/// 把生命周期声明转换成确定性的依赖计划；本类型不执行任何插件代码。
/// </summary>
internal static class PluginLifecyclePlanBuilder
{
    internal static PluginLifecyclePlan Build(
        IEnumerable<IPluginLifecycle> lifecycles)
    {
        ArgumentNullException.ThrowIfNull(lifecycles);
        var candidates = lifecycles.Select((lifecycle, index) =>
            CreateCandidate(lifecycle, index)).ToArray();
        var initialStates = new Dictionary<string, PluginLifecycleState>(
            StringComparer.Ordinal);

        foreach (var invalid in candidates.Where(item => !item.HasValidPluginId))
        {
            initialStates[invalid.StateKey] = FailureState(
                invalid.StateKey,
                "LIFECYCLE_PLUGIN_ID_INVALID",
                "插件生命周期必须提供非空 PluginId。",
                invalid.RequiredPluginIds);
        }

        var validCandidates = candidates.Where(item => item.HasValidPluginId).ToArray();
        var duplicateIds = validCandidates
            .GroupBy(item => item.PluginId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var duplicateId in duplicateIds)
        {
            var dependencies = validCandidates
                .First(item => item.PluginId == duplicateId)
                .RequiredPluginIds;
            initialStates[duplicateId] = FailureState(
                duplicateId,
                "LIFECYCLE_PLUGIN_ID_DUPLICATE",
                $"存在多个 PluginId 为 {duplicateId} 的生命周期注册。",
                dependencies);
        }

        var nodes = validCandidates
            .Where(item => !duplicateIds.Contains(item.PluginId))
            .ToDictionary(item => item.PluginId, StringComparer.Ordinal);

        foreach (var node in nodes.Values)
        {
            initialStates[node.PluginId] = new PluginLifecycleState(
                node.PluginId,
                PluginLifecycleStatus.NotStarted)
            {
                RequiredPluginIds = node.RequiredPluginIds,
            };

            var invalidDependency = node.RequiredPluginIds.FirstOrDefault(
                dependency =>
                    dependency == node.PluginId ||
                    duplicateIds.Contains(dependency) ||
                    !nodes.ContainsKey(dependency));
            if (invalidDependency is null)
            {
                continue;
            }

            var errorCode = invalidDependency == node.PluginId
                ? "LIFECYCLE_DEPENDENCY_SELF"
                : duplicateIds.Contains(invalidDependency)
                    ? "LIFECYCLE_DEPENDENCY_DUPLICATE"
                    : "LIFECYCLE_DEPENDENCY_MISSING";
            initialStates[node.PluginId] = BlockedState(
                node.PluginId,
                errorCode,
                invalidDependency == node.PluginId
                    ? "插件不能依赖自身。"
                    : $"依赖插件 {invalidDependency} 不存在或不可唯一识别。",
                node.RequiredPluginIds,
                invalidDependency);
        }

        var cycleIds = FindCycleIds(nodes);
        foreach (var cycleId in cycleIds)
        {
            var node = nodes[cycleId];
            initialStates[cycleId] = BlockedState(
                cycleId,
                "LIFECYCLE_DEPENDENCY_CYCLE",
                "插件生命周期依赖图中存在循环依赖。",
                node.RequiredPluginIds,
                node.RequiredPluginIds.FirstOrDefault(cycleIds.Contains));
        }

        return new PluginLifecyclePlan(
            TopologicalSort(nodes, cycleIds),
            initialStates);
    }

    private static Candidate CreateCandidate(
        IPluginLifecycle lifecycle,
        int index)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        var pluginId = lifecycle.PluginId?.Trim() ?? string.Empty;
        var stateKey = string.IsNullOrWhiteSpace(pluginId)
            ? $"<invalid:{index}:{lifecycle.GetType().Name}>"
            : pluginId;
        var dependencies = lifecycle is IPluginLifecycleDependencies declaration
            ? (declaration.RequiredPluginIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

        return new Candidate(
            lifecycle,
            pluginId,
            stateKey,
            !string.IsNullOrWhiteSpace(pluginId),
            dependencies);
    }

    private static HashSet<string> FindCycleIds(
        IReadOnlyDictionary<string, Candidate> nodes)
    {
        var visitStates = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        var cycleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pluginId in nodes.Keys.Order(StringComparer.Ordinal))
        {
            Visit(pluginId);
        }

        return cycleIds;

        void Visit(string pluginId)
        {
            if (visitStates.GetValueOrDefault(pluginId) == 2)
            {
                return;
            }

            visitStates[pluginId] = 1;
            stack.Add(pluginId);

            foreach (var dependency in nodes[pluginId].RequiredPluginIds)
            {
                if (dependency == pluginId || !nodes.ContainsKey(dependency))
                {
                    continue;
                }

                var dependencyState = visitStates.GetValueOrDefault(dependency);
                if (dependencyState == 0)
                {
                    Visit(dependency);
                }
                else if (dependencyState == 1)
                {
                    var cycleStart = stack.IndexOf(dependency);
                    for (var index = cycleStart; index < stack.Count; index++)
                    {
                        cycleIds.Add(stack[index]);
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visitStates[pluginId] = 2;
        }
    }

    private static IReadOnlyList<PluginLifecyclePlanNode> TopologicalSort(
        IReadOnlyDictionary<string, Candidate> nodes,
        IReadOnlySet<string> cycleIds)
    {
        var sortable = nodes.Values
            .Where(node => !cycleIds.Contains(node.PluginId))
            .ToDictionary(node => node.PluginId, StringComparer.Ordinal);
        var indegrees = sortable.Keys.ToDictionary(
            pluginId => pluginId,
            _ => 0,
            StringComparer.Ordinal);
        var dependents = sortable.Keys.ToDictionary(
            pluginId => pluginId,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var node in sortable.Values)
        {
            foreach (var dependency in node.RequiredPluginIds)
            {
                if (!sortable.ContainsKey(dependency))
                {
                    continue;
                }

                indegrees[node.PluginId]++;
                dependents[dependency].Add(node.PluginId);
            }
        }

        var comparer = Comparer<Candidate>.Create((left, right) =>
        {
            var order = left.Lifecycle.Order.CompareTo(right.Lifecycle.Order);
            return order != 0
                ? order
                : StringComparer.Ordinal.Compare(left.PluginId, right.PluginId);
        });
        var ready = new SortedSet<Candidate>(comparer);
        foreach (var node in sortable.Values.Where(node => indegrees[node.PluginId] == 0))
        {
            ready.Add(node);
        }

        var result = new List<PluginLifecyclePlanNode>(sortable.Count);
        while (ready.Count > 0)
        {
            var node = ready.Min!;
            ready.Remove(node);
            result.Add(new PluginLifecyclePlanNode(
                node.Lifecycle,
                node.PluginId,
                node.RequiredPluginIds));

            foreach (var dependent in dependents[node.PluginId])
            {
                indegrees[dependent]--;
                if (indegrees[dependent] == 0)
                {
                    ready.Add(sortable[dependent]);
                }
            }
        }

        return result;
    }

    private static PluginLifecycleState FailureState(
        string pluginId,
        string errorCode,
        string message,
        IReadOnlyList<string> dependencies) =>
        new(pluginId, PluginLifecycleStatus.Failed, message)
        {
            Stage = PluginLifecycleStage.Initialization,
            ErrorCode = errorCode,
            RequiredPluginIds = dependencies,
        };

    private static PluginLifecycleState BlockedState(
        string pluginId,
        string errorCode,
        string message,
        IReadOnlyList<string> dependencies,
        string? blockingPluginId) =>
        new(pluginId, PluginLifecycleStatus.Blocked, message)
        {
            Stage = PluginLifecycleStage.Initialization,
            ErrorCode = errorCode,
            RequiredPluginIds = dependencies,
            BlockingPluginId = blockingPluginId,
        };

    private sealed record Candidate(
        IPluginLifecycle Lifecycle,
        string PluginId,
        string StateKey,
        bool HasValidPluginId,
        IReadOnlyList<string> RequiredPluginIds);
}
