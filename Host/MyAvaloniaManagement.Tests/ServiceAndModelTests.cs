using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Converter;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.FileSystem;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证依赖注入、插件菜单及文件系统模型等无界面组件。
/// </summary>
public sealed class ServiceAndModelTests
{
    [Fact]
    public void 宿主服务可在作用域和构建验证开启时解析()
    {
        using var context = new TestHostContext();

        var first = context.CreateMainWindowViewModel();
        var second = context.CreateMainWindowViewModel();

        Assert.NotSame(first, second);
        Assert.Same(
            context.Factory,
            context.Provider.GetRequiredService<ManagementFactory>());
        Assert.NotNull(first.Layout);
    }

    [Fact]
    public void G5Descriptor中的文档都形成显式菜单分组()
    {
        var strategies = new IDocumentCreationStrategy[]
        {
            new StubDocumentStrategy(
            new DocumentMetadata(new DocumentTypeId("myavalonia.host.document.visible-a"), "A")
            {
                MenuCategory = "分类一"
            }),
            new StubDocumentStrategy(
            new DocumentMetadata(new DocumentTypeId("myavalonia.host.document.visible-b"), "B")
            {
                MenuCategory = "分类一"
            }),
            new StubDocumentStrategy(
            new DocumentMetadata(new DocumentTypeId("myavalonia.host.document.hidden"), "隐藏")
            {
                MenuCategory = "分类二",
                ShowInMenu = false
            }),
            new StubDocumentStrategy(
                new DocumentMetadata(
                    new DocumentTypeId("myavalonia.host.document.uncategorized"),
                    "未分类")),
        };
        using var context = new TestHostContext(documentStrategies: strategies);

        var groups = new PluginMenuService(context.Factory)
            .GetDocumentMetadataByCategory();

        Assert.Equal(2, groups["分类一"].Count);
        Assert.Single(groups["分类二"]);
        Assert.Contains("未归类插件", groups.Keys);
    }

    [Fact]
    public void 多入口策略展开为同一文档类型的独立菜单项()
    {
        using var context = new TestHostContext(
            documentStrategies: [new MultiIntentDocumentStrategy()]);

        var entries = new PluginMenuService(context.Factory)
            .GetCreationEntriesByCategory()["测试"];

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(
            "myavalonia.host.document.multi-intent",
            entry.DocumentTypeId.Value));
        Assert.Equal(
            ["quick-url", "personal-source"],
            entries.Select(entry => entry.CreationIntentId?.Value));
    }

    private sealed class MultiIntentDocumentStrategy : IDocumentCreationStrategy, IDocumentCreationIntentProvider
    {
        public Dock.Model.Mvvm.Controls.Document CreateDocument(DocumentCreationParams @params) => new();

        public DocumentMetadata GetMetadata() => new(
            new DocumentTypeId("myavalonia.host.document.multi-intent"),
            "下载") { MenuCategory = "测试" };

        public IReadOnlyList<DocumentCreationIntentMetadata> GetCreationIntents() =>
        [
            new(new CreationIntentId("quick-url"), "链接下载"),
            new(new CreationIntentId("personal-source"), "个人来源"),
        ];
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"C:\folder\file.txt", false)]
    [InlineData("", false)]
    public void 驱动器路径识别稳定(string path, bool expected) =>
        Assert.Equal(expected, FileHelper.IsDrivePath(path));

    [Fact]
    public void 文件系统节点延迟加载并可刷新()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "child"));
            File.WriteAllText(Path.Combine(root, "first.txt"), "one");
            var node = new FileSystemNode(root);

            Assert.True(node.IsDirectory);
            Assert.Equal(2, node.Children.Count);

            File.WriteAllText(Path.Combine(root, "second.txt"), "two");
            Assert.Equal(2, node.Children.Count);
            node.Refresh();
            Assert.Equal(3, node.Children.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 删除后的目录刷新返回空集合且不抛异常()
    {
        var root = CreateTempDirectory();
        var node = new FileSystemNode(root);
        _ = node.Children.Count;
        Directory.Delete(root);

        node.Refresh();

        Assert.Empty(node.Children);
    }

    [Fact]
    public void 文件图标转换器不支持反向转换() =>
        Assert.Throws<NotSupportedException>(() =>
            FileSystemIconConverter.Instance.ConvertBack(
                "📁",
                typeof(bool),
                null,
                CultureInfo.InvariantCulture));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "MyAvaloniaManagement.ModelTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
