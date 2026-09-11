using MyAvaloniaManagement.Models.FileSystem;

namespace MyAvaloniaManagement.PluginTests;

public sealed class SharedDependencyCompatibilityTests
{
    [Fact]
    public void MvvmToolkit生成属性更新值并发送变更通知()
    {
        var item = FileSystemNode.CreateDesignSample("C:/test", "测试目录", true);
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        item.IsExpanded = true;

        Assert.True(item.IsExpanded);
        Assert.Contains(nameof(FileSystemNode.IsExpanded), changedProperties);
    }

}
