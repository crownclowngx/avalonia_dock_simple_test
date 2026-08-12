using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.Models.DocumentCreation;

/// <summary>
/// 创建 Welcome 文档的策略。
/// </summary>
public class WelcomeDocumentStrategy : IDocumentCreationStrategy
{
    public Document CreateDocument(DocumentCreationParams @params)
    {
        var welcomeDoc = new WelcomeViewModel(toolId =>
        {
            ServiceProvider.GetRequiredService<ManagementFactory>()
                .ShowTool(toolId);
        })
        {
            Title = string.IsNullOrEmpty(@params.Title) ? "欢迎" : @params.Title,
            Text = string.IsNullOrEmpty(@params.InitializationData)
                ? "MyAvaloniaManagement 是基于 Avalonia 与 Dock 构建的插件化桌面框架，" +
                  "用可停靠布局组织工具，用独立插件扩展业务能力。"
                : @params.InitializationData
        };

        return welcomeDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(
            HostExtensionIds.WelcomeDocument,
            "欢迎主程序",
            [new DocumentTypeId("DD7A1E38-07C5-B38C-FB02-1B991896EF49")])
        {
            Description = "显示欢迎信息",
            MenuCategory = "帮助"
        };
    }
}
