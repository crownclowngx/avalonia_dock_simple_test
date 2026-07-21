using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Constants;
using MyPlugTest.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MyPlugTest.Models;

public class TestMessageReceiveDocumentStrategy: IDocumentCreationStrategy
{
    private readonly IServiceProvider _serviceProvider;

    public TestMessageReceiveDocumentStrategy(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        // Document ViewModel 使用 Transient；解析动作必须发生在用户创建文档的时刻，
        // 不能因为宿主提前发现策略就创建或复用 Document。
        var welcomeDoc = _serviceProvider.GetRequiredService<TestMessageReceiveViewModel>();
        welcomeDoc.Title = string.IsNullOrEmpty(@params.Title) ? "消息接收测试" : @params.Title;

        return welcomeDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(SaveDocumentTypeIdConstant.TestMessageReceiveDocumentId, "测试消息订阅组件")
        {
            Description = "消息订阅测试",
            MenuCategory = "测试插件"
        };
    }
}
