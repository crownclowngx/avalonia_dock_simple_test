using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Constants;
using MyPlugTest.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MyPlugTest.Models;

public class TestWelcomeDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IServiceProvider _serviceProvider;

    public TestWelcomeDocumentStrategy(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        // 策略实例会被宿主长期保存，因此不能在策略构造阶段缓存瞬态 Document ViewModel。
        // 每次用户明确创建 Document 时再从根容器解析，才能保证多个文档状态彼此独立。
        var welcomeDoc = _serviceProvider.GetRequiredService<TestWelcomeViewModel>();
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
