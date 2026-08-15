using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using MyAvaloniaManagement.Business.Presentation;

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
        AppBuilder.Configure(() => new App(NoOpDesktopShell.Instance))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true
            });

    /// <summary>
    /// Headless 套件只需要生产 App 资源，不创建第二套主窗口和根容器。
    /// 显式空 Shell 让测试意图可见，也避免为框架测试恢复 App 无参构造。
    /// </summary>
    private sealed class NoOpDesktopShell : IHostDesktopShell
    {
        internal static NoOpDesktopShell Instance { get; } = new();

        public void Attach(
            App application,
            IClassicDesktopStyleApplicationLifetime desktop)
        {
        }
    }
}
