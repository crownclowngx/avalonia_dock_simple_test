using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 测试组合根使用的最小注册上下文。它保留真实服务注册并记录显式贡献类型，
/// 但不依赖生产 HostRuntime 或磁盘 manifest。
/// </summary>
internal sealed class TestPluginRegistrationContext(
    PluginId pluginId,
    IServiceCollection services) : IPluginRegistrationContext
{
    public PluginId PluginId { get; } = pluginId;

    public IServiceCollection Services { get; } = services;

    internal List<(string Kind, Type First, Type? Second)> Contributions { get; } = [];

    public void AddDocument<TStrategy>() where TStrategy : class, IDocumentCreationStrategy =>
        Contributions.Add(("Document", typeof(TStrategy), null));

    public void AddTool<TStrategy>() where TStrategy : class, IToolCreationStrategy =>
        Contributions.Add(("Tool", typeof(TStrategy), null));

    public void AddView<TViewModel, TView>() where TView : Control, new() =>
        Contributions.Add(("View", typeof(TViewModel), typeof(TView)));

    public void AddLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle =>
        Contributions.Add(("Lifecycle", typeof(TLifecycle), null));
}
