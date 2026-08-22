using System;
using System.Threading.Tasks;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>WorkspaceSession 创建 Dock Adapter 所依赖的最小内部端口。</summary>
internal interface IHostDockableFactory
{
    /// <summary>同步创建 Host 内建 Document；默认布局不会阻塞等待插件异步代码。</summary>
    Document CreateHostDocument(
        DocumentTypeId documentTypeId,
        NewDocumentActivation activation);

    ValueTask<Document> CreateDocumentAsync(
        DocumentTypeId documentTypeId,
        DocumentActivation activation);
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
    WorkspaceCatalog catalog,
    HostWorkspaceActivator hostActivator,
    PluginContributionActivator pluginActivator,
    ViewLocator viewLocator) : IHostDockableFactory
{
    private readonly WorkspaceCatalog _catalog = catalog ??
        throw new ArgumentNullException(nameof(catalog));
    private readonly HostWorkspaceActivator _hostActivator = hostActivator ??
        throw new ArgumentNullException(nameof(hostActivator));
    private readonly PluginContributionActivator _pluginActivator = pluginActivator ??
        throw new ArgumentNullException(nameof(pluginActivator));
    private readonly ViewLocator _viewLocator = viewLocator ??
        throw new ArgumentNullException(nameof(viewLocator));

    public Document CreateHostDocument(
        DocumentTypeId documentTypeId,
        NewDocumentActivation activation)
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        ArgumentNullException.ThrowIfNull(activation);
        if (!_catalog.TryGetHostDocument(documentTypeId, out _))
        {
            throw new NotSupportedException($"不支持的 Host Document 类型：{documentTypeId.Value}。");
        }
        return CreateAdapter(
            _hostActivator.ActivateDocument(documentTypeId, activation),
            activation.Title);
    }

    public async ValueTask<Document> CreateDocumentAsync(
        DocumentTypeId documentTypeId,
        DocumentActivation activation)
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        ArgumentNullException.ThrowIfNull(activation);
        if (_catalog.TryGetHostDocument(documentTypeId, out _))
        {
            if (activation is not NewDocumentActivation hostActivation)
            {
                throw new NotSupportedException("Host 内建 Document 只支持新建激活。");
            }
            return CreateHostDocument(documentTypeId, hostActivation);
        }
        if (!_catalog.TryGetAvailablePluginDocument(documentTypeId, out _))
        {
            throw new NotSupportedException($"不支持的 Document 类型：{documentTypeId.Value}。");
        }
        var activatedDocument = _pluginActivator.ActivateDocument(documentTypeId);
        ManagedDocumentDockable? adapter = null;
        try
        {
            // 初始化发生在 Adapter、View 和 Dock 发布之前。插件只观察 Scope 的关闭令牌，
            // 任意失败都会由下方唯一回滚入口结束同一个 Scope。
            await activatedDocument.Model.InitializeAsync(
                activation,
                activatedDocument.ClosingToken);
            adapter = new ManagedDocumentDockable(activatedDocument, activation.Title);
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
                activatedDocument.Dispose();
            }
            throw;
        }
    }

    public Tool CreateTool(ToolTypeId toolTypeId)
    {
        ArgumentNullException.ThrowIfNull(toolTypeId);
        var activated = _catalog.TryGetHostTool(toolTypeId, out _)
            ? _hostActivator.ActivateTool(toolTypeId)
            : _catalog.TryGetAvailablePluginTool(toolTypeId, out _)
                ? _pluginActivator.ActivateTool(toolTypeId)
                : throw new NotSupportedException($"不支持的 Tool 类型：{toolTypeId.Value}。");
        var adapter = new ManagedToolDockable(activated);
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

    private ManagedDocumentDockable CreateAdapter(
        ActivatedWorkspaceDocument activatedDocument,
        string title)
    {
        ManagedDocumentDockable? adapter = null;
        try
        {
            adapter = new ManagedDocumentDockable(activatedDocument, title);
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
                activatedDocument.Dispose();
            }
            throw;
        }
    }
}
