using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>从真实文件的往返、故障及竞争验证开关意图，避免只比较序列化实现的内部字段。</summary>
public sealed class PluginEnablementSettingsTests
{
    private static readonly PluginId First = new("myavalonia.plugin.first");
    private static readonly PluginId Missing = new("myavalonia.plugin.not-installed");

    [Fact]
    public void 首次默认启用且不同数据根独立并保留未安装身份()
    {
        using var files = new EnablementTestFiles();
        var initial = files.Store.Load();
        Assert.True(initial.CanWrite);
        Assert.True(initial.Settings!.IsEnabled(First));
        Assert.False(File.Exists(files.Path));
        var saved = files.Store.TrySave(initial, new([Missing, First, First]));
        Assert.True(saved.Success);
        Assert.Equal(2, files.Store.Load().Settings!.DisabledPluginIds.Count);
        var enabled = files.Store.TrySave(saved.Snapshot, saved.Snapshot.Settings!.WithEnabled(First, true));
        Assert.True(enabled.Success);
        Assert.Equal([Missing], files.Store.Load().Settings!.DisabledPluginIds);
        Assert.Contains(First.Value, File.ReadAllText(files.Path + ".bak"));
        var other = new PluginEnablementSettingsStore(System.IO.Path.Combine(files.Directory, "other", "settings.json"));
        Assert.True(other.Load().Settings!.IsEnabled(Missing));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"disabledPluginIds\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"disabledPluginIds\":[\"invalid id\"]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"disabledPluginIds\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"disabledPluginIds\":[],\"typo\":true}")]
    [InlineData("{\"schemaVersion\":1,\"disabledPluginIds\":null}")]
    [InlineData("{\"schemaVersion\":1,\"disabledPluginIds\":[],}")]
    public void 无可靠配置时保留原件且不得回退全启用(string json)
    {
        using var files = new EnablementTestFiles();
        File.WriteAllText(files.Path, json);
        var result = files.Store.Load();
        Assert.Null(result.Settings);
        Assert.False(result.CanWrite);
        Assert.False(files.Store.TrySave(result, new([])).Success);
        Assert.Equal(json, File.ReadAllText(files.Path));
    }

    [Fact]
    public void 损坏主文件恢复有效备份但未知未来格式不能回退()
    {
        using var files = new EnablementTestFiles();
        var first = files.Store.TrySave(files.Store.Load(), new([First]));
        Assert.True(files.Store.TrySave(first.Snapshot, new([Missing])).Success);
        File.WriteAllText(files.Path, "broken-private-path-canary");
        var restored = files.Store.Load();
        Assert.False(restored.CanWrite);
        Assert.False(restored.Settings!.IsEnabled(First));
        Assert.DoesNotContain("canary", restored.Message);
        Assert.Equal("broken-private-path-canary", File.ReadAllText(files.Path));
        File.Delete(files.Path);
        Assert.False(files.Store.Load().CanWrite);
        Assert.False(files.Store.Load().Settings!.IsEnabled(First));
        File.WriteAllText(files.Path, "{\"schemaVersion\":99,\"disabledPluginIds\":[]}");
        Assert.Null(files.Store.Load().Settings);
        Assert.Contains("版本", files.Store.Load().Message);
    }

    [Fact]
    public void 原子提交前失败保留旧文件与快照且清理临时文件()
    {
        using var files = new EnablementTestFiles();
        var saved = files.Store.TrySave(files.Store.Load(), new([First]));
        var bytes = File.ReadAllBytes(files.Path);
        var failing = new PluginEnablementSettingsStore(files.Path, () => throw new IOException("secret-canary"));
        var failed = failing.TrySave(saved.Snapshot, new([Missing]));
        Assert.False(failed.Success);
        Assert.Same(saved.Snapshot, failed.Snapshot);
        Assert.Equal(bytes, File.ReadAllBytes(files.Path));
        Assert.Empty(System.IO.Directory.GetFiles(files.Directory, "*.tmp"));
        Assert.DoesNotContain("canary", failed.Message);
    }

    [Fact]
    public void 两个实例不能用旧快照覆盖最新意图且锁竞争不改变文件()
    {
        using var files = new EnablementTestFiles();
        var other = new PluginEnablementSettingsStore(files.Path);
        var stale = other.Load();
        Assert.True(files.Store.TrySave(files.Store.Load(), new([First])).Success);
        var conflict = other.TrySave(stale, new([Missing]));
        Assert.False(conflict.Success);
        Assert.Equal("PLUGIN_ENABLEMENT_CONFLICT", conflict.ErrorCode);
        var latest = other.Load();
        using (var lease = new FileStream(files.Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.False(other.TrySave(latest, new([Missing])).Success);
        Assert.True(other.TrySave(latest, latest.Settings!.WithEnabled(Missing, false)).Success);
        Assert.False(files.Store.Load().Settings!.IsEnabled(First));
        Assert.False(files.Store.Load().Settings!.IsEnabled(Missing));
    }

    [Fact]
    public void 文件大小限制和读取错误不伪装首次使用()
    {
        using var files = new EnablementTestFiles();
        File.WriteAllText(files.Path, new string(' ', 1024 * 1024 + 1));
        Assert.Null(files.Store.Load().Settings);
        File.Delete(files.Path);
        System.IO.Directory.CreateDirectory(files.Path);
        Assert.Null(files.Store.Load().Settings);
        Assert.False(files.Store.Load().CanWrite);
    }

    [Fact]
    public void 设置集合不可被调用方反向修改且稳定按身份保存()
    {
        using var files = new EnablementTestFiles();
        var input = new List<PluginId> { Missing, First };
        var settings = new PluginEnablementSettings(input);
        input.Clear();
        Assert.Equal(2, settings.DisabledPluginIds.Count);
        Assert.True(files.Store.TrySave(files.Store.Load(), settings).Success);
        var json = File.ReadAllText(files.Path);
        Assert.True(json.IndexOf(First.Value, StringComparison.Ordinal) < json.IndexOf(Missing.Value, StringComparison.Ordinal));
    }
}

/// <summary>每例只拥有自己的临时根；不使用生产默认路径，释放时仅删除该根。</summary>
internal sealed class EnablementTestFiles : IDisposable
{
    internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enablement-tests", Guid.NewGuid().ToString("N"));
    internal string Path => System.IO.Path.Combine(Directory, PluginEnablementSettingsStore.FileName);
    internal PluginEnablementSettingsStore Store { get; }
    internal EnablementTestFiles()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Store = new(Path);
    }
    public void Dispose() => System.IO.Directory.Delete(Directory, true);
}
