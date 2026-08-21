using Avalonia.Controls;

namespace MyAvaloniaManagementCommon.Presentation;

/// <summary>
/// Presents one control over the host window's content area while retaining the
/// window chrome.
/// </summary>
/// <remarks>
/// Implementations are UI-thread-affine. Ownership is compared by reference so
/// one document cannot replace or restore another document's fullscreen content.
/// </remarks>
public interface IWindowContentFullscreenHost
{
    /// <summary>尝试以指定所有者把控件覆盖到宿主窗口内容区。</summary>
    /// <param name="content">需要展示的控件；所有权仍由调用方持有。</param>
    /// <param name="owner">用于防止不同 Document 相互覆盖的引用身份。</param>
    /// <returns>内容成功展示时为 <see langword="true"/>；已有其他所有者时为 <see langword="false"/>。</returns>
    bool TryPresent(Control content, object owner);

    /// <summary>尝试恢复由指定所有者展示前的宿主窗口内容。</summary>
    /// <param name="owner">必须与展示时传入的对象为同一引用。</param>
    /// <returns>成功恢复时为 <see langword="true"/>；所有者不匹配时为 <see langword="false"/>。</returns>
    bool TryRestore(object owner);
}
