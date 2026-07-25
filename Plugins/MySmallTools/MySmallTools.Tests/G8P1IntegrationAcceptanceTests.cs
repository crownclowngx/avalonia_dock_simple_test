using System.Security.Cryptography;
using System.Text.Json;
using MySmallTools.Business.SecretVideoPlayer.Decryption;
using MySmallTools.Business.SecretVideoPlayer.Encryption;
using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.Business.SecretVideoPlayer.Operations;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.ViewModels.SecretVideoPlayer;
using Xunit;

namespace MySmallTools.Tests;

/// <summary>
/// G8 P1 的确定性规模、事务和脱敏门禁。
/// </summary>
/// <remarks>
/// 文件闭环使用真实 SECVID03；取消和失败时序继续由 G2/G5 的可控替身覆盖。这样既能证明
/// 百文件真实正确性，也不把依赖机器速度的竞态写入普通测试运行器。
/// </remarks>
[Collection(Secvid03Collection.Name)]
public sealed class G8P1IntegrationAcceptanceTests(Secvid03Fixture fixture)
{
    [Fact]
    public async Task 百文件加密解密闭环保持顺序哈希和无半成品()
    {
        var root = CreateDirectory("g8-roundtrip");
        var encryptedDirectory = Directory.CreateDirectory(
            Path.Combine(root, "encrypted")).FullName;
        var decryptedDirectory = Directory.CreateDirectory(
            Path.Combine(root, "decrypted")).FullName;
        try
        {
            var encryption = new VideoEncryptorService(new Secvid03Encryptor());
            var batchEncryption = new VideoBatchEncryptionService(
                encryption,
                new OutputPathConflictResolver());
            using var encryptionRunner =
                new SequentialVideoQueueRunner<PreparedEncryptionItem>();
            var requests = Enumerable.Range(0, 100)
                .Select(index => new BatchEncryptionItemRequest(
                    Guid.NewGuid(),
                    fixture.OriginalPath,
                    Path.Combine(encryptedDirectory, $"{index:D3}.secvid"),
                    $"G8 {index:D3}",
                    string.Empty))
                .ToArray();
            var encryptionPlan = await batchEncryption.PrepareAsync(
                requests,
                OutputConflictPolicy.Block,
                skippedSucceededCount: 0);

            var maximumActive = 0;
            var active = 0;
            var encryptionResult = await encryptionRunner.RunAsync(
                Guid.NewGuid(),
                encryptionPlan.Items,
                _ => true,
                async (item, progress, cancellationToken) =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);
                    try
                    {
                        await encryption.EncryptAsync(
                            item.Request,
                            Secvid03Fixture.Password,
                            progress,
                            cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                });

            Assert.Equal(100, encryptionResult.SucceededCount);
            Assert.Equal(1, maximumActive);

            var decryption = new VideoDecryptionService(
                new Secvid03Decryptor(),
                new DecryptionOutputPathResolver(),
                new StoragePreflightProbe());
            var encryptedPaths = requests
                .Select(request => request.RequestedOutputPath)
                .ToArray();
            var candidates = await decryption.InspectAsync(encryptedPaths);
            var decryptionResult = await decryption.DecryptBatchAsync(
                candidates,
                decryptedDirectory,
                Secvid03Fixture.Password);

            Assert.Equal(100, decryptionResult.SucceededCount);
            Assert.Equal(0, decryptionResult.FailedCount);
            Assert.Equal(100, decryptionResult.OutputPaths.Count);

            var expectedHash = Convert.ToHexString(
                SHA256.HashData(fixture.OriginalBytes));
            foreach (var path in decryptionResult.OutputPaths)
            {
                await using var output = File.OpenRead(path);
                Assert.Equal(
                    expectedHash,
                    Convert.ToHexString(
                        await SHA256.HashDataAsync(output)));
            }
            Assert.Empty(Directory.GetFiles(root, "*.partial-*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 预检后的竞争文件仍被事务拒绝且安全改名不覆盖哨兵()
    {
        var root = CreateDirectory("g8-conflict");
        try
        {
            var finalPath = Path.Combine(root, "competing.secvid");
            var request = new VideoEncryptionRequest(
                fixture.OriginalPath,
                finalPath,
                string.Empty,
                string.Empty);
            var encryption = new VideoEncryptorService(new Secvid03Encryptor());
            Assert.True((await encryption.PreflightAsync(request)).CanProceed);

            await File.WriteAllTextAsync(finalPath, "G8-SENTINEL");
            var encryptionError = await Assert.ThrowsAsync<VideoTaskException>(() =>
                encryption.EncryptAsync(request, Secvid03Fixture.Password));
            Assert.Equal(VideoTaskFailureCode.OutputConflict, encryptionError.FailureCode);
            Assert.Equal("G8-SENTINEL", await File.ReadAllTextAsync(finalPath));

            var decryption = new VideoDecryptionService(
                new Secvid03Decryptor(),
                new DecryptionOutputPathResolver(),
                new StoragePreflightProbe());
            var candidate = Assert.Single(await decryption.InspectAsync([fixture.EncryptedPath]));
            var strictTarget = Path.Combine(root, candidate.OriginalFileName);
            await File.WriteAllTextAsync(strictTarget, "G8-STRICT-SENTINEL");
            var strict = await decryption.PreflightAsync(
                [new DecryptionQueueRequest(Guid.NewGuid(), candidate)],
                root,
                OutputConflictPolicy.Block);
            Assert.False(Assert.Single(strict.Items).CanRun);

            var renamed = await decryption.PreflightAsync(
                [new DecryptionQueueRequest(Guid.NewGuid(), candidate)],
                root,
                OutputConflictPolicy.GenerateUniqueName);
            var prepared = Assert.Single(renamed.Items);
            Assert.True(prepared.CanRun);
            Assert.NotEqual(strictTarget, prepared.OutputPath);

            await File.WriteAllTextAsync(prepared.OutputPath, "G8-RENAME-SENTINEL");
            var decryptionError = await Assert.ThrowsAsync<VideoTaskException>(() =>
                decryption.DecryptAsync(prepared, Secvid03Fixture.Password));
            Assert.Equal(VideoTaskFailureCode.OutputConflict, decryptionError.FailureCode);
            Assert.Equal(
                "G8-RENAME-SENTINEL",
                await File.ReadAllTextAsync(prepared.OutputPath));
            Assert.Empty(Directory.GetFiles(root, "*.partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 千文件真实递归扫描没有重复且取消会停止枚举()
    {
        var root = CreateDirectory("g8-library");
        try
        {
            for (var index = 0; index < 1000; index++)
            {
                var level = (index % 3) switch
                {
                    0 => "a",
                    1 => "a/b",
                    _ => "a/b/c"
                };
                var directory = Directory.CreateDirectory(Path.Combine(root, level)).FullName;
                File.Copy(
                    fixture.EncryptedPath,
                    Path.Combine(directory, $"{index:D4}.secvid"));
            }

            var scanner = new VideoLibraryScanner();
            var items = new List<VideoLibraryScanResult>();
            await foreach (var item in scanner.ScanAsync(
                               root,
                               new VideoLibraryScanOptions(true),
                               CancellationToken.None))
            {
                items.Add(item);
            }

            Assert.Equal(1000, items.Count);
            Assert.Equal(
                1000,
                items.Select(item => item.FilePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
            Assert.All(items, item => Assert.Equal(VideoLibraryMetadataState.Ready, item.State));

            using var cancellation = new CancellationTokenSource();
            var observed = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in scanner.ScanAsync(
                                   root,
                                   new VideoLibraryScanOptions(true),
                                   cancellation.Token))
                {
                    if (++observed == 10)
                        cancellation.Cancel();
                }
            });
            Assert.InRange(observed, 10, 20);

            var stormDirectory =
                Directory.CreateDirectory(Path.Combine(root, "storm")).FullName;
            using var catalogCancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var updates = new VideoLibraryCatalogSession(scanner)
                .ObserveAsync(
                    root,
                    new VideoLibraryScanOptions(true),
                    catalogCancellation.Token)
                .GetAsyncEnumerator(catalogCancellation.Token);

            // 初扫完成后再集中制造超过阈值的不同路径。目录会话必须放弃逐项猜测，
            // 以 ReplaceAll 完整快照恢复文件系统事实，避免 watcher 丢事件后留下幽灵项。
            while (await updates.MoveNextAsync())
            {
                if (!updates.Current.IsScanning)
                    break;
            }

            for (var index = 0; index < 140; index++)
            {
                File.Copy(
                    fixture.EncryptedPath,
                    Path.Combine(stormDirectory, $"{index:D3}.secvid"));
            }

            VideoLibraryCatalogBatch? stormSnapshot = null;
            while (await updates.MoveNextAsync())
            {
                if (!updates.Current.ReplaceAll)
                    continue;
                stormSnapshot = updates.Current;
                break;
            }

            Assert.NotNull(stormSnapshot);
            Assert.Equal(1140, stormSnapshot.Upserts.Count);
            Assert.Equal(
                1140,
                stormSnapshot.Upserts
                    .Select(item => item.FilePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 持久化模型诊断快照和用户数据均不包含敏感Canary()
    {
        const string password = "G8-PASSWORD-CANARY";
        const string derivedKey = "G8-DERIVED-KEY-CANARY";
        const string plaintext = "G8-PLAINTEXT-CANARY";
        const string description = "G8-PUBLIC-DESCRIPTION-CANARY";
        var root = CreateDirectory("g8-sensitive");
        var userDataPath = Path.Combine(root, "user-data-v1.json");
        try
        {
            using (var store = new SecretVideoUserDataStore(userDataPath))
            {
                store.UpdatePreferences(new PlaybackPreferences(61, 1.25f));
                store.UpdateSettings(VideoLibrarySettings.Default with
                {
                    RecentFolder = root,
                    IncludeSubdirectories = true
                });
                store.Upsert(new VideoPlaybackHistoryEntry(
                    Path.Combine(root, "item.secvid"),
                    "00112233445566778899AABBCCDDEEFF",
                    100,
                    25,
                    100,
                    DateTimeOffset.UnixEpoch,
                    false));
            }

            var persisted = File.ReadAllText(userDataPath);
            var diagnostics = JsonSerializer.Serialize(
                SecurePlaybackDiagnostics.CaptureResources());
            var combined = persisted + diagnostics;
            foreach (var canary in new[] { password, derivedKey, plaintext, description })
                Assert.DoesNotContain(canary, combined, StringComparison.Ordinal);

            var persistentAndDiagnosticTypes = new[]
            {
                typeof(VideoPlaybackHistoryEntry),
                typeof(VideoLibrarySettings),
                typeof(PlaybackPreferences),
                typeof(VideoQueueProgress),
                typeof(VideoQueueRunResult),
                typeof(BatchDecryptionResult),
                typeof(PlaybackResourceSnapshot)
            };
            foreach (var type in persistentAndDiagnosticTypes)
            {
                Assert.DoesNotContain(
                    type.GetProperties(),
                    property => IsSensitiveMember(property.Name));
                Assert.DoesNotContain(
                    type.GetFields(),
                    field => IsSensitiveMember(field.Name));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string CreateDirectory(string prefix)
    {
        var path = Path.Combine(
            fixture.DirectoryPath,
            $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool IsSensitiveMember(string name) =>
        name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("DerivedKey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Plaintext", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("CachedPlaintextChunks", StringComparison.Ordinal) ||
        name.Contains("AuthenticationContext", StringComparison.OrdinalIgnoreCase);

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (candidate <= observed ||
                Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
            {
                return;
            }
        }
    }
}
