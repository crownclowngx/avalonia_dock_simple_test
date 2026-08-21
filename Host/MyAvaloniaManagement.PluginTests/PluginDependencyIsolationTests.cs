using System.Reflection;
using System.Runtime.Loader;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证不同插件可以使用同名不同版本私有依赖，同时仍共享同一个宿主公共契约程序集。
/// </summary>
public sealed class PluginDependencyIsolationTests
{
    [Fact]
    public void 两个插件分别加载自己的同名私有依赖并共享宿主契约()
    {
        var assemblies = AssemblyLoaderHelper.Discover(
            "PluginIsolationFixtures").Assemblies;

        Assert.Equal(2, assemblies.Count);
        var pluginV1 = Assert.Single(
            assemblies,
            assembly => assembly.GetName().Name == "PluginIsolation.PluginV1");
        var pluginV2 = Assert.Single(
            assemblies,
            assembly => assembly.GetName().Name == "PluginIsolation.PluginV2");

        Assert.Equal("private-v1", InvokePrivateVersion(pluginV1));
        Assert.Equal("private-v2", InvokePrivateVersion(pluginV2));

        var contextV1 = Assert.IsType<PluginLoadContext>(
            AssemblyLoadContext.GetLoadContext(pluginV1));
        var contextV2 = Assert.IsType<PluginLoadContext>(
            AssemblyLoadContext.GetLoadContext(pluginV2));
        Assert.NotSame(contextV1, contextV2);

        var dependencyV1 = contextV1.LoadFromAssemblyName(
            new AssemblyName("PluginIsolation.Dependency, Version=1.0.0.0"));
        var dependencyV2 = contextV2.LoadFromAssemblyName(
            new AssemblyName("PluginIsolation.Dependency, Version=2.0.0.0"));
        Assert.Equal(new Version(1, 0, 0, 0), dependencyV1.GetName().Version);
        Assert.Equal(new Version(2, 0, 0, 0), dependencyV2.GetName().Version);
        Assert.NotSame(dependencyV1, dependencyV2);
        Assert.Same(contextV1, AssemblyLoadContext.GetLoadContext(dependencyV1));
        Assert.Same(contextV2, AssemblyLoadContext.GetLoadContext(dependencyV2));
        Assert.DoesNotContain(
            AssemblyLoadContext.Default.Assemblies,
            assembly => assembly.GetName().Name == "PluginIsolation.Dependency");

        var sharedContract = typeof(IPluginModule).Assembly;
        Assert.Same(sharedContract, InvokeSharedContract(pluginV1));
        Assert.Same(sharedContract, InvokeSharedContract(pluginV2));
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(sharedContract));
    }

    [Fact]
    public void 一个插件缺少私有依赖时隔离整个候选且不阻断其他插件()
    {
        var snapshot = AssemblyLoaderHelper.Discover(
            "PluginIsolationMissingFixtures");
        var assemblies = snapshot.Assemblies;
        Assert.Single(assemblies);

        var pluginV2 = Assert.Single(
            assemblies,
            assembly => assembly.GetName().Name == "PluginIsolation.PluginV2");

        Assert.DoesNotContain(
            assemblies,
            assembly => assembly.GetName().Name == "PluginIsolation.PluginV1");
        Assert.Contains(
            snapshot.Diagnostics,
            item => item.Code == "PLUGIN_ASSEMBLY_LOAD_FAILED" &&
                    item.PluginDirectory == "PluginV1");
        Assert.Equal("private-v2", InvokePrivateVersion(pluginV2));
    }

    private static string InvokePrivateVersion(Assembly pluginAssembly) =>
        (string)GetProbeMethod(pluginAssembly, "ReadPrivateVersion")
            .Invoke(null, null)!;

    private static Assembly InvokeSharedContract(Assembly pluginAssembly) =>
        (Assembly)GetProbeMethod(pluginAssembly, "ReadSharedContract")
            .Invoke(null, null)!;

    private static MethodInfo GetProbeMethod(
        Assembly pluginAssembly,
        string methodName) =>
        pluginAssembly
            .GetType("PluginIsolation.Plugin.IsolationProbe", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(methodName);

}
