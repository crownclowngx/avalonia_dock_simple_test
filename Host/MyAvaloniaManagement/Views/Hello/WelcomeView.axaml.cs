using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace MyAvaloniaManagement.Views.Hello;

public partial class WelcomeView : UserControl
{
    private const double CompactWidth = 760;

    public WelcomeView()
    {
        InitializeComponent();

        SizeChanged += (_, args) => UpdateResponsiveLayout(args.NewSize.Width);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs args) =>
        SetMotionEnabled(true);

    private void OnDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs args) =>
        SetMotionEnabled(false);

    private void SetMotionEnabled(bool enabled)
    {
        IntroBlock.Classes.Set("motion-enter-1", enabled);
        HeroVisual.Classes.Set("motion-enter-2", enabled);
        FeatureCardOne.Classes.Set("motion-enter-3", enabled);
        FeatureCardTwo.Classes.Set("motion-enter-3", enabled);
        FeatureCardThree.Classes.Set("motion-enter-3", enabled);

        Orbit.Classes.Set("motion-orbit", enabled);
        DockCore.Classes.Set("motion-core", enabled);
        NodeA.Classes.Set("motion-node-a", enabled);
        NodeB.Classes.Set("motion-node-b", enabled);
        NodeC.Classes.Set("motion-node-b", enabled);
        NodeD.Classes.Set("motion-node-a", enabled);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var compact = width <= 0 || width < CompactWidth;
        Classes.Set("compact", compact);

        PageLayout.Margin = compact
            ? new Thickness(22, 26, 22, 20)
            : new Thickness(40, 34, 40, 24);

        HeroGrid.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("1.08*,0.92*");
        HeroGrid.RowDefinitions = compact
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("*");
        HeroGrid.MinHeight = compact ? 0 : 410;

        Grid.SetColumn(HeroVisual, compact ? 0 : 1);
        Grid.SetRow(HeroVisual, compact ? 1 : 0);
        HeroVisual.Height = compact ? 300 : 360;
        HeroVisual.Margin = compact
            ? new Thickness(0, 28, 0, 0)
            : new Thickness(48, 0, 0, 0);

        FeatureGrid.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,*,*");
        FeatureGrid.RowDefinitions = compact
            ? new RowDefinitions("Auto,Auto,Auto")
            : new RowDefinitions("*");

        ConfigureCard(FeatureCardOne, compact, 0);
        ConfigureCard(FeatureCardTwo, compact, 1);
        ConfigureCard(FeatureCardThree, compact, 2);
    }

    private static void ConfigureCard(
        Border card,
        bool compact,
        int index)
    {
        Grid.SetColumn(card, compact ? 0 : index);
        Grid.SetRow(card, compact ? index : 0);

        card.Margin = compact
            ? new Thickness(0, index == 0 ? 0 : 10, 0, 0)
            : index switch
            {
                0 => new Thickness(0, 0, 10, 0),
                1 => new Thickness(5, 0),
                _ => new Thickness(10, 0, 0, 0)
            };
    }
}
