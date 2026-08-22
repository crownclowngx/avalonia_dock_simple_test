using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 在 Headless Host 保存验收中暂停第一次主文件写入，使测试能够确定性插入一次更高 Revision。
/// </summary>
/// <remarks>
/// 替身只实现文件和选择器边界，不解释插件内容，也不直接确认脏状态；Revision 是否被正确回传
/// 仍完全由生产 <c>DocumentSaveService</c> 与插件 Document 的协作决定。
/// </remarks>
internal sealed class ControlledHostStorageService : IHostStorageService
{
    private readonly Dictionary<string, string> _files =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _primaryWriteStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _continuePrimaryWrite =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writeCount;

    internal string? SavePath { get; set; }
    internal Task PrimaryWriteStarted => _primaryWriteStarted.Task;
    internal void ReleasePrimaryWrite() => _continuePrimaryWrite.TrySetResult();

    public Task<IReadOnlyList<string>> PickOpenFilesAsync() =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickSaveFileAsync(string documentDisplayName) =>
        Task.FromResult(SavePath);

    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

    public bool FileExists(string path) => _files.ContainsKey(Path.GetFullPath(path));

    public long GetFileLength(string path) =>
        System.Text.Encoding.UTF8.GetByteCount(_files[Path.GetFullPath(path)]);

    public Task<string> ReadAllTextAsync(string path) =>
        Task.FromResult(_files[Path.GetFullPath(path)]);

    public async Task WriteAllTextAsync(string path, string content)
    {
        if (Interlocked.Increment(ref _writeCount) == 1)
        {
            _primaryWriteStarted.TrySetResult();
            await _continuePrimaryWrite.Task;
        }

        _files[Path.GetFullPath(path)] = content;
    }
}
