using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MySmallTools.Plugin;

namespace MySmallTools.Playback.IntegrationHarness;

internal static class Program
{
    private static Microsoft.Extensions.DependencyInjection.ServiceProvider? _provider;
    private static PluginProviderOwner? _pluginProviders;
    private static DocumentScopeRegistry? _documentScopes;
    private static HostDiagnosticSession? _diagnostics;
    private static string? _diagnosticDirectory;
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
        var registryBuilder = new PluginRegistryBuilder();
        _pluginProviders = new PluginProviderOwner();
        _documentScopes = new DocumentScopeRegistry();
        services.AddApplicationServices(registryBuilder, _pluginProviders, _documentScopes);
        services.AddViewModels();

        // 验收进程直接装配生产插件模块，但仍严格复用 G4 的 Host Provider → 插件 Provider 顺序；
        // 不从部署目录二次加载程序集，避免同一类型出现两个加载上下文。
        // G5 Host 只接受最终 SDK 模块；MySmallTools 按任务书到 G12 才迁移。
        // Harness 项目继续保持可编译，但本阶段不把 Legacy 模块伪装成 V2 贡献。
        var catalog = PluginModuleCatalog.CreateForTests(
            Array.Empty<(MyAvaloniaManagement.PluginSdk.PluginId,
                MyAvaloniaManagement.PluginSdk.UI.IPluginModule)>());
        services.AddSingleton(catalog);
        _diagnosticDirectory = Path.Combine(
            Path.GetTempPath(), $"my-small-tools-harness-{Guid.NewGuid():N}");
        _diagnostics = HostDiagnosticSession.Start(_diagnosticDirectory);
        services.AddSingleton(_diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(_diagnostics);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        _pluginProviders.Compose(
            catalog,
            _provider,
            registryBuilder,
            _documentScopes,
            _diagnostics);
        _lifecycleManager = _provider.GetRequiredService<PluginLifecycleManager>();
        _lifecycleManager.InitializeAllAsync().GetAwaiter().GetResult();

        IAcceptanceSuite suite = options.Suite switch
        {
            HarnessSuite.G3 => new G3PlaybackHarnessRunner(_provider, options),
            HarnessSuite.G8 => new G8P1AcceptanceSuite(_provider, options),
            HarnessSuite.G10 => new G3PlaybackHarnessRunner(_provider, options),
            HarnessSuite.Phase4 => new Phase4AcceptanceSuite(_provider, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Suite))
        };

        try
        {
            return HostAvaloniaBuilder.Build(_provider)
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
            _documentScopes.CloseAll();
            _lifecycleManager.ShutdownAllAsync().GetAwaiter().GetResult();
            _pluginProviders.Dispose();
            _provider.Dispose();
            _diagnostics.Dispose();
            if (_diagnosticDirectory is not null && Directory.Exists(_diagnosticDirectory))
            {
                Directory.Delete(_diagnosticDirectory, recursive: true);
            }
        }
    }
}

/// <summary>真实窗口验收套件的最小执行端口。</summary>
internal interface IAcceptanceSuite
{
    Task<int> RunAsync();
}
