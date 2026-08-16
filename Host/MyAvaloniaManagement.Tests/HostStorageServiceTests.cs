using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证宿主 Document 文件选择器的格式约定，防止打开与保存扩展名再次分叉。
/// </summary>
public sealed class HostStorageServiceTests
{
    [Fact]
    public void 打开文档仅筛选Mamdoc文件()
    {
        var options = AvaloniaHostStorageService.CreateOpenFilePickerOptions();

        var fileType = Assert.Single(options.FileTypeFilter!);
        Assert.Equal("管理文档 (.mamdoc)", fileType.Name);
        Assert.Equal(["*.mamdoc"], fileType.Patterns);
        Assert.DoesNotContain("*.txt", fileType.Patterns!);
        Assert.True(options.AllowMultiple);
    }

    [Fact]
    public void 保存文档缺少元数据时仍使用Mamdoc扩展名()
    {
        var options = AvaloniaHostStorageService.CreateSaveFilePickerOptions(null);

        var fileType = Assert.Single(options.FileTypeChoices!);
        Assert.Equal("管理文档 (.mamdoc)", fileType.Name);
        Assert.Equal("mamdoc", options.DefaultExtension);
        Assert.Equal(["*.mamdoc"], fileType.Patterns);
    }

    [Fact]
    public void 保存文档有元数据时保留显示名并使用Mamdoc扩展名()
    {
        var metadata = new DocumentMetadata(
            new DocumentTypeId("myavalonia.host.document.storage-test"),
            "测试文档");

        var options = AvaloniaHostStorageService.CreateSaveFilePickerOptions(metadata);

        var fileType = Assert.Single(options.FileTypeChoices!);
        Assert.Equal("测试文档", fileType.Name);
        Assert.Equal("mamdoc", options.DefaultExtension);
        Assert.Equal(["*.mamdoc"], fileType.Patterns);
    }
}
