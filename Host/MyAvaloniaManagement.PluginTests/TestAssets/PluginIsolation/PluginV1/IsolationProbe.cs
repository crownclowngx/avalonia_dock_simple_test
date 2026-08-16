using System;
using System.Reflection;
using MyAvaloniaManagementCommon.Plugin;
using PluginIsolation.Dependency;

namespace PluginIsolation.Plugin;

/// <summary>
/// V1 插件隔离探针。
/// 设计意图：同时触发私有依赖与宿主公共契约解析，验证两条解析路径具有不同共享语义。
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
    public void Configure(IPluginRegistrationContext context) =>
        ArgumentNullException.ThrowIfNull(context);
}
