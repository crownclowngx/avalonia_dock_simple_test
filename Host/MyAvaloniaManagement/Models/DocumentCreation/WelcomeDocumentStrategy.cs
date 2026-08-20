using System;
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
internal sealed class WelcomeDocumentStrategy(
    Func<ManagementFactory> managementFactory) : IDocumentCreationStrategy
{
    public Document CreateDocument(DocumentCreationParams @params)
    {
        var welcomeDoc = new WelcomeViewModel(toolId =>
        {
            managementFactory().ShowTool(toolId);
        })
        {
            Title = string.IsNullOrEmpty(@params.Title) ? "欢迎" : @params.Title,
            // 欢迎正文是宿主拥有的固定产品文案，不再通过无类型、无来源的创建参数覆盖。
            // 插件若需要多个明确入口，应声明 CreationIntent；业务输入则由插件自己的
            // 强类型 ViewModel 或服务接收，避免把字符串占位字段演变成隐藏协议。
            Text = "MyAvaloniaManagement 是基于 Avalonia 与 Dock 构建的插件化桌面框架，" +
                   "用可停靠布局组织工具，用独立插件扩展业务能力。"
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
