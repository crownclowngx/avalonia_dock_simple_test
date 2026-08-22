using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk.UI;
using MyPlugTest.Constants;
using MyPlugTest.Services;
using MyPlugTest.ViewModels;
using MyPlugTest.Views;

namespace MyPlugTest.Plugin;

/// <summary>
/// 通过当前 V3 声明式注册入口组合 MyPlugTest 的服务与可见贡献。
/// </summary>
/// <remarks>
/// 本模块只承担组合根职责：私有业务服务进入当前插件的独立容器，Document、Tool、View 与元数据则
/// 通过一次声明同时冻结。V3 G4 在模块返回并通过所有权校验后，才由 Host 最终追加 Document scoped、
/// Tool singleton 生命周期；因此这里不重复注册贡献模型，也不保留第二事实源。
/// </remarks>
public sealed class MyPlugTestPluginModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        // 这些对象只服务 MyPlugTest 自身。无状态 I/O 服务与 Builder 可跨 Document 复用；
        // URL 历史属于单个欢迎 Document，必须随该 Document 的 Scope 创建和释放。
        registration.Services.AddSingleton<IUrlContentService, FlurlUrlContentService>();
        registration.Services.AddSingleton<IExcelFileDialogService, AvaloniaExcelFileDialogService>();
        registration.Services.AddSingleton<IExcelWorkbookReader, EpplusExcelWorkbookReader>();
        registration.Services.AddSingleton<ExcelGetUrlBuilder>();
        registration.Services.AddScoped<UrlHistoryViewModel>();

        registration.AddPersistableDocument<TestWelcomeViewModel, TestWelcomeView>(
            new DocumentDescriptor(
                MyPlugTestContributionIds.WelcomeDocument,
                "欢迎",
                "显示欢迎信息2",
                "测试插件"));

        registration.AddDocument<TestMessageReceiveViewModel, TestMessageReceiveView>(
            new DocumentDescriptor(
                MyPlugTestContributionIds.MessageReceiverDocument,
                "测试消息订阅组件",
                "消息订阅测试",
                "测试插件"));

        registration.AddDocument<BatchHttpGetViewModel, BatchHttpGetView>(
            new DocumentDescriptor(
                MyPlugTestContributionIds.BatchHttpGetDocument,
                "逐行 HTTP GET",
                "将多行网址按输入顺序逐个执行 GET 请求",
                "测试插件"));

        registration.AddDocument<ExcelGetUrlGeneratorViewModel, ExcelGetUrlGeneratorView>(
            new DocumentDescriptor(
                MyPlugTestContributionIds.ExcelGetUrlGeneratorDocument,
                "Excel GET 地址生成器",
                "按 Excel 列映射批量生成 GET 请求地址",
                "测试插件"));

        registration.AddTool<MyCustomToolViewModel, MyCustomToolView>(
            new ToolDescriptor(
                MyPlugTestContributionIds.CustomTool,
                "我的自定义工具",
                "这是一个通过插件系统加载的自定义工具",
                ToolDockSide.Right,
                ToolCloseBehavior.Hide));
    }
}
