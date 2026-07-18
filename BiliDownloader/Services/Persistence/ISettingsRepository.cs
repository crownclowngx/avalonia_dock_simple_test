namespace BiliDownloader.Services.Persistence;

/// <summary>
/// 应用设置键值存储接口
/// </summary>
public interface ISettingsRepository
{
    /// <summary>初始化数据库（建表）</summary>
    Task InitAsync();

    /// <summary>获取配置项</summary>
    Task<string?> GetSettingAsync(string key);

    /// <summary>设置配置项</summary>
    Task SetSettingAsync(string key, string value);
}
