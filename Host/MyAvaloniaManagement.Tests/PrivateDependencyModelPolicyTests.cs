using System.Reflection;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Compatibility;
using Xunit;

namespace MyAvaloniaManagement.Tests;

/// <summary>部署禁带与真实共享归属分别验证，防止仅让构建通过却在运行时绑定到不存在的 Host 库。</summary>
public sealed class PrivateDependencyModelPolicyTests
{
    [Theory]
    [InlineData("Microsoft.Extensions.DependencyModel.dll", false)]
    [InlineData("runtimes/win-x64/lib/MICROSOFT.EXTENSIONS.DEPENDENCYMODEL.DLL", false)]
    [InlineData("Microsoft.Extensions.DependencyModel.Extra.dll", true)]
    [InlineData("Microsoft.Extensions.DependencyModelFake.dll", true)]
    [InlineData("Microsoft.Extensions.DependencyInjection.dll", true)]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions.dll", true)]
    [InlineData("Microsoft.Extensions.Configuration.Json.dll", true)]
    [InlineData("Microsoft.Extensions.Unprovided.dll", true)]
    [InlineData("nested/Avalonia.dll", true)]
    [InlineData("MyAvaloniaManagement.PluginSdk.dll", true)]
    public void 只开放DependencyModel精确身份其余边界不变(string path, bool forbidden)
    {
        Assert.Equal(forbidden, RuntimeProfile.Current.IsForbiddenAsset(path));
    }

    [Fact]
    public void DependencyModel不是Host共享闭包而DI仍由Host提供()
    {
        var policy = new HostContractAssemblyPolicy();
        Assert.False(policy.IsShared(new AssemblyName("Microsoft.Extensions.DependencyModel")));
        Assert.True(policy.IsShared(new AssemblyName("Microsoft.Extensions.DependencyInjection.Abstractions")));
        Assert.DoesNotContain("Microsoft.Extensions.DependencyModel", RuntimeProfile.Current.SharedRoots);
    }
}
