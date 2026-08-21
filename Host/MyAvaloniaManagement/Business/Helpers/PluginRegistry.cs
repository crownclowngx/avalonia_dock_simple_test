using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

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

    internal PluginRegistry(
        IReadOnlyList<PluginRegistryPlugin> plugins,
        IReadOnlyList<PluginDocumentRegistration> documents,
        IReadOnlyList<PluginToolRegistration> tools,
        IReadOnlyList<PluginLifecycleDeclaration> lifecycles)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(lifecycles);

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

    /// <summary>展开 Descriptor 中已经冻结的菜单入口，不执行模型或插件代码。</summary>
    internal IEnumerable<DocumentCreationMenuEntry> GetCreationEntries()
    {
        foreach (var registration in _documents.Values)
        {
            var descriptor = registration.Descriptor;
            if (descriptor.CreationIntents.Count == 0)
            {
                yield return new DocumentCreationMenuEntry(
                    descriptor.DocumentTypeId,
                    null,
                    descriptor.DisplayName,
                    descriptor.Description,
                    descriptor.IconPath,
                    descriptor.MenuCategory);
                continue;
            }

            foreach (var intent in descriptor.CreationIntents)
            {
                yield return new DocumentCreationMenuEntry(
                    descriptor.DocumentTypeId,
                    intent.IntentId,
                    intent.DisplayName,
                    string.IsNullOrWhiteSpace(intent.Description)
                        ? descriptor.Description
                        : intent.Description,
                    string.IsNullOrWhiteSpace(intent.IconPath)
                        ? descriptor.IconPath
                        : intent.IconPath,
                    descriptor.MenuCategory);
            }
        }
    }
}

/// <summary>Host 菜单使用的内部创建项；它不进入 Plugin SDK public API。</summary>
/// <remarks>该投影只含 Descriptor 数据，不携带模型、Provider 或执行回调。</remarks>
internal sealed record DocumentCreationMenuEntry(
    DocumentTypeId DocumentTypeId,
    CreationIntentId? CreationIntentId,
    string DisplayName,
    string Description,
    string IconPath,
    string MenuCategory);

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
    bool IsPersistable);

/// <summary>冻结一种 Tool 的所有者、描述符、singleton 模型类型和 View 工厂。</summary>
internal sealed record PluginToolRegistration(
    PluginId OwnerId,
    ToolDescriptor Descriptor,
    Type ModelType,
    Type ViewType,
    Func<Control> ViewFactory);

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
