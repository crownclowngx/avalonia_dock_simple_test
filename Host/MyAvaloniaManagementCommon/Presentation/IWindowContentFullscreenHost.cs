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
    bool TryPresent(Control content, object owner);

    bool TryRestore(object owner);
}
