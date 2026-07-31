using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.Views.Hello;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class WelcomeViewTests
{
    [AvaloniaFact]
    public void WelcomeViewLoadsVectorContentAndCompiledBindings()
    {
        var view = new WelcomeView
        {
            DataContext = new WelcomeViewModel()
        };

        view.Measure(new Size(1100, 720));
        view.Arrange(new Rect(0, 0, 1100, 720));

        Assert.NotNull(view.FindControl<Border>("HeroVisual")?.Parent);
        Assert.True(view.GetLogicalDescendants().OfType<PathIcon>().Count() >= 8);
        Assert.Contains(
            view.GetLogicalDescendants().OfType<TextBlock>(),
            text => text.Text?.Contains("桌面工作空间") == true);
    }

    [AvaloniaFact]
    public void WelcomeViewSwitchesBetweenCompactAndWideLayouts()
    {
        var view = new WelcomeView
        {
            DataContext = new WelcomeViewModel()
        };

        view.Measure(new Size(680, 900));
        view.Arrange(new Rect(0, 0, 680, 900));

        Assert.Contains("compact", view.Classes);
        Assert.Equal(3, view.FindControl<Grid>("FeatureGrid")!.RowDefinitions.Count);

        view.Measure(new Size(1000, 720));
        view.Arrange(new Rect(0, 0, 1000, 720));

        Assert.DoesNotContain("compact", view.Classes);
        Assert.Single(view.FindControl<Grid>("FeatureGrid")!.RowDefinitions);
    }

    [AvaloniaFact]
    public void WelcomeViewProvidesDistinctLightAndDarkThemeBrushes()
    {
        var view = new WelcomeView();

        Assert.True(view.TryGetResource(
            "WelcomeCardBrush",
            ThemeVariant.Light,
            out var lightValue));
        Assert.True(view.TryGetResource(
            "WelcomeCardBrush",
            ThemeVariant.Dark,
            out var darkValue));

        var light = Assert.IsType<SolidColorBrush>(lightValue);
        var dark = Assert.IsType<SolidColorBrush>(darkValue);
        Assert.NotEqual(light.Color, dark.Color);
    }
}
