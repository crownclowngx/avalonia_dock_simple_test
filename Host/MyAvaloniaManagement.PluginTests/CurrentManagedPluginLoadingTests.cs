using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Workspace;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyPlugTest.Constants;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用四个当前 Managed Plugin 的真实构建输出验证统一入口、deps 解析和公共契约共享。
/// </summary>
public sealed class CurrentManagedPluginLoadingTests
{
    [Fact]
    public void G12最终测试Zip通过真实V3发现组合并进入Workspace目录()
    {
        var packageRoot = Environment.GetEnvironmentVariable(
            "MYAVALONIA_G12_V3_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            // 普通回归不重复打包；G12 专项脚本设置目录后必须执行本测试。
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(packageRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("BiliDownloader", assembly.GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);
        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"bili-g12-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            pluginProviders.Compose(
                catalog, provider, registryBuilder, documentScopes, diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();
            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal("myavalonia.plugin.bili-downloader", plugin.Manifest.PluginId.Value);
            Assert.Single(plugin.DocumentTypes);
            Assert.Single(plugin.ToolTypes);
            var lifecycle = Assert.Single(registry.Lifecycles);
            Assert.Equal(plugin.Manifest.PluginId.Value, lifecycle.OwnerId.Value);
            Assert.Equal(
                [plugin.Manifest.PluginId.Value],
                pluginProviders.AvailablePluginIds.Select(pluginId => pluginId.Value));

            // 最终包不能只证明 Registry 中存在描述符。这里不启动会读写用户 SQLite/设置的
            // Lifecycle；其执行行为由 G12 插件测试在隔离数据路径上覆盖。确认 Lifecycle 已进入
            // 真实 Registry 后，只推进 Host 自己的可用性投影，再验证 Workspace 消费同一冻结目录。
            provider.GetRequiredService<PluginLifecycleStateStore>().SetState(
                new PluginLifecycleState(
                    new PluginId("myavalonia.plugin.bili-downloader"),
                    PluginLifecycleStatus.Ready));
            var workspace = provider.GetRequiredService<WorkspaceSession>();
            var documentEntries = workspace.GetAllDocumentCreationEntries().Where(entry =>
                entry.DocumentTypeId.Value.StartsWith(
                    "myavalonia.plugin.bili-downloader.document.",
                    StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, documentEntries.Length);
            Assert.Equal(
                ["personal-source", "quick-url"],
                documentEntries.Select(entry => entry.CreationIntentId!.Value).Order().ToArray());
            Assert.True(workspace.GetAvailableToolDescriptors().ContainsKey(
                new ToolTypeId("myavalonia.plugin.bili-downloader.tool.scheduler")));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    [Fact]
    public void G11最终测试Zip通过真实V3发现组合并进入Workspace目录()
    {
        var packageRoot = Environment.GetEnvironmentVariable(
            "MYAVALONIA_G11_V3_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            // 普通回归不重复构建大型 LibVLC 包；G11 专项脚本负责设置目录并执行本测试。
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(packageRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("MySmallTools", assembly.GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);
        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"mysmalltools-g11-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            pluginProviders.Compose(
                catalog, provider, registryBuilder, documentScopes, diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();
            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal("myavalonia.plugin.my-small-tools", plugin.Manifest.PluginId.Value);
            Assert.Equal(4, plugin.DocumentTypes.Count);
            Assert.Empty(plugin.ToolTypes);
            Assert.All(plugin.DocumentTypes, modelType =>
                Assert.Equal("MySmallTools", modelType.Assembly.GetName().Name));
            var workspace = provider.GetRequiredService<WorkspaceSession>();
            Assert.Equal(4, workspace.GetAllDocumentCreationEntries().Count(entry =>
                entry.DocumentTypeId.Value.StartsWith(
                    "myavalonia.plugin.my-small-tools.document.",
                    StringComparison.Ordinal)));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    [Fact]
    public void G10最终测试Zip通过真实V3发现组合并进入Workspace目录()
    {
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_G10_V3_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            // 普通回归不构建 G10 测试 ZIP；专项脚本会设置变量并单独执行本测试。
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(packageRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("DaTangAccountingHelpPlug", assembly.GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);
        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"datang-g10-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            pluginProviders.Compose(
                catalog, provider, registryBuilder, documentScopes, diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();
            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal("myavalonia.plugin.datang-accounting-help", plugin.Manifest.PluginId.Value);
            Assert.Equal(2, plugin.DocumentTypes.Count);
            Assert.Empty(plugin.ToolTypes);
            Assert.All(plugin.DocumentTypes, modelType =>
                Assert.Equal("DaTangAccountingHelpPlug", modelType.Assembly.GetName().Name));
            var workspace = provider.GetRequiredService<WorkspaceSession>();
            Assert.Equal(2, workspace.GetAllDocumentCreationEntries().Count(entry =>
                entry.DocumentTypeId.Value.StartsWith(
                    "myavalonia.plugin.datang-accounting-help.document.",
                    StringComparison.Ordinal)));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    [Fact]
    public void G9最终测试Zip通过真实V3发现组合并进入Workspace目录()
    {
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_G9_V3_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            // 普通单元回归没有构建测试 ZIP；G9 专项脚本必须设置该环境变量并单独执行本测试。
            return;
        }

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(packageRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("MyPlugTest", assembly.GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(),
            $"my-plug-test-g9-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            pluginProviders.Compose(
                catalog,
                provider,
                registryBuilder,
                documentScopes,
                diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();
            var plugin = Assert.Single(registry.Plugins);
            Assert.Equal("myavalonia.plugin.my-plug-test", plugin.Manifest.PluginId.Value);
            Assert.Equal(4, plugin.DocumentTypes.Count);
            Assert.Single(plugin.ToolTypes);
            Assert.All(plugin.DocumentTypes, modelType =>
                Assert.Equal("MyPlugTest", modelType.Assembly.GetName().Name));

            // 真实 ZIP 不能只停在 Loader 或 Registry。WorkspaceSession 必须从同一个冻结目录
            // 取得四个创建入口和插件 Tool 描述符，证明最终 Host 创建链没有使用测试专用注册表。
            var workspace = provider.GetRequiredService<WorkspaceSession>();
            Assert.Equal(
                4,
                workspace.GetAllDocumentCreationEntries().Count(entry =>
                    entry.DocumentTypeId.Value.StartsWith(
                        MyPlugTestContributionIds.Plugin.Value + ".document.",
                        StringComparison.Ordinal)));
            Assert.True(workspace.GetAvailableToolDescriptors().ContainsKey(
                MyPlugTestContributionIds.CustomTool));
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("BiliDownloader/BiliDownloader", "BiliDownloader", "BiliDownloader", "myavalonia.plugin.bili-downloader", true)]
    [InlineData("MyPlugTest/MyPlugTest", "MyPlugTest", "MyPlugTest", "myavalonia.plugin.my-plug-test", true)]
    [InlineData("DaTangAccountingHelpPlug/DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug", "DaTang", "myavalonia.plugin.datang-accounting-help", true)]
    [InlineData("MySmallTools/MySmallTools", "MySmallTools", "SmallTools", "myavalonia.plugin.my-small-tools", true)]
    public void 真实业务插件构建目录只接受当前V3入口(
        string projectPath,
        string assemblyName,
        string directoryName,
        string pluginId,
        bool expectedV3Entry)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException("无法确定测试构建配置。");
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_G3_PACKAGE_ROOT");
        var pluginDirectory = string.IsNullOrWhiteSpace(packageRoot)
            ? Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "Plugins",
                projectPath.Replace('/', Path.DirectorySeparatorChar),
                "bin",
                configuration,
                "net10.0"))
            : Path.GetFullPath(Path.Combine(packageRoot, "Controls", directoryName));
        var pluginAssemblyPath = Path.Combine(
            pluginDirectory,
            assemblyName + ".dll");

        Assert.True(
            File.Exists(pluginAssemblyPath),
            $"插件构建输出不存在：{pluginAssemblyPath}");
        Assert.True(
            File.Exists(Path.Combine(pluginDirectory, assemblyName + ".deps.json")),
            $"插件缺少标准 deps 入口：{assemblyName}");
        Assert.True(
            PluginManifestReader.TryRead(
                pluginDirectory,
                out var manifest,
                out var manifestErrorCode,
                out var manifestErrorDetail),
            $"插件清单无效：{manifestErrorCode}: {manifestErrorDetail}");
        Assert.Equal(pluginId, manifest!.PluginId.Value);
        Assert.Equal(assemblyName + ".dll", manifest.EntryPoint.Assembly);

        var context = new PluginLoadContext(pluginDirectory);
        var pluginAssembly = context.LoadFromAssemblyPath(pluginAssemblyPath);
        Assert.True(PluginCompatibilityEvaluator.HasMatchingPluginVersion(
            manifest,
            pluginAssembly.GetName().Version));
        var entryType = pluginAssembly.GetType(
            manifest.EntryPoint.Type, throwOnError: false, ignoreCase: false);
        var accepted = PluginModulePreflight.TryValidate(
            entryType, out var validatedType, out var entryCode, out _);
        Assert.Equal(expectedV3Entry, accepted);
        if (expectedV3Entry)
        {
            Assert.Same(entryType, validatedType);
            Assert.Null(entryCode);
        }
        else
        {
            Assert.Null(validatedType);
            Assert.Equal(HostDiagnosticCodes.PluginEntryInvalid, entryCode);
        }
        Assert.Equal(pluginId, manifest.PluginId.Value);
    }
}
