using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

public sealed class InternalRefactorTests
{
    [Fact]
    public void Host内部直接协作消费者不依赖Sdk事件总线()
    {
        var hostAssembly = typeof(MyAvaloniaManagement.ViewModels.ManagementFactory).Assembly;
        const string removedNamespace = "MyAvaloniaManagement.Message.";
        Assert.Null(hostAssembly.GetType(removedNamespace + "OpenFile" + "Message"));
        Assert.Null(hostAssembly.GetType(removedNamespace + "UpdateLayout" + "Message"));
        Assert.Null(hostAssembly.GetType(removedNamespace + "ToolVisibilityChanged" + "Message"));

        Type[] directCoordinationConsumers =
        [
            typeof(MyAvaloniaManagement.ViewModels.MainWindowViewModel),
            typeof(MyAvaloniaManagement.ViewModels.ManagementFactory),
            typeof(MyAvaloniaManagement.Business.Layout.ToolDockCoordinator),
            typeof(MyAvaloniaManagement.ViewModels.Tools.FileSystemTreeViewModel),
            typeof(MyAvaloniaManagement.ViewModels.Tools.ToolManagementViewModel)
        ];
        foreach (var consumer in directCoordinationConsumers)
        {
            var constructorParameters = consumer
                .GetConstructors(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType);
            Assert.DoesNotContain(
                typeof(IHostEventBus),
                constructorParameters);
        }

        Assert.Equal(
            "MyAvaloniaManagement.PluginSdk",
            typeof(IHostEventBus).Assembly.GetName().Name);
    }

    [Fact]
    public void Descriptor不会激活模型且重复注册原子失败()
    {
        CountingToolModel.ConstructionCount = 0;
        var builder = new PluginRegistryBuilder();
        var services = new ServiceCollection();
        var registration = new PluginRegistration(
            new MyAvaloniaManagement.PluginSdk.PluginId("myavalonia.host"),
            services,
            builder);
        var duplicateId =
            new MyAvaloniaManagement.PluginSdk.DocumentTypeId(
                "myavalonia.host.document.duplicate-test");
        registration.AddDocument<FirstDocumentModel, EmptyView>(
            new DocumentDescriptor(duplicateId, "First", "First", "测试"));
        registration.AddDocument<SecondDocumentModel, EmptyView>(
            new DocumentDescriptor(duplicateId, "Second", "Second", "测试"));
        registration.AddTool<CountingToolModel, EmptyView>(
            new ToolDescriptor(
                new MyAvaloniaManagement.PluginSdk.ToolTypeId(
                    "myavalonia.host.tool.counting"),
                "Counting",
                "Counting",
                MyAvaloniaManagement.PluginSdk.UI.ToolDockSide.Left,
                ToolCloseBehavior.Hide));

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Equal(0, CountingToolModel.ConstructionCount);
        Assert.Contains(exception.Diagnostics, item =>
            item.Code == "DOCUMENT_ID_DUPLICATE" &&
            item.StableId == "myavalonia.host.document.duplicate-test");
    }

    [Fact]
    public async Task PluginRootCacheIsThreadSafeAndDoesNotExposeMutableSnapshot()
    {
        var rootName = "ConcurrentPluginScan-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(AppContext.BaseDirectory, rootName);

        try
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => Task.Run(() =>
                        AssemblyLoaderHelper.Discover(rootName))));

            Assert.All(results, result => Assert.Empty(result.Assemblies));
            Assert.True(Directory.Exists(root));
            Assert.All(results, result => Assert.Same(results[0], result));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AtomicTextReplacementLeavesCompleteFileAndNoTemporaryFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "MyAvaloniaManagement.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "document.json");

        try
        {
            await File.WriteAllTextAsync(path, "old");

            await AtomicFileTransaction.WriteAllTextAsync(path, "new-content");

            Assert.Equal("new-content", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FirstDocumentModel : MyAvaloniaManagement.PluginSdk.IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("First");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(DocumentActivation context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class SecondDocumentModel : MyAvaloniaManagement.PluginSdk.IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("Second");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(DocumentActivation context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class CountingToolModel
    {
        internal static int ConstructionCount { get; set; }
        public CountingToolModel() => ConstructionCount++;
    }

    private sealed class EmptyView : Avalonia.Controls.UserControl;
}
