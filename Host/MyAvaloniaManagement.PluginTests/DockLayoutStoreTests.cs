using System.Text.Json;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.PluginTests;

public sealed class DockLayoutStoreTests
{
    [Fact]
    public void 合法V2快照可以原子往返且不留下临时文件()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        var store = new DockLayoutStore(workspace.LayoutPath);
        var snapshot = CreateValidSnapshot();
        store.Save(snapshot);

        var loaded = Assert.IsType<DockLayoutSnapshotV2>(store.Load());

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(snapshot.ActiveToolId, loaded.ActiveToolId);
        Assert.Equal(snapshot.Panes, loaded.Panes);
        Assert.Equal(snapshot.Tools, loaded.Tools);
        Assert.Empty(Directory.EnumerateFiles(workspace.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void 已有V2布局通过原子替换更新()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        var store = new DockLayoutStore(workspace.LayoutPath);
        store.Save(CreateValidSnapshot());
        var updated = CreateValidSnapshot() with { ActiveToolId = null };
        updated.Tools[0] = updated.Tools[0] with { IsVisible = false };

        store.Save(updated);

        var loaded = Assert.IsType<DockLayoutSnapshotV2>(store.Load());
        Assert.Null(loaded.ActiveToolId);
        Assert.False(loaded.Tools[0].IsVisible);
        Assert.Empty(Directory.EnumerateFiles(workspace.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void V1文件不读取不迁移也不隔离()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        var v1Path = Path.Combine(workspace.DirectoryPath, "layout-v1.json");
        const string v1 = "{\"schemaVersion\":1,\"panes\":[],\"tools\":[],\"activeToolId\":null}";
        File.WriteAllText(v1Path, v1);

        Assert.Null(new DockLayoutStore(workspace.LayoutPath).Load());
        Assert.Equal(v1, File.ReadAllText(v1Path));
        Assert.Empty(EnumerateInvalidBackups(workspace.DirectoryPath));
    }

    [Fact]
    public void V2严格拒绝未知重复缺失浮动字段和V1版本()
    {
        var invalidCases = new (string Json, string Code)[]
        {
            ("[]", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":1,\"panes\":[],\"tools\":[],\"activeToolId\":null}", "LAYOUT_SCHEMA_UNSUPPORTED"),
            ("{\"schemaVersion\":\"2\",\"panes\":[],\"tools\":[],\"activeToolId\":null}", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":2,\"schemaVersion\":2,\"panes\":[],\"tools\":[],\"activeToolId\":null}", "LAYOUT_ROOT_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[]}", "LAYOUT_ROOT_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[],\"activeToolId\":null,\"unknown\":0}", "LAYOUT_ROOT_FIELDS_INVALID"),
            ("{\"SchemaVersion\":2,\"panes\":[],\"tools\":[],\"activeToolId\":null}", "LAYOUT_ROOT_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[{\"id\":\"LeftPane\",\"id\":\"LeftPane\",\"proportion\":0.2}],\"tools\":[],\"activeToolId\":null}", "LAYOUT_PANE_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[{\"id\":\"LeftPane\"}],\"tools\":[],\"activeToolId\":null}", "LAYOUT_PANE_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[{\"id\":\"LeftPane\",\"proportion\":0.2,\"extra\":true}],\"tools\":[],\"activeToolId\":null}", "LAYOUT_PANE_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[{\"Id\":\"LeftPane\",\"proportion\":0.2}],\"tools\":[],\"activeToolId\":null}", "LAYOUT_PANE_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[{\"id\":1,\"proportion\":0.2}],\"tools\":[],\"activeToolId\":null}", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[{\"id\":\"LeftPane\",\"proportion\":\"0.2\"}],\"tools\":[],\"activeToolId\":null}", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":{},\"tools\":[],\"activeToolId\":null}", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[{\"id\":\"myavalonia.host.tool.sample\",\"dockId\":\"LeftTools\",\"order\":0,\"isVisible\":true}],\"activeToolId\":null}", "LAYOUT_TOOL_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[{\"id\":\"myavalonia.host.tool.sample\",\"dockId\":\"LeftTools\",\"order\":0,\"isVisible\":true,\"isPinned\":false,\"isPinned\":false}],\"activeToolId\":null}", "LAYOUT_TOOL_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[{\"Id\":\"myavalonia.host.tool.sample\",\"dockId\":\"LeftTools\",\"order\":0,\"isVisible\":true,\"isPinned\":false}],\"activeToolId\":null}", "LAYOUT_TOOL_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[{\"id\":\"myavalonia.host.tool.sample\",\"dockId\":\"LeftTools\",\"order\":0.5,\"isVisible\":true,\"isPinned\":false}],\"activeToolId\":null}", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[{\"id\":\"myavalonia.host.tool.sample\",\"dockId\":\"LeftTools\",\"order\":0,\"isVisible\":\"true\",\"isPinned\":false}],\"activeToolId\":null}", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[{\"id\":\"tool\",\"dockId\":\"LeftTools\",\"order\":0,\"isVisible\":true,\"isPinned\":false,\"isFloating\":true}],\"activeToolId\":null}", "LAYOUT_TOOL_FIELDS_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":\"bad\",\"activeToolId\":null}", "LAYOUT_FIELD_TYPE_INVALID"),
            ("{\"schemaVersion\":2,\"panes\":[],\"tools\":[],\"activeToolId\":42}", "LAYOUT_FIELD_TYPE_INVALID"),
        };

        foreach (var (json, expectedCode) in invalidCases)
        {
            using var workspace = new TemporaryLayoutWorkspace();
            File.WriteAllText(workspace.LayoutPath, json);
            var codes = new List<string>();
            var store = new DockLayoutStore(workspace.LayoutPath, (code, _) => codes.Add(code));

            Assert.Null(store.Load());
            Assert.Contains(expectedCode, codes);
            Assert.Single(EnumerateInvalidBackups(workspace.DirectoryPath));
        }
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,//comment\n\"panes\":[],\"tools\":[],\"activeToolId\":null}")]
    [InlineData("{\"schemaVersion\":2,\"panes\":[],\"tools\":[],\"activeToolId\":null,}")]
    [InlineData("{not-json:password=ShouldNeverReachLogs}")]
    public void 损坏Json整体隔离且日志不包含原文(string json)
    {
        using var workspace = new TemporaryLayoutWorkspace();
        File.WriteAllText(workspace.LayoutPath, json);
        var logs = new List<string>();
        var store = new DockLayoutStore(workspace.LayoutPath, (code, id) => logs.Add($"{code}:{id}"));

        Assert.Null(store.Load());
        Assert.False(File.Exists(workspace.LayoutPath));
        Assert.Single(EnumerateInvalidBackups(workspace.DirectoryPath));
        Assert.Contains(logs, log => log.StartsWith("LAYOUT_JSON_INVALID:", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, log => log.Contains(json, StringComparison.Ordinal));
    }

    [Fact]
    public void 重复顺序非法比例隐藏Pinned和错误活动项均被拒绝()
    {
        var duplicateOrder = CreateValidSnapshot();
        duplicateOrder.Tools.Add(duplicateOrder.Tools[0] with { Id = "myavalonia.host.tool.second" });
        Assert.Equal("LAYOUT_TOOL_ORDER_INVALID", DockLayoutSnapshotValidator.Validate(duplicateOrder)?.Code);

        var invalidPane = CreateValidSnapshot();
        invalidPane.Panes[0] = invalidPane.Panes[0] with { Proportion = double.NaN };
        Assert.Equal("LAYOUT_PANE_PROPORTION_INVALID", DockLayoutSnapshotValidator.Validate(invalidPane)?.Code);

        var invalidPinned = CreateValidSnapshot();
        invalidPinned.Tools[0] = invalidPinned.Tools[0] with { IsVisible = false, IsPinned = true };
        Assert.Equal("LAYOUT_PINNED_STATE_INVALID", DockLayoutSnapshotValidator.Validate(invalidPinned)?.Code);

        var invalidActive = CreateValidSnapshot() with { ActiveToolId = "unknown.tool" };
        Assert.Equal("LAYOUT_ACTIVE_TOOL_INVALID", DockLayoutSnapshotValidator.Validate(invalidActive)?.Code);
    }

    [Fact]
    public void V2结构校验覆盖空对象版本集合和全部Id重复边界()
    {
        Assert.Equal("LAYOUT_EMPTY", DockLayoutSnapshotValidator.Validate(null)?.Code);
        Assert.Equal(
            "LAYOUT_SCHEMA_UNSUPPORTED",
            DockLayoutSnapshotValidator.Validate(
                CreateValidSnapshot() with { SchemaVersion = 1 })?.Code);
        Assert.Equal(
            "LAYOUT_COLLECTION_INVALID",
            DockLayoutSnapshotValidator.Validate(
                CreateValidSnapshot() with { Panes = null! })?.Code);

        var invalidPaneId = CreateValidSnapshot();
        invalidPaneId.Panes[0] = invalidPaneId.Panes[0] with { Id = " " };
        Assert.Equal("LAYOUT_PANE_ID_INVALID", DockLayoutSnapshotValidator.Validate(invalidPaneId)?.Code);

        var duplicatePane = CreateValidSnapshot();
        duplicatePane.Panes.Add(duplicatePane.Panes[0] with { });
        Assert.Equal("LAYOUT_PANE_ID_DUPLICATE", DockLayoutSnapshotValidator.Validate(duplicatePane)?.Code);

        var invalidToolId = CreateValidSnapshot();
        invalidToolId.Tools[0] = invalidToolId.Tools[0] with { Id = "包含中文" };
        Assert.Equal("LAYOUT_TOOL_ID_INVALID", DockLayoutSnapshotValidator.Validate(invalidToolId)?.Code);

        var duplicateTool = CreateValidSnapshot();
        duplicateTool.Tools.Add(duplicateTool.Tools[0] with { Order = 1 });
        Assert.Equal("LAYOUT_TOOL_ID_DUPLICATE", DockLayoutSnapshotValidator.Validate(duplicateTool)?.Code);

        var invalidDockId = CreateValidSnapshot();
        invalidDockId.Tools[0] = invalidDockId.Tools[0] with { DockId = "bad/dock" };
        Assert.Equal("LAYOUT_TOOL_DOCK_ID_INVALID", DockLayoutSnapshotValidator.Validate(invalidDockId)?.Code);
    }

    [Fact]
    public void 快照模型只包含V2结构字段和原生布尔状态()
    {
        using var workspace = new TemporaryLayoutWorkspace();
        new DockLayoutStore(workspace.LayoutPath).Save(CreateValidSnapshot());
        var json = File.ReadAllText(workspace.LayoutPath);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            ["schemaVersion", "panes", "tools", "activeToolId"],
            document.RootElement.EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain("floating", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 严格Json编解码器拒绝空流和空快照参数()
    {
        Assert.Throws<ArgumentNullException>(() => DockLayoutSnapshotV2Json.Read(null!));
        Assert.Throws<ArgumentNullException>(() => DockLayoutSnapshotV2Json.Write(null!, CreateValidSnapshot()));
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => DockLayoutSnapshotV2Json.Write(stream, null!));
    }

    private static DockLayoutSnapshotV2 CreateValidSnapshot() =>
        new()
        {
            Panes = [new DockPaneSnapshotV2 { Id = DockLayoutIds.LeftPane, Proportion = 0.2 }],
            Tools =
            [
                new DockToolSnapshotV2
                {
                    Id = "myavalonia.host.tool.file-system-tree",
                    DockId = DockLayoutIds.LeftTools,
                    Order = 0,
                    IsVisible = true,
                    IsPinned = false,
                },
            ],
            ActiveToolId = "myavalonia.host.tool.file-system-tree",
        };

    private static IEnumerable<string> EnumerateInvalidBackups(string directoryPath) =>
        Directory.EnumerateFiles(directoryPath, "*.invalid.bak");

    private sealed class TemporaryLayoutWorkspace : IDisposable
    {
        internal TemporaryLayoutWorkspace()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"myavalonia-layout-v2-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            LayoutPath = Path.Combine(DirectoryPath, DockLayoutStore.LayoutFileName);
        }

        internal string DirectoryPath { get; }
        internal string LayoutPath { get; }

        public void Dispose()
        {
            var fullPath = Path.GetFullPath(DirectoryPath);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(tempRoot + "myavalonia-layout-v2-tests-", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("拒绝清理布局测试工作区以外的目录。");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
