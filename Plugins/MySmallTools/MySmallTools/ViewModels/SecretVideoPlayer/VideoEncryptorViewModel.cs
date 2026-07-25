using MySmallTools.Business.SecretVideoPlayer.Encryption;
using MySmallTools.Business.SecretVideoPlayer.Operations;
using MySmallTools.ViewModels.SecretVideoPlayer.Encryption;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 批量加密 Document 的兼容外壳；实现和状态由功能包中的
/// <see cref="EncryptionBatchViewModel"/> 统一拥有。
/// </summary>
public sealed class VideoEncryptorViewModel : EncryptionBatchViewModel
{
    public VideoEncryptorViewModel(
        IVideoEncryptionService singleFileService,
        IVideoBatchEncryptionService batchService,
        ISequentialVideoQueueRunner<PreparedEncryptionItem> queueRunner)
        : base(singleFileService, batchService, queueRunner)
    {
    }

    public VideoEncryptorViewModel(IVideoEncryptionService singleFileService)
        : base(singleFileService)
    {
    }
}
