using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 保护需要表达设计语义、不能只靠签名文本说明的 Plugin SDK 边界。
/// </summary>
/// <remarks>
/// 完整 public 签名由 G13 的 Shipped/Unshipped 文本和专项变异脚本负责；本测试只保留
/// “通用事件总线已删除且旧消息包装器不能回流”这类结构断言，避免再维护第二套反射 API 格式化器。
/// </remarks>
public sealed class PublicApiContractTests
{
    [Fact]
    public void PluginSdk不再公开通用事件总线或旧消息包装器()
    {
        var coreAssembly = typeof(IPluginLifecycle).Assembly;
        Assert.Null(coreAssembly.GetType("MyAvaloniaManagement.PluginSdk.IHostEventBus"));
        Assert.DoesNotContain(
            typeof(IPluginModule).Assembly.ExportedTypes
                .SelectMany(type => type.GetMembers())
                .Select(member => member.ToString()),
            signature => signature?.Contains(
                "CommunityToolkit.Mvvm.Messaging",
                StringComparison.Ordinal) == true);
        var assembly = typeof(IPluginModule).Assembly;
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.IMessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessageHandler`2"));
    }
}
