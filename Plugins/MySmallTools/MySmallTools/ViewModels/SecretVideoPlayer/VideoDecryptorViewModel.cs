using MySmallTools.Business.SecretVideoPlayer.Decryption;
using MySmallTools.Business.SecretVideoPlayer.Operations;
using MySmallTools.ViewModels.SecretVideoPlayer.Decryption;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 批量解密 Document 的兼容外壳；实现和状态由功能包中的
/// <see cref="DecryptionBatchViewModel"/> 统一拥有。
/// </summary>
public sealed class VideoDecryptorViewModel : DecryptionBatchViewModel
{
    public VideoDecryptorViewModel(
        IVideoDecryptionService decryptionService,
        ISequentialVideoQueueRunner<CandidateDecryptionPreflight> queueRunner)
        : base(decryptionService, queueRunner)
    {
    }

    public VideoDecryptorViewModel(IVideoDecryptionService decryptionService)
        : base(decryptionService)
    {
    }
}
