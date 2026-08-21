using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证插件清单的严格语法、版本边界以及“先兼容检查、后程序集加载”的顺序保证。
/// </summary>
public sealed class PluginManifestCompatibilityTests
{
    [Fact]
    public void 有效清单可解析为规范化四段版本()
    {
        WithManifest(
            ValidManifest(),
            directory =>
            {
                var success = PluginManifestReader.TryRead(
                    directory,
                    out var manifest,
                    out var errorCode,
                    out var errorDetail);

                Assert.True(success, $"{errorCode}: {errorDetail}");
                Assert.Equal("myavalonia.plugin.manifest-test", manifest!.PluginId.Value);
                Assert.Equal(new Version(1, 2, 3, 0), manifest.PluginVersion);
                Assert.Equal("ManifestTest.dll", manifest.EntryPoint.Assembly);
                Assert.Equal("ManifestTest.Plugin.ManifestTestModule", manifest.EntryPoint.Type);
                Assert.Equal("[1.0.0.0, 2.0.0.0)", manifest.Sdk.ToString());
            });
    }

    [Fact]
    public void 缺失空白损坏未知字段重复字段与未知Schema均被稳定拒绝()
    {
        var cases = new (string? Json, string Code)[]
        {
            (null, HostDiagnosticCodes.PluginManifestMissing),
            (string.Empty, HostDiagnosticCodes.PluginManifestInvalid),
            ("{", HostDiagnosticCodes.PluginManifestInvalid),
            (ValidManifest(schemaVersion: 1), HostDiagnosticCodes.PluginManifestSchemaUnsupported),
            (ValidManifest().Replace(
                "\"pluginVersion\": \"1.2.3\",",
                "\"pluginVersion\": \"1.2.3\", \"unknown\": true,",
                StringComparison.Ordinal), HostDiagnosticCodes.PluginManifestInvalid),
            (ValidManifest().Replace(
                "\"schemaVersion\": 2,",
                "\"schemaVersion\": 2, \"schemaVersion\": 2,",
                StringComparison.Ordinal), HostDiagnosticCodes.PluginManifestInvalid),
            (ValidManifest().Replace(
                "\"type\": \"ManifestTest.Plugin.ManifestTestModule\"",
                "\"type\": \"ManifestTest.Plugin.ManifestTestModule\", \"unknown\": true",
                StringComparison.Ordinal), HostDiagnosticCodes.PluginManifestInvalid),
            (ValidManifest().Replace(
                "\"type\": \"ManifestTest.Plugin.ManifestTestModule\"",
                "\"type\": \"ManifestTest.Plugin.ManifestTestModule\", " +
                "\"type\": \"ManifestTest.Plugin.OtherModule\"",
                StringComparison.Ordinal), HostDiagnosticCodes.PluginManifestInvalid),
            (ValidManifest().Replace("{", "{/*comment*/", StringComparison.Ordinal),
                HostDiagnosticCodes.PluginManifestInvalid),
            (ValidManifest().Replace("\n}", "\n,}", StringComparison.Ordinal),
                HostDiagnosticCodes.PluginManifestInvalid),
        };

        foreach (var (json, expectedCode) in cases)
        {
            WithManifest(
                json,
                directory =>
                {
                    Assert.False(PluginManifestReader.TryRead(
                        directory,
                        out _,
                        out var code,
                        out _));
                    Assert.Equal(expectedCode, code);
                });
        }
    }

    [Fact]
    public void 非法身份版本区间和入口路径均被拒绝()
    {
        var invalidDocuments = new[]
        {
            ValidManifest(pluginId: "other.plugin"),
            ValidManifest(pluginVersion: "1.0"),
            ValidManifest(pluginVersion: "1.0.0-beta"),
            ValidManifest(entryAssembly: "../ManifestTest.dll"),
            ValidManifest(entryAssembly: "sub/ManifestTest.dll"),
            ValidManifest(entryType: "ManifestTestModule"),
            ValidManifest(entryType: "ManifestTest.Plugin.Outer+Module"),
            ValidManifest(entryType: "ManifestTest.Plugin.Module`1"),
            ValidManifest(sdkMinimum: "2.0.0", sdkMaximum: "2.0.0"),
        };

        foreach (var json in invalidDocuments)
        {
            WithManifest(
                json,
                directory =>
                {
                    Assert.False(PluginManifestReader.TryRead(
                        directory,
                        out _,
                        out var code,
                        out _));
                    Assert.Equal(HostDiagnosticCodes.PluginManifestInvalid, code);
                });
        }
    }

    [Fact]
    public void 超过大小限制的清单在JSON解析前被拒绝()
    {
        WithManifest(
            new string(' ', 64 * 1024 + 1),
            directory =>
            {
                Assert.False(PluginManifestReader.TryRead(
                    directory,
                    out _,
                    out var code,
                    out var detail));
                Assert.Equal(HostDiagnosticCodes.PluginManifestInvalid, code);
                Assert.Contains("超过", detail, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void 超过JSON深度限制的清单被拒绝且不产生部分模型()
    {
        var deepPayload = "{\"schemaVersion\":2,\"pluginId\":\"myavalonia.plugin.deep\"," +
                          "\"pluginVersion\":\"1.0.0\",\"entryPoint\":{" +
                          "\"assembly\":\"Deep.dll\",\"type\":\"Deep.Plugin.DeepModule\"}," +
                          "\"sdk\":{\"minInclusive\":\"1.0.0\",\"maxExclusive\":" +
                          "[[[[[[[[\"2.0.0\"]]]]]]]]}}";

        WithManifest(deepPayload, directory =>
        {
            Assert.False(PluginManifestReader.TryRead(
                directory, out var manifest, out var code, out _));
            Assert.Null(manifest);
            Assert.Equal(HostDiagnosticCodes.PluginManifestInvalid, code);
        });
    }

    [Fact]
    public void SDK版本区间遵循左闭右开边界并返回单一稳定错误码()
    {
        var manifest = CreateManifestModel(
            new PluginVersionRange(new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)));

        Assert.True(PluginCompatibilityEvaluator.TryEvaluate(
            manifest,
            new PluginSdkCompatibilityProfile(new Version(1, 0, 0, 0)),
            out _,
            out _));

        Assert.False(PluginCompatibilityEvaluator.TryEvaluate(
            manifest,
            new PluginSdkCompatibilityProfile(new Version(0, 9, 9, 9)),
            out var belowCode,
            out _));
        Assert.Equal(HostDiagnosticCodes.PluginSdkIncompatible, belowCode);

        Assert.False(PluginCompatibilityEvaluator.TryEvaluate(
            manifest,
            new PluginSdkCompatibilityProfile(new Version(2, 0, 0, 0)),
            out var upperCode,
            out _));
        Assert.Equal(HostDiagnosticCodes.PluginSdkIncompatible, upperCode);
    }

    [Fact]
    public void 不兼容损坏入口不会被读取且不阻断兼容插件()
    {
        var snapshot = AssemblyLoaderHelper.Discover(
            "PluginManifestCompatibilityFixtures");

        var assembly = Assert.Single(snapshot.Assemblies);
        Assert.Equal("PluginIsolation.PluginV2", assembly.GetName().Name);
        Assert.Contains(
            snapshot.Diagnostics,
            item => item.Code == HostDiagnosticCodes.PluginSdkIncompatible &&
                    item.PluginDirectory == "Incompatible");
        Assert.DoesNotContain(
            snapshot.Diagnostics,
            item => item.PluginDirectory == "Incompatible" &&
                    item.Code == HostDiagnosticCodes.PluginAssemblyLoadFailed);
    }

    [Fact]
    public void 重复PluginId在任何入口程序集加载前形成致命歧义()
    {
        var rootName = "DuplicateManifest-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(AppContext.BaseDirectory, rootName);
        Directory.CreateDirectory(root);

        try
        {
            foreach (var directoryName in new[] { "First", "Second" })
            {
                var directory = Path.Combine(root, directoryName);
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, PluginManifestReader.FileName),
                    ValidManifest(
                        pluginId: "myavalonia.plugin.duplicate",
                        entryAssembly: "Broken.dll"));
                File.WriteAllText(Path.Combine(directory, "Broken.dll"), "不是程序集");
            }

            var snapshot = AssemblyLoaderHelper.Discover(rootName);

            Assert.Empty(snapshot.Assemblies);
            Assert.Equal(
                2,
                snapshot.Diagnostics.Count(item =>
                    item.Code == HostDiagnosticCodes.PluginManifestIdentityDuplicate));
            Assert.DoesNotContain(
                snapshot.Diagnostics,
                item => item.Code is HostDiagnosticCodes.PluginEntryInvalid
                    or HostDiagnosticCodes.PluginAssemblyLoadFailed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 插件版本与程序集版本必须精确一致()
    {
        var manifest = CreateManifestModel(
            new PluginVersionRange(new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)));

        Assert.True(PluginCompatibilityEvaluator.HasMatchingPluginVersion(
            manifest,
            new Version(1, 2, 3, 0)));
        Assert.False(PluginCompatibilityEvaluator.HasMatchingPluginVersion(
            manifest,
            new Version(1, 2, 4, 0)));
        Assert.False(PluginCompatibilityEvaluator.HasMatchingPluginVersion(manifest, null));
    }

    [Fact]
    public void 模块上下文身份只来自已经验证的清单()
    {
        var assembly = typeof(ManifestMismatchModule).Assembly;
        var manifest = CreateManifestModel(
            new PluginVersionRange(new Version(1, 0, 0, 0), new Version(2, 0, 0, 0))) with
        {
            PluginId = new PluginId("myavalonia.plugin.manifest-identity"),
        };
        var snapshot = new PluginDiscoverySnapshot(
            [assembly],
            new Dictionary<Assembly, PluginManifest>
            {
                [assembly] = manifest,
            },
            new Dictionary<Assembly, Type>
            {
                [assembly] = typeof(ManifestMismatchModule),
            },
            diagnostics: []);

        ManifestMismatchModule.ObservedPluginId = null;
        var catalog = PluginModuleCatalog.Discover(snapshot);
        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        services.AddApplicationServices(builder, pluginProviders, documentScopes);
        services.AddSingleton(catalog);
        using var diagnostics = HostDiagnosticSession.Start(
            Path.Combine(Path.GetTempPath(), $"manifest-identity-{Guid.NewGuid():N}"));
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        using var hostProvider = services.BuildServiceProvider();

        pluginProviders.Compose(
            catalog,
            hostProvider,
            builder,
            documentScopes,
            diagnostics);

        Assert.Equal(
            manifest.PluginId.Value,
            ManifestMismatchModule.ObservedPluginId?.Value);
    }

    private static PluginManifest CreateManifestModel(PluginVersionRange sdk) =>
        new(
            PluginManifestReader.CurrentSchemaVersion,
            new PluginId("myavalonia.plugin.manifest-test"),
            new Version(1, 2, 3, 0),
            new PluginEntryPoint(
                "ManifestTest.dll",
                "ManifestTest.Plugin.ManifestTestModule"),
            sdk);

    private static void WithManifest(string? json, Action<string> assertion)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "MyAvalonia-Manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (json is not null)
            {
                File.WriteAllText(
                    Path.Combine(directory, PluginManifestReader.FileName),
                    json);
            }

            assertion(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ValidManifest(
        int schemaVersion = 2,
        string pluginId = "myavalonia.plugin.manifest-test",
        string pluginVersion = "1.2.3",
        string entryAssembly = "ManifestTest.dll",
        string entryType = "ManifestTest.Plugin.ManifestTestModule",
        string sdkMinimum = "1.0.0",
        string sdkMaximum = "2.0.0") =>
        $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "pluginId": "{{pluginId}}",
          "pluginVersion": "{{pluginVersion}}",
          "entryPoint": {
            "assembly": "{{entryAssembly}}",
            "type": "{{entryType}}"
          },
          "sdk": { "minInclusive": "{{sdkMinimum}}", "maxExclusive": "{{sdkMaximum}}" }
        }
        """;

    public sealed class ManifestMismatchModule : MyAvaloniaManagement.PluginSdk.UI.IPluginModule
    {
        internal static MyAvaloniaManagement.PluginSdk.PluginId? ObservedPluginId { get; set; }

        public void Configure(MyAvaloniaManagement.PluginSdk.UI.IPluginRegistration context) =>
            ObservedPluginId = context.PluginId;
    }
}
