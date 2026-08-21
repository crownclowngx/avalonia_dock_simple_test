using System.Reflection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 保护需要表达设计语义、不能只靠签名文本说明的 Plugin SDK 边界。
/// </summary>
/// <remarks>
/// 完整 public 签名由 G13 的 Shipped/Unshipped 文本和专项变异脚本负责；本测试只保留
/// “事件总线不泄漏第三方消息器”这类行为断言，避免再维护第二套反射 API 格式化器。
/// </remarks>
public sealed class PublicApiContractTests
{
    [Fact]
    public void PluginSdk事件总线只暴露Sdk自有类型和Bcl令牌()
    {
        var eventBusType = typeof(IHostEventBus);
        var methods = eventBusType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(["Publish", "Subscribe"], methods.Select(method => method.Name).Order().ToArray());
        Assert.DoesNotContain(
            typeof(IPluginModule).Assembly.ExportedTypes
                .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                .Select(member => member.ToString()),
            signature => signature?.Contains(
                "CommunityToolkit.Mvvm.Messaging",
                StringComparison.Ordinal) == true);
        Assert.Equal(typeof(IDisposable), methods.Single(method => method.Name == "Subscribe").ReturnType);
        var assembly = typeof(IPluginModule).Assembly;
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.IMessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessageHandler`2"));
    }
}
