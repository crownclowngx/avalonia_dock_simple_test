using Dock.Model.Mvvm.Controls;

namespace MyAvaloniaManagement.ViewModels.Hello;

public class WelcomeViewModel: Document
{
    private string _text = "";
    
    
    public string Text
    {
        get => _text;
        set
        {
            if (value != _text)
            {
                SetProperty(ref _text, value);
                IsModified = false;
            }
        }
    }
}