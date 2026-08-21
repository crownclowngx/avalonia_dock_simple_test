using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证 Managed Plugin V2 数据根的选择、覆盖和 V1 数据保留边界。
/// </summary>
/// <remarks>
/// 这些测试只向纯 Policy 传入路径，不修改进程环境变量，避免并行测试互相污染。
/// 具体存储继续通过显式路径构造，以证明路径政策与文件读写职责保持分离。
/// </remarks>
public sealed class HostDataRootPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 未配置覆盖时默认进入独立V2目录(string? configuredDataDirectory)
    {
        using var workspace = new TemporaryWorkspace();

        var actual = HostDataRootPolicy.Resolve(
            configuredDataDirectory,
            workspace.Root);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                workspace.Root,
                HostDataRootPolicy.ProductDirectoryName,
                HostDataRootPolicy.CurrentGeneration)),
            actual);
    }

    [Fact]
    public void 显式覆盖表示完整根目录且不再追加V2()
    {
        using var workspace = new TemporaryWorkspace();
        var configuredRoot = Path.Combine(workspace.Root, "smoke-isolation");

        var actual = HostDataRootPolicy.Resolve(
            configuredRoot,
            Path.Combine(workspace.Root, "unused-local-app-data"));

        Assert.Equal(Path.GetFullPath(configuredRoot), actual);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar + HostDataRootPolicy.CurrentGeneration,
            actual[configuredRoot.Length..],
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V2存储不读取改写迁移或删除V1目录()
    {
        using var workspace = new TemporaryWorkspace();
        var legacyRoot = Path.Combine(
            workspace.Root,
            HostDataRootPolicy.ProductDirectoryName,
            "v1");
        Directory.CreateDirectory(legacyRoot);

        var legacyAppearancePath = Path.Combine(
            legacyRoot,
            AppearanceSettingsStore.SettingsFileName);
        var legacyLayoutPath = Path.Combine(
            legacyRoot,
            DockLayoutStore.LayoutFileName);
        var legacyDiagnosticsDirectory = Path.Combine(legacyRoot, "Diagnostics");
        Directory.CreateDirectory(legacyDiagnosticsDirectory);
        var legacyDiagnosticPath = Path.Combine(
            legacyDiagnosticsDirectory,
            "session-v1-must-remain.jsonl");
        const string legacyAppearance = "{\"schemaVersion\":1,\"theme\":\"Dark\"}";
        const string legacyLayout = "legacy-layout-must-remain-untouched";
        const string legacyDiagnostic = "legacy-diagnostic-must-remain-untouched";
        File.WriteAllText(legacyAppearancePath, legacyAppearance);
        File.WriteAllText(legacyLayoutPath, legacyLayout);
        File.WriteAllText(legacyDiagnosticPath, legacyDiagnostic);

        var v2Root = HostDataRootPolicy.Resolve(null, workspace.Root);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                workspace.Root,
                HostDataRootPolicy.ProductDirectoryName,
                "v2")),
            v2Root);
        var appearance = new AppearanceSettingsStore(Path.Combine(
            v2Root,
            AppearanceSettingsStore.SettingsFileName));
        var layout = new DockLayoutStore(Path.Combine(
            v2Root,
            DockLayoutStore.LayoutFileName));

        // V2 根中没有文件时必须使用默认值，不能回退到 V1 根探测或迁移旧数据。
        Assert.Equal(ApplicationThemeMode.System, appearance.Load());
        Assert.Null(layout.Load());
        Assert.True(appearance.Save(ApplicationThemeMode.Light));
        string newDiagnosticPath;
        using (var diagnostics = HostDiagnosticSession.Start(v2Root))
        {
            newDiagnosticPath = Assert.IsType<string>(diagnostics.LogPath);
            Assert.StartsWith(
                Path.Combine(v2Root, "Diagnostics"),
                newDiagnosticPath,
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(legacyAppearance, File.ReadAllText(legacyAppearancePath));
        Assert.Equal(legacyLayout, File.ReadAllText(legacyLayoutPath));
        Assert.Equal(legacyDiagnostic, File.ReadAllText(legacyDiagnosticPath));
        Assert.True(File.Exists(legacyAppearancePath));
        Assert.True(File.Exists(legacyLayoutPath));
        Assert.True(File.Exists(legacyDiagnosticPath));
        Assert.True(File.Exists(Path.Combine(
            v2Root,
            AppearanceSettingsStore.SettingsFileName)));
        Assert.True(File.Exists(newDiagnosticPath));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        internal TemporaryWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "MyAvaloniaManagement.HostDataRootPolicyTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
