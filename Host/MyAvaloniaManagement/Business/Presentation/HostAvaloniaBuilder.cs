using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>为生产宿主和获准的真实窗口 Harness 建立同一套 Avalonia 配置。</summary>
/// <remarks>
/// 这里是组合根的一部分：服务解析仅发生在 Avalonia 请求创建 <see cref="App"/> 的瞬间，
/// 提供器不会写入静态字段，也不会向业务对象暴露。用工厂重载而不是无参泛型重载，使 App
/// 可以坚持构造注入，同时保持 Avalonia 平台配置只有一个事实源。
/// </remarks>
internal static class HostAvaloniaBuilder
{
    internal static AppBuilder Build(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AppBuilder.Configure(services.GetRequiredService<App>)
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
