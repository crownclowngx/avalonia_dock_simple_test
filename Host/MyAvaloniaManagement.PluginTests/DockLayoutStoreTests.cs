using System.Text.Json;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.PluginTests;

public sealed class DockLayoutStoreTests
{
    [Fact]
    public void 合法结构快照可以原子往返且不留下临时文件()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        var store = new DockLayoutStore(workspace.LayoutPath);
        var snapshot = CreateValidSnapshot();

        store.Save(snapshot);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(snapshot.ActiveToolId, loaded.ActiveToolId);
        Assert.Equal(snapshot.Panes, loaded.Panes);
        Assert.Equal(snapshot.Tools, loaded.Tools);
        Assert.Empty(Directory.EnumerateFiles(
            workspace.DirectoryPath,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void 已有布局文件会通过原子替换更新()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        var store = new DockLayoutStore(workspace.LayoutPath);
        store.Save(CreateValidSnapshot());

        var updated = CreateValidSnapshot() with { ActiveToolId = null };
        updated.Tools[0] = updated.Tools[0] with { IsVisible = false };
        store.Save(updated);

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Null(loaded.ActiveToolId);
        Assert.False(loaded.Tools[0].IsVisible);
        Assert.Empty(Directory.EnumerateFiles(
            workspace.DirectoryPath,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void 损坏Json会被隔离且日志不包含原始内容()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        const string sensitiveContent = "password=ShouldNeverReachLogs";
        File.WriteAllText(workspace.LayoutPath, $"{{not-json:{sensitiveContent}");
        var logs = new List<string>();
        var store = new DockLayoutStore(
            workspace.LayoutPath,
            (code, id) => logs.Add($"{code}:{id}"));

        var loaded = store.Load();

        Assert.Null(loaded);
        Assert.False(File.Exists(workspace.LayoutPath));
        Assert.Single(EnumerateInvalidBackups(workspace.DirectoryPath));
        Assert.Contains(logs, log => log.StartsWith("LAYOUT_JSON_INVALID:", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, log => log.Contains(sensitiveContent, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2, "LAYOUT_SCHEMA_UNSUPPORTED")]
    [InlineData(1, "LAYOUT_TOOL_ID_DUPLICATE")]
    public void 非法版本或重复工具Id会整体回退(
        int schemaVersion,
        string expectedCode)
    {
        using var workspace = new TemporaryLayoutWorkspace();
        var snapshot = CreateValidSnapshot() with { SchemaVersion = schemaVersion };
        if (expectedCode == "LAYOUT_TOOL_ID_DUPLICATE")
        {
            snapshot.Tools.Add(snapshot.Tools[0] with { Order = 1 });
        }

        File.WriteAllText(
            workspace.LayoutPath,
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        var logs = new List<string>();
        var store = new DockLayoutStore(
            workspace.LayoutPath,
            (code, _) => logs.Add(code));

        Assert.Null(store.Load());
        Assert.Contains(expectedCode, logs);
        Assert.Single(EnumerateInvalidBackups(workspace.DirectoryPath));
    }

    [Fact]
    public void 快照模型只包含Dock结构字段()
    {
        var json = JsonSerializer.Serialize(CreateValidSnapshot());

        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mediaPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("playback", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PinnedStateRoundTripsAndLegacyJsonDefaultsToExpanded()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        var store = new DockLayoutStore(workspace.LayoutPath);
        var pinned = CreateValidSnapshot();
        pinned.Tools[0] = pinned.Tools[0] with { IsPinned = true };

        store.Save(pinned);

        var loadedPinned = store.Load();
        Assert.NotNull(loadedPinned);
        Assert.True(loadedPinned.Tools[0].IsPinned);

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var legacyJson = JsonSerializer.Serialize(
                CreateValidSnapshot(),
                serializerOptions)
            .Replace("\"isPinned\":false,", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("isPinned", legacyJson, StringComparison.Ordinal);
        File.WriteAllText(workspace.LayoutPath, legacyJson);

        var loadedLegacy = store.Load();
        Assert.NotNull(loadedLegacy);
        Assert.False(loadedLegacy.Tools[0].IsPinned);
        Assert.True(loadedLegacy.Tools[0].IsVisible);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void InvalidPinnedStateIsRejected(bool isVisible, bool isFloating)
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Tools[0] = snapshot.Tools[0] with
        {
            IsVisible = isVisible,
            IsPinned = true,
            IsFloating = isFloating,
            FloatingBounds = isFloating
                ? new DockFloatingBoundsV1
                {
                    X = 10,
                    Y = 10,
                    Width = 300,
                    Height = 300
                }
                : null
        };

        var error = DockLayoutSnapshotValidator.Validate(snapshot);

        Assert.NotNull(error);
        Assert.Equal("LAYOUT_PINNED_STATE_INVALID", error.Value.Code);
    }

    private static DockLayoutSnapshotV1 CreateValidSnapshot() =>
        new()
        {
            Panes =
            [
                new DockPaneSnapshotV1
                {
                    Id = DockLayoutIds.LeftPane,
                    Proportion = 0.2
                },
                new DockPaneSnapshotV1
                {
                    Id = DockLayoutIds.RightPane,
                    Proportion = 0.2
                }
            ],
            Tools =
            [
                new DockToolSnapshotV1
                {
                    Id = "fileSystemTree",
                    DockId = DockLayoutIds.LeftTools,
                    Order = 0,
                    IsVisible = true,
                    IsFloating = false
                }
            ],
            ActiveToolId = "fileSystemTree"
        };

    private static IEnumerable<string> EnumerateInvalidBackups(string directoryPath) =>
        Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".invalid.bak", StringComparison.Ordinal));

    private sealed class TemporaryLayoutWorkspace : IDisposable
    {
        public TemporaryLayoutWorkspace()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"myavalonia-layout-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            LayoutPath = Path.Combine(DirectoryPath, DockLayoutStore.LayoutFileName);
        }

        public string DirectoryPath { get; }

        public string LayoutPath { get; }

        public void Dispose()
        {
            var fullPath = Path.GetFullPath(DirectoryPath);
            var tempPath = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!fullPath.StartsWith(
                    tempPath + Path.DirectorySeparatorChar + "myavalonia-layout-tests-",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("拒绝清理测试工作区以外的目录。");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
