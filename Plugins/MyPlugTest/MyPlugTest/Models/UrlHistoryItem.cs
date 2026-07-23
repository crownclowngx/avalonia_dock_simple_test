namespace MyPlugTest.Models;

public class UrlHistoryItem
{
    public string Url { get; set; }
    public DateTime RequestTime { get; set; }
    public string DisplayTime => RequestTime.ToString("yyyy-MM-dd HH:mm:ss");

    public UrlHistoryItem(string url)
    {
        Url = url;
        RequestTime = DateTime.Now;
    }
}