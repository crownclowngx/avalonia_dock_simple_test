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

/// <summary>
/// 未在 manifest v2 中声明的第二模块。构造函数故意抛错，用来证明 Loader 不再扫描并激活第二个候选。
/// </summary>
public sealed class UndeclaredPluginModule : IPluginModule
{
    public UndeclaredPluginModule() =>
        throw new InvalidOperationException("未声明模块不应被构造。");

    public void Configure(IPluginRegistrationContext context) =>
        throw new InvalidOperationException("未声明模块不应被配置。");
}

// 以下类型只服务 G3 真实目录 Loader 负例。它们与主入口同处一个程序集，能够证明失败来自精确
// entryPoint.type 的结构预检，而不是缺少程序集、依赖或测试替身绕过了真实加载链。
internal sealed class InternalPluginModule : IPluginModule
{
    public void Configure(IPluginRegistrationContext context) =>
        throw new InvalidOperationException("不可访问入口不应被配置。");
}

public abstract class AbstractPluginModule : IPluginModule
{
    public abstract void Configure(IPluginRegistrationContext context);
}

public sealed class WrongContractEntry;

public sealed class PrivateConstructorPluginModule : IPluginModule
{
    private PrivateConstructorPluginModule() =>
        throw new InvalidOperationException("非 public 构造函数不应被执行。");

    public void Configure(IPluginRegistrationContext context) =>
        throw new InvalidOperationException("无 public 无参构造入口不应被配置。");
}

public sealed class GenericPluginModule<T> : IPluginModule
{
    public void Configure(IPluginRegistrationContext context) =>
        throw new InvalidOperationException("泛型入口不应被配置。");
}
