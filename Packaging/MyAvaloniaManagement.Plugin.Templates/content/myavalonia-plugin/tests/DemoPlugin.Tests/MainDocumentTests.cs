using DemoPlugin.Features.Main;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace DemoPlugin.Tests;

public sealed class MainDocumentTests
{
    [Fact]
    public async Task 初始化时采用Host提供的标题()
    {
        var document = new MainDocument();

        await document.InitializeAsync(
            new NewDocumentActivation("测试标题"),
            CancellationToken.None);

        Assert.Equal("测试标题", document.Presentation.Title);
    }
}
