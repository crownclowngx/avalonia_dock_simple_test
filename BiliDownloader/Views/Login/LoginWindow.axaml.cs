using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BiliDownloader.ViewModels.Login;

namespace BiliDownloader.Views.Login;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is LoginWindowViewModel vm)
        {
            vm.CancelPolling();
        }

        base.OnClosed(e);
    }
}
