using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 为单个 Headless UI 测试建立隔离的服务容器、主 ViewModel 和布局目录。
/// </summary>
/// <remarks>
/// 上下文直接从自己拥有的容器解析生产对象；每次测试结束都会释放容器并删除临时数据。
/// 它不会写入任何进程全局服务入口，因此并行或连续测试不会取得另一个上下文的对象。
/// </remarks>
internal sealed class UiTestContext : IDisposable
{
    public UiTestContext()
    {
        TempDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyAvaloniaManagement.UiTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
        Storage = new UiStorageService();
        Messenger = new UiMessengerService();

        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();
        services.AddSingleton<IHostStorageService>(Storage);
        services.AddSingleton<IMessengerService>(Messenger);
        services.AddSingleton(new DockLayoutStore(
            Path.Combine(TempDirectory, DockLayoutStore.LayoutFileName)));
        services.AddSingleton(new AppearanceSettingsStore(
            Path.Combine(
                TempDirectory,
                AppearanceSettingsStore.SettingsFileName)));
        services.AddSingleton(PluginModuleCatalog.Discover([]));
        Provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        Factory = Provider.GetRequiredService<ManagementFactory>();
        ViewModel = Provider.GetRequiredService<MainWindowViewModel>();
    }

    public string TempDirectory { get; }

    public UiStorageService Storage { get; }

    public UiMessengerService Messenger { get; }

    public Microsoft.Extensions.DependencyInjection.ServiceProvider Provider { get; }

    public ManagementFactory Factory { get; }

    public MainWindowViewModel ViewModel { get; }

    public ApplicationThemeService ThemeService =>
        Provider.GetRequiredService<ApplicationThemeService>();

    public string LayoutPath =>
        Path.Combine(TempDirectory, DockLayoutStore.LayoutFileName);

    /// <summary>
    /// 释放服务容器并清理本次 UI 测试的临时目录。
    /// </summary>
    public void Dispose()
    {
        Provider.Dispose();
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }
}

/// <summary>
/// Headless UI 测试使用的无选择器存储实现。
/// </summary>
/// <remarks>
/// UI 组件测试不应弹出原生对话框，因此选择器统一返回取消；
/// 若测试需要真实文本读写，则限定在上下文创建的临时目录中。
/// </remarks>
internal sealed class UiStorageService : IHostStorageService
{
    public Task<IReadOnlyList<string>> PickOpenFilesAsync() =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickSaveFileAsync(DocumentMetadata? metadata) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync() =>
        Task.FromResult<string?>(null);

    public bool FileExists(string path) => File.Exists(path);

    public Task<string> ReadAllTextAsync(string path) =>
        File.ReadAllTextAsync(path);

    public Task WriteAllTextAsync(string path, string content) =>
        File.WriteAllTextAsync(path, content);
}

/// <summary>
/// 在当前测试线程同步派发消息的 UI 测试实现。
/// </summary>
internal sealed class UiMessengerService : IMessengerService
{
    private readonly List<Registration> _registrations = [];

    public IMessenger Messenger => WeakReferenceMessenger.Default;

    public void Send<TMessage>(TMessage message)
        where TMessage : class
    {
        foreach (var registration in _registrations
                     .Where(item => item.MessageType == typeof(TMessage))
                     .ToArray())
        {
            registration.Handler(registration.Receiver, message);
        }
    }

    public void Register<TReceiver, TMessage>(
        TReceiver receiver,
        MyAvaloniaManagementCommon.Message.MessageHandler<TReceiver, TMessage> handler)
        where TReceiver : class
        where TMessage : class =>
        _registrations.Add(new Registration(
            typeof(TMessage),
            receiver,
            (target, message) =>
                handler((TReceiver)target, (TMessage)message)));

    public void Unregister<TMessage>(object receiver)
        where TMessage : class =>
        _registrations.RemoveAll(item =>
            item.MessageType == typeof(TMessage) &&
            ReferenceEquals(item.Receiver, receiver));

    public void UnregisterAll(object receiver) =>
        _registrations.RemoveAll(item =>
            ReferenceEquals(item.Receiver, receiver));

    private sealed record Registration(
        Type MessageType,
        object Receiver,
        Action<object, object> Handler);
}
