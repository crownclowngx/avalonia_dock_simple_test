using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;
using PluginIsolation.Dependency;

namespace PluginIsolation.Plugin;

/// <summary>
/// V2 插件隔离探针。
/// 设计意图：保持与 V1 相同的探针类型全名，确保测试结果只由加载上下文和依赖版本决定。
/// </summary>
public static class IsolationProbe
{
    public static string ReadPrivateVersion() => VersionMarker.Value;

    public static Assembly ReadSharedContract() => typeof(IPluginModule).Assembly;
}

/// <summary>
/// 将隔离探针声明为完整 Managed Plugin，而不是只依赖加载器偶然扫描到的程序集。
/// </summary>
public sealed class IsolationPluginModule : IPluginModule
{
    public PluginId PluginId { get; } = new("myavalonia.plugin.isolation-v2");

    public void ConfigureServices(IServiceCollection services) =>
        ArgumentNullException.ThrowIfNull(services);
}
