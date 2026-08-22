using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 为只验证 layout-v2 的 PluginTests 提供最小 Dock 文档创建 seam。
/// </summary>
/// <remarks>
/// 本类型只存在于测试程序集，不解析插件模型，也不模拟 Document V2 生命周期。G7 的 Document
/// 所有权链由专用 Unit/UI 测试覆盖；这里返回空 Dock 文档只是为了隔离验证本阶段明确不修改的
/// layout-v2 几何行为。
/// </remarks>
internal static class PluginTestManagementFactory
{
    internal static ManagementFactory Create(
        PluginRegistry registry,
        DocumentScopeManager scopeManager,
        PluginAvailabilityReadModel? availability = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scopeManager);
        var scopeRegistry = new DocumentScopeRegistry();
        scopeRegistry.Register(scopeManager);
        return new ManagementFactory(
            registry,
            new LayoutOnlyDockableFactory(),
            scopeRegistry,
            availability: availability);
    }

    /// <summary>只为布局测试创建无业务模型的 Dock 文档。</summary>
    private sealed class LayoutOnlyDockableFactory : IHostDockableFactory
    {
        public ValueTask<Document> CreateDocumentAsync(
            DocumentTypeId documentTypeId,
            DocumentActivation context)
        {
            ArgumentNullException.ThrowIfNull(documentTypeId);
            ArgumentNullException.ThrowIfNull(context);
            return ValueTask.FromResult<Document>(new Document { Title = context.Title });
        }

        public Tool CreateTool(ToolTypeId toolTypeId) =>
            throw new NotSupportedException("布局测试 seam 不负责创建插件 Tool。");
    }
}
