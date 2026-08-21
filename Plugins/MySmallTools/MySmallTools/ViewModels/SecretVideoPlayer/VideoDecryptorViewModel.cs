using MyAvaloniaManagement.PluginSdk;
using MySmallTools.Business.SecretVideoPlayer.Decryption;
using MySmallTools.Business.SecretVideoPlayer.Operations;
using MySmallTools.ViewModels.SecretVideoPlayer.Decryption;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 批量解密 Document 的兼容外壳；实现和状态由功能包中的
/// <see cref="DecryptionBatchViewModel"/> 统一拥有。
/// </summary>
public sealed class VideoDecryptorViewModel : DecryptionBatchViewModel, IPluginDocument
{
    private string _title = "批量视频解密器";

    public VideoDecryptorViewModel(
        IVideoDecryptionService decryptionService,
        ISequentialVideoQueueRunner<CandidateDecryptionPreflight> queueRunner,
        IDocumentLifetime documentLifetime)
        : base(decryptionService, queueRunner, documentLifetime)
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
        var title = string.IsNullOrWhiteSpace(context.Title) ? "批量视频解密器" : context.Title;
        if (!string.Equals(_title, title, StringComparison.Ordinal))
        {
            _title = title;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        return ValueTask.CompletedTask;
    }
}
