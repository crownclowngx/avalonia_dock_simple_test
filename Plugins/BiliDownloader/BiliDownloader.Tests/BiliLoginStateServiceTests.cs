using BiliDownloader.Services.Auth;

namespace BiliDownloader.Tests;

public sealed class BiliLoginStateServiceTests
{
    [Fact]
    public async Task 本地恢复不访问网络_后台验证后缓存真实账号资料()
    {
        var store = CreateStore(userName: null);
        var api = new ScriptedSessionApi();
        api.EnqueueResult(new LoginValidationResult(
            LoginValidationStatus.Valid,
            "remote-user",
            "remote-avatar"));
        var state = CreateState(store, api);

        await state.RestoreSavedSessionAsync();

        Assert.True(state.IsLoggedIn);
        Assert.Null(state.UserName);
        Assert.Equal(0, api.ValidationCount);

        state.StartBackgroundValidation();
        await state.StopAsync();

        Assert.Equal(1, api.ValidationCount);
        Assert.Equal("remote-user", state.UserName);
        Assert.Equal("remote-avatar", state.UserAvatar);
        Assert.Equal("remote-user", store.Session?.UserName);
        Assert.Equal("remote-avatar", store.Session?.UserAvatar);
        Assert.Equal(1, store.SaveCount);
        Assert.Null(state.StatusMessage);
    }

    [Fact]
    public async Task 网络不可用时保留Cookie和缓存账号资料()
    {
        var store = CreateStore("cached-user");
        var api = new ScriptedSessionApi();
        api.EnqueueResult(new LoginValidationResult(LoginValidationStatus.Unavailable));
        var state = CreateState(store, api);

        await state.RestoreSavedSessionAsync();
        state.StartBackgroundValidation();
        await state.StopAsync();

        Assert.True(state.IsLoggedIn);
        Assert.True(state.IsPersistentLogin);
        Assert.Equal("cached-user", state.UserName);
        Assert.NotNull(store.Session);
        Assert.Equal(0, store.DeleteCount);
        Assert.Equal("暂时无法验证登录状态，已保留本地登录信息。", state.StatusMessage);
    }

    [Fact]
    public async Task 服务端明确失效时删除凭据并切换为未登录()
    {
        var store = CreateStore("expired-user");
        var api = new ScriptedSessionApi();
        api.EnqueueResult(new LoginValidationResult(LoginValidationStatus.Invalid));
        var state = CreateState(store, api);

        await state.RestoreSavedSessionAsync();
        state.StartBackgroundValidation();
        await state.StopAsync();

        Assert.False(state.IsLoggedIn);
        Assert.False(state.IsPersistentLogin);
        Assert.Null(store.Session);
        Assert.Equal(1, store.DeleteCount);
        Assert.Equal("登录已过期，请重新登录。", state.StatusMessage);
    }

    [Fact]
    public async Task 后台验证不阻塞启动且关闭会取消并等待请求()
    {
        var store = CreateStore("cached-user");
        var api = new ScriptedSessionApi();
        var pending = api.EnqueuePendingResult(observeCancellation: true);
        var state = CreateState(store, api);

        await state.RestoreSavedSessionAsync();
        state.StartBackgroundValidation();
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var stopTask = state.StopAsync();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(pending.CancellationObserved.Task.IsCompletedSuccessfully);
        Assert.True(state.IsLoggedIn);
    }

    [Fact]
    public async Task 旧验证结果不会覆盖退出后重新登录的新会话()
    {
        var store = CreateStore("old-user");
        var api = new ScriptedSessionApi();
        var oldValidation = api.EnqueuePendingResult(observeCancellation: false);
        api.EnqueueResult(new LoginValidationResult(
            LoginValidationStatus.Valid,
            "new-user",
            "new-avatar"));
        var state = CreateState(store, api);

        await state.RestoreSavedSessionAsync();
        var oldValidationTask = state.CheckLoginValidAsync();
        await oldValidation.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await state.LogoutAsync();
        var loginResult = await state.LoginAsync(
        [
            ("SESSDATA", "new-session"),
            ("bili_jct", "new-csrf"),
        ]);
        oldValidation.Complete(new LoginValidationResult(
            LoginValidationStatus.Valid,
            "stale-user",
            "stale-avatar"));
        await oldValidationTask;

        Assert.True(loginResult);
        Assert.True(state.IsLoggedIn);
        Assert.Equal("new-user", state.UserName);
        Assert.Equal("new-avatar", state.UserAvatar);
        Assert.Equal("new-user", store.Session?.UserName);
        Assert.Contains("SESSDATA=new-session", new BiliCredentialProvider(state).GetCookieHeader());
    }

    private static InMemoryBiliCredentialStore CreateStore(string? userName)
        => new(new BiliCredentialSession(
        [
            new("SESSDATA", "stored-session"),
            new("bili_jct", "stored-csrf"),
        ], userName, userName is null ? null : "cached-avatar"));

    private static BiliLoginStateService CreateState(
        IBiliCredentialStore store,
        IBiliSessionApi api)
        => new(store, api, new IsolatedMessengerService());

    private sealed class ScriptedSessionApi : IBiliSessionApi
    {
        private readonly Queue<Func<CancellationToken, Task<LoginValidationResult>>> _checks = [];

        public int ValidationCount { get; private set; }

        public void EnqueueResult(LoginValidationResult result)
            => _checks.Enqueue(_ => Task.FromResult(result));

        public PendingValidation EnqueuePendingResult(bool observeCancellation)
        {
            var pending = new PendingValidation(observeCancellation);
            _checks.Enqueue(pending.RunAsync);
            return pending;
        }

        public Task<LoginValidationResult> CheckLoginAsync(
            string cookieHeader,
            CancellationToken cancellationToken = default)
        {
            ValidationCount++;
            return _checks.Dequeue()(cancellationToken);
        }

        public Task<bool> ExitLoginAsync(
            string cookieHeader,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class PendingValidation
    {
        private readonly bool _observeCancellation;
        private readonly TaskCompletionSource<LoginValidationResult> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingValidation(bool observeCancellation)
        {
            _observeCancellation = observeCancellation;
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(LoginValidationResult result) => _result.TrySetResult(result);

        public async Task<LoginValidationResult> RunAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (!_observeCancellation)
            {
                return await _result.Task;
            }

            try
            {
                return await _result.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }
    }
}
