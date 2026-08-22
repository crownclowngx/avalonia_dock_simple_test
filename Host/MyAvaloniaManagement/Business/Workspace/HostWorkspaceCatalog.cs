using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 保存 Host 内建 Welcome 与 Tool 的不可变工作区目录。
/// </summary>
/// <remarks>
/// 本目录不模拟 manifest、插件 Provider 或插件可用性。模型工厂由组合根按精确类型提供，目录
/// 本身不接收通用服务容器，也不能解析任意服务。构造函数会防御性复制全部集合，发布后
/// 调用方只能读取固定事实。
/// </remarks>
internal sealed class HostWorkspaceCatalog
{
    private readonly IReadOnlyDictionary<DocumentTypeId, HostWorkspaceDocumentRegistration> _documents;
    private readonly IReadOnlyDictionary<ToolTypeId, HostWorkspaceToolRegistration> _tools;
    private readonly IReadOnlyDictionary<Type, HostWorkspaceViewRegistration> _views;

    internal HostWorkspaceCatalog(
        IReadOnlyList<HostWorkspaceDocumentRegistration> documents,
        IReadOnlyList<HostWorkspaceToolRegistration> tools)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(tools);
        _documents = documents.ToDictionary(item => item.Descriptor.DocumentTypeId);
        _tools = tools.ToDictionary(item => item.Descriptor.ToolTypeId);
        _views = documents.Select(item => new HostWorkspaceViewRegistration(
                item.ModelType, item.ViewType, item.ViewFactory))
            .Concat(tools.Select(item => new HostWorkspaceViewRegistration(
                item.ModelType, item.ViewType, item.ViewFactory)))
            .ToDictionary(item => item.ModelType);
    }

    internal IReadOnlyCollection<HostWorkspaceDocumentRegistration> Documents =>
        _documents.Values.ToArray();

    internal IReadOnlyCollection<HostWorkspaceToolRegistration> Tools =>
        _tools.Values.ToArray();

    internal bool TryGetDocument(
        DocumentTypeId id,
        out HostWorkspaceDocumentRegistration registration) =>
        _documents.TryGetValue(id, out registration!);

    internal bool TryGetTool(
        ToolTypeId id,
        out HostWorkspaceToolRegistration registration) =>
        _tools.TryGetValue(id, out registration!);

    internal bool TryGetView(Type modelType, out HostWorkspaceViewRegistration registration) =>
        _views.TryGetValue(modelType, out registration!);
}
