using BiliDownloader.ViewModels;
using MyAvaloniaManagement.PluginSdk;

namespace BiliDownloader.Tests;

/// <summary>
/// 让既有业务测试通过当前激活与修订快照入口表达意图，避免为了测试给生产模型增加旁路保存 API。
/// </summary>
internal static class BiliDownloaderDocumentTestExtensions
{
    internal static DocumentContent CreateContentSnapshot(this BiliDownloaderViewModel viewModel) =>
        viewModel.CaptureSaveSnapshotAsync(CancellationToken.None)
            .AsTask().GetAwaiter().GetResult().Content;

    internal static void AcceptCurrentRevision(this BiliDownloaderViewModel viewModel)
    {
        var snapshot = viewModel.CaptureSaveSnapshotAsync(CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        viewModel.AcceptChanges(snapshot.Revision);
    }

    internal static void RestoreContent(
        this BiliDownloaderViewModel viewModel,
        DocumentContent content) =>
        viewModel.InitializeAsync(
                new DocumentActivationContext(viewModel.Title, restoredContent: content),
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    internal static Task InitializeAsync(this BiliDownloaderViewModel viewModel) =>
        viewModel.InitializeAsync(
                new DocumentActivationContext(viewModel.Title),
                CancellationToken.None)
            .AsTask();
}

/// <summary>单元测试拥有的可控 Document 关闭信号。</summary>
internal sealed class TestDocumentLifetime : IDocumentLifetime, IDisposable
{
    private readonly CancellationTokenSource _source = new();

    public CancellationToken ClosingToken => _source.Token;

    public bool IsClosing => _source.IsCancellationRequested;

    internal void Close() => _source.Cancel();

    public void Dispose() => _source.Dispose();
}
