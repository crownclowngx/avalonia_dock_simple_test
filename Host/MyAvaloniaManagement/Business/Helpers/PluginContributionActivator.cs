using System;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 根据不可变 Registry 声明，在正确的 Host 或插件 Provider 中创建贡献模型。
/// </summary>
/// <remarks>
/// 本类型是 G5 唯一接触 Provider 的贡献创建边界。Registry 不保存 Provider，插件也得不到宿主
/// <see cref="IServiceProvider"/>。G5 暂时要求创建结果仍是 Dock Document/Tool；G6 将只替换这里的
/// Dock 投影为 Adapter，而不改变声明、冲突判断或 Provider 所有权。
/// </remarks>
internal sealed class PluginContributionActivator(
    IServiceProvider hostProvider,
    PluginRegistry registry,
    PluginProviderOwner pluginProviders)
{
    private readonly IServiceProvider _hostProvider =
        hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
    private readonly PluginRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly PluginProviderOwner _pluginProviders =
        pluginProviders ?? throw new ArgumentNullException(nameof(pluginProviders));

    internal Document CreateDocument(
        DocumentTypeId documentTypeId,
        string title = "")
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        if (!_registry.TryGetDocumentRegistration(documentTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Document 类型：{documentTypeId.Value}。");
        }

        var manager = registration.OwnerId == HostExtensionIds.V2Owner
            ? _hostProvider.GetRequiredService<DocumentScopeManager>()
            : _pluginProviders.GetDocumentScopeManager(registration.OwnerId);
        var document = manager.CreateDocument(registration.ModelType);
        document.Title = string.IsNullOrEmpty(title)
            ? registration.Descriptor.DisplayName
            : title;
        return document;
    }

    internal Tool CreateTool(ToolTypeId toolTypeId)
    {
        ArgumentNullException.ThrowIfNull(toolTypeId);
        if (!_registry.TryGetToolRegistration(toolTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Tool 类型：{toolTypeId.Value}。");
        }

        var instance = registration.OwnerId == HostExtensionIds.V2Owner
            ? _hostProvider.GetRequiredService(registration.ModelType)
            : _pluginProviders.GetRequiredService(registration.OwnerId, registration.ModelType);
        if (instance is not Tool tool)
        {
            throw new InvalidOperationException(
                $"G5 过渡模型 {registration.ModelType.FullName} 尚不是 Dock Tool；请由 G6 Adapter 承载。");
        }

        tool.Id = registration.Descriptor.ToolTypeId.Value;
        tool.Title = registration.Descriptor.DisplayName;
        tool.CanClose = registration.Descriptor.CloseBehavior == ToolCloseBehavior.Hide;
        return tool;
    }
}
