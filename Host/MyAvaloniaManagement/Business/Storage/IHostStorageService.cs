using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Storage;

/// <summary>
/// 定义主窗体访问文件选择器和文件系统的最小边界。
/// </summary>
/// <remarks>
/// ViewModel 只依赖路径和文本，不直接依赖 Avalonia 窗口或 <see cref="System.IO.File"/>。
/// 这样生产环境可以使用系统选择器，测试则可以使用内存文件替身，避免弹窗和真实磁盘副作用。
/// </remarks>
internal interface IHostStorageService
{
    /// <summary>
    /// 让用户选择一个或多个待打开文件。
    /// </summary>
    /// <returns>所选文件的本地绝对路径；取消或当前没有可用窗口时返回空集合。</returns>
    Task<IReadOnlyList<string>> PickOpenFilesAsync();

    /// <summary>
    /// 选择文档保存路径。
    /// </summary>
    /// <param name="documentDisplayName">用于形成文件选择器类型名称的 Host 已验证展示名称。</param>
    /// <returns>所选本地路径；取消保存时返回 <see langword="null"/>。</returns>
    Task<string?> PickSaveFileAsync(string documentDisplayName);

    /// <summary>
    /// 选择一个文件夹。
    /// </summary>
    /// <returns>所选文件夹的本地路径；取消时返回 <see langword="null"/>。</returns>
    Task<string?> PickFolderAsync();

    /// <summary>
    /// 判断指定路径是否对应已存在的文件。
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// 判断指定绝对路径是否对应已存在的目录。
    /// </summary>
    /// <remarks>
    /// UNC 检查也必须经过此端口，ViewModel 不直接访问真实网络共享。
    /// </remarks>
    bool DirectoryExists(string path);

    /// <summary>
    /// 获取指定文件当前占用的字节数。
    /// </summary>
    /// <remarks>
    /// Document 打开流程会在分配整份文本前先做资源上限检查；读取后仍会再次校验，
    /// 因而即使文件在两次操作之间发生变化，也不能绕过最终的信封大小限制。
    /// </remarks>
    long GetFileLength(string path);

    /// <summary>
    /// 异步读取指定文件的全部文本。
    /// </summary>
    Task<string> ReadAllTextAsync(string path);

    /// <summary>
    /// 异步覆盖写入指定文件的全部文本。
    /// </summary>
    Task WriteAllTextAsync(string path, string content);
}
