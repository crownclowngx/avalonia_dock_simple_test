using System;
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
    private bool _sealed;

    internal PluginRegistrationContext(
        PluginId pluginId,
        IServiceCollection services,
        PluginRegistryBuilder builder)
    {
        PluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
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
    /// 结束模块唯一的写入窗口。
    /// </summary>
    /// <remarks>
    /// G4 以后 <see cref="Services"/> 只属于当前插件。插件直接登记贡献接口不会使其进入宿主 Registry，
    /// 删除或替换描述符也只能影响自己的对象图，因此不再扫描描述符或维护旁路黑名单。
    /// </remarks>
    internal void Seal()
    {
        _sealed = true;
    }

    private void EnsureWritable()
    {
        if (_sealed)
        {
            throw new InvalidOperationException("插件注册上下文已经封闭，不能在模块返回后追加贡献。");
        }
    }
}
