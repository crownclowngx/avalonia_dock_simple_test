using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 经完整校验后一次性发布的声明式插件贡献快照。
/// </summary>
/// <remarks>
/// Registry 只保存身份、描述符和实现类型，不持有 Provider、Scope、Dock 对象或生命周期运行状态。
/// 所有集合都在构造时复制为只读索引；模型创建由 <see cref="PluginContributionActivator"/> 根据这些
/// 事实完成，从而把“声明了什么”和“何时创建实例”保持为两个单一职责。
/// </remarks>
internal sealed class PluginRegistry
{
    private readonly IReadOnlyDictionary<DocumentTypeId, PluginDocumentRegistration> _documents;
    private readonly IReadOnlyDictionary<ToolTypeId, PluginToolRegistration> _tools;
    private readonly IReadOnlyDictionary<Type, PluginViewRegistration> _views;
    private readonly IReadOnlyDictionary<WorkflowActionId, PluginWorkflowActionRegistration>
        _workflowActions;
    private readonly IReadOnlyDictionary<CommandId, PluginWorkbenchCommandRegistration>
        _workbenchCommands;
    private readonly IReadOnlyDictionary<CommandPlacementId, PluginMenuCommandContribution>
        _menuCommandContributions;
    private readonly IReadOnlyDictionary<CommandPlacementId, PluginKeyBindingContribution>
        _keyBindingContributions;

    internal PluginRegistry(
        IReadOnlyList<PluginRegistryPlugin> plugins,
        IReadOnlyList<PluginDocumentRegistration> documents,
        IReadOnlyList<PluginToolRegistration> tools,
        IReadOnlyList<PluginLifecycleDeclaration> lifecycles,
        IReadOnlyList<PluginWorkflowActionRegistration>? workflowActions = null,
        IReadOnlySet<PluginId>? workflowConsumers = null,
        IReadOnlyList<PluginWorkbenchCommandRegistration>? workbenchCommands = null,
        IReadOnlyList<PluginMenuCommandContribution>? menuCommandContributions = null,
        IReadOnlyList<PluginKeyBindingContribution>? keyBindingContributions = null)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(lifecycles);
        workflowActions ??= [];
        workflowConsumers ??= new HashSet<PluginId>();
        workbenchCommands ??= [];
        menuCommandContributions ??= [];
        keyBindingContributions ??= [];

        // 插件快照内部也包含集合，必须逐层复制；只包住最外层数组仍允许调用方通过原始数组
        // 改写 Document/View/Lifecycle 列表，破坏 Registry 的发布后不变性。
        Plugins = Array.AsReadOnly(plugins.Select(plugin => plugin with
        {
            DocumentTypes = Array.AsReadOnly(plugin.DocumentTypes.ToArray()),
            ToolTypes = Array.AsReadOnly(plugin.ToolTypes.ToArray()),
            Views = Array.AsReadOnly(plugin.Views.ToArray()),
            LifecycleTypes = Array.AsReadOnly(plugin.LifecycleTypes.ToArray()),
        }).ToArray());
        Lifecycles = Array.AsReadOnly(lifecycles.ToArray());
        _documents = documents.ToDictionary(item => item.Descriptor.DocumentTypeId);
        _tools = tools.ToDictionary(item => item.Descriptor.ToolTypeId);
        _workflowActions = workflowActions.ToDictionary(item => item.Descriptor.Id);
        _workbenchCommands = workbenchCommands.ToDictionary(item => item.Descriptor.CommandId);
        _menuCommandContributions = menuCommandContributions.ToDictionary(
            item => item.Descriptor.PlacementId);
        _keyBindingContributions = keyBindingContributions.ToDictionary(
            item => item.Descriptor.PlacementId);
        WorkflowActionConsumerIds = workflowConsumers.ToHashSet();
        _views = documents.Select(item => new PluginViewRegistration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory))
            .Concat(tools.Select(item => new PluginViewRegistration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory)))
            .GroupBy(item => item.ModelType)
            .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>测试使用的空/最小声明式 Registry 构造入口。</summary>
    internal PluginRegistry(
        IReadOnlyList<PluginDocumentRegistration> documents,
        IReadOnlyList<PluginToolRegistration> tools)
        : this([], documents, tools, [])
    {
    }

    internal IReadOnlyList<PluginRegistryPlugin> Plugins { get; }

    /// <summary>获取已经冻结但尚未由 G8 编排执行的最终 SDK 生命周期声明。</summary>
    internal IReadOnlyList<PluginLifecycleDeclaration> Lifecycles { get; }

    /// <summary>
    /// 返回不可变声明中出现的全部所有者。测试可构造不含 manifest 快照的最小 Registry，
    /// 因而状态存储不能把 <see cref="Plugins"/> 当作贡献所有权的第二份唯一事实。
    /// </summary>
    internal IReadOnlySet<PluginId> DeclaredOwnerIds => Plugins
        .Select(plugin => new PluginId(plugin.Manifest.PluginId.Value))
        .Concat(_documents.Values.Select(document => document.OwnerId))
        .Concat(_tools.Values.Select(tool => tool.OwnerId))
        .Concat(Lifecycles.Select(lifecycle => lifecycle.OwnerId))
        .Concat(_workflowActions.Values.Select(action => action.OwnerId))
        .Concat(WorkflowActionConsumerIds)
        .Concat(_workbenchCommands.Values.Select(command => command.OwnerId))
        .Concat(_menuCommandContributions.Values.Select(contribution => contribution.OwnerId))
        .Concat(_keyBindingContributions.Values.Select(contribution => contribution.OwnerId))
        .ToHashSet();

    /// <summary>获取冻结的插件工作台命令声明；集合不包含 Host 内建命令和运行状态。</summary>
    internal IReadOnlyCollection<PluginWorkbenchCommandRegistration> WorkbenchCommands =>
        _workbenchCommands.Values.ToArray();

    /// <summary>获取冻结的插件菜单命令贡献；G1 尚不把它们投影为 Avalonia 控件。</summary>
    internal IReadOnlyCollection<PluginMenuCommandContribution> MenuCommandContributions =>
        _menuCommandContributions.Values.ToArray();

    /// <summary>获取冻结的插件快捷键贡献；G1 尚不创建 Avalonia KeyBinding。</summary>
    internal IReadOnlyCollection<PluginKeyBindingContribution> KeyBindingContributions =>
        _keyBindingContributions.Values.ToArray();

    internal bool TryGetWorkbenchCommand(
        CommandId commandId,
        out PluginWorkbenchCommandRegistration registration) =>
        _workbenchCommands.TryGetValue(commandId, out registration!);

    /// <summary>获取不可变 Workflow Action 注册集合，不包含可用性运行状态。</summary>
    internal IReadOnlyCollection<PluginWorkflowActionRegistration> WorkflowActions =>
        _workflowActions.Values.ToArray();

    /// <summary>获取显式请求 caller-bound Gateway 的 Consumer 身份快照。</summary>
    internal IReadOnlySet<PluginId> WorkflowActionConsumerIds { get; }

    internal bool TryGetWorkflowAction(
        WorkflowActionId actionId,
        out PluginWorkflowActionRegistration registration) =>
        _workflowActions.TryGetValue(actionId, out registration!);

    /// <summary>完整插件 Document 注册；集合不包含 Host 内建 Document。</summary>
    internal IReadOnlyCollection<PluginDocumentRegistration> Documents =>
        _documents.Values.ToArray();

    /// <summary>供 Host internal 可用性投影筛选的完整 Tool 声明；返回集合不包含运行状态。</summary>
    internal IReadOnlyCollection<PluginToolRegistration> Tools => _tools.Values.ToArray();

    internal IReadOnlyDictionary<DocumentTypeId, DocumentDescriptor> DocumentDescriptors =>
        _documents.ToDictionary(item => item.Key, item => item.Value.Descriptor);

    internal IReadOnlyDictionary<ToolTypeId, ToolDescriptor> ToolDescriptors =>
        _tools.ToDictionary(item => item.Key, item => item.Value.Descriptor);

    internal bool TryGetView(Type modelType, out PluginViewRegistration registration) =>
        _views.TryGetValue(modelType, out registration!);

    internal bool TryGetDocumentRegistration(
        DocumentTypeId documentTypeId,
        out PluginDocumentRegistration registration) =>
        _documents.TryGetValue(documentTypeId, out registration!);

    internal bool TryGetToolRegistration(
        ToolTypeId toolTypeId,
        out PluginToolRegistration registration) =>
        _tools.TryGetValue(toolTypeId, out registration!);

    internal bool TryResolveToolTypeId(string value, out ToolTypeId? toolTypeId)
    {
        if (!ToolTypeId.TryParse(value, out var parsed) || !_tools.ContainsKey(parsed!))
        {
            toolTypeId = null;
            return false;
        }

        toolTypeId = parsed;
        return true;
    }

}

/// <summary>描述一个已经通过两阶段提交的插件及其全部贡献类型快照。</summary>
/// <remarks>嵌套集合由 Registry 构造函数逐层防御性复制，调用者不能在发布后改写。</remarks>
internal sealed record PluginRegistryPlugin(
    PluginManifest Manifest,
    Assembly EntryAssembly,
    Type ModuleType,
    IReadOnlyList<Type> DocumentTypes,
    IReadOnlyList<Type> ToolTypes,
    IReadOnlyList<PluginViewTypePair> Views,
    IReadOnlyList<Type> LifecycleTypes);

/// <summary>记录一个模型类型与其显式 Avalonia View 类型的精确映射。</summary>
internal sealed record PluginViewTypePair(Type ModelType, Type ViewType);

/// <summary>冻结一种 Document 的所有者、描述符、模型、View 工厂和持久化能力。</summary>
/// <remarks>记录本身不拥有 Scope；创建时由 Activator 路由到所有者的 DocumentScopeManager。</remarks>
internal sealed record PluginDocumentRegistration(
    PluginId OwnerId,
    DocumentDescriptor Descriptor,
    Type ModelType,
    Type ViewType,
    Func<Control> ViewFactory,
    bool IsPersistable) : IWorkspaceDocumentRegistration;

/// <summary>冻结一种 Tool 的所有者、描述符、singleton 模型类型和 View 工厂。</summary>
internal sealed record PluginToolRegistration(
    PluginId OwnerId,
    ToolDescriptor Descriptor,
    Type ModelType,
    Type ViewType,
    Func<Control> ViewFactory) : IWorkspaceToolRegistration;

/// <summary>供 ViewLocator 按精确模型类型查询的只读 View 注册事实。</summary>
internal sealed record PluginViewRegistration(
    PluginId OwnerId,
    Type ModelType,
    Type ViewType,
    Func<Control> Factory);

/// <summary>记录 G5 已验证可解析、但要到 G8 才执行的生命周期实现类型。</summary>
internal sealed record PluginLifecycleDeclaration(
    PluginId OwnerId,
    Type ImplementationType);

/// <summary>冻结动作所有者、不可变描述符和所有者 Provider 内的 scoped Handler 类型。</summary>
/// <remarks>本记录不持有 Provider、Scope、Handler 实例、授权结果或运行状态。</remarks>
internal sealed record PluginWorkflowActionRegistration(
    PluginId OwnerId,
    WorkflowActionDescriptor Descriptor,
    Type HandlerType);

/// <summary>冻结命令所有者、不可变描述符和其目标 Document 类型。</summary>
/// <remarks>本记录不持有 Target、模型、Handler、Provider、Scope、Control、Dock 或 ICommand。</remarks>
internal sealed record PluginWorkbenchCommandRegistration(
    PluginId OwnerId,
    CommandDescriptor Descriptor,
    DocumentTypeId TargetDocumentTypeId);

/// <summary>冻结菜单命令贡献的所有者和不可变描述符。</summary>
internal sealed record PluginMenuCommandContribution(
    PluginId OwnerId,
    MenuCommandContributionDescriptor Descriptor);

/// <summary>冻结快捷键贡献的所有者和不可变描述符。</summary>
internal sealed record PluginKeyBindingContribution(
    PluginId OwnerId,
    KeyBindingContributionDescriptor Descriptor);
