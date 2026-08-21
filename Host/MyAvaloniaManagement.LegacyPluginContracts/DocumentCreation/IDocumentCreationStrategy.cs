using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 创建Document的策略接口
/// </summary>
public interface IDocumentCreationStrategy
{
    /// <summary>
    /// 创建Document
    /// </summary>
    /// <param name="params">创建参数</param>
    /// <returns>创建的Document实例</returns>
    Document CreateDocument(DocumentCreationParams @params);

    /// <summary>
    /// 获取文档类型的元数据
    /// </summary>
    DocumentMetadata GetMetadata();
}