using System.Reflection;
using System.Text.Json;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证统一诊断会话的持久化、失败降级和启动决策。
/// </summary>
[Collection("HostDiagnosticsSensitiveOutput")]
public sealed class HostDiagnosticsTests
{
    private static readonly string[] SensitiveCanaries =
    [
        "G15-password=CorrectHorseBatteryStaple",
        "Cookie: session=G15-cookie",
        "Bearer G15-token",
        "https://example.test/download?signature=G15-signature",
        @"C:\Users\secret\G15-private.mamdoc",
        "/home/secret/G15-private.mamdoc",
        "G15-document-body",
    ];

    [Fact]
    public void 会话记录写入内存和可逐条解析的JsonLines日志()
    {
        using var workspace = new DiagnosticWorkspace();
        string logPath;
        using (var session = HostDiagnosticSession.Start(workspace.Root))
        {
            var record = session.Report(new HostDiagnosticDraft(
                "LAYOUT_JSON_INVALID",
                HostDiagnosticPhase.Layout)
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
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("LAYOUT_JSON_INVALID", root.GetProperty("code").GetString());
        Assert.Equal("Warning", root.GetProperty("severity").GetString());
        Assert.Equal("Continue", root.GetProperty("disposition").GetString());
        Assert.Equal(
            typeof(InvalidDataException).FullName,
            root.GetProperty("exceptionType").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("technicalDetail").ValueKind);
        Assert.DoesNotContain("technical-only", line, StringComparison.Ordinal);
    }

    [Fact]
    public void 默认诊断的内存JsonLines和镜像均不包含异常敏感正文()
    {
        using var workspace = new DiagnosticWorkspace();
        var previous = Environment.GetEnvironmentVariable(
            HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName);
        var originalError = Console.Error;
        using var captured = new StringWriter();
        string logPath;
        HostDiagnosticRecord record;
        try
        {
            Environment.SetEnvironmentVariable(
                HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName,
                null);
            Console.SetError(captured);
            using var session = HostDiagnosticSession.Start(workspace.Root);
            record = session.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.PluginAssemblyLoadFailed,
                HostDiagnosticPhase.PluginAssemblyLoad)
            {
                PluginId = new PluginId("myavalonia.plugin.g15-test"),
                PluginDirectory = "G15Plugin",
                AssemblyName = new AssemblyName("G15.Plugin"),
                StableId = "myavalonia.plugin.g15-test.document.sample",
                Exception = new InvalidOperationException(
                    string.Join(" | ", SensitiveCanaries)),
            });
            logPath = Assert.IsType<string>(session.LogPath);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable(
                HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName,
                previous);
        }

        var recordText = JsonSerializer.Serialize(record);
        var jsonLines = File.ReadAllText(logPath);
        AssertSensitiveCanariesAbsent(recordText);
        AssertSensitiveCanariesAbsent(jsonLines);
        AssertSensitiveCanariesAbsent(captured.ToString());
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("myavalonia.plugin.g15-test", record.PluginId);
        Assert.Equal("G15.Plugin", record.AssemblyName);
        Assert.Null(record.TechnicalDetail);
    }

    [Fact]
    public void 白名单转换丢弃路径和非法结构字段并保留受控阶段耗时()
    {
        var invalid = HostDiagnosticRedactionPolicy.Create(
            Guid.NewGuid(),
            new HostDiagnosticDraft(
                "bad code with spaces",
                HostDiagnosticPhase.PluginAssemblyLoad)
            {
                PluginDirectory = SensitiveCanaries[4],
                AssemblyName = new AssemblyName("Unsafe Assembly"),
                StableId = SensitiveCanaries[3],
            },
            DateTimeOffset.UtcNow);

        Assert.Equal(HostDiagnosticCodes.DiagnosticInputRejected, invalid.Code);
        Assert.Equal("诊断输入未通过白名单校验，原始输入未被保存。", invalid.UserMessage);
        Assert.Null(invalid.PluginDirectory);
        Assert.Null(invalid.AssemblyName);
        Assert.Null(invalid.StableId);
        AssertSensitiveCanariesAbsent(JsonSerializer.Serialize(invalid));

        var controlled = HostDiagnosticRedactionPolicy.Create(
            Guid.NewGuid(),
            new HostDiagnosticDraft(
                "LIFECYCLE_INITIALIZE_FAILED",
                HostDiagnosticPhase.PluginLifecycle)
            {
                PluginId = new PluginId("myavalonia.plugin.g15-test"),
                PluginDirectory = "G15Plugin",
                AssemblyName = new AssemblyName("G15.Plugin"),
                StableId = "myavalonia.plugin.g15-test",
                PluginVersion = new Version(1, 2, 3),
                SdkRange = new PluginVersionRange(
                    new Version(1, 0),
                    new Version(2, 0)),
                LifecycleStage = PluginLifecycleStage.Initialization,
                Duration = TimeSpan.FromMilliseconds(12.5),
            },
            DateTimeOffset.UtcNow);

        Assert.Equal("myavalonia.plugin.g15-test", controlled.PluginId);
        Assert.Equal("G15Plugin", controlled.PluginDirectory);
        Assert.Equal("G15.Plugin", controlled.AssemblyName);
        Assert.Equal("1.2.3.0", controlled.PluginVersion);
        Assert.Equal("[1.0.0.0, 2.0.0.0)", controlled.SdkRange);
        Assert.Equal("stage=Initialization; durationMs=12.5", controlled.TechnicalDetail);
    }

    [Fact]
    public void 显式敏感开关只把异常原文写入临时输出()
    {
        using var workspace = new DiagnosticWorkspace();
        var previous = Environment.GetEnvironmentVariable(
            HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName);
        var originalError = Console.Error;
        using var captured = new StringWriter();
        string logPath;
        HostDiagnosticRecord record;
        try
        {
            Environment.SetEnvironmentVariable(
                HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName,
                "1");
            Console.SetError(captured);
            using var session = HostDiagnosticSession.Start(workspace.Root);
            record = session.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.HostContainerBuildFailed,
                HostDiagnosticPhase.HostContainerBuild)
            {
                Exception = new InvalidOperationException(
                    string.Join(" | ", SensitiveCanaries)),
            });
            logPath = Assert.IsType<string>(session.LogPath);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable(
                HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName,
                previous);
        }

        Assert.Contains("敏感诊断已开启", captured.ToString(), StringComparison.Ordinal);
        Assert.Contains(SensitiveCanaries[0], captured.ToString(), StringComparison.Ordinal);
        AssertSensitiveCanariesAbsent(JsonSerializer.Serialize(record));
        AssertSensitiveCanariesAbsent(File.ReadAllText(logPath));
    }

    [Fact]
    public void 非精确开关值不会开启敏感输出()
    {
        var previous = Environment.GetEnvironmentVariable(
            HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(
                HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName,
                "true");
            Assert.False(HostSensitiveDiagnosticDebugOutput.IsEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                HostSensitiveDiagnosticDebugOutput.EnvironmentVariableName,
                previous);
        }
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
            HostDiagnosticPhase.PluginRootDiscovery));

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
    [InlineData(HostDiagnosticCodes.PluginManifestMissing, "PluginManifestPreflight", "Continue")]
    [InlineData(HostDiagnosticCodes.PluginSdkIncompatible, "PluginManifestPreflight", "Continue")]
    [InlineData(HostDiagnosticCodes.PluginManifestIdentityDuplicate, "PluginManifestPreflight", "AbortStartup")]
    [InlineData(HostDiagnosticCodes.PluginManifestDescriptionMismatch, "PluginManifestPreflight", "AbortStartup")]
    [InlineData("LIFECYCLE_INITIALIZE_FAILED", "PluginLifecycle", "Continue")]
    [InlineData("LAYOUT_APPLY_FAILED", "Layout", "Continue")]
    [InlineData(HostDiagnosticCodes.PluginRootScanFailed, "PluginRootDiscovery", "AbortStartup")]
    [InlineData("PLUGIN_ID_DUPLICATE", "PluginModuleDiscovery", "AbortStartup")]
    [InlineData(HostDiagnosticCodes.PluginServiceRegistrationFailed, "PluginServiceRegistration", "Continue")]
    [InlineData(HostDiagnosticCodes.PluginContainerBuildFailed, "PluginServiceRegistration", "Continue")]
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
    public void 组合诊断转换后只保留稳定Id而不保存贡献类型技术详情()
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
            HostDiagnosticPhase.ExtensionDiscovery);

        var record = Assert.Single(session.Snapshot);
        Assert.Equal("myavalonia.plugin.sample.document.report", record.PluginId);
        Assert.Equal("myavalonia.plugin.sample.document.report", record.StableId);
        Assert.Null(record.TechnicalDetail);
        Assert.Equal(HostDiagnosticDisposition.AbortStartup, record.Disposition);
    }

    [Fact]
    public void 生命周期失败的内存JsonLines和默认日志均不包含插件异常正文()
    {
        using var workspace = new DiagnosticWorkspace();
        var originalError = Console.Error;
        using var captured = new StringWriter();
        HostDiagnosticRecord record;
        string logPath;
        try
        {
            Console.SetError(captured);
            using var session = HostDiagnosticSession.Start(workspace.Root);
            record = session.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.LifecycleInitializeFailed,
                HostDiagnosticPhase.PluginLifecycle)
            {
                PluginId = new PluginId("myavalonia.plugin.g15-lifecycle"),
                LifecycleStage = PluginLifecycleStage.Initialization,
                Duration = TimeSpan.FromMilliseconds(12),
                Exception = new InvalidOperationException(
                    string.Join(" | ", SensitiveCanaries)),
            });
            logPath = Assert.IsType<string>(session.LogPath);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal("插件初始化失败或超时，已隔离该插件贡献。", record.UserMessage);
        Assert.Equal("stage=Initialization; durationMs=12", record.TechnicalDetail);
        AssertSensitiveCanariesAbsent(JsonSerializer.Serialize(record));
        AssertSensitiveCanariesAbsent(File.ReadAllText(logPath));
        AssertSensitiveCanariesAbsent(captured.ToString());
    }

    [Fact]
    public void 文档错误映射不泄漏内部格式异常正文()
    {
        var exception = new DocumentEnvelopeException(
            string.Join(" | ", SensitiveCanaries),
            new InvalidOperationException(SensitiveCanaries[0]));

        var message = DocumentPersistenceErrorMapper.ToOpenFailureMessage(exception);

        Assert.Equal(
            "无法打开所选文件：文档内容不受支持或已损坏。 原文件未被修改。",
            message);
        AssertSensitiveCanariesAbsent(message);
    }

    [Fact]
    public void 插件状态模型包含尚未取得PluginId的隔离候选()
    {
        using var workspace = new DiagnosticWorkspace();
        using var session = HostDiagnosticSession.Start(workspace.Root);
        session.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.PluginEntryInvalid,
            HostDiagnosticPhase.PluginRootDiscovery)
        {
            PluginDirectory = "BrokenPlugin",
        });

        var viewModel = new PluginStatusViewModel(
            new PluginRegistry([], []),
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

    private static void AssertSensitiveCanariesAbsent(string text)
    {
        foreach (var canary in SensitiveCanaries)
        {
            Assert.DoesNotContain(canary, text, StringComparison.OrdinalIgnoreCase);
        }
    }

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

[CollectionDefinition("HostDiagnosticsSensitiveOutput", DisableParallelization = true)]
public sealed class HostDiagnosticsSensitiveOutputCollection;
