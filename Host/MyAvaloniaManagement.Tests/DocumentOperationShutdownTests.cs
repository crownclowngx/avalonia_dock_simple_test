using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Documents;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证功能中心新增入口在退出时仍受真实操作排空约束，不能只依据窗口是否关闭释放资源。</summary>
public sealed class DocumentOperationShutdownTests
{
    [Fact]
    public async Task 关闭拒绝新请求并清理已排队但未执行的请求()
    {
        var gate = new DocumentOperationGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = 0;
        var first = gate.RunAsync(async () => { executed++; await release.Task; return 1; });
        var queued = gate.RunAsync(() => { executed++; return Task.FromResult(2); });
        gate.BeginShutdown();
        Assert.False(await gate.WaitForDrainAsync(TimeSpan.FromMilliseconds(10)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(() => Task.FromResult(3)));
        release.SetResult();
        Assert.Equal(1, await first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => queued);
        Assert.Equal(1, executed);
        Assert.True(await gate.WaitForDrainAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task 初始化超时保留全部所有权直到原操作实际结束()
    {
        var gate = new DocumentOperationGate(TimeSpan.FromMilliseconds(15));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = gate.RunAsync(async () => { await release.Task; return true; });
        var participants = new HostShutdownParticipants();
        participants.Record(gate);
        var calls = new List<string>();
        var retained = new HostResourceRetention();
        var shutdown = new HostRuntimeShutdown(new Probe("plugin", calls), new Probe("host", calls),
            () => calls.Add("scope"), participants, retained, null);
        var result = await shutdown.RunAsync();
        Assert.True(result.ResourcesRetained);
        Assert.Single(result.Failures);
        Assert.Empty(calls);
        Assert.Equal(1, retained.Count);
        release.SetResult();
        await operation;
        Assert.Empty(calls); // 迟到完成不会擅自改变 V5 明确规定的保留政策。
    }

    [Fact]
    public async Task 正常排空后按原有顺序释放Scope与Provider()
    {
        var gate = new DocumentOperationGate(TimeSpan.FromSeconds(1));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = gate.RunAsync(async () => { await release.Task; return true; });
        var participants = new HostShutdownParticipants();
        participants.Record(gate);
        var calls = new List<string>();
        var shutdown = new HostRuntimeShutdown(new Probe("plugin", calls), new Probe("host", calls),
            () => calls.Add("scope"), participants, new HostResourceRetention(), null);
        var completion = shutdown.RunAsync();
        Assert.Empty(calls);
        release.SetResult();
        await operation;
        var result = await completion;
        Assert.False(result.ResourcesRetained);
        Assert.Empty(result.Failures);
        Assert.Equal(new[] { "scope", "plugin", "host" }, calls);
    }

    private sealed class Probe(string name, List<string> calls) : IDisposable
    {
        public void Dispose() => calls.Add(name);
    }
}
