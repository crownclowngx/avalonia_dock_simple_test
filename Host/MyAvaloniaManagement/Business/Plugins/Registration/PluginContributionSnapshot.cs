using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.PluginSdk;
using static MyAvaloniaManagement.Business.Plugins.Registration.PluginRegistryBuilder;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>在校验边界复制声明集合，向纯规则提供不会随 Builder 后续写入改变的事实。</summary>
/// <remarks>
/// 只复制集合；Descriptor 和声明记录在注册时已经冻结，工厂委托只作为事实保留而不执行。
/// 本快照不拥有模型、Provider 或 Scope，也不提供提交能力。复制后加只读包装，避免调用方
/// 将 IReadOnlyList 强转回数组再修改，造成校验和发布看到不同声明。
/// </remarks>
internal sealed class PluginContributionSnapshot
{
    /// <summary>从当前候选集合建立独立快照；调用方须在原组合线程同步捕获。</summary>
    internal PluginContributionSnapshot(
        IEnumerable<DocumentDeclaration> documents,
        IEnumerable<ToolDeclaration> tools,
        IEnumerable<LifecycleDeclaration> lifecycles,
        IEnumerable<WorkflowActionDeclaration> workflowActions,
        IEnumerable<PluginId> workflowConsumers,
        IEnumerable<WorkbenchCommandDeclaration> workbenchCommands,
        IEnumerable<MenuCommandContributionDeclaration> menuCommandContributions,
        IEnumerable<KeyBindingContributionDeclaration> keyBindingContributions,
        IEnumerable<PluginIconRegistration> icons)
    {
        Documents = Array.AsReadOnly(documents.ToArray());
        Tools = Array.AsReadOnly(tools.ToArray());
        Lifecycles = Array.AsReadOnly(lifecycles.ToArray());
        WorkflowActions = Array.AsReadOnly(workflowActions.ToArray());
        WorkflowConsumers = Array.AsReadOnly(workflowConsumers.ToArray());
        WorkbenchCommands = Array.AsReadOnly(workbenchCommands.ToArray());
        MenuCommandContributions = Array.AsReadOnly(menuCommandContributions.ToArray());
        KeyBindingContributions = Array.AsReadOnly(keyBindingContributions.ToArray());
        Icons = Array.AsReadOnly(icons.ToArray());
    }

    internal IReadOnlyList<DocumentDeclaration> Documents { get; }
    internal IReadOnlyList<ToolDeclaration> Tools { get; }
    internal IReadOnlyList<LifecycleDeclaration> Lifecycles { get; }
    internal IReadOnlyList<WorkflowActionDeclaration> WorkflowActions { get; }
    internal IReadOnlyList<PluginId> WorkflowConsumers { get; }
    internal IReadOnlyList<WorkbenchCommandDeclaration> WorkbenchCommands { get; }
    internal IReadOnlyList<MenuCommandContributionDeclaration> MenuCommandContributions { get; }
    internal IReadOnlyList<KeyBindingContributionDeclaration> KeyBindingContributions { get; }
    internal IReadOnlyList<PluginIconRegistration> Icons { get; }
}
