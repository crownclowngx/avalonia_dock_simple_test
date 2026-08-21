using MyAvaloniaManagement.Models.Tools;

namespace MyAvaloniaManagement.PluginTests;

public sealed class SharedDependencyCompatibilityTests
{
    [Fact]
    public void MvvmToolkit生成属性更新值并发送变更通知()
    {
        var item = new ToolManagementItem();
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        item.DisplayName = "阶段三工具";
        item.IsVisible = false;

        Assert.Equal("阶段三工具", item.DisplayName);
        Assert.False(item.IsVisible);
        Assert.Contains(nameof(ToolManagementItem.DisplayName), changedProperties);
        Assert.Contains(nameof(ToolManagementItem.IsVisible), changedProperties);
    }

}
