using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证工作台命令诊断只长期保存白名单字段和固定中文说明。</summary>
public sealed class WorkbenchCommandDiagnosticTests
{
    [Fact]
    public void 执行异常正文与路径不会进入诊断记录()
    {
        var record = HostDiagnosticRedactionPolicy.Create(
            Guid.NewGuid(),
            new HostDiagnosticDraft(
                HostDiagnosticCodes.WorkbenchCommandExecutionFailed,
                HostDiagnosticPhase.WorkbenchCommand)
            {
                StableId = "myavalonia.host.command.document.open",
                Exception = new InvalidOperationException(
                    "secret C:\\private\\payload.json"),
            },
            DateTimeOffset.UnixEpoch);

        Assert.Equal(HostDiagnosticSeverity.Error, record.Severity);
        Assert.Equal(HostDiagnosticDisposition.Continue, record.Disposition);
        Assert.Equal("工作台命令执行失败；异常正文未写入诊断。", record.UserMessage);
        Assert.Equal("myavalonia.host.command.document.open", record.StableId);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Null(record.TechnicalDetail);
        Assert.DoesNotContain("secret", record.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", record.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 关闭超时只记录固定消息与受控时长()
    {
        var record = HostDiagnosticRedactionPolicy.Create(
            Guid.NewGuid(),
            new HostDiagnosticDraft(
                HostDiagnosticCodes.WorkbenchCommandShutdownTimeout,
                HostDiagnosticPhase.WorkbenchCommand)
            {
                Duration = TimeSpan.FromSeconds(10),
                Exception = new TimeoutException("secret-timeout-body"),
            },
            DateTimeOffset.UnixEpoch);

        Assert.Equal(
            "工作台命令在关闭宽限内没有退出，宿主已阻止不安全的工作区和 Provider 释放。",
            record.UserMessage);
        Assert.Equal("durationMs=10000", record.TechnicalDetail);
        Assert.DoesNotContain("secret", record.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", record.TechnicalDetail!, StringComparison.OrdinalIgnoreCase);
    }
}
