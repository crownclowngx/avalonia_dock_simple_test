using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace MyAvaloniaManagement.Tests;

public sealed class WorkflowArtifactStoreTests
{
    [Fact]
    public async Task PendingArtifactIsInvisibleAndMismatchReleasesItsQuota()
    {
        var run = new WorkflowArtifactStore().CreateRun();
        using var pending = run.Begin(CancellationToken.None);
        await pending.WriteAsync(new byte[8 * 1024 * 1024], CancellationToken.None);
        var reference = pending.Seal("audio", "audio/mpeg");
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await run.OpenReadAsync(reference.Id, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => pending.Publish(Output(reference, size: 1)));
        pending.Dispose();

        using var first = await PublishFullArtifact(run);
        using var second = await PublishFullArtifact(run);
        Assert.Equal(8 * 1024 * 1024, first.Length);
        Assert.Equal(first.Length, second.Length);
        using var overBudget = run.Begin(CancellationToken.None);
        await Assert.ThrowsAsync<WorkflowArtifactQuotaException>(async () =>
            await overBudget.WriteAsync(new byte[1], CancellationToken.None));
        run.Close();
        Assert.Throws<ObjectDisposedException>(() => first.ReadByte());
    }

    [Fact]
    public async Task CancellationBeforePublicationPreventsOpeningArtifact()
    {
        var run = new WorkflowArtifactStore().CreateRun();
        using var cancellation = new CancellationTokenSource();
        using var pending = run.Begin(cancellation.Token);
        await pending.WriteAsync("mp3"u8.ToArray(), cancellation.Token);
        var reference = pending.Seal("audio", "audio/mpeg");

        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => pending.Publish(Output(reference)));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await run.OpenReadAsync(reference.Id, CancellationToken.None));
    }

    private static async Task<Stream> PublishFullArtifact(WorkflowArtifactStore.Run run)
    {
        using var pending = run.Begin(CancellationToken.None);
        await pending.WriteAsync(new byte[8 * 1024 * 1024], CancellationToken.None);
        var reference = pending.Seal("audio", "audio/mpeg");
        pending.Publish(Output(reference));
        return await run.OpenReadAsync(reference.Id, CancellationToken.None);
    }

    private static JsonElement Output(WorkflowActionArtifactReference reference, long? size = null) =>
        JsonSerializer.SerializeToElement(new
        {
            audio = new { id = reference.Id, mediaType = reference.MediaType, sizeBytes = size ?? reference.SizeBytes },
        });
}
