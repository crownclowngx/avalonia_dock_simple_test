using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.Plugin;
using MySmallTools.Plugin;

namespace MySmallTools.Playback.IntegrationHarness;

internal static class Program
{
    private static Microsoft.Extensions.DependencyInjection.ServiceProvider? _provider;
    private static PluginLifecycleManager? _lifecycleManager;

    [STAThread]
    public static int Main(string[] args)
    {
        HarnessOptions options;
        try
        {
            options = HarnessOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Console.Error.WriteLine("MySmallTools 真实窗口集成门禁仅支持 Windows x64。");
            return 2;
        }

        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();

        // 验收进程直接装配生产插件模块，保证 Document Scope 与真实宿主一致；
        // 不从部署目录二次加载程序集，避免同一类型出现两个加载上下文。
        var catalog = PluginModuleCatalog.Discover([typeof(MySmallToolsPluginModule).Assembly]);
        catalog.ConfigureServices(services);
        services.AddSingleton(catalog);
        services.AddSingleton<PluginLifecycleManager>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        MyAvaloniaManagement.Business.Helpers.ServiceProvider.Initialize(_provider);
        _lifecycleManager = _provider.GetRequiredService<PluginLifecycleManager>();
        _lifecycleManager.InitializeAllAsync().GetAwaiter().GetResult();

        IAcceptanceSuite suite = options.Suite switch
        {
            HarnessSuite.G3 => new G3PlaybackHarnessRunner(_provider, options),
            HarnessSuite.G8 => new G8P1AcceptanceSuite(_provider, options),
            HarnessSuite.G10 => new G3PlaybackHarnessRunner(_provider, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Suite))
        };

        try
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .AfterSetup(_ =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var exitCode = await suite.RunAsync();
                        if (Application.Current?.ApplicationLifetime is
                            IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.Shutdown(exitCode);
                        }
                    });
                })
                .StartWithClassicDesktopLifetime([]);
        }
        finally
        {
            _lifecycleManager.ShutdownAllAsync().GetAwaiter().GetResult();
            _provider.Dispose();
        }
    }
}

/// <summary>真实窗口验收套件的最小执行端口。</summary>
internal interface IAcceptanceSuite
{
    Task<int> RunAsync();
}
