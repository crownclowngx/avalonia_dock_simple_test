using CommunityToolkit.Mvvm.ComponentModel;

namespace MyPlugTest.Models;

public class MessageItem : ObservableObject
{
    private string _id = string.Empty;
    private string _content = string.Empty;
    private bool _isRead;
    
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }
    
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }
    
    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }
}