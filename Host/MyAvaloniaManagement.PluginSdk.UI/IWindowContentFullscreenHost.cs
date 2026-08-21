using Avalonia.Controls;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>为插件提供受控的宿主窗口内容区全屏展示端口。</summary>
/// <remarks>
/// 实现具有 UI 线程亲和性，调用方必须在 Avalonia UI 线程调用全部成员。所有权按对象引用比较，因此
/// 一个 Document 不能覆盖或恢复另一个 Document 的全屏内容。Host 只暂存并恢复视觉内容，不接管
/// content 或 owner 的释放责任；端口不暴露宿主窗口、Dock 树或任意导航服务。
/// </remarks>
public interface IWindowContentFullscreenHost
{
    /// <summary>尝试以指定所有者把控件覆盖到宿主窗口内容区。</summary>
    /// <param name="content">需要展示的控件；所有权仍由调用方持有。</param>
    /// <param name="owner">用于隔离不同 Document 的引用身份。</param>
    /// <returns>成功展示时为 true；已有其他所有者时为 false。</returns>
    /// <exception cref="ArgumentNullException">content 或 owner 为 null。</exception>
    /// <exception cref="InvalidOperationException">调用线程不是 Avalonia UI 线程。</exception>
    bool TryPresent(Control content, object owner);

    /// <summary>尝试恢复指定所有者展示前的宿主内容。</summary>
    /// <param name="owner">必须与展示时传入的对象为同一引用。</param>
    /// <returns>成功恢复时为 true；当前所有者不匹配时为 false。</returns>
    /// <exception cref="ArgumentNullException">owner 为 null。</exception>
    /// <exception cref="InvalidOperationException">调用线程不是 Avalonia UI 线程。</exception>
    bool TryRestore(object owner);
}
