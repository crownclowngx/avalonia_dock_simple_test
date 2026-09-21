namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 保护 Host 自身已删除的兼容类型；SDK 类型与导出签名由 SdkBoundaryTests 统一负责。
/// </summary>
/// <remarks>
/// 完整 public 签名由 G13 的 Shipped/Unshipped 文本和专项变异脚本负责；本测试只保留
/// “通用事件总线已删除且旧消息包装器不能回流”这类结构断言，避免再维护第二套反射 API 格式化器。
/// </remarks>
public sealed class PublicApiContractTests
{
    [Fact]
    public void Host程序集不再包含V2Facade总线或伪插件所有者()
    {
        var hostAssembly = typeof(MyAvaloniaManagement.Business.Workspace.WorkspaceSession).Assembly;
        foreach (var removedType in new[]
                 {
                     "MyAvaloniaManagement.ViewModels.ManagementFactory",
                     "MyAvaloniaManagement.ViewModels.Tools.ToolManagementData",
                     "MyAvaloniaManagement.Business.Events.HostEventBus",
                     "MyAvaloniaManagement.Business.Helpers.V2Owner",
                 })
        {
            Assert.Null(hostAssembly.GetType(removedType));
        }
    }

}
