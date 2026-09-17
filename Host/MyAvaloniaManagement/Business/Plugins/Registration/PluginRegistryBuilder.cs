using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 在组合阶段收集声明式贡献，并在完整校验后一次性发布不可变 Registry。
/// </summary>
/// <remarks>
/// 每个插件先写入独立的临时 Builder。模块配置、插件内校验或 Provider 构建失败时直接丢弃该
/// Builder；只有候选成功后才导入全局 Builder。最终构建再以简单分组判重找出跨插件冲突，
/// 整体排除冲突所有者，不使用可变 Registry、回滚事务或通用规则引擎。
/// </remarks>
internal sealed class PluginRegistryBuilder
{
    private readonly List<DocumentDeclaration> _documents = [];
    private readonly List<ToolDeclaration> _tools = [];
    private readonly List<LifecycleDeclaration> _lifecycles = [];
    private readonly List<WorkflowActionDeclaration> _workflowActions = [];
    private readonly HashSet<PluginId> _workflowConsumers = [];
    private readonly List<WorkbenchCommandDeclaration> _workbenchCommands = [];
    private readonly List<MenuCommandContributionDeclaration> _menuCommandContributions = [];
    private readonly List<KeyBindingContributionDeclaration> _keyBindingContributions = [];
    private bool _built;
    private readonly List<PluginIconRegistration> _icons = [];

    /// <summary>
    /// 图标只进入当前候选的临时集合；名称不能携带命名空间，避免伪造其他插件或内建资源。
    /// 重复声明在 Seal 时与其他贡献一起拒绝，不采用依赖加载顺序的覆盖规则。
    /// </summary>
    internal string AddIcon(PluginId ownerId, string localName, VectorIconDefinition definition)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(definition);
        if (localName is null || !System.Text.RegularExpressions.Regex.IsMatch(
                localName, "\\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\\z",
                System.Text.RegularExpressions.RegexOptions.NonBacktracking))
        {
            throw new ArgumentException("图标本地名称须以小写字母开头，只允许小写字母、数字及单个连字符分段。", nameof(localName));
        }

        var reference = $"plugin:{ownerId.Value}/{localName}";
        _icons.Add(new PluginIconRegistration(ownerId, reference, definition));
        return reference;
    }

    internal void AddDocument(
        PluginId ownerId,
        DocumentDescriptor descriptor,
        Type modelType,
        Type viewType,
        Func<Control> viewFactory,
        bool isPersistable)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(viewType);
        ArgumentNullException.ThrowIfNull(viewFactory);
        _documents.Add(new DocumentDeclaration(
            ownerId, descriptor, modelType, viewType, viewFactory, isPersistable));
    }

    internal void AddTool(
        PluginId ownerId,
        ToolDescriptor descriptor,
        Type modelType,
        Type viewType,
        Func<Control> viewFactory)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(viewType);
        ArgumentNullException.ThrowIfNull(viewFactory);
        _tools.Add(new ToolDeclaration(ownerId, descriptor, modelType, viewType, viewFactory));
    }

    internal void AddLifecycle(PluginId ownerId, Type implementationType)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(implementationType);
        _lifecycles.Add(new LifecycleDeclaration(ownerId, implementationType));
    }

    /// <summary>收集一条尚未提交的 Document Command 声明。</summary>
    internal void AddDocumentCommand(
        PluginId ownerId,
        CommandDescriptor descriptor,
        DocumentTypeId targetDocumentTypeId)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(targetDocumentTypeId);
        _workbenchCommands.Add(new WorkbenchCommandDeclaration(
            ownerId,
            descriptor,
            targetDocumentTypeId));
    }

    /// <summary>收集一条尚未提交的菜单命令展示声明。</summary>
    internal void AddMenuCommandContribution(
        PluginId ownerId,
        MenuCommandContributionDescriptor descriptor)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        _menuCommandContributions.Add(new MenuCommandContributionDeclaration(ownerId, descriptor));
    }

    /// <summary>收集一条尚未提交的快捷键展示声明。</summary>
    internal void AddKeyBindingContribution(
        PluginId ownerId,
        KeyBindingContributionDescriptor descriptor)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        _keyBindingContributions.Add(new KeyBindingContributionDeclaration(ownerId, descriptor));
    }

    /// <summary>收集一个尚未提交的 Workflow Action 声明。</summary>
    internal void AddWorkflowAction(
        PluginId ownerId,
        WorkflowActionDescriptor descriptor,
        Type handlerType)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handlerType);
        WorkflowActionSchemaValidator.ValidateDescriptor(descriptor);
        _workflowActions.Add(new WorkflowActionDeclaration(ownerId, descriptor, handlerType));
    }

    /// <summary>记录当前插件显式请求 caller-bound Gateway。</summary>
    internal void AddWorkflowActionConsumer(PluginId ownerId)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        if (!_workflowConsumers.Add(ownerId))
        {
            throw new HostCompositionException([
                new HostCompositionDiagnostic(
                    "WORKFLOW_ACTION_CONSUMER_DUPLICATE",
                    ownerId.Value,
                    [])]);
        }
    }

    /// <summary>把一个已经通过插件内校验和 Provider 构建的候选原子导入全局集合。</summary>
    internal void Import(PluginRegistryBuilder source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable();
        source.ValidateSingleOwner();
        _documents.AddRange(source._documents);
        _tools.AddRange(source._tools);
        _lifecycles.AddRange(source._lifecycles);
        _workflowActions.AddRange(source._workflowActions);
        _workflowConsumers.UnionWith(source._workflowConsumers);
        _workbenchCommands.AddRange(source._workbenchCommands);
        _menuCommandContributions.AddRange(source._menuCommandContributions);
        _keyBindingContributions.AddRange(source._keyBindingContributions);
        _icons.AddRange(source._icons);
    }

    /// <summary>返回需要在候选发布前验证构造的生命周期 singleton 类型。</summary>
    internal IReadOnlyList<Type> GetLifecycleTypes() =>
        _lifecycles.Select(item => item.ImplementationType).Distinct().ToArray();

    /// <summary>返回需要在候选发布前由独立 Scope 验证解析的 Handler 类型。</summary>
    internal IReadOnlyList<Type> GetWorkflowActionHandlerTypes() =>
        _workflowActions.Select(item => item.HandlerType).Distinct().ToArray();

    internal bool IsWorkflowActionConsumer(PluginId ownerId) =>
        _workflowConsumers.Contains(ownerId);

    /// <summary>
    /// 校验当前临时 Builder 只表示一个完整且自洽的所有者。
    /// </summary>
    /// <exception cref="HostCompositionException">存在重复、所有者不匹配或生命周期冲突。</exception>
    internal void ValidateSingleOwner(PluginId? expectedOwner = null)
    {
        EnsureWritable();
        var diagnostics = PluginContributionValidator.Validate(CaptureContributions(), expectedOwner);
        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }
    }

    /// <summary>捕获当前声明事实；不改变可写状态，不发布或激活任何贡献。</summary>
    internal PluginContributionSnapshot CaptureContributions() => new(
        _documents, _tools, _lifecycles, _workflowActions, _workflowConsumers,
        _workbenchCommands, _menuCommandContributions, _keyBindingContributions, _icons);

    /// <summary>
    /// 完成跨所有者冲突隔离并发布本次 Runtime 唯一的不可变 Registry。
    /// </summary>
    internal PluginRegistry Build(
        PluginModuleCatalog? catalog,
        IHostDiagnosticSink? diagnosticSink = null,
        PluginProviderOwner? pluginProviders = null)
    {
        EnsureWritable();
        _built = true;

        var rejectedOwners = new HashSet<PluginId>();
        // 逐项消费纯分析结果，保留原诊断顺序与抛错边界。诊断失败时不会继续分析后续冲突，
        // 更不会提前提交 Provider；完整 Registry 的构造和提交仍只在此入口完成。
        foreach (var conflict in PluginConflictAnalyzer.Analyze(CaptureContributions()))
        {
            rejectedOwners.Add(conflict.OwnerId);
            diagnosticSink?.Report(new HostDiagnosticDraft(conflict.Code, HostDiagnosticPhase.ExtensionDiscovery)
            {
                PluginId = conflict.OwnerId,
                StableId = conflict.StableId,
                AssemblyName = conflict.Contributor?.Assembly.GetName(),
            });
        }

        var acceptedDocuments = _documents
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginDocumentRegistration(
                item.OwnerId,
                item.Descriptor,
                item.ModelType,
                item.ViewType,
                item.ViewFactory,
                item.IsPersistable))
            .ToArray();
        var acceptedTools = _tools
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginToolRegistration(
                item.OwnerId,
                item.Descriptor,
                item.ModelType,
                item.ViewType,
                item.ViewFactory))
            .ToArray();
        var acceptedLifecycles = _lifecycles
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginLifecycleDeclaration(
                item.OwnerId, item.ImplementationType))
            .ToArray();
        var acceptedActions = _workflowActions
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginWorkflowActionRegistration(
                item.OwnerId,
                item.Descriptor,
                item.HandlerType))
            .ToArray();
        var acceptedConsumers = _workflowConsumers
            .Where(owner => !rejectedOwners.Contains(owner))
            .ToHashSet();
        var acceptedWorkbenchCommands = _workbenchCommands
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginWorkbenchCommandRegistration(
                item.OwnerId,
                item.Descriptor,
                item.TargetDocumentTypeId))
            .ToArray();
        var acceptedMenuCommandContributions = _menuCommandContributions
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginMenuCommandContribution(
                item.OwnerId,
                item.Descriptor))
            .ToArray();
        var acceptedKeyBindingContributions = _keyBindingContributions
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginKeyBindingContribution(
                item.OwnerId,
                item.Descriptor))
            .ToArray();

        var acceptedPluginIds = pluginProviders?.AvailablePluginIds
            .Where(owner => !rejectedOwners.Contains(owner))
            .ToHashSet();

        // 先完成所有只读事实的构造，再提交 Provider 与 Scope 租约。这样即使快照复制或索引
        // 构造意外失败，也没有任何运行期所有权已经对外可见，失败原子性不依赖事后回滚。
        var registry = new PluginRegistry(
            catalog?.CreatePluginSnapshots(
                acceptedDocuments,
                acceptedTools,
                acceptedLifecycles,
                acceptedPluginIds) ?? [],
            acceptedDocuments,
            acceptedTools,
            acceptedLifecycles,
            acceptedActions,
            acceptedConsumers,
            acceptedWorkbenchCommands,
            acceptedMenuCommandContributions,
            acceptedKeyBindingContributions,
            _icons.Where(item => !rejectedOwners.Contains(item.OwnerId)).ToArray());
        pluginProviders?.CommitRegistryResult(rejectedOwners);
        return registry;
    }

    private void EnsureWritable()
    {
        if (_built)
        {
            throw new InvalidOperationException("Plugin Registry 已经构建，不能再次修改或发布。");
        }
    }

    /// <summary>插件局部 Builder 中尚未提交的 Document 候选事实。</summary>
    internal sealed record DocumentDeclaration(
        PluginId OwnerId,
        DocumentDescriptor Descriptor,
        Type ModelType,
        Type ViewType,
        Func<Control> ViewFactory,
        bool IsPersistable);

    /// <summary>插件局部 Builder 中尚未提交的 Tool 候选事实。</summary>
    internal sealed record ToolDeclaration(
        PluginId OwnerId,
        ToolDescriptor Descriptor,
        Type ModelType,
        Type ViewType,
        Func<Control> ViewFactory);

    /// <summary>插件局部 Builder 中尚未提交的生命周期候选事实。</summary>
    internal sealed record LifecycleDeclaration(PluginId OwnerId, Type ImplementationType);

    /// <summary>插件局部 Builder 中尚未提交的 Workflow Action 候选事实。</summary>
    internal sealed record WorkflowActionDeclaration(
        PluginId OwnerId,
        WorkflowActionDescriptor Descriptor,
        Type HandlerType);

    /// <summary>插件局部 Builder 中尚未提交的 Document Command 候选事实。</summary>
    internal sealed record WorkbenchCommandDeclaration(
        PluginId OwnerId,
        CommandDescriptor Descriptor,
        DocumentTypeId TargetDocumentTypeId);

    /// <summary>插件局部 Builder 中尚未提交的菜单命令候选事实。</summary>
    internal sealed record MenuCommandContributionDeclaration(
        PluginId OwnerId,
        MenuCommandContributionDescriptor Descriptor);

    /// <summary>插件局部 Builder 中尚未提交的快捷键候选事实。</summary>
    internal sealed record KeyBindingContributionDeclaration(
        PluginId OwnerId,
        KeyBindingContributionDescriptor Descriptor);
}
