using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

/// <summary>用独立临时数据根验证实际文件事务、迁移及独占句柄；不触碰用户布局。</summary>
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
    [InlineData(false)]
    [InlineData(true)]
    public void V2只读转换并保留V1和V2原始字节(bool invalid)
    {
        using var directory = new DataRoot();
        var original = invalid ? "{broken" : "{\"schemaVersion\":2,\"panes\":[],\"tools\":[],\"activeToolId\":null}";
        File.WriteAllText(directory.File("layout-v1.json"), "v1-original");
        File.WriteAllText(directory.File("layout-v2.json"), original);
        using var store = directory.Open();
        var migrated = store.Load();
        if (invalid) Assert.Null(migrated);
        else
        {
            Assert.Equal(3, migrated!.SchemaVersion);
            Assert.True(store.Save(migrated));
        }
        Assert.Equal(original, File.ReadAllText(directory.File("layout-v2.json")));
        Assert.Equal("v1-original", File.ReadAllText(directory.File("layout-v1.json")));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.invalid.bak"));
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
