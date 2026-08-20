using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>文件树打开宿主 Document 所需的最小能力。</summary>
/// <remarks>
/// 文件树只表达“打开这个现有路径”的用户意图，不应依赖主窗口、Dock Factory、保存流程或
/// 文档错误状态。生产实现复用 <see cref="DocumentPersistenceCoordinator"/>，测试则可用一个
/// 只记录路径的轻量替身验证命令边界。
/// </remarks>
internal interface IHostDocumentOpenService
{
    /// <summary>异步打开指定路径，并把结果提交到当前 HostRuntime 的文档操作状态。</summary>
    Task OpenPathAsync(string filePath);
}
