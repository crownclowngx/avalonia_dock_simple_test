using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Converter;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.FileSystem;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels;
using Avalonia.Controls;

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
        using var context = new TestHostContext(configureContributions: (services, builder) =>
        {
            AddDocument<VisibleDocumentA>(services, builder, "visible-a", "A", "分类一");
            AddDocument<VisibleDocumentB>(services, builder, "visible-b", "B", "分类一");
            AddDocument<VisibleDocumentC>(services, builder, "visible-c", "C", "分类二");
            AddDocument<VisibleDocumentD>(services, builder, "uncategorized", "未分类", "其他");
        });

        var groups = new PluginMenuService(context.Factory)
            .GetCreationEntriesByCategory();

        Assert.Equal(2, groups["分类一"].Count);
        Assert.Single(groups["分类二"]);
        Assert.Contains("其他", groups.Keys);
    }

    [Fact]
    public void 多入口策略展开为同一文档类型的独立菜单项()
    {
        using var context = new TestHostContext(configureContributions: (services, builder) =>
        {
            services.AddScoped<MultiIntentDocument>();
            builder.AddDocument(
                MyAvaloniaManagement.Business.Constants.HostExtensionIds.V2Owner,
                new DocumentDescriptor(
                    new DocumentTypeId("myavalonia.host.document.multi-intent"),
                    "下载",
                    "多入口",
                    "测试",
                    creationIntents:
                    [
                        new DocumentCreationIntentDescriptor(new CreationIntentId("quick-url"), "链接下载"),
                        new DocumentCreationIntentDescriptor(new CreationIntentId("personal-source"), "个人来源"),
                    ]),
                typeof(MultiIntentDocument),
                typeof(UserControl),
                static () => new UserControl(),
                false);
        });

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

    private static void AddDocument<TDocument>(
        IServiceCollection services,
        PluginRegistryBuilder builder,
        string idSuffix,
        string displayName,
        string category)
        where TDocument : class, IPluginDocument
    {
        services.AddScoped<TDocument>();
        builder.AddDocument(
            MyAvaloniaManagement.Business.Constants.HostExtensionIds.V2Owner,
            new DocumentDescriptor(
                new DocumentTypeId($"myavalonia.host.document.{idSuffix}"),
                displayName,
                "菜单测试",
                category),
            typeof(TDocument),
            typeof(UserControl),
            static () => new UserControl(),
            false);
    }

    private abstract class MenuDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation => new(string.Empty);
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(DocumentActivationContext context, CancellationToken token) =>
            ValueTask.CompletedTask;
    }

    private sealed class VisibleDocumentA : MenuDocument;
    private sealed class VisibleDocumentB : MenuDocument;
    private sealed class VisibleDocumentC : MenuDocument;
    private sealed class VisibleDocumentD : MenuDocument;
    private sealed class MultiIntentDocument : MenuDocument;

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
