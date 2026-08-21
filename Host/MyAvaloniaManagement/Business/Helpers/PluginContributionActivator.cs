using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 根据不可变 Registry 声明，在正确的 Host 或插件 Provider 中创建贡献模型。
/// </summary>
/// <remarks>
/// 本类型是唯一接触 Provider 的贡献创建边界。Registry 不保存 Provider，插件也得不到宿主
/// <see cref="IServiceProvider"/>。返回值只包含普通模型和所有权租约，不引用 Dock；实际投影由
/// HostDockAdapterFactory 完成。
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

    internal ActivatedPluginDocument ActivateDocument(DocumentTypeId documentTypeId)
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        if (!_registry.TryGetDocumentRegistration(documentTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Document 类型：{documentTypeId.Value}。");
        }

        var manager = registration.OwnerId == HostExtensionIds.V2Owner
            ? _hostProvider.GetRequiredService<DocumentScopeManager>()
            : _pluginProviders.GetDocumentScopeManager(registration.OwnerId);
        var document = manager.CreatePluginDocument(registration.ModelType);
        return new ActivatedPluginDocument(registration, document, manager);
    }

    internal ActivatedPluginTool ActivateTool(ToolTypeId toolTypeId)
    {
        ArgumentNullException.ThrowIfNull(toolTypeId);
        if (!_registry.TryGetToolRegistration(toolTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Tool 类型：{toolTypeId.Value}。");
        }

        var instance = registration.OwnerId == HostExtensionIds.V2Owner
            ? _hostProvider.GetRequiredService(registration.ModelType)
            : _pluginProviders.GetRequiredService(registration.OwnerId, registration.ModelType);
        return new ActivatedPluginTool(registration, instance);
    }
}

/// <summary>保存一次普通 Document 模型激活及其唯一 Scope 释放权。</summary>
internal sealed class ActivatedPluginDocument(
    PluginDocumentRegistration registration,
    IPluginDocument model,
    DocumentScopeManager scopeManager) : IDisposable
{
    private int _disposed;

    internal PluginDocumentRegistration Registration { get; } = registration;
    internal IPluginDocument Model { get; } = model;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            scopeManager.Release(Model);
        }
    }
}

/// <summary>保存 Tool singleton 模型及其已冻结注册事实；模型释放权仍属于 Provider。</summary>
internal sealed record ActivatedPluginTool(
    PluginToolRegistration Registration,
    object Model);
