using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(
    MyAvaloniaManagement.UiTests.TestAppBuilder))]

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 为 xUnit v3 测试构建加载生产 <see cref="App"/> 的 Avalonia Headless 应用。
/// </summary>
/// <remarks>
/// 开启 HeadlessDrawing 后可以实例化真实控件、主题和 Dock 样式，
/// 但不依赖显示器、显卡驱动或像素截图。
/// </remarks>
public static class TestAppBuilder
{
    /// <summary>
    /// 创建供 AvaloniaTest 使用的无界面应用构建器。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true
            });
}
