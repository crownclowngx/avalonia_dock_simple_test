using MyAvaloniaManagement.Business.Navigation;

namespace MyAvaloniaManagement.Tests;

/// <summary>使用独立临时目录验证偏好格式、中文自定义名称、损坏保留与写入失败降级。</summary>
public sealed class PluginNavigationSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "navigation-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, PluginNavigationSettingsStore.FileName);

    [Fact]
    public void 缺省旧版且自定义名称与模式一起原子保存()
    {
        var store = new PluginNavigationSettingsStore(SettingsPath);
        Assert.Equal(PluginMenuMode.Legacy, store.Load().Mode);
        Assert.False(Directory.Exists(_directory));
        var settings = new PluginNavigationSettings(PluginMenuMode.Tree, "经典目录", "业务树");
        Assert.True(store.Save(settings));
        Assert.Equal(settings, store.Load());
        Assert.True(store.Save(settings with { Mode = PluginMenuMode.Legacy }));
        Assert.Equal("业务树", store.Load().TreeName);
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Contains("\"pluginMenuMode\": \"legacy\"", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":9}")]
    public void 损坏设置保留原文件并回退旧版(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, content);
        var store = new PluginNavigationSettingsStore(SettingsPath);
        Assert.Equal(PluginMenuMode.Legacy, store.Load().Mode);
        var backup = Assert.Single(Directory.GetFiles(_directory, "*.invalid.bak"));
        Assert.Equal(content, File.ReadAllText(backup));
        Assert.True(store.Save(new()));
    }

    [Fact]
    public void 未知模式和空白名称各自回退且有效别名保留()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """
            {"schemaVersion":1,"pluginMenuMode":"future","modeDisplayNames":{"legacy":" ","tree":"  业务树  "}}
            """);
        var settings = new PluginNavigationSettingsStore(SettingsPath).Load();
        Assert.Equal(PluginMenuMode.Legacy, settings.Mode);
        Assert.Equal("旧版（平铺）", settings.LegacyName);
        Assert.Equal("业务树", settings.TreeName);
    }

    [Fact]
    public void 无法写入时报告失败但不抛异常()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "作为父目录的普通文件");
        var messages = new List<string>();
        var store = new PluginNavigationSettingsStore(Path.Combine(SettingsPath, "nested.json"), messages.Add);
        Assert.False(store.Save(new(PluginMenuMode.Tree)));
        Assert.Contains("NAVIGATION_WRITE_FAILED", messages);
        Assert.Equal("作为父目录的普通文件", File.ReadAllText(SettingsPath));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
