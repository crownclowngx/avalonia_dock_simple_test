using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Constants;
using MyPlugTest.ViewModels;

namespace MyPlugTest.Models;

public class TestMessageReceiveDocumentStrategy: IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public TestMessageReceiveDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory
                                ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var welcomeDoc = _documentScopeFactory.CreateDocument<TestMessageReceiveViewModel>();
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
