using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.WorkflowActions;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证关闭门控只在确认 Handler 排空后允许继续释放插件所有权图。</summary>
public sealed class WorkflowActionShutdownGateTests
{
    [Fact]
    public void 排空成功允许继续释放且先关闭入口()
    {
        var participant = new FakeParticipant(Task.FromResult(true));
        var gate = new WorkflowActionShutdownGate(participant);

        gate.BeginShutdown();
        var drained = gate.TryDrain(out var failure);

        Assert.True(participant.Began);
        Assert.True(drained);
        Assert.Null(failure);
        Assert.Equal(participant.ShutdownGrace, participant.ObservedTimeout);
    }

    [Fact]
    public void 宽限超时记录脱敏诊断并禁止释放Provider()
    {
        var participant = new FakeParticipant(Task.FromResult(false));
        var diagnostics = new RecordingDiagnosticSink();
        var gate = new WorkflowActionShutdownGate(participant, diagnostics);

        var drained = gate.TryDrain(out var failure);

        Assert.False(drained);
        Assert.IsType<TimeoutException>(failure);
        var draft = Assert.Single(diagnostics.Drafts);
        Assert.Equal(HostDiagnosticCodes.WorkflowActionShutdownTimeout, draft.Code);
        Assert.Equal(HostDiagnosticPhase.WorkflowAction, draft.Phase);
        Assert.Equal(participant.ShutdownGrace, draft.Duration);
    }

    [Fact]
    public void 排空检查异常按无法证明安全处理且不伪造超时诊断()
    {
        var expected = new InvalidOperationException("测试排空失败");
        var participant = new FakeParticipant(Task.FromException<bool>(expected));
        var diagnostics = new RecordingDiagnosticSink();
        var gate = new WorkflowActionShutdownGate(participant, diagnostics);

        var drained = gate.TryDrain(out var failure);

        Assert.False(drained);
        Assert.Same(expected, failure);
        Assert.Empty(diagnostics.Drafts);
    }

    private sealed class FakeParticipant(Task<bool> drainTask)
        : IWorkflowActionShutdownParticipant
    {
        public TimeSpan ShutdownGrace { get; } = TimeSpan.FromMilliseconds(10);
        internal bool Began { get; private set; }
        internal TimeSpan? ObservedTimeout { get; private set; }

        public void BeginShutdown() => Began = true;

        public Task<bool> WaitForDrainAsync(TimeSpan timeout)
        {
            ObservedTimeout = timeout;
            return drainTask;
        }
    }

    private sealed class RecordingDiagnosticSink : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Drafts { get; } = [];

        public HostDiagnosticRecord Report(HostDiagnosticDraft diagnostic)
        {
            Drafts.Add(diagnostic);
            return new HostDiagnosticRecord
            {
                SessionId = Guid.Empty,
                Sequence = Drafts.Count,
                TimestampUtc = DateTimeOffset.UnixEpoch,
                Code = diagnostic.Code,
                Severity = HostDiagnosticSeverity.Error,
                Phase = diagnostic.Phase,
                Disposition = HostDiagnosticDisposition.Continue,
                UserMessage = "测试诊断",
            };
        }
    }
}
