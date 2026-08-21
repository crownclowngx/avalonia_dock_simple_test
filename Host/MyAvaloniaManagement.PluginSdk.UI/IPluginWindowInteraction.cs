using Avalonia.Platform.Storage;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>为插件提供受控的宿主窗口文件选择与剪贴板交互端口。</summary>
/// <remarks>
/// 本接口刻意只返回本地路径和操作结果，不暴露宿主 <c>Window</c>、<c>TopLevel</c>、
/// <see cref="IStorageProvider"/> 或剪贴板实例。调用方必须位于 Avalonia UI 线程；原生文件
/// 选择器通常不能被取消令牌强制关闭，因此实现会在调用前和系统窗口返回后检查取消状态，
/// 以保证 Document 关闭期间产生的迟到结果不会提交给插件模型。
/// </remarks>
public interface IPluginWindowInteraction
{
    /// <summary>使用宿主主窗口选择本地文件。</summary>
    /// <param name="options">由插件创建、在本次异步调用完成前保持有效的 Avalonia 选择参数。</param>
    /// <param name="cancellationToken">Document 关闭或命令取消时触发的协作取消令牌。</param>
    /// <returns>按选择顺序排列的本地路径；用户取消或没有可用主窗口时返回空集合。</returns>
    Task<IReadOnlyList<string>> PickOpenFilesAsync(
        FilePickerOpenOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>使用宿主主窗口选择一个本地保存路径。</summary>
    /// <param name="options">由插件创建、在本次异步调用完成前保持有效的 Avalonia 保存参数。</param>
    /// <param name="cancellationToken">Document 关闭或命令取消时触发的协作取消令牌。</param>
    /// <returns>选中的本地路径；用户取消或没有可用主窗口时返回 <see langword="null"/>。</returns>
    Task<string?> PickSaveFileAsync(
        FilePickerSaveOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>尝试把文本写入宿主主窗口关联的剪贴板。</summary>
    /// <param name="text">需要写入的非 null 文本。</param>
    /// <param name="cancellationToken">Document 关闭或命令取消时触发的协作取消令牌。</param>
    /// <returns>写入成功时为 <see langword="true"/>；没有主窗口或剪贴板时为 <see langword="false"/>。</returns>
    Task<bool> TrySetClipboardTextAsync(
        string text,
        CancellationToken cancellationToken = default);
}
