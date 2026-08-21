using System;
using System.Reflection;
using MyAvaloniaManagement.PluginSdk.UI;
using PluginIsolation.Dependency;

namespace PluginIsolation.Plugin;

/// <summary>
/// V2 插件隔离探针；类型全名与 V1 相同，以验证私有依赖按加载上下文隔离。
/// </summary>
public static class IsolationProbe
{
    public static string ReadPrivateVersion() => VersionMarker.Value;

    public static Assembly ReadSharedContract() => typeof(IPluginModule).Assembly;
}

/// <summary>通过最终 UI SDK 声明的 G5 加载隔离测试模块。</summary>
public sealed class IsolationPluginModule : IPluginModule
{
    public void Configure(IPluginRegistration context) =>
        ArgumentNullException.ThrowIfNull(context);
}
