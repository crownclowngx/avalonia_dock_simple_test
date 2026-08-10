using Avalonia;
using Dock.Model.Mvvm.Controls;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 在 Dock 确认文档关闭后释放宿主持有的控件缓存和依赖注入作用域。
/// 延迟到关闭完成后处理，可避免可取消关闭流程提前销毁文档资源。
/// </summary>
internal sealed class DockDocumentLifetime(DocumentScopeManager scopeManager)
{
    internal void Release(Document document)
    {
        if (Application.Current?.Resources["ControlRecyclingKey"]
            is DocumentControlRecycling recycling)
        {
            recycling.Remove(document);
        }

        scopeManager.Release(document);
    }
}
