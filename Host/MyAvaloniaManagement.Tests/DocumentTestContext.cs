using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 集中登记当前 Document 生产链使用的普通测试模型，避免单元测试为旧阶段契约保留第二套组合入口。
/// </summary>
internal static class DocumentTestContext
{
    internal static TestHostContext Create(
        Action<IServiceCollection>? configureServices = null,
        bool persistable = true) =>
        new(
            configureServices: services =>
            {
                services.AddScoped<TestSavableDocument>();
                configureServices?.Invoke(services);
            },
            configureContributions: (services, builder) =>
            {
                builder.AddDocument(
                    TestPluginIds.Owner,
                    new DocumentDescriptor(
                        TestDocumentIds.TypeId,
                        "测试文档",
                        "当前 Document 生产链测试",
                        "测试",
                        creationIntents:
                        [
                            new DocumentCreationIntentDescriptor(
                                new CreationIntentId("sample-intent"),
                                "示例入口"),
                        ]),
                    typeof(TestSavableDocument),
                    typeof(UserControl),
                    static () => new UserControl(),
                    persistable);
            });
}

/// <summary>为 Document 专项测试编排插件边界失败，不向生产类型加入测试开关。</summary>
internal sealed class DocumentTestProbe
{
    internal Exception? InitializeException { get; set; }
    internal Exception? CaptureException { get; set; }
    internal Exception? AcceptChangesException { get; set; }
    internal TaskCompletionSource? InitializeBlocker { get; set; }
    internal bool ReturnNullContent { get; set; }
    internal List<DocumentActivation> ActivationContexts { get; } = [];
    internal int DisposeCount { get; set; }
    internal bool ClosingObservedDuringDispose { get; set; }
}

internal static class TestDocumentIds
{
    internal static readonly DocumentTypeId TypeId =
        new("myavalonia.plugin.host-tests.document.test");
}
