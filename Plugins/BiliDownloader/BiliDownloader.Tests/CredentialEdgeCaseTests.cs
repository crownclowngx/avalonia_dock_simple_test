using System.Security.Cryptography;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Tests;

public sealed class CredentialEdgeCaseTests
{
    [Fact]
    public void 安装密钥会复用且Reset后重新生成()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var store = new InstallationKeyStore(paths);

        var first = store.GetOrCreateKey();
        var second = store.GetOrCreateKey();
        Assert.Equal(first, second);

        store.Reset();
        var third = store.GetOrCreateKey();
        Assert.NotEqual(Convert.ToBase64String(first), Convert.ToBase64String(third));
    }

    [Theory]
    [InlineData("WRONG\nAAAA\n", "标识无效")]
    [InlineData("BILIKEY1\nnot-base64!\n", "Base64")]
    [InlineData("BILIKEY1\nAQID\n", "32 字节")]
    public void 损坏的安装密钥会被明确拒绝(string contents, string expected)
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.CredentialKeyPath, contents);

        var ex = Assert.Throws<InvalidDataException>(
            () => new InstallationKeyStore(paths).GetOrCreateKey());

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 凭据信封会拒绝错误版本Nonce与Tag长度()
    {
        using var paths = new TestDataPaths();
        var protector = new AesGcmCredentialProtector(new InstallationKeyStore(paths));

        Assert.Throws<CryptographicException>(() =>
            protector.Unprotect(new CredentialEnvelope(2, new byte[12], [], new byte[16])));
        Assert.Throws<CryptographicException>(() =>
            protector.Unprotect(new CredentialEnvelope(1, new byte[11], [], new byte[16])));
        Assert.Throws<CryptographicException>(() =>
            protector.Unprotect(new CredentialEnvelope(1, new byte[12], [], new byte[15])));
    }

    [Fact]
    public async Task 凭据保存会过滤空名称排序并在读取时去重()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new BiliCredentialStore(
            paths,
            new AesGcmCredentialProtector(new InstallationKeyStore(paths)));
        await store.SaveSessionAsync(new BiliCredentialSession(
        [
            new("z_cookie", "z"),
            new("", "ignored"),
            new("SESSDATA", "old"),
            new("SESSDATA", "new"),
            new("a_cookie", "a"),
        ]));

        var loaded = Assert.IsType<BiliCredentialSession>(await store.LoadSessionAsync());

        Assert.Equal(["SESSDATA", "a_cookie", "z_cookie"], loaded.Cookies.Select(x => x.Name));
        Assert.Equal("new", loaded.Cookies.Single(x => x.Name == "SESSDATA").Value);
    }

    [Fact]
    public async Task 删除凭据幂等且空数据库返回Null()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new BiliCredentialStore(
            paths,
            new AesGcmCredentialProtector(new InstallationKeyStore(paths)));

        Assert.Null(await store.LoadSessionAsync());
        await store.DeleteAllAsync();
        await store.DeleteAllAsync();
        Assert.Null(await store.LoadSessionAsync());
    }

    [Fact]
    public async Task 已取消令牌会在数据库边界传播()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new BiliCredentialStore(
            paths,
            new AesGcmCredentialProtector(new InstallationKeyStore(paths)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.InitAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveSessionAsync(new BiliCredentialSession([]), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.LoadSessionAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.DeleteAllAsync(cts.Token));
    }
}
