using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>只在 G0 的原始 3.0 隔离副本中运行的新插件反向兼容测试。</summary>
public sealed class WorkflowActionG0OldHostTests
{
    [Fact]
    public void 三零Host在加载入口程序集前拒绝要求三一Sdk的新插件()
    {
        var rootName = "WorkflowG0OldHost-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(AppContext.BaseDirectory, rootName);
        var pluginDirectory = Path.Combine(root, "Requires31");
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(pluginDirectory, "plugin.manifest.json"),
                """
                {
                  "schemaVersion": 2,
                  "pluginId": "myavalonia.plugin.workflow-g0-new",
                  "pluginVersion": "1.0.0",
                  "entryPoint": {
                    "assembly": "BrokenPlugin.dll",
                    "type": "WorkflowG0.NewPlugin.Module"
                  },
                  "sdk": { "minInclusive": "3.1.0", "maxExclusive": "4.0.0" }
                }
                """);
            // 伪 DLL 是执行顺序哨兵：若 Host 在兼容检查前尝试加载，会产生程序集加载错误。
            File.WriteAllText(Path.Combine(pluginDirectory, "BrokenPlugin.dll"), "不是程序集");

            var snapshot = AssemblyLoaderHelper.Discover(rootName);

            Assert.Empty(snapshot.Assemblies);
            var diagnostic = Assert.Single(snapshot.Diagnostics);
            Assert.Equal(HostDiagnosticCodes.PluginSdkIncompatible, diagnostic.Code);
            Assert.DoesNotContain(
                snapshot.Diagnostics,
                item => item.Code == HostDiagnosticCodes.PluginAssemblyLoadFailed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
