namespace MyAvaloniaManagementCommon.Plugin;

internal sealed record PluginLifecyclePlanNode(
    IPluginLifecycle Lifecycle,
    PluginId PluginId,
    IReadOnlyList<PluginId> RequiredPluginIds);

internal sealed record PluginLifecyclePlan(
    IReadOnlyList<PluginLifecyclePlanNode> OrderedNodes,
    IReadOnlyDictionary<PluginId, PluginLifecycleState> InitialStates);

/// <summary>
/// 把生命周期声明转换成确定性的强类型依赖计划；本类型不执行任何插件代码。
/// </summary>
internal static class PluginLifecyclePlanBuilder
{
    internal static PluginLifecyclePlan Build(IEnumerable<IPluginLifecycle> lifecycles)
    {
        ArgumentNullException.ThrowIfNull(lifecycles);
        var candidates = lifecycles.Select(CreateCandidate).ToArray();
        var initialStates = new Dictionary<PluginId, PluginLifecycleState>();
        var duplicateIds = candidates
            .GroupBy(item => item.PluginId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var duplicateId in duplicateIds)
        {
            var dependencies = candidates.First(item => item.PluginId == duplicateId).RequiredPluginIds;
            initialStates[duplicateId] = FailureState(
                duplicateId,
                "LIFECYCLE_PLUGIN_ID_DUPLICATE",
                $"存在多个 PluginId 为 {duplicateId} 的生命周期注册。",
                dependencies);
        }

        var nodes = candidates
            .Where(item => !duplicateIds.Contains(item.PluginId))
            .ToDictionary(item => item.PluginId);

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

        return new PluginLifecyclePlan(TopologicalSort(nodes, cycleIds), initialStates);
    }

    private static Candidate CreateCandidate(IPluginLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        var pluginId = lifecycle.PluginId ??
                       throw new ArgumentException("插件生命周期必须提供 PluginId。", nameof(lifecycle));
        var dependencies = lifecycle is IPluginLifecycleDependencies declaration
            ? (declaration.RequiredPluginIds ?? [])
                .Where(id => id is not null)
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray()
            : [];
        return new Candidate(lifecycle, pluginId, dependencies);
    }

    private static HashSet<PluginId> FindCycleIds(
        IReadOnlyDictionary<PluginId, Candidate> nodes)
    {
        var visitStates = new Dictionary<PluginId, int>();
        var stack = new List<PluginId>();
        var cycleIds = new HashSet<PluginId>();
        foreach (var pluginId in nodes.Keys.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            Visit(pluginId);
        }

        return cycleIds;

        void Visit(PluginId pluginId)
        {
            if (visitStates.GetValueOrDefault(pluginId) == 2) return;
            visitStates[pluginId] = 1;
            stack.Add(pluginId);
            foreach (var dependency in nodes[pluginId].RequiredPluginIds)
            {
                if (dependency == pluginId || !nodes.ContainsKey(dependency)) continue;
                var dependencyState = visitStates.GetValueOrDefault(dependency);
                if (dependencyState == 0)
                {
                    Visit(dependency);
                }
                else if (dependencyState == 1)
                {
                    var cycleStart = stack.IndexOf(dependency);
                    for (var index = cycleStart; index < stack.Count; index++)
                        cycleIds.Add(stack[index]);
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visitStates[pluginId] = 2;
        }
    }

    private static IReadOnlyList<PluginLifecyclePlanNode> TopologicalSort(
        IReadOnlyDictionary<PluginId, Candidate> nodes,
        IReadOnlySet<PluginId> cycleIds)
    {
        var sortable = nodes.Values
            .Where(node => !cycleIds.Contains(node.PluginId))
            .ToDictionary(node => node.PluginId);
        var indegrees = sortable.Keys.ToDictionary(pluginId => pluginId, _ => 0);
        var dependents = sortable.Keys.ToDictionary(pluginId => pluginId, _ => new List<PluginId>());
        foreach (var node in sortable.Values)
        {
            foreach (var dependency in node.RequiredPluginIds.Where(sortable.ContainsKey))
            {
                indegrees[node.PluginId]++;
                dependents[dependency].Add(node.PluginId);
            }
        }

        var comparer = Comparer<Candidate>.Create((left, right) =>
        {
            var order = left.Lifecycle.Order.CompareTo(right.Lifecycle.Order);
            return order != 0
                ? order
                : StringComparer.Ordinal.Compare(left.PluginId.Value, right.PluginId.Value);
        });
        var ready = new SortedSet<Candidate>(comparer);
        foreach (var node in sortable.Values.Where(node => indegrees[node.PluginId] == 0))
            ready.Add(node);

        var result = new List<PluginLifecyclePlanNode>(sortable.Count);
        while (ready.Count > 0)
        {
            var node = ready.Min!;
            ready.Remove(node);
            result.Add(new PluginLifecyclePlanNode(node.Lifecycle, node.PluginId, node.RequiredPluginIds));
            foreach (var dependent in dependents[node.PluginId])
            {
                if (--indegrees[dependent] == 0) ready.Add(sortable[dependent]);
            }
        }

        return result;
    }

    private static PluginLifecycleState FailureState(
        PluginId pluginId,
        string errorCode,
        string message,
        IReadOnlyList<PluginId> dependencies) =>
        new(pluginId, PluginLifecycleStatus.Failed, message)
        {
            Stage = PluginLifecycleStage.Initialization,
            ErrorCode = errorCode,
            RequiredPluginIds = dependencies,
        };

    private static PluginLifecycleState BlockedState(
        PluginId pluginId,
        string errorCode,
        string message,
        IReadOnlyList<PluginId> dependencies,
        PluginId? blockingPluginId) =>
        new(pluginId, PluginLifecycleStatus.Blocked, message)
        {
            Stage = PluginLifecycleStage.Initialization,
            ErrorCode = errorCode,
            RequiredPluginIds = dependencies,
            BlockingPluginId = blockingPluginId,
        };

    private sealed record Candidate(
        IPluginLifecycle Lifecycle,
        PluginId PluginId,
        IReadOnlyList<PluginId> RequiredPluginIds);
}
