using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.Tests;

/// <summary>用独立临时数据根验证当前布局文件事务、旧输入忽略边界及独占句柄；不触碰用户布局。</summary>
public sealed class DockLayoutV3StoreTests
{
    [Fact]
    public void 原子更新保留上一有效布局且不残留临时文件()
    {
        using var directory = new DataRoot();
        using var store = directory.Open();
        var first = DockLayoutV3Tests.Sample();
        var next = first with { MainWindow = first.MainWindow with { Bounds = first.MainWindow.Bounds with { X = 240 } } };
        Assert.True(store.Save(first));
        Assert.True(store.Save(next));
        Assert.Equal(DockLayoutV3Tests.Write(next), DockLayoutV3Tests.Write(store.Load()!));
        Assert.Equal(DockLayoutV3Tests.Write(first), File.ReadAllText(store.BackupPath));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("broken")]
    [InlineData("locked")]
    public async Task 旧布局不参与恢复或诊断且仍可保存V3(string scenario)
    {
        using var context = new TestHostContext();
        // 这个真实旧输入要求显示插件菜单，明显不同于默认全隐藏布局。
        // 只验证旧字节不变无法排除“读取后在内存迁移”，因此必须经过实际恢复及最终保存。
        const string oldVisible = """
            {"schemaVersion":2,"panes":[{"id":"RightPane","proportion":0.73}],
             "tools":[{"id":"myavalonia.host.tool.plugin-menu","dockId":"RightTools","order":0,"isVisible":true,"isPinned":false}],
             "activeToolId":"myavalonia.host.tool.plugin-menu"}
            """;
        Assert.Contains(HostExtensionIds.PluginMenu.Value, oldVisible);
        var oldPaths = new[] { "layout-v1.json", "layout-v2.json" }
            .Select(name => Path.Combine(context.TempDirectory, name)).ToArray();
        foreach (var path in oldPaths) File.WriteAllText(path, scenario == "broken" ? "{broken" : oldVisible);
        var originalBytes = oldPaths.Select(File.ReadAllBytes).ToArray();
        var diagnostics = new List<string>();
        using var store = new DockLayoutV3Store(context.TempDirectory, (code, _) => diagnostics.Add(code));
        using var lifecycle = new DockLayoutLifecycle(store);
        FileStream[] handles = [];
        try
        {
            if (scenario == "locked")
                handles = oldPaths.Select(path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)).ToArray();
            var defaultRoot = lifecycle.Prepare(context.Workspace);
            Assert.Same(defaultRoot, lifecycle.ApplyPending(context.Workspace));
            Assert.Contains(HostExtensionIds.PluginMenu.Value, context.Workspace.CreatedTools.Keys);
            Assert.NotEmpty(context.Workspace.CreatedTools);
            Assert.All(context.Workspace.CreatedTools.Values, tool =>
            {
                Assert.False(DockTreeNavigator.IsDockableAttached(defaultRoot, tool));
                Assert.False(DockTreeNavigator.IsToolPinned(defaultRoot, tool));
            });
            Assert.True(store.CanWrite);
            Assert.True(lifecycle.Save(context.Workspace));
            await lifecycle.FlushAsync();
            var saved = Assert.IsType<DockLayoutSnapshotV3>(store.Load());
            Assert.Equal(3, saved.SchemaVersion);
            Assert.All(saved.Tools, tool => Assert.Equal("hidden", tool.State));
            Assert.Empty(diagnostics);
            Assert.Empty(lifecycle.Message);
            Assert.Empty(Directory.EnumerateFiles(context.TempDirectory, "*.invalid.bak"));
            Assert.Equal(new[] { "layout-v1.json", "layout-v2.json", "layout-v3.json" },
                Directory.EnumerateFiles(context.TempDirectory, "layout-*.json").Select(Path.GetFileName).Order());
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
        for (var index = 0; index < oldPaths.Length; index++)
            Assert.Equal(originalBytes[index], File.ReadAllBytes(oldPaths[index]));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":3,//comment\n}")]
    [InlineData("{\"schemaVersion\":3,}")]
    [InlineData("{not-json:password=ShouldNeverReachLogs}")]
    public void 损坏V3保留原件和诊断副本且错误不回显原文(string json)
    {
        using var directory = new DataRoot();
        var diagnostics = new List<(string Code, Exception? Error)>();
        using var store = new DockLayoutV3Store(directory.Path, (code, error) => diagnostics.Add((code, error)));
        File.WriteAllText(store.LayoutPath, json);
        Assert.Null(store.Load());
        Assert.Equal(json, File.ReadAllText(store.LayoutPath));
        Assert.Equal(json, File.ReadAllText(Assert.Single(Directory.EnumerateFiles(directory.Path, "*.invalid.bak"))));
        var reported = Assert.Single(diagnostics);
        Assert.Equal("LAYOUT_JSON_INVALID", reported.Code);
        var error = Assert.IsType<DockLayoutFormatException>(reported.Error);
        Assert.Equal(reported.Code, error.Message);
        Assert.Null(error.InnerException);
        Assert.Null(error.StableId);
        Assert.DoesNotContain(json, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 主文件坏时先恢复有效V3备份并保留损坏原件()
    {
        using var directory = new DataRoot();
        using var store = directory.Open();
        var snapshot = DockLayoutV3Tests.Sample();
        File.WriteAllText(store.LayoutPath, "broken-v3");
        File.WriteAllText(store.BackupPath, DockLayoutV3Tests.Write(snapshot));
        File.WriteAllText(directory.File("layout-v2.json"), "must-not-read");
        Assert.Equal(DockLayoutV3Tests.Write(snapshot), DockLayoutV3Tests.Write(store.Load()!));
        Assert.Contains(Directory.EnumerateFiles(directory.Path, "*.invalid.bak"), path => File.ReadAllText(path) == "broken-v3");
        Assert.True(store.Save(snapshot));
        Assert.Equal(DockLayoutV3Tests.Write(snapshot), File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void 双坏或只剩隔离历史时不会重新导入过期V2()
    {
        using var directory = new DataRoot();
        using var store = directory.Open();
        File.WriteAllText(store.LayoutPath, "bad-main");
        File.WriteAllText(store.BackupPath, "bad-backup");
        File.WriteAllText(directory.File("layout-v2.json"), "{\"schemaVersion\":2,\"panes\":[],\"tools\":[],\"activeToolId\":null}");
        Assert.Null(store.Load());
        Assert.Equal(2, Directory.EnumerateFiles(directory.Path, "*.invalid.bak").Count());
        File.Delete(store.LayoutPath);
        File.Delete(store.BackupPath);
        Assert.Null(store.Load());
        Assert.True(store.Save(DockLayoutV3Tests.Sample()));
        Assert.Equal(3, store.Load()!.SchemaVersion);
    }

    [Fact]
    public void 损坏备份先隔离再由有效主文件更新()
    {
        using var directory = new DataRoot();
        using var store = directory.Open();
        var snapshot = DockLayoutV3Tests.Sample();
        Assert.True(store.Save(snapshot));
        File.WriteAllText(store.BackupPath, "bad-backup");
        Assert.True(store.Save(snapshot));
        Assert.Equal(DockLayoutV3Tests.Write(snapshot), File.ReadAllText(store.BackupPath));
        Assert.Equal("bad-backup", File.ReadAllText(Assert.Single(Directory.EnumerateFiles(directory.Path, "*.invalid.bak"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 未来格式保持只读不隔离不降级(bool inBackup)
    {
        using var directory = new DataRoot();
        using var store = directory.Open();
        const string future = "{\"schemaVersion\":4,\"future\":true}";
        File.WriteAllText(inBackup ? store.BackupPath : store.LayoutPath, future);
        Assert.Null(store.Load());
        Assert.False(store.CanWrite);
        Assert.False(store.Save(DockLayoutV3Tests.Sample()));
        Assert.Equal(future, File.ReadAllText(inBackup ? store.BackupPath : store.LayoutPath));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.invalid.bak"));
    }

    [Fact]
    public void 同数据根只有一个写入者且只读会话不自动接管或隔离()
    {
        using var directory = new DataRoot();
        using var first = directory.Open();
        using var second = directory.Open();
        Assert.True(first.CanWrite);
        Assert.False(second.CanWrite);
        File.WriteAllText(first.LayoutPath, "bad-main");
        Assert.Null(second.Load());
        Assert.False(second.Save(DockLayoutV3Tests.Sample()));
        Assert.Equal("bad-main", File.ReadAllText(first.LayoutPath));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.invalid.bak"));
        first.Dispose();
        Assert.False(second.CanWrite);
        using var third = directory.Open();
        Assert.True(third.CanWrite);
        using var otherRoot = new DataRoot();
        using var other = otherRoot.Open();
        Assert.True(other.CanWrite);
    }

    [Fact]
    public void 备份提交失败保留有效主文件并清理临时文件()
    {
        using var directory = new DataRoot();
        using var store = directory.Open();
        var original = DockLayoutV3Tests.Sample();
        Assert.True(store.Save(original));
        // 在专属临时根中制造目标目录冲突，真实触发原子文件提交失败，不伪造成功返回。
        Directory.CreateDirectory(store.BackupPath);
        Assert.ThrowsAny<IOException>(() => store.Save(original));
        Assert.Equal(DockLayoutV3Tests.Write(original), File.ReadAllText(store.LayoutPath));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void 已占用文件加载失败后保持本会话只读()
    {
        using var directory = new DataRoot();
        using var store = directory.Open();
        Assert.True(store.Save(DockLayoutV3Tests.Sample()));
        using (var external = new FileStream(store.LayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(store.Load());
        Assert.False(store.CanWrite);
        Assert.False(store.Save(DockLayoutV3Tests.Sample()));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.invalid.bak"));
    }

    private sealed class DataRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mam-layout-v3-" + Guid.NewGuid().ToString("N"));
        internal DataRoot() => Directory.CreateDirectory(Path);
        internal string File(string name) => System.IO.Path.Combine(Path, name);
        internal DockLayoutV3Store Open() => new(Path, (_, _) => { });
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
