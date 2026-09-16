using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 显式输入冻结的 Controls 目录，验证真实 DLL 的加载、DI 组合、声明视图及回收器兼容。
/// 不启动插件生命周期，不执行下载、登录、定时任务或原生播放；这些属于独立业务验收。
/// </summary>
public sealed class ExternalPluginBinaryAcceptanceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> PluginDirectories()
    {
        var root = Environment.GetEnvironmentVariable("MYAVALONIA_EXTERNAL_CONTROLS");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException("显式产物验收需要 MYAVALONIA_EXTERNAL_CONTROLS 指向冻结的 Controls 副本。");
        var directories = Directory.GetDirectories(root)
            .Where(path => File.Exists(Path.Combine(path, "plugin.manifest.json"))).Order().ToArray();
        if (directories.Length == 0)
            throw new InvalidOperationException("验收目录没有插件清单，不能把空输入记为通过。");
        return directories.Select(path => new object[] { path });
    }

    [AvaloniaTheory]
    [MemberData(nameof(PluginDirectories))]
    public void 真实插件产物在新版Host加载组合并创建全部声明视图(string directory)
    {
        var all = AssemblyLoaderHelper.Discover(Path.GetDirectoryName(directory)!);
        Assert.Empty(all.Diagnostics);
        Assert.Equal(PluginDirectories().Count(), all.Assemblies.Count);
        var assembly = Assert.Single(all.Assemblies, item =>
            Path.GetDirectoryName(item.Location) == directory);
        var manifest = all.GetManifest(assembly);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name => name.Name!.StartsWith("Dock.", StringComparison.Ordinal));
        Assert.NotEmpty(assembly.GetTypes());
        var loadContext = AssemblyLoadContext.GetLoadContext(assembly)!;
        Assert.NotSame(AssemblyLoadContext.Default, loadContext);
        foreach (var reference in assembly.GetReferencedAssemblies().Where(name =>
                     name.Name!.StartsWith("Avalonia", StringComparison.Ordinal) ||
                     name.Name.StartsWith("MyAvaloniaManagement.PluginSdk", StringComparison.Ordinal)))
        {
            var shared = loadContext.LoadFromAssemblyName(reference);
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(shared));
            Assert.True(shared.GetName().Version >= reference.Version);
        }

        // 一个用例只组合一个真实模块，保留其原 manifest 和预检结果；不靠源码 ProjectReference
        // 替代旧 DLL，也不通过复制 shared 程序集伪造兼容性。
        var snapshot = new PluginDiscoverySnapshot([assembly],
            new Dictionary<Assembly, PluginManifest> { [assembly] = manifest },
            new Dictionary<Assembly, Type> { [assembly] = all.GetModuleType(assembly) }, []);
        var modules = PluginModuleCatalog.Discover(snapshot);
        var builder = new PluginRegistryBuilder();
        var scopes = new DocumentScopeRegistry();
        using var owners = new PluginProviderOwner();
        var services = new ServiceCollection();
        services.AddApplicationServices(builder, owners, scopes);
        services.AddViewModels();
        services.AddSingleton(modules);
        var diagnostics = new RecordingDiagnostics();
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        owners.Compose(modules, provider, builder, scopes, diagnostics);
        var registry = provider.GetRequiredService<PluginRegistry>();
        Assert.Single(registry.Plugins);
        Assert.Empty(diagnostics.Items);
        var factories = registry.Documents.Select(item => item.ViewFactory)
            .Concat(registry.Tools.Select(item => item.ViewFactory)).ToArray();
        Assert.NotEmpty(factories);
        var recycling = provider.GetRequiredService<DocumentControlRecycling>();
        foreach (var factory in factories)
        {
            var view = factory();
            Assert.NotNull(view);
            view.ApplyTemplate();
            view.Measure(new Size(1000, 700));
            view.Arrange(new Rect(0, 0, 1000, 700));
            var key = new object();
            var originalParent = new StackPanel { Children = { view } };
            recycling.Add(key, view);
            Assert.Same(view, recycling.Build(key, null, null));
            Assert.Empty(originalParent.Children);
            var destination = new StackPanel { Children = { view } };
            Assert.True(recycling.Remove(key));
            Assert.Empty(destination.Children);
            Assert.Null(view.GetVisualParent());
            Assert.False(recycling.Remove(key));
        }
        output.WriteLine($"{manifest.PluginId}: {assembly.GetName().Version}; 文档 {registry.Documents.Count}、工具 {registry.Tools.Count}、视图 {factories.Length}; 共享 SDK {typeof(IPluginDocument).Assembly.GetName().Version}");
    }

    private sealed class RecordingDiagnostics : IHostDiagnosticSink
    {
        public List<HostDiagnosticDraft> Items { get; } = [];
        public HostDiagnosticRecord Report(HostDiagnosticDraft diagnostic)
        {
            Items.Add(diagnostic);
            return new HostDiagnosticRecord
            {
                SessionId = Guid.Empty, Sequence = Items.Count, TimestampUtc = DateTimeOffset.UnixEpoch,
                Code = diagnostic.Code, Phase = diagnostic.Phase, Severity = HostDiagnosticSeverity.Warning,
                Disposition = HostDiagnosticDisposition.Continue, UserMessage = "产物验收诊断"
            };
        }
    }
}
