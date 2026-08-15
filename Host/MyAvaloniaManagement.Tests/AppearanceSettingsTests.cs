using System.Text.Json;
using Avalonia.Styling;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证外观设置的容错持久化和主题模式映射。
/// </summary>
public sealed class AppearanceSettingsTests
{
    [Fact]
    public void 设置文件不存在时使用跟随系统()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);

        Assert.Equal(ApplicationThemeMode.System, store.Load());
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void 三种主题模式可以往返保存(string modeName)
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var expected = Enum.Parse<ApplicationThemeMode>(modeName);

        Assert.True(store.Save(expected));

        Assert.Equal(expected, store.Load());
        using var document = JsonDocument.Parse(
            File.ReadAllText(store.SettingsPath));
        Assert.Equal(
            1,
            document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            expected.ToString(),
            document.RootElement.GetProperty("theme").GetString());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("""{"schemaVersion":2,"theme":"Dark"}""")]
    [InlineData("""{"schemaVersion":1,"theme":"Blue"}""")]
    public void 损坏或未知设置回退系统并隔离原文件(string content)
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        File.WriteAllText(store.SettingsPath, content);

        Assert.Equal(ApplicationThemeMode.System, store.Load());

        Assert.False(File.Exists(store.SettingsPath));
        Assert.Single(Directory.GetFiles(
            directory.Path,
            "appearance-v1.*.invalid.bak"));
    }

    [Fact]
    public void 写入目标不可用时返回失败且不抛出()
    {
        using var directory = new TemporaryDirectory();
        var errors = new List<string>();
        var store = new AppearanceSettingsStore(
            directory.Path,
            errors.Add);

        Assert.False(store.Save(ApplicationThemeMode.Dark));
        Assert.Contains("APPEARANCE_WRITE_IO_FAILED", errors);
    }

    [Theory]
    [InlineData("System", "Default")]
    [InlineData("Light", "Light")]
    [InlineData("Dark", "Dark")]
    public void 主题模式映射到Avalonia主题变体(
        string modeName,
        string expectedKey) =>
        Assert.Equal(
            expectedKey,
            ApplicationThemeService.ToThemeVariant(
                Enum.Parse<ApplicationThemeMode>(modeName)).Key);

    [Fact]
    public void 主题服务即使尚未绑定应用也会保存选择()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var service = new ApplicationThemeService(store);

        service.SetMode(ApplicationThemeMode.Dark);

        Assert.Equal(ApplicationThemeMode.Dark, service.CurrentMode);
        Assert.Equal(ApplicationThemeMode.Dark, store.Load());
    }

    [Fact]
    public void 自动化数据目录会重定向外观设置文件()
    {
        using var directory = new TemporaryDirectory();

        var path = Path.Combine(
            HostDataRootPolicy.Resolve(
                directory.Path,
                Path.Combine(directory.Path, "unused")),
            AppearanceSettingsStore.SettingsFileName);

        Assert.Equal(
            Path.Combine(
                directory.Path,
                AppearanceSettingsStore.SettingsFileName),
            path);
    }

    private static AppearanceSettingsStore CreateStore(string directory) =>
        new(Path.Combine(
            directory,
            AppearanceSettingsStore.SettingsFileName));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MyAvaloniaManagement.AppearanceTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
