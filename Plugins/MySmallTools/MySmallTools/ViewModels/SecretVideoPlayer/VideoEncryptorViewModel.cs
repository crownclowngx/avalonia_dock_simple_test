using MyAvaloniaManagement.PluginSdk;
using MySmallTools.Business.SecretVideoPlayer.Encryption;
using MySmallTools.Business.SecretVideoPlayer.Operations;
using MySmallTools.ViewModels.SecretVideoPlayer.Encryption;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 批量加密 Document 的兼容外壳；实现和状态由功能包中的
/// <see cref="EncryptionBatchViewModel"/> 统一拥有。
/// </summary>
public sealed class VideoEncryptorViewModel : EncryptionBatchViewModel, IPluginDocument
{
    private string _title = "视频文件加密器";

    public VideoEncryptorViewModel(
        IVideoEncryptionService singleFileService,
        IVideoBatchEncryptionService batchService,
        ISequentialVideoQueueRunner<PreparedEncryptionItem> queueRunner,
        IDocumentLifetime documentLifetime)
        : base(singleFileService, batchService, queueRunner, documentLifetime)
    {
    }

    public DocumentPresentationState Presentation => new(_title);

    public event EventHandler? PresentationChanged;

    public ValueTask InitializeAsync(
        DocumentActivationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var title = string.IsNullOrWhiteSpace(context.Title) ? "视频文件加密器" : context.Title;
        if (!string.Equals(_title, title, StringComparison.Ordinal))
        {
            _title = title;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        return ValueTask.CompletedTask;
    }
}
