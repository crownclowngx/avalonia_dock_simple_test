using System.Text.Json;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证统一诊断会话的持久化、失败降级和启动决策。
/// </summary>
public sealed class HostDiagnosticsTests
{
    [Fact]
    public void 会话记录写入内存和可逐条解析的JsonLines日志()
    {
        using var workspace = new DiagnosticWorkspace();
        string logPath;
        using (var session = HostDiagnosticSession.Start(workspace.Root))
        {
            var record = session.Report(new HostDiagnosticDraft(
                "LAYOUT_JSON_INVALID",
                HostDiagnosticPhase.Layout,
                "布局文件无效。")
            {
                StableId = "layout",
                Exception = new InvalidDataException("technical-only"),
            });

            Assert.Equal(HostDiagnosticSeverity.Warning, record.Severity);
            Assert.Equal(HostDiagnosticDisposition.Continue, record.Disposition);
            Assert.Single(session.Snapshot);
            logPath = Assert.IsType<string>(session.LogPath);
        }

        var lines = File.ReadAllLines(logPath);
        var line = Assert.Single(lines);
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("LAYOUT_JSON_INVALID", root.GetProperty("code").GetString());
        Assert.Equal("Warning", root.GetProperty("severity").GetString());
        Assert.Equal("Continue", root.GetProperty("disposition").GetString());
        Assert.Contains("technical-only", root.GetProperty("technicalDetail").GetString());
    }

    [Fact]
    public void 启动时只保留包含当前文件在内的最近二十次会话()
    {
        using var workspace = new DiagnosticWorkspace();
        var directory = Path.Combine(workspace.Root, "Diagnostics");
        Directory.CreateDirectory(directory);
        for (var index = 0; index < 25; index++)
        {
            File.WriteAllText(
                Path.Combine(directory, $"session-20000101T000000{index:0000000}Z-1.jsonl"),
                "{}\n");
        }

        using var session = HostDiagnosticSession.Start(workspace.Root);

        Assert.Equal(20, Directory.GetFiles(directory, "session-*.jsonl").Length);
        Assert.NotNull(session.LogPath);
    }

    [Fact]
    public void 日志目录不可用时退化为内存诊断且继续接收记录()
    {
        using var workspace = new DiagnosticWorkspace();
        var blockingFile = Path.Combine(workspace.Root, "blocking-file");
        File.WriteAllText(blockingFile, "not-a-directory");

        using var session = HostDiagnosticSession.Start(blockingFile);
        session.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.PluginEntryInvalid,
            HostDiagnosticPhase.PluginRootDiscovery,
            "测试插件入口无效。"));

        Assert.Null(session.LogPath);
        Assert.Contains(
            session.Snapshot,
            item => item.Code == HostDiagnosticCodes.PersistenceUnavailable);
        Assert.Contains(
            session.Snapshot,
            item => item.Code == HostDiagnosticCodes.PluginEntryInvalid);
        Assert.All(session.Snapshot, item =>
            Assert.Equal(HostDiagnosticDisposition.Continue, item.Disposition));
    }

    [Theory]
    [InlineData(HostDiagnosticCodes.PluginEntryInvalid, "PluginRootDiscovery", "Continue")]
    [InlineData(HostDiagnosticCodes.PluginAssemblyLoadFailed, "PluginAssemblyLoad", "Continue")]
    [InlineData("LIFECYCLE_INITIALIZE_FAILED", "PluginLifecycle", "Continue")]
    [InlineData("LAYOUT_APPLY_FAILED", "Layout", "Continue")]
    [InlineData(HostDiagnosticCodes.PluginRootScanFailed, "PluginRootDiscovery", "AbortStartup")]
    [InlineData("PLUGIN_ID_DUPLICATE", "PluginModuleDiscovery", "AbortStartup")]
    [InlineData(HostDiagnosticCodes.PluginServiceRegistrationFailed, "PluginServiceRegistration", "AbortStartup")]
    [InlineData(HostDiagnosticCodes.HostContainerBuildFailed, "HostContainerBuild", "AbortStartup")]
    public void 失败策略按阶段和错误码给出稳定启动决策(
        string code,
        string phaseName,
        string expectedDisposition)
    {
        var phase = Enum.Parse<HostDiagnosticPhase>(phaseName);
        var expected = Enum.Parse<HostDiagnosticDisposition>(expectedDisposition);
        var actual = HostDiagnosticFailurePolicy.Classify(code, phase);

        Assert.Equal(expected, actual.Disposition);
    }

    [Fact]
    public void 组合诊断转换后保留稳定Id和全部贡献来源()
    {
        using var workspace = new DiagnosticWorkspace();
        using var session = HostDiagnosticSession.Start(workspace.Root);
        var exception = new HostCompositionException([
            new HostCompositionDiagnostic(
                "DOCUMENT_ID_DUPLICATE",
                "myavalonia.plugin.sample.document.report",
                [
                    new HostCompositionContributor("Sample.First", "Sample.Plugin"),
                    new HostCompositionContributor("Sample.Second", "Sample.Plugin")
                ])
        ]);

        HostRuntime.ReportCompositionDiagnostics(
            session,
            exception,
            HostDiagnosticPhase.ExtensionDiscovery,
            "扩展冲突。");

        var record = Assert.Single(session.Snapshot);
        Assert.Equal("myavalonia.plugin.sample.document.report", record.PluginId);
        Assert.Equal("myavalonia.plugin.sample.document.report", record.StableId);
        Assert.Contains("Sample.First (Sample.Plugin)", record.TechnicalDetail);
        Assert.Contains("Sample.Second (Sample.Plugin)", record.TechnicalDetail);
        Assert.Equal(HostDiagnosticDisposition.AbortStartup, record.Disposition);
    }

    [Fact]
    public void 插件状态模型包含尚未取得PluginId的隔离候选()
    {
        using var workspace = new DiagnosticWorkspace();
        using var session = HostDiagnosticSession.Start(workspace.Root);
        session.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.PluginEntryInvalid,
            HostDiagnosticPhase.PluginRootDiscovery,
            "插件目录没有入口程序集。")
        {
            PluginDirectory = "BrokenPlugin",
        });

        var viewModel = new PluginStatusViewModel(
            PluginModuleCatalog.Discover([]),
            new PluginLifecycleManager([]),
            session);

        var item = Assert.Single(viewModel.Items);
        Assert.Equal("目录：BrokenPlugin", item.PluginId);
        Assert.Equal("加载失败 · 已隔离", item.StatusText);
        Assert.Contains(HostDiagnosticCodes.PluginEntryInvalid, item.Detail);
        Assert.Contains("目录发现", item.Detail);
    }

    [Fact]
    public void 启动失败上下文按严重程度排序并可在会话间清理()
    {
        var sessionId = Guid.NewGuid();
        var warning = CreateRecord(
            sessionId,
            sequence: 1,
            "LAYOUT_JSON_INVALID",
            HostDiagnosticSeverity.Warning);
        var fatal = CreateRecord(
            sessionId,
            sequence: 2,
            "PLUGIN_ID_DUPLICATE",
            HostDiagnosticSeverity.Fatal);

        HostStartupFailureContext.Set([warning, fatal], "diagnostics.jsonl");

        var current = Assert.IsType<HostStartupFailureContext>(
            HostStartupFailureContext.Current);
        Assert.Equal("PLUGIN_ID_DUPLICATE", current.Diagnostics[0].Code);
        Assert.Equal("diagnostics.jsonl", current.LogPath);

        HostStartupFailureContext.Clear();
        Assert.Null(HostStartupFailureContext.Current);
    }

    [Fact]
    public void 加载异常映射优先识别共享契约版本冲突()
    {
        var mismatch = new FileLoadException(
            "PLUGIN_SHARED_ASSEMBLY_MISMATCH: test");
        var wrapped = new InvalidOperationException("outer", mismatch);

        Assert.Equal(
            HostDiagnosticCodes.PluginSharedAssemblyMismatch,
            PluginLoadExceptionMapper.GetCode(wrapped));
        Assert.Equal(
            HostDiagnosticCodes.PluginAssemblyLoadFailed,
            PluginLoadExceptionMapper.GetCode(new FileNotFoundException("missing")));
    }

    private static HostDiagnosticRecord CreateRecord(
        Guid sessionId,
        long sequence,
        string code,
        HostDiagnosticSeverity severity) =>
        new()
        {
            SessionId = sessionId,
            Sequence = sequence,
            TimestampUtc = DateTimeOffset.UtcNow,
            Code = code,
            Severity = severity,
            Phase = HostDiagnosticPhase.HostBootstrap,
            Disposition = severity == HostDiagnosticSeverity.Fatal
                ? HostDiagnosticDisposition.AbortStartup
                : HostDiagnosticDisposition.Continue,
            UserMessage = code,
        };

    private sealed class DiagnosticWorkspace : IDisposable
    {
        internal DiagnosticWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "MyAvaloniaManagement.DiagnosticsTests",
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
