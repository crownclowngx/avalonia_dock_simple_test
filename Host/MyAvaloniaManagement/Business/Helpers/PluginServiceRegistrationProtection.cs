using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 保存一次宿主组合阶段中不得由插件追加实现的服务类型集合。
/// </summary>
/// <remarks>
/// <para>
/// 设计意图：保护规则以“插件开始运行前，宿主已经拥有哪些服务”为事实来源，而不是维护一份
/// 容易随重构漂移的核心服务名单。因此后续新增宿主服务会自动受到保护，校验算法不需要修改。
/// </para>
/// <para>
/// 本策略只保护可信进程内插件的组合边界，不是安全沙箱。插件代码已经在宿主进程中执行，仍可
/// 使用反射、线程或原生代码；本策略承诺的是错误的 DI 注册不会悄悄改变宿主对象图。
/// </para>
/// </remarks>
internal sealed class HostServiceDescriptorPolicy
{
    private readonly HashSet<Type> _protectedServiceTypes;

    private HostServiceDescriptorPolicy(HashSet<Type> protectedServiceTypes)
    {
        _protectedServiceTypes = protectedServiceTypes;
    }

    /// <summary>从完整宿主注册集合建立本次启动不可变的保护基线。</summary>
    internal static HostServiceDescriptorPolicy Capture(IServiceCollection hostServices)
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        var protectedTypes = hostServices
            .Select(descriptor => descriptor.ServiceType)
            .ToHashSet();

        // 这些类型由默认容器隐式提供，通常不会出现在 IServiceCollection 中。显式保留它们可避免
        // 插件利用“基线中没有描述符”这一实现细节改变 Provider、Scope 或 keyed-service 语义。
        protectedTypes.UnionWith([
            typeof(IServiceProvider),
            typeof(IServiceScopeFactory),
            typeof(IServiceProviderIsService),
            typeof(IServiceProviderIsKeyedService),
            typeof(IKeyedServiceProvider),
        ]);

        // 三类宿主可见贡献必须经过 G5 Context API。把它们同时列入 G6 保留类型属于纵深校验；
        // 正常情况下 PluginRegistrationContext 会先以更准确的旁路错误码拒绝这些注册。
        protectedTypes.UnionWith([
            typeof(IDocumentCreationStrategy),
            typeof(IToolCreationStrategy),
            typeof(IPluginLifecycle),
        ]);

        return new HostServiceDescriptorPolicy(protectedTypes);
    }

    internal bool IsProtected(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _protectedServiceTypes.Contains(serviceType);
    }
}

/// <summary>描述插件服务注册违反宿主保护规则的确定性类别。</summary>
internal enum PluginServiceRegistrationViolationKind
{
    ExistingDescriptorChanged,
    ProtectedServiceAdded,
}

/// <summary>供组合编排和结构化诊断共同使用的最小违规事实。</summary>
internal sealed record PluginServiceRegistrationViolation(
    PluginServiceRegistrationViolationKind Kind,
    ServiceDescriptor Descriptor);

/// <summary>
/// 为单个插件提供一次“复制、校验、提交”的服务注册事务。
/// </summary>
/// <remarks>
/// <para>
/// Microsoft DI 的 <see cref="IServiceCollection"/> 本身没有事务能力。直接把正式集合交给插件，
/// 即使随后发现异常，也无法可靠区分并撤销插件已经做过的删除、替换和插入。本类型因此把当前
/// 集合复制到短生命周期工作副本，插件只接触该副本；验证成功后再提交尾部新增描述符。
/// </para>
/// <para>
/// 既有描述符按引用和顺序比较，而不是比较类型、实现和生命周期的表面值。工厂委托或实例可能
/// 具有相同显示信息却代表不同对象图，只有原描述符引用仍位于原位置才能证明宿主注册未被替换。
/// </para>
/// </remarks>
internal sealed class PluginServiceRegistrationTransaction
{
    private readonly IServiceCollection _target;
    private readonly ServiceDescriptor[] _baseline;
    private readonly IServiceCollection _workingServices = new ServiceCollection();
    private bool _completed;

    internal PluginServiceRegistrationTransaction(IServiceCollection target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _baseline = target.ToArray();
        foreach (var descriptor in _baseline)
        {
            _workingServices.Add(descriptor);
        }
    }

    /// <summary>
    /// 获取只属于当前插件配置调用的工作副本。模块返回后保存并修改该引用不会影响正式集合。
    /// </summary>
    internal IServiceCollection Services => _workingServices;

    /// <summary>
    /// 验证工作副本并在成功时只提交新增描述符；失败时正式集合保持调用前状态。
    /// </summary>
    internal bool TryCommit(
        HostServiceDescriptorPolicy policy,
        out PluginServiceRegistrationViolation? violation)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_completed)
        {
            throw new InvalidOperationException("插件服务注册事务已经结束，不能重复提交。");
        }

        violation = FindExistingDescriptorChange();
        if (violation is null)
        {
            violation = _workingServices
                .Skip(_baseline.Length)
                .Where(descriptor => policy.IsProtected(descriptor.ServiceType))
                .Select(descriptor => new PluginServiceRegistrationViolation(
                    PluginServiceRegistrationViolationKind.ProtectedServiceAdded,
                    descriptor))
                .FirstOrDefault();
        }

        _completed = true;
        if (violation is not null)
        {
            return false;
        }

        // 提交时不复制整个集合，也不执行 Replace。只追加已经验证的增量，既保留 Microsoft DI
        // 对多实现注册的顺序语义，也确保插件不能通过工作副本取得正式集合的后续写入能力。
        foreach (var descriptor in _workingServices.Skip(_baseline.Length))
        {
            _target.Add(descriptor);
        }

        return true;
    }

    private PluginServiceRegistrationViolation? FindExistingDescriptorChange()
    {
        if (_workingServices.Count < _baseline.Length)
        {
            return new PluginServiceRegistrationViolation(
                PluginServiceRegistrationViolationKind.ExistingDescriptorChanged,
                _baseline[_workingServices.Count]);
        }

        for (var index = 0; index < _baseline.Length; index++)
        {
            if (!ReferenceEquals(_baseline[index], _workingServices[index]))
            {
                return new PluginServiceRegistrationViolation(
                    PluginServiceRegistrationViolationKind.ExistingDescriptorChanged,
                    _baseline[index]);
            }
        }

        return null;
    }
}
