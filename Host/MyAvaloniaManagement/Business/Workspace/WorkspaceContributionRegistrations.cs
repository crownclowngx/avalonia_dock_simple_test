using System;
using System.Threading;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// Workspace 创建和展示 Document 所需的最小只读注册事实。
/// </summary>
/// <remarks>
/// Host 与插件分别使用不同的具体注册记录实现本接口，因而不需要用可空 PluginId、布尔标记
/// 或伪插件身份表达所有权。接口只统一 Workspace 真正共同需要的数据，不提供模型创建或
/// Provider 访问能力。
/// </remarks>
internal interface IWorkspaceDocumentRegistration
{
    DocumentDescriptor Descriptor { get; }
    Type ModelType { get; }
    Type ViewType { get; }
    Func<Control> ViewFactory { get; }
    bool IsPersistable { get; }
}

/// <summary>Workspace 创建和展示 Tool 所需的最小只读注册事实。</summary>
internal interface IWorkspaceToolRegistration
{
    ToolDescriptor Descriptor { get; }
    Type ModelType { get; }
    Type ViewType { get; }
    Func<Control> ViewFactory { get; }
}

/// <summary>
/// ViewLocator 精确匹配模型与 View 所需的共同只读事实。
/// </summary>
/// <remarks>
/// 诊断需要插件身份时由调用方对具体的 <see cref="PluginWorkspaceViewRegistration"/>
/// 做类型匹配；Host View 从类型层面不存在 PluginId。
/// </remarks>
internal interface IWorkspaceViewRegistration
{
    Type ModelType { get; }
    Type ViewType { get; }
    Func<Control> Factory { get; }
}

/// <summary>Host 内建 Document 的描述、精确 View 与同步初始化工厂。</summary>
internal sealed record HostWorkspaceDocumentRegistration(
    DocumentDescriptor Descriptor,
    Type ModelType,
    Type ViewType,
    Func<Control> ViewFactory,
    Func<ManagedDocumentScopeLease> ModelFactory,
    Action<IPluginDocument, NewDocumentActivation, CancellationToken> Initialize)
    : IWorkspaceDocumentRegistration
{
    /// <summary>Host Welcome 不进入插件内容持久化协议。</summary>
    public bool IsPersistable => false;
}

/// <summary>Host 内建 Tool 的描述、精确 View 与 singleton 模型工厂。</summary>
internal sealed record HostWorkspaceToolRegistration(
    ToolDescriptor Descriptor,
    Type ModelType,
    Type ViewType,
    Func<Control> ViewFactory,
    Func<object> ModelFactory) : IWorkspaceToolRegistration;

/// <summary>Host View 的精确映射；该记录没有插件所有权字段。</summary>
internal sealed record HostWorkspaceViewRegistration(
    Type ModelType,
    Type ViewType,
    Func<Control> Factory) : IWorkspaceViewRegistration;

/// <summary>插件 View 的精确映射，同时保留真实 manifest 插件身份供诊断使用。</summary>
internal sealed record PluginWorkspaceViewRegistration(
    PluginId OwnerId,
    Type ModelType,
    Type ViewType,
    Func<Control> Factory) : IWorkspaceViewRegistration;

/// <summary>Host 菜单使用的内部创建项；它不进入 Plugin SDK public API。</summary>
/// <remarks>该投影只含 Descriptor 数据，不携带模型、Provider 或执行回调。</remarks>
internal sealed record DocumentCreationMenuEntry(
    DocumentTypeId DocumentTypeId,
    CreationIntentId? CreationIntentId,
    string DisplayName,
    string Description,
    string IconPath,
    string MenuCategory);
