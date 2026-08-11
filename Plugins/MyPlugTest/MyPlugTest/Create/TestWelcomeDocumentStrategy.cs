using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Constants;
using MyPlugTest.ViewModels;

namespace MyPlugTest.Models;

public class TestWelcomeDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public TestWelcomeDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory
                                ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var welcomeDoc = _documentScopeFactory.CreateDocument<TestWelcomeViewModel>();
        welcomeDoc.Title = string.IsNullOrEmpty(@params.Title) ? "Test欢迎" : @params.Title;

        return welcomeDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(SaveDocumentTypeIdConstant.TestWelcomeDocumentId, "欢迎")
        {
            Description = "显示欢迎信息2",
            MenuCategory = "测试插件"
        };
    }
}
