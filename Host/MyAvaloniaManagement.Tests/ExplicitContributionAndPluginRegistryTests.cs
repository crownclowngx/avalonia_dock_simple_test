using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证 G5 的声明一次完成、元数据无副作用和不可变发布边界。
/// </summary>
public sealed class ExplicitContributionAndPluginRegistryTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.explicit-test");

    [Fact]
    public void 未登记类型不会因存在于程序集而进入Registry且构建不激活模型()
    {
        RegisteredDocument.ConstructionCount = 0;
        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var registration = new PluginRegistration(Owner, services, builder);
        new ExplicitModule().Configure(registration);
        registration.Seal();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var registry = builder.Build(catalog: null);

        Assert.Equal(
            [new DocumentTypeId("myavalonia.plugin.explicit-test.document.registered")],
            registry.DocumentDescriptors.Keys);
        Assert.False(registry.TryGetView(typeof(UnregisteredDocument), out _));
        Assert.True(registry.TryGetView(typeof(RegisteredDocument), out var view));
        Assert.Equal(typeof(RegisteredView), view.ViewType);
        Assert.Equal(0, RegisteredDocument.ConstructionCount);
        Assert.Equal(Owner, Assert.Single(registry.Lifecycles).OwnerId);
    }

    [Fact]
    public void 同一模型重复绑定在插件候选封闭时给出结构诊断()
    {
        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var registration = new PluginRegistration(Owner, services, builder);
        registration.AddDocument<RegisteredDocument, RegisteredView>(Document());
        registration.AddDocument<RegisteredDocument, AlternateRegisteredView>(
            new DocumentDescriptor(
                new DocumentTypeId("myavalonia.plugin.explicit-test.document.alternate"),
                "第二项",
                "第二项",
                "测试"));

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Contains(exception.Diagnostics,
            item => item.Code == "DOCUMENT_CONTRIBUTION_TYPE_DUPLICATE");
        Assert.Contains(exception.Diagnostics,
            item => item.Code == "VIEW_MODEL_REGISTRATION_DUPLICATE");
    }

    [Fact]
    public void Tool模型角色View映射和多生命周期冲突在插件内一次汇总()
    {
        var registration = new PluginRegistration(
            Owner,
            new ServiceCollection(),
            new PluginRegistryBuilder());
        var duplicateToolId = new ToolTypeId(
            "myavalonia.plugin.explicit-test.tool.duplicate");
        registration.AddTool<RegisteredTool, RegisteredToolView>(new ToolDescriptor(
            duplicateToolId, "第一 Tool", "测试", ToolDockSide.Left, ToolCloseBehavior.Hide));
        registration.AddTool<SecondRegisteredTool, AlternateRegisteredView>(new ToolDescriptor(
            duplicateToolId, "第二 Tool", "测试", ToolDockSide.Right, ToolCloseBehavior.Hide));
        registration.AddDocument<RegisteredDocument, RegisteredView>(Document());
        registration.AddTool<RegisteredDocument, AlternateRegisteredView>(new ToolDescriptor(
            new ToolTypeId("myavalonia.plugin.explicit-test.tool.document-model"),
            "错误角色", "测试", ToolDockSide.Left, ToolCloseBehavior.Hide));
        registration.UseLifecycle<RegisteredLifecycle>();
        registration.UseLifecycle<SecondRegisteredLifecycle>();

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Contains(exception.Diagnostics, item => item.Code == "TOOL_ID_DUPLICATE");
        Assert.Contains(exception.Diagnostics, item => item.Code == "CONTRIBUTION_MODEL_TYPE_CONFLICT");
        Assert.Contains(exception.Diagnostics, item => item.Code == "VIEW_MODEL_REGISTRATION_DUPLICATE");
        Assert.Contains(exception.Diagnostics, item => item.Code == "LIFECYCLE_PLUGIN_ID_DUPLICATE");
    }

    [Fact]
    public void 普通Di服务不会被误解释为宿主贡献()
    {
        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var registration = new PluginRegistration(Owner, services, builder);
        registration.Services.AddSingleton<PrivateService>();
        registration.Seal();
        using var provider = services.BuildServiceProvider();

        var registry = builder.Build(catalog: null);

        Assert.Empty(registry.DocumentDescriptors);
        Assert.Empty(registry.ToolDescriptors);
        Assert.NotNull(provider.GetService<PrivateService>());
    }

    [Fact]
    public void 注册入口封闭后拒绝任何追加()
    {
        var registration = new PluginRegistration(
            Owner,
            new ServiceCollection(),
            new PluginRegistryBuilder());
        registration.Seal();

        Assert.Throws<InvalidOperationException>(() =>
            registration.AddDocument<RegisteredDocument, RegisteredView>(Document()));
        Assert.Throws<InvalidOperationException>(() =>
            registration.Services.AddSingleton<PrivateService>());
    }

    [Fact]
    public void 注册方法固定DocumentScoped以及Tool和LifecycleSingleton()
    {
        var services = new ServiceCollection();
        var registration = new PluginRegistration(
            Owner,
            services,
            new PluginRegistryBuilder());
        registration.AddDocument<RegisteredDocument, RegisteredView>(Document());
        registration.AddTool<RegisteredTool, RegisteredToolView>(new ToolDescriptor(
            new ToolTypeId("myavalonia.plugin.explicit-test.tool.registered"),
            "已登记 Tool",
            "测试生命周期",
            ToolDockSide.Left,
            ToolCloseBehavior.Hide));
        registration.UseLifecycle<RegisteredLifecycle>();
        registration.Seal();

        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, item =>
                item.ServiceType == typeof(RegisteredDocument)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, item =>
                item.ServiceType == typeof(RegisteredTool)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, item =>
                item.ServiceType == typeof(RegisteredLifecycle)).Lifetime);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<RegisteredDocument>(),
            secondScope.ServiceProvider.GetRequiredService<RegisteredDocument>());
        Assert.Same(
            provider.GetRequiredService<RegisteredTool>(),
            provider.GetRequiredService<RegisteredTool>());
        Assert.Same(
            provider.GetRequiredService<RegisteredLifecycle>(),
            provider.GetRequiredService<RegisteredLifecycle>());
    }

    [Fact]
    public void View构造失败记录稳定脱敏诊断并返回占位控件()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "g5-view-diagnostics", Guid.NewGuid().ToString("N"));
        using var diagnostics = HostDiagnosticSession.Start(directory);
        var registry = new PluginRegistry(
            [new PluginDocumentRegistration(
                Owner,
                Document(),
                typeof(RegisteredDocument),
                typeof(RegisteredView),
                () => throw new InvalidOperationException("插件私有异常正文"),
                false)],
            []);
        var locator = new ViewLocator(registry, diagnostics);

        var result = locator.Build(new RegisteredDocument());

        Assert.IsType<TextBlock>(result);
        var record = Assert.Single(diagnostics.Snapshot,
            item => item.Code == "VIEW_CREATION_FAILED");
        Assert.Equal(Owner.Value, record.PluginId);
        Assert.Null(record.TechnicalDetail);
        var persisted = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.jsonl").Select(File.ReadAllText));
        Assert.DoesNotContain("插件私有异常正文", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void 两个组合根拥有独立且发布后不可写的Registry()
    {
        var firstServices = new ServiceCollection();
        var firstBuilder = new PluginRegistryBuilder();
        firstServices.AddApplicationServices(firstBuilder).AddViewModels();
        using var firstProvider = firstServices.BuildServiceProvider();

        var secondServices = new ServiceCollection();
        var secondBuilder = new PluginRegistryBuilder();
        secondServices.AddApplicationServices(secondBuilder).AddViewModels();
        using var secondProvider = secondServices.BuildServiceProvider();

        var first = firstProvider.GetRequiredService<PluginRegistry>();
        var second = secondProvider.GetRequiredService<PluginRegistry>();

        Assert.NotSame(first, second);
        Assert.Equal(first.DocumentDescriptors.Keys, second.DocumentDescriptors.Keys);
        Assert.Throws<InvalidOperationException>(() =>
            firstBuilder.AddDocument(
                Owner,
                Document(),
                typeof(RegisteredDocument),
                typeof(RegisteredView),
                static () => new RegisteredView(),
                false));
        Assert.Single(second.DocumentDescriptors);
    }

    [Fact]
    public void Host内建Welcome与四个Tool全部来自声明式目录()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices().AddViewModels();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<PluginRegistry>();

        Assert.Equal(
            [HostExtensionIds.V2WelcomeDocument],
            registry.DocumentDescriptors.Keys);
        Assert.Equal(
            [
                HostExtensionIds.V2FileSystemTree,
                HostExtensionIds.V2ToolManagement,
                HostExtensionIds.V2PluginMenu,
                HostExtensionIds.V2PluginStatus,
            ],
            registry.ToolDescriptors.Keys.OrderBy(item => item.Value));
        Assert.True(registry.TryGetDocumentRegistration(
            HostExtensionIds.V2WelcomeDocument,
            out var welcome));
        Assert.Equal(
            typeof(MyAvaloniaManagement.ViewModels.Hello.WelcomeViewModel),
            welcome.ModelType);
        Assert.All(registry.ToolDescriptors.Keys, toolId =>
        {
            Assert.True(registry.TryGetToolRegistration(toolId, out var tool));
            Assert.Equal(HostExtensionIds.V2Owner, tool.OwnerId);
        });
    }

    private static DocumentDescriptor Document() => new(
        new DocumentTypeId("myavalonia.plugin.explicit-test.document.registered"),
        "已登记 Document",
        "测试声明式 Document",
        "测试");

    private sealed class ExplicitModule : IPluginModule
    {
        public void Configure(IPluginRegistration registration)
        {
            registration.AddDocument<RegisteredDocument, RegisteredView>(Document());
            registration.UseLifecycle<RegisteredLifecycle>();
        }
    }

    private sealed class RegisteredDocument : IPluginDocument
    {
        internal static int ConstructionCount { get; set; }
        public RegisteredDocument() => ConstructionCount++;
        public DocumentPresentationState Presentation { get; } = new("已登记");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class UnregisteredDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("未登记");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class RegisteredLifecycle : MyAvaloniaManagement.PluginSdk.IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SecondRegisteredLifecycle : MyAvaloniaManagement.PluginSdk.IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PrivateService;
    private sealed class RegisteredTool;
    private sealed class SecondRegisteredTool;
    private sealed class RegisteredView : UserControl;
    private sealed class RegisteredToolView : UserControl;
    private sealed class AlternateRegisteredView : UserControl;
}
