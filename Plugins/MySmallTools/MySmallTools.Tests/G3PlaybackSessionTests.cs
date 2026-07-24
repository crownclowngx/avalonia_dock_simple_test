using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using Xunit;

namespace MySmallTools.Tests;

public sealed class G3PlaybackSessionTests
{
    [Fact]
    public async Task FailedCandidate_DoesNotReplaceCurrentMedia()
    {
        var first = new FakeLease(1);
        var factory = new FakeLeaseFactory(
            _ => Task.FromResult<IPlaybackMediaLease>(first),
            _ => throw new PlaybackOperationException(PlaybackFailureMapper.ParseFailed()));
        using var session = new SecureVideoPlayer(factory);

        var loaded = await session.LoadAsync("first.secvid", "password");
        var failed = await session.LoadAsync("broken.secvid", "password");

        Assert.True(loaded.Success);
        Assert.False(failed.Success);
        Assert.Equal(PlaybackFailureCode.ParseFailed, failed.Failure?.Code);
        Assert.Equal(1, session.Snapshot.MediaGeneration);
        Assert.True(session.Snapshot.HasMedia);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public async Task NewerLoad_InvalidatesCandidatePreparedByOlderRequest()
    {
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeLease(1);
        var second = new FakeLease(2);
        var factory = new FakeLeaseFactory(
            async cancellationToken =>
            {
                firstEntered.TrySetResult();
                try
                {
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                    return first;
                }
                catch
                {
                    first.Dispose();
                    throw;
                }
            },
            _ => Task.FromResult<IPlaybackMediaLease>(second));
        using var session = new SecureVideoPlayer(factory);

        var older = session.LoadAsync("first.secvid", "password");
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var newer = session.LoadAsync("second.secvid", "password");
        releaseFirst.TrySetResult();

        var olderResult = await older;
        var newerResult = await newer;

        Assert.False(olderResult.Success);
        Assert.Equal(PlaybackFailureCode.Cancelled, olderResult.Failure?.Code);
        Assert.True(first.IsDisposed);
        Assert.True(newerResult.Success);
        Assert.Equal(2, session.Snapshot.MediaGeneration);
    }

    [Fact]
    public async Task EventsFromDisposedOldLease_CannotChangeNewSession()
    {
        var first = new FakeLease(1);
        var second = new FakeLease(2);
        var factory = new FakeLeaseFactory(
            _ => Task.FromResult<IPlaybackMediaLease>(first),
            _ => Task.FromResult<IPlaybackMediaLease>(second));
        using var session = new SecureVideoPlayer(factory);

        Assert.True((await session.LoadAsync("first.secvid", "password")).Success);
        Assert.True((await session.LoadAsync("second.secvid", "password")).Success);
        first.RaiseState(PlaybackState.Faulted);
        first.RaiseFailure(new PlaybackFailure(
            PlaybackFailureCode.CorruptedContent,
            "stale"));

        Assert.True(first.IsDisposed);
        Assert.Equal(2, session.Snapshot.MediaGeneration);
        Assert.Equal(PlaybackState.Ready, session.Snapshot.State);
    }

    [Fact]
    public async Task Pause_IsIdempotentAndNeverTogglesPlayback()
    {
        var lease = new FakeLease(1);
        var factory = new FakeLeaseFactory(
            _ => Task.FromResult<IPlaybackMediaLease>(lease));
        using var session = new SecureVideoPlayer(factory);
        Assert.True((await session.LoadAsync("video.secvid", "password")).Success);

        Assert.True((await session.PauseAsync()).Success);
        Assert.True((await session.PauseAsync()).Success);

        Assert.Equal([true, true], lease.PauseRequests);
        Assert.Equal(PlaybackState.Paused, session.Snapshot.State);
    }

    [Fact]
    public async Task UserStopDuringSurfaceLoss_CancelsAutomaticRestore()
    {
        var lease = new FakeLease(1);
        var factory = new FakeLeaseFactory(
            _ => Task.FromResult<IPlaybackMediaLease>(lease));
        using var session = new SecureVideoPlayer(factory);
        Assert.True((await session.LoadAsync("video.secvid", "password")).Success);

        var firstSurface = new VideoSurfaceToken(1, (nint)101);
        Assert.True((await session.AttachAndRestoreSurfaceAsync(firstSurface)).Success);
        Assert.True((await session.PlayAsync()).Success);
        session.DetachSurface(firstSurface);
        Assert.True((await session.StopAsync()).Success);

        var secondSurface = new VideoSurfaceToken(2, (nint)202);
        Assert.True((await session.AttachAndRestoreSurfaceAsync(secondSurface)).Success);

        Assert.Equal(0, lease.RestoreCalls);
        Assert.Equal(PlaybackState.Stopped, session.Snapshot.State);
    }

    [Fact]
    public void FailureMapper_UsesStableCodesAndSafeMessages()
    {
        var path = @"C:\private\secret-name.secvid";
        var load = PlaybackFailureMapper.MapLoad(new FileNotFoundException(path));
        var content = PlaybackFailureMapper.MapMediaInput(new InvalidDataException(path));

        Assert.Equal(PlaybackFailureCode.InputUnavailable, load.Code);
        Assert.Equal(PlaybackFailureCode.CorruptedContent, content.Code);
        Assert.DoesNotContain("private", load.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-name", content.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MediaInput_PreservesFirstTypedFailureAndConsumesItOnce()
    {
        using var stream = new SequenceFailureStream(
            new InvalidDataException("first"),
            new IOException("second"));
        using var input = new SeekableStreamMediaInput(stream);
        var native = Marshal.AllocHGlobal(4);
        try
        {
            Assert.Equal(-1, input.Read(native, 4));
            Assert.Equal(-1, input.Read(native, 4));
            Assert.True(input.TryTakeLastFailure(out var failure));
            Assert.Equal(PlaybackFailureCode.CorruptedContent, failure?.Code);
            Assert.False(input.TryTakeLastFailure(out _));
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    private sealed class FakeLeaseFactory(
        params Func<CancellationToken, Task<IPlaybackMediaLease>>[] factories)
        : IPlaybackMediaLeaseFactory
    {
        private int _index;

        public Task<IPlaybackMediaLease> CreateAsync(
            long generation,
            string filePath,
            string password,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return factories[Math.Min(index, factories.Length - 1)](cancellationToken);
        }
    }

    private sealed class FakeLease(long generation) : IPlaybackMediaLease
    {
        public long Generation { get; } = generation;
        public MediaPlayer? NativePlayer => null;
        public long PositionMs { get; private set; } = 1_000;
        public long DurationMs { get; } = 6_000;
        public bool IsSeekable { get; } = true;
        public bool HasVideo { get; } = true;
        public bool HasAudio { get; } = true;
        public int VideoTrackCount { get; } = 1;
        public int AudioTrackCount { get; } = 1;
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public int Volume { get; private set; } = 50;
        public bool IsDisposed { get; private set; }
        public List<bool> PauseRequests { get; } = [];
        public int RestoreCalls { get; private set; }
        public nint OutputHandle { get; private set; }

        public event Action<IPlaybackMediaLease, PlaybackState>? StateChanged;
        public event EventHandler? PositionChanged;
        public event Action<IPlaybackMediaLease, PlaybackFailure>? Failed;

        public void PrepareForPlayback()
        {
        }

        public void RequestStop()
        {
        }

        public void Stop()
        {
            IsPlaying = false;
            IsPaused = false;
            StateChanged?.Invoke(this, PlaybackState.Stopped);
        }

        public void SetPause(bool paused)
        {
            PauseRequests.Add(paused);
            IsPaused = paused;
            IsPlaying = !paused;
            StateChanged?.Invoke(this, paused ? PlaybackState.Paused : PlaybackState.Playing);
        }

        public bool Play()
        {
            IsPlaying = true;
            IsPaused = false;
            StateChanged?.Invoke(this, PlaybackState.Playing);
            return true;
        }

        public void SetVolume(int volume) => Volume = Math.Clamp(volume, 0, 100);

        public void SetVideoOutputHandle(nint handle) => OutputHandle = handle;

        public Task SeekAsync(
            long positionMs,
            bool waitForFrame,
            CancellationToken cancellationToken)
        {
            PositionMs = Math.Clamp(positionMs, 0, DurationMs);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<bool> RestoreSurfaceAsync(
            long positionMs,
            bool restorePaused,
            CancellationToken cancellationToken)
        {
            RestoreCalls++;
            PositionMs = positionMs;
            IsPaused = restorePaused;
            IsPlaying = !restorePaused;
            return Task.FromResult(true);
        }

        public void RaiseState(PlaybackState state) => StateChanged?.Invoke(this, state);

        public void RaiseFailure(PlaybackFailure failure) => Failed?.Invoke(this, failure);

        public void Dispose()
        {
            IsDisposed = true;
            StateChanged = null;
            PositionChanged = null;
            Failed = null;
        }
    }

    private sealed class SequenceFailureStream(params Exception[] failures) : Stream
    {
        private int _readIndex;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 4;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw failures[Math.Min(_readIndex++, failures.Length - 1)];

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = offset;
            return Position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
