using Avalonia.Controls;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>为插件提供受控的宿主窗口内容区全屏展示端口。</summary>
/// <remarks>
/// 实现具有 UI 线程亲和性，调用方必须在 Avalonia UI 线程请求展示，并在同一线程首次释放成功返回的
/// 租约。Host 在租约有效期间只暂借 <see cref="Control"/>，不接管控件或其业务资源的释放责任。
/// 租约只代表本次展示的恢复权限，不暴露宿主窗口、Dock 树或任意导航服务。
/// </remarks>
public interface IWindowContentFullscreenHost
{
    /// <summary>尝试把控件覆盖到宿主窗口内容区，并返回本次展示的唯一恢复租约。</summary>
    /// <param name="content">需要展示的控件；所有权仍由调用方持有。</param>
    /// <returns>
    /// 成功时返回可幂等释放的租约；已有活动展示或窗口内容宿主已经失效时返回 <see langword="null"/>。
    /// Host 自动关闭展示后，调用方仍可安全地重复释放旧租约，旧租约也不能影响后续展示。
    /// </returns>
    /// <exception cref="ArgumentNullException">content 为 null。</exception>
    /// <exception cref="InvalidOperationException">调用线程不是 Avalonia UI 线程。</exception>
    IDisposable? TryPresent(Control content);
}
