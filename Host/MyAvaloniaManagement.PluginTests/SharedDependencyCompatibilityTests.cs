using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagementCommon.Save;

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

    [Fact]
    public void Document内容快照只暴露独立Schema与正文()
    {
        var snapshot = new DocumentContentSnapshot(3, "{\"position\":1250}");

        Assert.Equal(3, snapshot.ContentSchemaVersion);
        Assert.Equal("{\"position\":1250}", snapshot.Payload);
        Assert.Equal(
            ["ContentSchemaVersion", "Payload"],
            typeof(DocumentContentSnapshot).GetProperties().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public void G8保存接口只暴露内容创建与恢复()
    {
        Assert.Empty(typeof(ISavableDocument).GetProperties());
        Assert.Equal(
            ["CreateContentSnapshot", "RestoreContent"],
            typeof(ISavableDocument).GetMethods().Select(method => method.Name).Order().ToArray());
        Assert.Null(typeof(ISavableDocument).Assembly.GetType(
            "MyAvaloniaManagementCommon.Save.DocumentSaveData"));
    }
}
