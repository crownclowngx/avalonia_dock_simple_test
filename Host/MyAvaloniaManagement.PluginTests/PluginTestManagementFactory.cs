using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 为 G7 前仍验证 layout/document v1 的 PluginTests 组合旧 Dock 文档创建 seam。
/// </summary>
/// <remarks>
/// 本类型只存在于测试程序集。生产 `ManagementFactory` 只接收 `IHostDockableFactory`，生产 DI 也只注册
/// `HostDockAdapterFactory`；因此 Legacy Document 不会重新进入 Host 运行路径。
/// </remarks>
internal static class PluginTestManagementFactory
{
    internal static ManagementFactory Create(
        PluginRegistry registry,
        DocumentScopeManager scopeManager)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scopeManager);
        var scopeRegistry = new DocumentScopeRegistry();
        scopeRegistry.Register(scopeManager);
        return new ManagementFactory(
            registry,
            new LegacyDockableFactory(registry, scopeManager),
            scopeRegistry);
    }

    /// <summary>只实现旧 Document 创建；G6 PluginTests 不通过该 seam 创建 Tool。</summary>
    private sealed class LegacyDockableFactory(
        PluginRegistry registry,
        DocumentScopeManager scopeManager) : IHostDockableFactory
    {
        public Document CreateDocument(DocumentTypeId documentTypeId, string title = "")
        {
            if (!registry.TryGetDocumentRegistration(documentTypeId, out var registration))
            {
                throw new NotSupportedException(
                    $"测试 Registry 不支持 Document：{documentTypeId.Value}。");
            }

            var document = scopeManager.CreateLegacyDocument(registration.ModelType);
            document.Title = string.IsNullOrWhiteSpace(title)
                ? registration.Descriptor.DisplayName
                : title;
            return document;
        }

        public Tool CreateTool(ToolTypeId toolTypeId) =>
            throw new NotSupportedException("G7 前旧持久化测试 seam 不创建 Tool。");
    }
}
