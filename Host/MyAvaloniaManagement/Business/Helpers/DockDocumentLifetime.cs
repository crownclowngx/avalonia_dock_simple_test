using Avalonia;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Docking;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 在 Dock 确认文档关闭后释放宿主持有的控件缓存和依赖注入作用域。
/// 延迟到关闭完成后处理，可避免可取消关闭流程提前销毁文档资源。
/// </summary>
internal sealed class DockDocumentLifetime
{
    internal void Release(Document document)
    {
        try
        {
            if (Application.Current?.Resources["ControlRecyclingKey"]
                is DocumentControlRecycling recycling)
            {
                recycling.Remove(document);
            }
        }
        finally
        {
            // 控件回收器可能触发插件 View 的自定义清理。无论该步骤是否抛出，
            // ClosingToken 与 Document Scope 都是更底层的所有权兜底，必须继续释放。
            if (document is ManagedDocumentDockable adapter)
            {
                adapter.Dispose();
            }
        }
    }
}
