using System.Reflection;
using System.Runtime.Loader;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 回归真实 Host 曾缺少日志抽象的问题。测试直接经过生产共享策略，不能因测试进程碰巧带有
/// Logging DLL 就放行：只有 Host 明确登记为共享根并能解析同一实例，外部插件才可以省略它。
/// </summary>
public sealed class PluginLoggingDependencyTests
{
    [Fact]
    public void Titanium请求的日志抽象由Host共享且保持默认上下文身份()
    {
        var requested = new AssemblyName(
            "Microsoft.Extensions.Logging.Abstractions, Version=10.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60");
        var policy = new HostContractAssemblyPolicy();

        Assert.True(policy.IsShared(requested));
        var resolved = policy.ResolveSharedAssembly(requested);
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(resolved));
        Assert.Same(AssemblyLoadContext.Default.LoadFromAssemblyName(requested), resolved);
        Assert.NotNull(resolved.GetType("Microsoft.Extensions.Logging.ILoggerFactory", throwOnError: true));
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Logging.Abstractions, Version=99.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60")]
    [InlineData("Microsoft.Extensions.Logging.Abstractions, Version=10.0.0.0, Culture=neutral, PublicKeyToken=null")]
    public void 日志共享仍拒绝更高版本和错误签名身份(string identity)
    {
        // 补齐依赖不能变成按名称无条件绑定；版本和公钥身份仍由现有生产契约检查。
        var policy = new HostContractAssemblyPolicy();
        var exception = Assert.Throws<FileLoadException>(() => policy.ResolveSharedAssembly(new AssemblyName(identity)));
        Assert.Contains("PLUGIN_SHARED_ASSEMBLY_MISMATCH", exception.Message, StringComparison.Ordinal);
    }
}
