using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Tests;

public sealed class InternalRefactorTests
{
    [Fact]
    public void StrategyMetadataIsReadOnceAndDuplicateRegistrationKeepsFirst()
    {
        using var context = new TestHostContext();
        var toolStrategy = new CountingToolStrategy();
        var first = new StubDocumentStrategy(
            new DocumentMetadata("duplicate-test", "First"));
        var second = new StubDocumentStrategy(
            new DocumentMetadata("duplicate-test", "Second"));

        context.Factory.RegisterToolStrategy(toolStrategy);
        context.Factory.RegisterStrategy(first);
        context.Factory.RegisterStrategy(second);

        Assert.Equal(1, toolStrategy.MetadataReadCount);
        Assert.Equal(
            "First",
            Assert.Single(context.Factory.GetAllDocumentMetadata(),
                item => item.DocumentTypeId == "duplicate-test").DisplayName);
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
                        AssemblyLoaderHelper.LoadPluginsFromDirectories(rootName))));

            Assert.All(results, Assert.Empty);
            Assert.True(Directory.Exists(root));
            Assert.NotSame(results[0], results[1]);
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

    private sealed class CountingToolStrategy : IToolCreationStrategy
    {
        public int MetadataReadCount { get; private set; }

        public Tool CreateTool() => new() { Id = "counting-tool" };

        public ToolMetadata GetMetadata()
        {
            MetadataReadCount++;
            return new ToolMetadata
            {
                ToolTypeId = "counting-tool",
                DisplayName = "Counting",
                Description = string.Empty,
                IconPath = string.Empty,
                Alignment = "Left"
            };
        }
    }
}
