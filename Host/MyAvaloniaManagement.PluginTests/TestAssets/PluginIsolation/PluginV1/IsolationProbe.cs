using System;
using System.Reflection;
using MyAvaloniaManagement.PluginSdk.UI;
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

/// <summary>通过最终 UI SDK 声明的 G5 加载隔离测试模块。</summary>
public sealed class IsolationPluginModule : IPluginModule
{
    public void Configure(IPluginRegistration context) =>
        ArgumentNullException.ThrowIfNull(context);
}

/// <summary>未在 manifest 中声明的第二模块，用于证明加载器不扫描其他候选。</summary>
public sealed class UndeclaredPluginModule : IPluginModule
{
    public UndeclaredPluginModule() =>
        throw new InvalidOperationException("未声明模块不应被构造。");

    public void Configure(IPluginRegistration context) =>
        throw new InvalidOperationException("未声明模块不应被配置。");
}

// 以下类型服务精确 entryPoint.type 的结构预检负例；它们不参与贡献注册。
internal sealed class InternalPluginModule : IPluginModule
{
    public void Configure(IPluginRegistration context) =>
        throw new InvalidOperationException("不可访问入口不应被配置。");
}

public abstract class AbstractPluginModule : IPluginModule
{
    public abstract void Configure(IPluginRegistration context);
}

public sealed class WrongContractEntry;

public sealed class PrivateConstructorPluginModule : IPluginModule
{
    private PrivateConstructorPluginModule() =>
        throw new InvalidOperationException("非 public 构造函数不应被执行。");

    public void Configure(IPluginRegistration context) =>
        throw new InvalidOperationException("无 public 无参构造入口不应被配置。");
}

public sealed class GenericPluginModule<T> : IPluginModule
{
    public void Configure(IPluginRegistration context) =>
        throw new InvalidOperationException("泛型入口不应被配置。");
}
