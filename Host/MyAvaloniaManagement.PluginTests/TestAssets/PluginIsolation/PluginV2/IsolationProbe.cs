using System.Reflection;
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
