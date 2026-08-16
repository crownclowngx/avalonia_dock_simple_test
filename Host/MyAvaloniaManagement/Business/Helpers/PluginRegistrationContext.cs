using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 把一个已经通过清单预检的插件绑定到当前 Registry Builder。
/// </summary>
/// <remarks>
/// 本类型是组合根的短生命周期写入端：模块返回后立即封闭。所有权在构造时由宿主注入，
/// 因此任何贡献类型即使位于插件的私有辅助程序集，也不会依赖程序集名称猜测身份。
/// </remarks>
internal sealed class PluginRegistrationContext : IPluginRegistrationContext
{
    private readonly PluginRegistryBuilder _builder;
    private readonly int _initialServiceCount;
    private bool _sealed;

    internal PluginRegistrationContext(
        PluginId pluginId,
        IServiceCollection services,
        PluginRegistryBuilder builder)
    {
        PluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _initialServiceCount = services.Count;
    }

    public PluginId PluginId { get; }

    public IServiceCollection Services { get; }

    public void AddDocument<TStrategy>()
        where TStrategy : class, IDocumentCreationStrategy
    {
        EnsureWritable();
        Services.AddSingleton<TStrategy>();
        _builder.AddDocument(PluginId, typeof(TStrategy));
    }

    public void AddTool<TStrategy>()
        where TStrategy : class, IToolCreationStrategy
    {
        EnsureWritable();
        Services.AddSingleton<TStrategy>();
        _builder.AddTool(PluginId, typeof(TStrategy));
    }

    public void AddView<TViewModel, TView>() where TView : Control, new()
    {
        EnsureWritable();
        _builder.AddView(
            PluginId,
            typeof(TViewModel),
            typeof(TView),
            static () => new TView());
    }

    public void AddLifecycle<TLifecycle>()
        where TLifecycle : class, IPluginLifecycle
    {
        EnsureWritable();
        Services.AddSingleton<TLifecycle>();
        _builder.AddLifecycle(PluginId, typeof(TLifecycle));
    }

    /// <summary>
    /// 结束模块的唯一写入窗口，并检查其是否绕过显式贡献 API。
    /// </summary>
    /// <remarks>
    /// G5 只保护四类贡献入口；任意宿主服务替换属于 G6。检查服务描述符增量而不是整个集合，
    /// 可以把错误准确归因到当前插件，同时不把宿主自己的注册误判为违规。
    /// </remarks>
    internal IReadOnlyList<Type> SealAndGetBypassedContributionTypes()
    {
        _sealed = true;
        var result = new List<Type>();
        for (var index = _initialServiceCount; index < Services.Count; index++)
        {
            var serviceType = Services[index].ServiceType;
            if (serviceType == typeof(IDocumentCreationStrategy) ||
                serviceType == typeof(IToolCreationStrategy) ||
                serviceType == typeof(IPluginLifecycle))
            {
                result.Add(serviceType);
            }
        }

        return result;
    }

    private void EnsureWritable()
    {
        if (_sealed)
        {
            throw new InvalidOperationException("插件注册上下文已经封闭，不能在模块返回后追加贡献。");
        }
    }
}
