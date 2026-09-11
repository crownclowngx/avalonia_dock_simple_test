using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Icons;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>不启动 Avalonia 即可验证图标数据契约与旧注册接口的二进制扩展边界。</summary>
public sealed class IconContractTests
{
    [Theory]
    [InlineData(0, 24)]
    [InlineData(-1, 24)]
    [InlineData(24, 0)]
    [InlineData(double.NaN, 24)]
    [InlineData(24, double.NaN)]
    [InlineData(double.PositiveInfinity, 24)]
    [InlineData(24, double.NegativeInfinity)]
    public void 画布只接受有限正数(double width, double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorIconDefinition("M0,0", width, height));

    [Fact]
    public void 字段校验与几何解析分阶段且值不可修改()
    {
        Assert.Throws<ArgumentNullException>(() => new VectorIconDefinition(null!, 24, 24));
        Assert.Throws<ArgumentException>(() => new VectorIconDefinition(" ", 24, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorIconDefinition("M0,0", 24, 24, (IconFillRule)9));
        var value = new VectorIconDefinition("由 UI 阶段识别坏路径", 32, 16, IconFillRule.NonZero);
        Assert.Equal(32, value.ViewBoxWidth);
        Assert.Equal(16, value.ViewBoxHeight);
        Assert.Equal(IconFillRule.NonZero, value.FillRule);
        Assert.Equal("由 UI 阶段识别坏路径", value.PathData);
        Assert.All(typeof(VectorIconDefinition).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void 旧接口无需增加成员且缺少可选能力时错误清楚()
    {
        IPluginRegistration legacy = new LegacyRegistration();
        var value = new VectorIconDefinition("M0,0 H24 V24 Z", 24, 24);
        Assert.DoesNotContain(typeof(IPluginRegistration).GetMethods(), method => method.Name == "AddIcon");
        Assert.Contains("3.4.0", Assert.Throws<NotSupportedException>(() => legacy.AddIcon("sample", value)).Message);
        Assert.Throws<ArgumentNullException>(() => IconRegistrationExtensions.AddIcon(null!, "sample", value));
        Assert.Throws<ArgumentNullException>(() => legacy.AddIcon("sample", null!));
        var capable = new IconRegistration();
        Assert.Equal("accepted:sample", ((IPluginRegistration)capable).AddIcon("sample", value));
        Assert.Same(value, capable.Value);
    }

    [Fact]
    public void 公共资源保持稳定键只读集合且程序集不依赖SDK或UI()
    {
        var expected = new[] { CommonIcons.Module, CommonIcons.Folder, CommonIcons.Table, CommonIcons.Chart,
            CommonIcons.TextCheck, CommonIcons.Image, CommonIcons.Video, CommonIcons.Download };
        Assert.Equal(expected, CommonIcons.All);
        Assert.Equal(expected.Length, CommonIcons.All.Select(asset => asset.Key).Distinct().Count());
        Assert.All(CommonIcons.All, asset =>
        {
            Assert.StartsWith("builtin:", asset.Key);
            Assert.False(string.IsNullOrWhiteSpace(asset.PathData));
            Assert.True(double.IsFinite(asset.ViewBoxWidth) && asset.ViewBoxWidth > 0);
            Assert.True(double.IsFinite(asset.ViewBoxHeight) && asset.ViewBoxHeight > 0);
        });
        Assert.All(typeof(CommonIconAsset).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Throws<NotSupportedException>(() => ((IList<CommonIconAsset>)CommonIcons.All).Clear());
        Assert.All(typeof(CommonIcons).Assembly.GetReferencedAssemblies(), assembly =>
            Assert.StartsWith("System.", assembly.Name));
    }

    private class LegacyRegistration : IPluginRegistration
    {
        public PluginId PluginId { get; } = new("myavalonia.plugin.icon-contract");
        public IServiceCollection Services { get; } = new ServiceCollection();
        public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle { }
        public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPluginDocument where TView : Control, new() { }
        public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPersistablePluginDocument where TView : Control, new() { }
        public void AddTool<TTool, TView>(ToolDescriptor descriptor)
            where TTool : class where TView : Control, new() { }
    }

    private sealed class IconRegistration : LegacyRegistration, IPluginIconRegistration
    {
        internal VectorIconDefinition? Value { get; private set; }
        public string AddIcon(string localName, VectorIconDefinition definition)
        {
            Value = definition;
            return $"accepted:{localName}";
        }
    }
}
