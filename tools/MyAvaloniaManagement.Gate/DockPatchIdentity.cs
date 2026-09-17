using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MyAvaloniaManagement.Gate;

/// <summary>
/// 核对构建实际复制的 Dock DLL。NuGet 已正确还原并不代表 bin 已更新：MSBuild 的快速复制
/// 可能把同大小、同时间戳的旧 DLL 判作未变化，因此此检查必须位于构建后、测试前。
/// </summary>
internal static class DockPatchIdentity
{
    internal static void Verify(string repositoryRoot, IEnumerable<string> assemblies)
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "patches", "dock-area-fill", "baseline.json")));
        var version = baseline.RootElement.GetProperty("packageVersion").GetString();
        var package = Path.Combine(repositoryRoot, "artifacts", "dock-area-fill", "feed", $"Dock.Avalonia.{version}.nupkg");
        using var archive = ZipFile.OpenRead(package);
        var entry = archive.GetEntry("lib/net10.0/Dock.Avalonia.dll")
            ?? throw new GateFailureException("补丁包缺少 net10.0 Dock.Avalonia.dll。");
        using var content = entry.Open();
        var expected = SHA256.HashData(content);
        foreach (var path in assemblies)
        {
            if (!File.Exists(path)) throw new GateFailureException($"构建输出缺少补丁 DLL：{path}");
            using var actual = File.OpenRead(path);
            if (!expected.AsSpan().SequenceEqual(SHA256.HashData(actual)))
                throw new GateFailureException($"构建输出仍为旧 Dock DLL 或与补丁包不一致：{path}。请重建受影响项目后再验证。");
        }
    }
}
