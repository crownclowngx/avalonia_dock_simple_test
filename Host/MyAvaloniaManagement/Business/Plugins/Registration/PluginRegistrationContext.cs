using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 把一个已经验证的 manifest 身份绑定到一次声明式贡献注册窗口。
/// </summary>
/// <remarks>
/// 本类型只负责管理 public 声明窗口：私有 DI 描述符进入插件集合，贡献根描述符则暂存在 Host
/// 拥有的列表。它不执行模型构造、不读取运行期元数据，也不决定跨插件冲突。模块返回后由宿主
/// 立即封闭窗口，避免插件保存 <see cref="IPluginRegistration"/> 并在运行期改变对象图或 Registry。
/// </remarks>
internal sealed class PluginRegistration : IPluginRegistration
{
    private readonly PluginRegistryBuilder _builder;
    private readonly SealableServiceCollection _services;
    private readonly List<ServiceDescriptor> _hostOwnedServiceDescriptors = [];
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
        // 生命周期根由 Host 持有。这里只保存最终要提交的描述符，不把它放入插件可写集合，
        // 从结构上消除模块随后 Remove、Replace 或重复登记而改变协议生命周期的机会。
        _hostOwnedServiceDescriptors.Add(
            ServiceDescriptor.Singleton(typeof(TLifecycle), typeof(TLifecycle)));
        _builder.AddLifecycle(PluginId, typeof(TLifecycle));
    }

    public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPluginDocument
        where TView : Control, new()
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(descriptor);
        _hostOwnedServiceDescriptors.Add(
            ServiceDescriptor.Scoped(typeof(TDocument), typeof(TDocument)));
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
        _hostOwnedServiceDescriptors.Add(
            ServiceDescriptor.Scoped(typeof(TDocument), typeof(TDocument)));
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
        _hostOwnedServiceDescriptors.Add(
            ServiceDescriptor.Singleton(typeof(TTool), typeof(TTool)));
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
        _builder.ValidateSingleOwner(PluginId);
    }

    /// <summary>取得 Seal 后由 Host 最终提交的贡献根服务描述符。</summary>
    /// <remarks>
    /// 返回值是防御性复制。调用方只能把这些描述符追加到 Host 拥有的原始集合，插件保存的
    /// <see cref="Services"/> 包装器已经封闭，因而不能取得或移除这里的任何一项。
    /// </remarks>
    internal IReadOnlyList<ServiceDescriptor> GetHostOwnedServiceDescriptors()
    {
        EnsureSealed();
        return _hostOwnedServiceDescriptors.ToArray();
    }

    /// <summary>取得声明式 Document、Tool 与 Lifecycle 的具体根类型。</summary>
    /// <remarks>
    /// Commit Guard 用此集合识别插件是否绕过专用 API 手工登记同一个根类型。比较的是精确
    /// ServiceType；插件私有接口、多实现和开放泛型不会被误判为宿主可见贡献。
    /// </remarks>
    internal IReadOnlySet<Type> GetContributionRootTypes()
    {
        EnsureSealed();
        return _hostOwnedServiceDescriptors
            .Select(descriptor => descriptor.ServiceType)
            .ToHashSet();
    }

    private void EnsureWritable()
    {
        if (_sealed)
        {
            throw new InvalidOperationException("插件注册入口已经封闭，不能在模块返回后追加贡献。");
        }
    }

    private void EnsureSealed()
    {
        if (!_sealed)
        {
            throw new InvalidOperationException("插件注册入口尚未封闭，不能提交 Host 拥有的服务。");
        }
    }
}
