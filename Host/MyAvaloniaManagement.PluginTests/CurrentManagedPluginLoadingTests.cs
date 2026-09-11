using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Workspace;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyPlugTest.Constants;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 使用当前内置 Managed Plugin 的真实构建输出验证统一入口、deps 解析和公共契约共享。
/// </summary>
public sealed class CurrentManagedPluginLoadingTests
{
    [Fact]
    [Trait("Category", "PackageAcceptance")]
    public void MyPlugTest真实Zip通过Host发现组合并进入Workspace目录()
    {
        var packageRoot = Environment.GetEnvironmentVariable("MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(packageRoot),
            "包验收必须由 Gate 在打包后提供 MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT。");
        Assert.True(Directory.Exists(packageRoot), $"包验收目录不存在：{packageRoot}");

        var snapshot = AssemblyLoaderHelper.Discover(Path.GetFullPath(packageRoot));
        Assert.Empty(snapshot.Diagnostics);
        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("MyPlugTest", assembly.GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);

        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(),
            $"my-plug-test-package-{Guid.NewGuid():N}");
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
            Assert.Equal(2, registry.Icons.Count);
            Assert.All(registry.Icons, icon => Assert.Equal(MyPlugTestContributionIds.Plugin, icon.OwnerId));
            var iconAssembly = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly)!.Assemblies
                .Single(item => item.GetName().Name == "MyAvaloniaManagement.Icons");
            Assert.NotSame(typeof(MyAvaloniaManagement.Icons.CommonIcons).Assembly, iconAssembly);
            Assert.True(File.Exists(Path.Combine(packageRoot!, "MyPlugTest", "MyAvaloniaManagement.Icons.dll")));
            Assert.False(File.Exists(Path.Combine(packageRoot!, "MyPlugTest", "MyAvaloniaManagement.PluginSdk.UI.dll")));
            Assert.All(plugin.DocumentTypes, modelType =>
                Assert.Equal("MyPlugTest", modelType.Assembly.GetName().Name));

            // 真实 ZIP 不能只停在 Loader 或 Registry。WorkspaceSession 必须从同一个冻结目录
            // 取得四个创建入口和插件 Tool 描述符，证明最终 Host 创建链没有使用测试专用注册表。
            var workspace = provider.GetRequiredService<WorkspaceSession>();
            var entries = workspace.GetAllDocumentCreationEntries().ToArray();
            var excel = entries.Single(item => item.DocumentTypeId == MyPlugTestContributionIds.ExcelGetUrlGeneratorDocument);
            Assert.Equal(MyPlugTestContributionIds.Plugin, excel.OwnerId);
            Assert.Equal($"plugin:{MyPlugTestContributionIds.Plugin}/excel-table", excel.IconPath);
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
    [InlineData("MyPlugTest/MyPlugTest", "MyPlugTest", "myavalonia.plugin.my-plug-test", true)]
    public void 真实业务插件构建目录只接受当前V3入口(
        string projectPath,
        string assemblyName,
        string pluginId,
        bool expectedV3Entry)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException("无法确定测试构建配置。");
        var pluginDirectory = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "Plugins",
                projectPath.Replace('/', Path.DirectorySeparatorChar),
                "bin",
                configuration,
                "net10.0"));
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
