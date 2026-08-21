using System;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>ManagementFactory 创建 Dock 项所依赖的最小内部端口。</summary>
internal interface IHostDockableFactory
{
    Document CreateDocument(DocumentTypeId documentTypeId, string title = "");
    Tool CreateTool(ToolTypeId toolTypeId);
}

/// <summary>
/// 把普通贡献模型、Dock Adapter 与统一 View Locator 组合为可发布 Dock 项。
/// </summary>
/// <remarks>
/// 模型解析仍由 <see cref="PluginContributionActivator"/> 负责；本 Factory 只完成适配和 View 预构建。
/// 这条边界保证 View 构造失败发生在 Dock 发布之前，Document Scope 可以原子回滚。
/// </remarks>
internal sealed class HostDockAdapterFactory(
    PluginContributionActivator activator,
    ViewLocator viewLocator) : IHostDockableFactory
{
    private readonly PluginContributionActivator _activator = activator ??
        throw new ArgumentNullException(nameof(activator));
    private readonly ViewLocator _viewLocator = viewLocator ??
        throw new ArgumentNullException(nameof(viewLocator));

    public Document CreateDocument(DocumentTypeId documentTypeId, string title = "")
    {
        var activation = _activator.ActivateDocument(documentTypeId);
        ManagedDocumentDockable? adapter = null;
        try
        {
            adapter = new ManagedDocumentDockable(activation, title);
            _viewLocator.Prepare(adapter);
            return adapter;
        }
        catch
        {
            if (adapter is not null)
            {
                adapter.Dispose();
            }
            else
            {
                activation.Dispose();
            }
            throw;
        }
    }

    public Tool CreateTool(ToolTypeId toolTypeId)
    {
        var adapter = new ManagedToolDockable(_activator.ActivateTool(toolTypeId));
        try
        {
            _viewLocator.Prepare(adapter);
            return adapter;
        }
        catch
        {
            // Tool 模型归插件 Provider；失败 Adapter 只释放自己已经取得的 View，不越权释放 singleton。
            adapter.Dispose();
            throw;
        }
    }
}
