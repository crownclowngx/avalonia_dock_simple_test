using Dock.Model.Mvvm.Controls;

namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 为一个 Document 创建独立依赖注入作用域的公共入口。
/// </summary>
/// <remarks>
/// 插件只负责声明“需要哪一种 Document”，不保存 <c>IServiceScope</c>，也不决定何时释放。
/// 具体作用域由宿主创建并跟踪；当 Dock 确认 Document 已关闭后，宿主释放对应作用域，
/// 从而让容器按照依赖创建顺序的逆序自动释放 ViewModel、播放器和其他资源。
/// </remarks>
public interface IDocumentScopeFactory
{
    /// <summary>
    /// 创建一个由独立 DI Scope 托管的 Document。
    /// </summary>
    /// <typeparam name="TDocument">已经在插件模块中注册的 Document 类型。</typeparam>
    /// <returns>从新作用域解析出的 Document。</returns>
    TDocument CreateDocument<TDocument>() where TDocument : Document;
}
