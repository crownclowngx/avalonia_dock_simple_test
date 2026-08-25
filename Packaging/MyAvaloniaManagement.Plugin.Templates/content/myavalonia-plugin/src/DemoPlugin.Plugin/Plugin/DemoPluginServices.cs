using Microsoft.Extensions.DependencyInjection;

namespace DemoPlugin.Plugin;

public static class TemplateProjectIdentifierServices
{
    /// <summary>登记插件自己的业务服务；Standalone 可以复用同一个组合入口。</summary>
    public static IServiceCollection AddTemplateProjectIdentifierServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
