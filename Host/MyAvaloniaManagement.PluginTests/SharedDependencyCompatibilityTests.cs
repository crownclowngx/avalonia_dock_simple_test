using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

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
    public void NewtonsoftJson往返保持文档保存契约()
    {
        var expected = new DocumentSaveData
        {
            DocumentTypeId = "phase3-document",
            Title = "阶段三文档",
            SaveTime = new DateTime(2026, 7, 26, 12, 30, 0, DateTimeKind.Utc),
            Content = "{\"position\":1250}",
            PluginMetadata = "{\"version\":\"3\"}",
        };

        var json = JsonConvert.SerializeObject(expected);
        var actual = JsonConvert.DeserializeObject<DocumentSaveData>(json);

        Assert.NotNull(actual);
        Assert.Equal(expected.DocumentTypeId, actual.DocumentTypeId);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.SaveTime, actual.SaveTime);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.PluginMetadata, actual.PluginMetadata);
    }
}
