using System;
using System.Threading;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 根据不可变 Registry 声明，在所属插件 Provider 中创建贡献模型。
/// </summary>
/// <remarks>
/// 本类型是唯一接触插件 Provider 的贡献创建边界。Registry 不保存 Provider，插件也得不到宿主
///容器。返回值只包含普通模型和所有权租约，不引用 Dock；实际投影由 HostDockAdapterFactory 完成。
/// </remarks>
internal sealed class PluginContributionActivator(
    PluginRegistry registry,
    PluginProviderOwner pluginProviders,
    PluginAvailabilityReadModel? availability = null)
{
    private readonly PluginRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly PluginProviderOwner _pluginProviders =
        pluginProviders ?? throw new ArgumentNullException(nameof(pluginProviders));
    private readonly PluginAvailabilityReadModel _availability =
        availability ?? new PluginAvailabilityReadModel(
            new PluginLifecycleStateStore(
                registry ?? throw new ArgumentNullException(nameof(registry))));

    internal ActivatedWorkspaceDocument ActivateDocument(DocumentTypeId documentTypeId)
    {
        ArgumentNullException.ThrowIfNull(documentTypeId);
        if (!_registry.TryGetDocumentRegistration(documentTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Document 类型：{documentTypeId.Value}。");
        }
        EnsureAvailable(registration.OwnerId);

        var manager = _pluginProviders.GetDocumentScopeManager(registration.OwnerId);
        var lease = manager.CreateDocument(registration.ModelType);
        return new ActivatedWorkspaceDocument(registration, lease);
    }

    internal ActivatedWorkspaceTool ActivateTool(ToolTypeId toolTypeId)
    {
        ArgumentNullException.ThrowIfNull(toolTypeId);
        if (!_registry.TryGetToolRegistration(toolTypeId, out var registration))
        {
            throw new NotSupportedException($"不支持的 Tool 类型：{toolTypeId.Value}。");
        }
        EnsureAvailable(registration.OwnerId);

        var instance = _pluginProviders.GetRequiredService(
            registration.OwnerId,
            registration.ModelType);
        return new ActivatedWorkspaceTool(registration, instance);
    }

    private void EnsureAvailable(PluginId pluginId)
    {
        if (!_availability.IsAvailable(pluginId))
        {
            throw new InvalidOperationException(
                $"插件 {pluginId.Value} 当前不可用，不能激活其贡献。");
        }
    }
}

/// <summary>保存一次普通 Document 模型激活及其唯一 Scope 释放权。</summary>
internal sealed class ActivatedWorkspaceDocument(
    IWorkspaceDocumentRegistration registration,
    ManagedDocumentScopeLease scopeLease) : IDisposable
{
    private int _disposed;

    internal IWorkspaceDocumentRegistration Registration { get; } = registration;
    internal IPluginDocument Model => scopeLease.Model;
    internal CancellationToken ClosingToken => scopeLease.ClosingToken;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            scopeLease.Dispose();
        }
    }
}

/// <summary>保存 Tool singleton 模型及其已冻结注册事实；模型释放权仍属于 Provider。</summary>
internal sealed record ActivatedWorkspaceTool(
    IWorkspaceToolRegistration Registration,
    object Model);
