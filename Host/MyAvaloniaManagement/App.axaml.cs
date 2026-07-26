using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>(),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
