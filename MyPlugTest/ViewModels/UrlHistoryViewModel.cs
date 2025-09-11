using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyPlugTest.Models;

namespace MyPlugTest.ViewModels;

public partial class UrlHistoryViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<UrlHistoryItem> _historyItems = new();

    public void AddUrl(string url)
    {
        // 检查URL是否已经存在，如果存在则不添加
        if (!HistoryItems.Any(item => item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
        {
            HistoryItems.Insert(0, new UrlHistoryItem(url));
        }
    }
    [RelayCommand]
    public void ClearHistory()
    {
        HistoryItems.Clear();
    }
}