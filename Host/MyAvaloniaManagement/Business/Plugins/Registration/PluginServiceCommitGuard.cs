using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Business.WorkflowActions;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 在插件声明窗口关闭后校验服务所有权，并由 Host 一次性提交协议底座。
/// </summary>
/// <remarks>
/// 本类型不是自定义容器或通用规则引擎。它只解决一个明确职责：插件可以自由使用 Microsoft DI
/// 登记私有对象，但 Host Port、Document Scope 基础设施和声明式贡献根必须始终由 Host 最后追加。
/// 校验失败发生在 Provider 构建前，因此丢弃当前集合即可实现原子隔离，不需要回滚事务。
/// </remarks>
internal static class PluginServiceCommitGuard
{
    private static readonly IReadOnlySet<Type> ReservedHostServiceTypes = new HashSet<Type>
    {
        typeof(IPluginWindowInteraction),
        typeof(IDocumentLifetime),
        typeof(DocumentLifetime),
        typeof(DocumentScopeManager),
        typeof(IWorkflowActionGateway),
        typeof(IWorkflowActionRun),
    };

    /// <summary>校验当前插件的私有描述符，并在成功后追加 Host 拥有的全部服务。</summary>
    /// <param name="pluginServices">模块配置过、但尚未建立 Provider 的插件私有集合。</param>
    /// <param name="registration">已经 Seal 的声明窗口。</param>
    /// <param name="hostProvider">只用于取得 SDK 明确承诺的 Host Port 实例。</param>
    internal static void ValidateAndCommit(
        IServiceCollection pluginServices,
        PluginRegistration registration,
        IServiceProvider hostProvider)
    {
        ArgumentNullException.ThrowIfNull(pluginServices);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(hostProvider);

        var contributionRoots = registration.GetContributionRootTypes();
        var diagnostics = new List<HostCompositionDiagnostic>();
        foreach (var descriptor in pluginServices)
        {
            if (ReservedHostServiceTypes.Contains(descriptor.ServiceType))
            {
                diagnostics.Add(CreateDiagnostic(
                    HostDiagnosticCodes.PluginHostServiceRegistrationForbidden,
                    descriptor));
            }

            if (contributionRoots.Contains(descriptor.ServiceType))
            {
                diagnostics.Add(CreateDiagnostic(
                    HostDiagnosticCodes.PluginContributionServiceRegistrationForbidden,
                    descriptor));
            }
        }

        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }

        // 以下描述符全部在模块返回并通过校验后才进入原始集合。插件保存的是已 Seal 的包装器，
        // 因此 Clear/Remove/Replace 无法观察或改变这些条目，正确性不依赖“最后注册胜出”。
        pluginServices.AddSingleton(hostProvider.GetRequiredService<IPluginWindowInteraction>());
        pluginServices.AddScoped<DocumentLifetime>();
        pluginServices.AddScoped<IDocumentLifetime>(provider =>
            provider.GetRequiredService<DocumentLifetime>());

        if (registration.UsesWorkflowActionGateway)
        {
            // Gateway 实例只捕获可信 manifest 身份和 Host internal Run 管理器；请求 DTO 没有
            // CallerId 写入口，因此插件不能通过 JSON 或自行注册的服务伪造调用者。
            pluginServices.AddSingleton<IWorkflowActionGateway>(
                new CallerBoundWorkflowActionGateway(
                    registration.PluginId,
                    hostProvider.GetRequiredService<WorkflowActionRunManager>()));
        }

        AppendContributionDescriptors(pluginServices, registration);
        pluginServices.AddSingleton<DocumentScopeManager>();
    }

    private static void AppendContributionDescriptors(
        IServiceCollection services,
        PluginRegistration registration)
    {
        foreach (var descriptor in registration.GetHostOwnedServiceDescriptors())
        {
            services.Add(descriptor);
        }
    }

    private static HostCompositionDiagnostic CreateDiagnostic(
        string code,
        ServiceDescriptor descriptor)
    {
        var contributorType = GetContributorType(descriptor);
        return new HostCompositionDiagnostic(
            code,
            descriptor.ServiceType.FullName,
            [new HostCompositionContributor(
                contributorType.FullName ?? contributorType.Name,
                contributorType.Assembly.GetName().Name ?? "UnknownAssembly")]);
    }

    /// <summary>在普通和 keyed ServiceDescriptor 之间取得可审阅的贡献来源类型。</summary>
    private static Type GetContributorType(ServiceDescriptor descriptor)
    {
        if (descriptor.IsKeyedService)
        {
            return descriptor.KeyedImplementationType ??
                   descriptor.KeyedImplementationInstance?.GetType() ??
                   descriptor.ServiceType;
        }

        return descriptor.ImplementationType ??
               descriptor.ImplementationInstance?.GetType() ??
               descriptor.ServiceType;
    }
}
