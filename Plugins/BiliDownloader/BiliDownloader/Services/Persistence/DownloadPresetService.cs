using BiliDownloader.Models;

namespace BiliDownloader.Services.Persistence;

public interface IDownloadPresetService
{
    Task<IReadOnlyList<DownloadPreset>> GetAllAsync();
    Task<DownloadPreset?> GetByIdAsync(string id);
    Task<DownloadPreset> SaveCopyAsync(DownloadProfile profile, string name);
    Task<DownloadPreset?> RenameAsync(string id, string newName);
    Task DeleteAsync(string id);
}

/// <summary>Owns preset lifecycle rules; the repository only persists snapshots.</summary>
public sealed class DownloadPresetService(IPresetRepository repository) : IDownloadPresetService
{
    public async Task<IReadOnlyList<DownloadPreset>> GetAllAsync() => await repository.GetAllAsync();

    public Task<DownloadPreset?> GetByIdAsync(string id) => repository.GetByIdAsync(id);

    public async Task<DownloadPreset> SaveCopyAsync(DownloadProfile profile, string name)
    {
        var preset = DownloadPreset.FromProfile(
            Guid.NewGuid().ToString("N"), RequireName(name), profile);
        await repository.SaveAsync(preset);
        return preset;
    }

    public async Task<DownloadPreset?> RenameAsync(string id, string newName)
    {
        var current = await repository.GetByIdAsync(id);
        if (current is null || current.IsBuiltIn) return null;
        var renamed = current with { Name = RequireName(newName) };
        await repository.SaveAsync(renamed);
        return renamed;
    }

    public Task DeleteAsync(string id) => repository.DeleteAsync(id);

    private static string RequireName(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 or > 40)
            throw new ArgumentException("预设名称应为 1–40 个字符。", nameof(value));
        return normalized;
    }
}
