using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.Models.DocumentCreation;

/// <summary>
/// 创建Welcome文档的策略
/// </summary>
public class WelcomeDocumentStrategy : IDocumentCreationStrategy
{ 

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var welcomeDoc = new WelcomeViewModel
        {
            Title = string.IsNullOrEmpty(@params.Title) ? "欢迎1" : @params.Title,
            Text = string.IsNullOrEmpty(@params.InitializationData)
                ? "欢迎使用MyAvaloniaManagement"
                : @params.InitializationData
        };

        return welcomeDoc;
    }
    
    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata("DD7A1E38-07C5-B38C-FB02-1B991896EF49", "欢迎主程序")
        {
            Description = "显示欢迎信息",
            MenuCategory = "帮助"
        };
    }
}