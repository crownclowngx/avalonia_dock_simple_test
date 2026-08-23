using System;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 只激活 HostWorkspaceCatalog 中已经冻结的 Host 内建模型。
/// </summary>
/// <remarks>
/// 本类型不持有通用服务容器，也不接受任意模型类型。目录项中的工厂由组合根按精确类型建立，
/// 因而这里既不能访问插件 Provider，也不能退化为通用服务定位器。Document 初始化采用 Host
/// 同步协议，插件的异步初始化仍由 PluginContributionActivator 路径负责。
/// </remarks>
internal sealed class HostWorkspaceActivator(HostWorkspaceCatalog catalog)
{
    private readonly HostWorkspaceCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    internal ActivatedWorkspaceDocument ActivateDocument(
        DocumentTypeId id,
        NewDocumentActivation activation)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(activation);
        if (!_catalog.TryGetDocument(id, out var registration))
        {
            throw new NotSupportedException($"不支持的 Host Document 类型：{id.Value}。");
        }

        var lease = registration.ModelFactory();
        try
        {
            registration.Initialize(lease.Model, activation, lease.ClosingToken);
            return new ActivatedWorkspaceDocument(registration, lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal ActivatedWorkspaceTool ActivateTool(ToolTypeId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_catalog.TryGetTool(id, out var registration))
        {
            throw new NotSupportedException($"不支持的 Host Tool 类型：{id.Value}。");
        }
        return new ActivatedWorkspaceTool(registration, registration.ModelFactory());
    }
}
