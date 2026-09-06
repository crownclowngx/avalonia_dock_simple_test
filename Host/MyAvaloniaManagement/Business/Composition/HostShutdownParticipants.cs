using System;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.WorkflowActions;

namespace MyAvaloniaManagement.Business.Composition;

/// <summary>
/// 记录实际已经交付的关闭参与者。插件组合可能间接创建 Workflow 管理器，因此回滚不能靠阶段标记，
/// 更不能为了查询“是否存在”再次解析 DI。本记录只服务 Host 关闭，不提供服务定位或通用注册表。
/// </summary>
internal sealed class HostShutdownParticipants
{
    private readonly object _gate = new();
    private bool _frozen;
    private HostShutdownSnapshot _snapshot = new(null, null, null, null, null);

    internal void Record(IWorkflowActionShutdownParticipant value) => Update(() => _snapshot with { Workflow = value });
    internal void Record(IWorkbenchCommandShutdownParticipant value) => Update(() => _snapshot with { Commands = value });
    internal void Record(PluginLifecycleCoordinator value) => Update(() => _snapshot with { Lifecycles = value });
    internal void Record(PluginLifecycleStateStore value) => Update(() => _snapshot with { States = value });
    internal void Record(WorkspaceSession value) => Update(() => _snapshot with { Workspace = value });

    private void Update(Func<HostShutdownSnapshot> update)
    {
        lock (_gate)
        {
            if (_frozen) throw new InvalidOperationException("Host 已进入关闭，不能交付新的关闭参与者。");
            _snapshot = update();
        }
    }

    internal HostShutdownSnapshot Freeze()
    {
        lock (_gate)
        {
            _frozen = true;
            return _snapshot;
        }
    }
}

internal sealed record HostShutdownSnapshot(
    IWorkflowActionShutdownParticipant? Workflow,
    IWorkbenchCommandShutdownParticipant? Commands,
    PluginLifecycleCoordinator? Lifecycles,
    PluginLifecycleStateStore? States,
    WorkspaceSession? Workspace);
