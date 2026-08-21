using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 把一个已经验证的 manifest 身份绑定到一次声明式贡献注册窗口。
/// </summary>
/// <remarks>
/// 本类型只负责把 public 注册调用翻译为插件私有服务注册和普通内存声明。它不执行模型构造、
/// 不读取运行期元数据，也不决定跨插件冲突。模块返回后由宿主立即封闭该窗口，避免插件保存
/// <see cref="IPluginRegistration"/> 并在运行期改变已经发布的 Registry。
/// </remarks>
internal sealed class PluginRegistration : IPluginRegistration
{
    private readonly PluginRegistryBuilder _builder;
    private readonly SealableServiceCollection _services;
    private bool _sealed;

    internal PluginRegistration(
        PluginId pluginId,
        IServiceCollection services,
        PluginRegistryBuilder builder)
    {
        PluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
        _services = new SealableServiceCollection(
            services ?? throw new ArgumentNullException(nameof(services)));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    public PluginId PluginId { get; }

    public IServiceCollection Services => _services;

    public void UseLifecycle<TLifecycle>()
        where TLifecycle : class, IPluginLifecycle
    {
        EnsureWritable();
        Services.AddSingleton<TLifecycle>();
        _builder.AddLifecycle(PluginId, typeof(TLifecycle));
    }

    public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPluginDocument
        where TView : Control, new()
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(descriptor);
        Services.AddScoped<TDocument>();
        _builder.AddDocument(
            PluginId,
            descriptor,
            typeof(TDocument),
            typeof(TView),
            static () => new TView(),
            isPersistable: false);
    }

    public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPersistablePluginDocument
        where TView : Control, new()
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(descriptor);
        Services.AddScoped<TDocument>();
        _builder.AddDocument(
            PluginId,
            descriptor,
            typeof(TDocument),
            typeof(TView),
            static () => new TView(),
            isPersistable: true);
    }

    public void AddTool<TTool, TView>(ToolDescriptor descriptor)
        where TTool : class
        where TView : Control, new()
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(descriptor);
        Services.AddSingleton<TTool>();
        _builder.AddTool(
            PluginId,
            descriptor,
            typeof(TTool),
            typeof(TView),
            static () => new TView());
    }

    /// <summary>
    /// 结束模块唯一的写入窗口，并在构建 Provider 前完成插件内结构校验。
    /// </summary>
    /// <remarks>
    /// 校验发生在局部 Builder 上。任何错误都会使调用方丢弃整个插件候选，因此不会出现一个插件
    /// 只发布部分 Document、Tool 或 View 的状态。
    /// </remarks>
    internal void Seal()
    {
        EnsureWritable();
        _sealed = true;
        _services.Seal();
        _builder.ValidateSingleOwner();
    }

    private void EnsureWritable()
    {
        if (_sealed)
        {
            throw new InvalidOperationException("插件注册入口已经封闭，不能在模块返回后追加贡献。");
        }
    }
}
