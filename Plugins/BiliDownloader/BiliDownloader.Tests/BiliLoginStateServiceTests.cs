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
        var provider = new BiliCredentialProvider(state);
        Assert.True(provider.IsLoggedIn);
        Assert.Contains("SESSDATA=new-session", provider.GetCookieHeader());
    }

    [Fact]
    public async Task 本地凭据读取失败时安全保持未登录并给出可恢复提示()
    {
        var store = new FaultingCredentialStore { ThrowOnLoad = true };
        var state = CreateState(store, new ScriptedSessionApi());

        await state.RestoreSavedSessionAsync();

        Assert.False(state.IsLoggedIn);
        Assert.Equal("无法读取已保存的登录信息，可重新登录并仅在本次会话使用。", state.StatusMessage);
    }

    [Theory]
    [InlineData(LoginValidationStatus.Invalid, "登录凭据无效")]
    [InlineData(LoginValidationStatus.Unavailable, "当前无法验证账号信息")]
    public async Task 主动登录失败使用结构化状态且不保存凭据(
        LoginValidationStatus status,
        string expectedMessage)
    {
        var store = new FaultingCredentialStore();
        var api = new ScriptedSessionApi();
        api.EnqueueResult(new LoginValidationResult(status));
        var state = CreateState(store, api);

        var result = await state.LoginAsync([("SESSDATA", "session")]);

        Assert.False(result);
        Assert.False(state.IsLoggedIn);
        Assert.Contains(expectedMessage, state.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task 登录持久化失败时保留仅本次会话状态()
    {
        var store = new FaultingCredentialStore { ThrowOnSave = true };
        var api = new ScriptedSessionApi();
        api.EnqueueResult(new LoginValidationResult(LoginValidationStatus.Valid, "user", "avatar"));
        var state = CreateState(store, api);

        var result = await state.LoginAsync([("SESSDATA", "session")]);

        Assert.True(result);
        Assert.True(state.IsLoggedIn);
        Assert.False(state.IsPersistentLogin);
        Assert.Equal("登录信息仅在本次会话有效。", state.StatusMessage);
    }

    [Fact]
    public async Task 退出时删除本地凭据失败仍清空内存登录态()
    {
        var store = new FaultingCredentialStore(CreateStore("user").Session)
        {
            ThrowOnDelete = true,
        };
        var state = CreateState(store, new ScriptedSessionApi());
        await state.RestoreSavedSessionAsync();

        await state.LogoutAsync();

        Assert.False(state.IsLoggedIn);
        Assert.Equal(string.Empty, state.CookieHeader);
    }

    [Fact]
    public async Task 后台验证异常按网络不可用处理并保留凭据()
    {
        var store = CreateStore("cached-user");
        var api = new ScriptedSessionApi();
        api.EnqueueException(new InvalidOperationException("network failed"));
        var state = CreateState(store, api);
        await state.RestoreSavedSessionAsync();

        state.StartBackgroundValidation();
        await state.StopAsync();

        Assert.True(state.IsLoggedIn);
        Assert.Equal("暂时无法验证登录状态，已保留本地登录信息。", state.StatusMessage);
    }

    [Fact]
    public async Task 后台验证缓存写入和失效删除失败均不破坏内存事实()
    {
        var validStore = new FaultingCredentialStore(CreateStore("old").Session) { ThrowOnSave = true };
        var validApi = new ScriptedSessionApi();
        validApi.EnqueueResult(new LoginValidationResult(LoginValidationStatus.Valid, "new", "avatar"));
        var validState = CreateState(validStore, validApi);
        await validState.RestoreSavedSessionAsync();
        validState.StartBackgroundValidation();
        await validState.StopAsync();

        Assert.True(validState.IsLoggedIn);
        Assert.Equal("new", validState.UserName);
        Assert.Equal("账号信息已验证，但更新本地缓存失败。", validState.StatusMessage);

        var invalidStore = new FaultingCredentialStore(CreateStore("expired").Session) { ThrowOnDelete = true };
        var invalidApi = new ScriptedSessionApi();
        invalidApi.EnqueueResult(new LoginValidationResult(LoginValidationStatus.Invalid));
        var invalidState = CreateState(invalidStore, invalidApi);
        await invalidState.RestoreSavedSessionAsync();
        invalidState.StartBackgroundValidation();
        await invalidState.StopAsync();

        Assert.False(invalidState.IsLoggedIn);
        Assert.Equal("登录已过期，请重新登录。", invalidState.StatusMessage);
    }

    [Fact]
    public async Task 登录消息发送失败不改变登录判断结果()
    {
        var api = new ScriptedSessionApi();
        api.EnqueueResult(new LoginValidationResult(LoginValidationStatus.Invalid));
        var messenger = new RecordingHostEventBus { ThrowOnPublish = true };
        var state = new BiliLoginStateService(new FaultingCredentialStore(), api, messenger);

        var result = await state.LoginAsync([("SESSDATA", "invalid")]);

        Assert.False(result);
        Assert.False(state.IsLoggedIn);
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
        => new(store, api, new IsolatedHostEventBus());

    private sealed class ScriptedSessionApi : IBiliSessionApi
    {
        private readonly Queue<Func<CancellationToken, Task<LoginValidationResult>>> _checks = [];

        public int ValidationCount { get; private set; }

        public void EnqueueResult(LoginValidationResult result)
            => _checks.Enqueue(_ => Task.FromResult(result));

        public void EnqueueException(Exception exception)
            => _checks.Enqueue(_ => Task.FromException<LoginValidationResult>(exception));

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

    private sealed class FaultingCredentialStore : IBiliCredentialStore
    {
        public FaultingCredentialStore(BiliCredentialSession? session = null)
        {
            Session = session;
        }

        public BiliCredentialSession? Session { get; private set; }
        public bool ThrowOnLoad { get; init; }
        public bool ThrowOnSave { get; init; }
        public bool ThrowOnDelete { get; init; }
        public int SaveCount { get; private set; }

        public Task InitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task SaveSessionAsync(
            BiliCredentialSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnSave) throw new IOException("save failed");
            Session = session;
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<BiliCredentialSession?> LoadSessionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnLoad) throw new IOException("load failed");
            return Task.FromResult(Session);
        }

        public Task DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnDelete) throw new IOException("delete failed");
            Session = null;
            return Task.CompletedTask;
        }
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
