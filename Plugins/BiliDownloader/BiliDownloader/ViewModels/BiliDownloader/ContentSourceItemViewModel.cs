using BiliDownloader.Models.ContentSources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliDownloader.ViewModels.BiliDownloader;

public partial class ContentSourceItemViewModel : ObservableObject
{
    public ContentSourceItemViewModel(ContentSourceItem item) => Item = item;

    public ContentSourceItem Item { get; }
    public string Title => Item.Title;
    public string Detail => string.Join(" · ", new[]
        { Item.Author, Item.PublishedAt?.LocalDateTime.ToString("yyyy-MM-dd") }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    [ObservableProperty]
    private bool _isSelected;
}
