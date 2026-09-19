using System.Runtime.Loader;
using System.Text.Json.Nodes;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.Business.Compatibility;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>使用真实清单和 DLL 验证过滤边界；通过程序集位置核对，避免其他测试的同名程序集造成假阳性。</summary>
public sealed class PluginEnablementLoadingTests
{
    internal static readonly PluginId Owner = new("myavalonia.plugin.isolation-v1");

    [Fact]
    public void 同次启动保留启用插件且目录改名不改变禁用身份()
    {
        using var files = new EnablementPluginFiles();
        var second = Path.Combine(files.Root, "PluginV2");
        Directory.CreateDirectory(second);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PluginIsolationFixtures", "PluginV2")))
            File.Copy(file, Path.Combine(second, Path.GetFileName(file)));
        var renamed = Path.Combine(files.Root, "renamed");
        Directory.Move(files.PluginDirectory, renamed);
        var discovery = AssemblyLoaderHelper.Discover(files.Root, new(new([Owner]), "test"), files.DataRoot);
        Assert.Equal(2, discovery.Candidates.Count);
        Assert.Single(discovery.Assemblies);
        Assert.Single(PluginModuleCatalog.Discover(discovery).Entries);
        Assert.Empty(discovery.Diagnostics);
        AssertNoLoadedFiles(renamed);
    }

    [Fact]
    public async Task 禁用项检查磁盘产物不加载程序集且保留启动事实()
    {
        using var files = new EnablementPluginFiles();
        var discovery = AssemblyLoaderHelper.Discover(files.Root, new(new([Owner]), "test"), files.DataRoot);
        var registry = new PluginRegistry([], []);
        var evidence = new PluginDashboardEvidence(registry, new CompatibilityReportStore(Path.Combine(files.DataRoot, "reports")),
            TimeProvider.System, discovery: discovery);
        var snapshot = await evidence.ReadAsync(true, CancellationToken.None);
        var artifact = Assert.Single(snapshot.Artifacts);
        Assert.NotNull(artifact.Artifact);
        Assert.Contains("本次未加载", artifact.State);
        Assert.False(discovery.StartupSettings.Settings!.IsEnabled(Owner));
        AssertNoLoadedFiles(files.Root);
    }

    [Fact]
    public void 禁用项保留身份且没有加载上下文与模块目录()
    {
        using var files = new EnablementPluginFiles();
        var disabled = new PluginEnablementReadResult(new([Owner]), "test");
        var discovery = AssemblyLoaderHelper.Discover(files.Root, disabled, files.DataRoot);
        Assert.Single(discovery.Candidates);
        Assert.Empty(discovery.Assemblies);
        Assert.Empty(PluginModuleCatalog.Discover(discovery).Entries);
        Assert.Empty(discovery.Diagnostics);
        AssertNoLoadedFiles(files.Root);
        Assert.Same(disabled, discovery.StartupSettings);
    }

    [Theory]
    [InlineData("PluginIsolation.PluginV1.dll")]
    [InlineData("PluginIsolation.PluginV1.deps.json")]
    [InlineData("incompatible-sdk")]
    public void 禁用判断在文件布局与SDK预检之前(string change)
    {
        using var files = new EnablementPluginFiles();
        if (change == "incompatible-sdk")
        {
            var manifest = JsonNode.Parse(File.ReadAllText(files.ManifestPath))!;
            manifest["sdk"]!["minInclusive"] = "90.0.0";
            manifest["sdk"]!["maxExclusive"] = "91.0.0";
            File.WriteAllText(files.ManifestPath, manifest.ToJsonString());
        }
        else File.Delete(Path.Combine(files.PluginDirectory, change));
        var discovery = AssemblyLoaderHelper.Discover(files.Root, new(new([Owner]), "test"), files.DataRoot);
        Assert.Single(discovery.Candidates);
        Assert.Empty(discovery.Diagnostics);
        AssertNoLoadedFiles(files.Root);
    }

    [Fact]
    public void 相同配置根缓存启动策略但不同数据根可独立发现()
    {
        using var files = new EnablementPluginFiles();
        var disabled = AssemblyLoaderHelper.Discover(files.Root, new(new([Owner]), "test"), files.DataRoot);
        var repeated = AssemblyLoaderHelper.Discover(files.Root, PluginEnablementReadResult.Default, files.DataRoot);
        Assert.Same(disabled, repeated);
        AssertNoLoadedFiles(files.Root);
        var enabled = AssemblyLoaderHelper.Discover(files.Root, PluginEnablementReadResult.Default, files.DataRoot + "-other");
        Assert.Single(enabled.Assemblies);
        Assert.Single(PluginModuleCatalog.Discover(enabled).Entries);
    }

    [Fact]
    public void 重复身份即使被禁用仍在任何加载之前拒绝()
    {
        using var files = new EnablementPluginFiles();
        var duplicate = Path.Combine(files.Root, "duplicate");
        Directory.CreateDirectory(duplicate);
        File.Copy(files.ManifestPath, Path.Combine(duplicate, PluginManifestReader.FileName));
        var discovery = AssemblyLoaderHelper.Discover(files.Root, new(new([Owner]), "test"), files.DataRoot);
        Assert.Equal(2, discovery.Diagnostics.Count(item => item.Code == HostDiagnosticCodes.PluginManifestIdentityDuplicate));
        Assert.Empty(discovery.Assemblies);
        AssertNoLoadedFiles(files.Root);
    }

    [Fact]
    public void 配置错误暂停加载而坏清单仍有独立诊断()
    {
        using var files = new EnablementPluginFiles();
        var broken = Path.Combine(files.Root, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, PluginManifestReader.FileName), "{}");
        var discovery = AssemblyLoaderHelper.Discover(files.Root, new(null, "invalid", "PLUGIN_ENABLEMENT_INVALID"), files.DataRoot);
        Assert.Single(discovery.Candidates);
        Assert.Contains(discovery.Diagnostics, item => item.Code == "PLUGIN_ENABLEMENT_INVALID");
        Assert.Contains(discovery.Diagnostics, item => item.Code == HostDiagnosticCodes.PluginManifestInvalid);
        AssertNoLoadedFiles(files.Root);
    }

    internal static void AssertNoLoadedFiles(string root) => Assert.DoesNotContain(AssemblyLoadContext.All.SelectMany(context => context.Assemblies),
        assembly => !assembly.IsDynamic && assembly.Location.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
}

/// <summary>复制已由构建准备的真实插件资产；每例拥有独立根，绝不使用部署目录或用户数据。</summary>
internal sealed class EnablementPluginFiles : IDisposable
{
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "enablement-plugin", Guid.NewGuid().ToString("N"));
    internal string Root => Path.Combine(DirectoryPath, "Controls");
    internal string DataRoot => Path.Combine(DirectoryPath, "Data");
    internal string PluginDirectory => Path.Combine(Root, "PluginV1");
    internal string ManifestPath => Path.Combine(PluginDirectory, PluginManifestReader.FileName);
    internal EnablementPluginFiles()
    {
        Directory.CreateDirectory(PluginDirectory);
        Directory.CreateDirectory(DataRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "PluginIsolationFixtures", "PluginV1");
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(PluginDirectory, Path.GetFileName(file)));
    }
    public void Dispose()
    {
        // 非可回收 ALC 的 DLL 在 Windows 上可能锁定到进程退出。只有此例的 GUID 目录可保留，
        // 不能为了测试清理改变生产加载上下文寿命；独立进程往返由父进程在退出后清理。
        try { Directory.Delete(DirectoryPath, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
