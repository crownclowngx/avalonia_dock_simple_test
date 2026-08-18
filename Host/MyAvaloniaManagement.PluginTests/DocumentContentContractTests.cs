using MyAvaloniaManagementCommon.Save;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 从真实插件消费方向锁定 G8 的最终内容契约。
/// 这组测试故意使用独立类名，使 G8 门禁可以用 DocumentContent 过滤器精确执行。
/// </summary>
public sealed class DocumentContentContractTests
{
    [Fact]
    public void 内容快照不可变且拒绝非法构造参数()
    {
        var snapshot = new DocumentContentSnapshot(3, "{\"position\":1250}");

        Assert.Equal(3, snapshot.ContentSchemaVersion);
        Assert.Equal("{\"position\":1250}", snapshot.Payload);
        Assert.All(
            typeof(DocumentContentSnapshot).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentContentSnapshot(0, "{}"));
        Assert.Throws<ArgumentNullException>(() => new DocumentContentSnapshot(1, null!));
    }

    [Fact]
    public void 保存接口只暴露内容创建与恢复()
    {
        Assert.Empty(typeof(ISavableDocument).GetProperties());
        Assert.Equal(
            ["CreateContentSnapshot", "RestoreContent"],
            typeof(ISavableDocument).GetMethods().Select(method => method.Name).Order().ToArray());
        Assert.Null(typeof(ISavableDocument).Assembly.GetType(
            "MyAvaloniaManagementCommon.Save.DocumentSaveData"));
    }
}
