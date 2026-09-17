using System.Security.Cryptography;
using System.Text.Json;

namespace MyAvaloniaManagement.Gate;

/// <summary>
/// 验证宿主实际运行文件与固定补丁一致。以仓库基线为信任起点，不能仅把本机缓存收据
/// 和本机 DLL 互相比对；否则两者一起残留旧版本时可能误报成功。
/// </summary>
internal static class AvaloniaLayoutPatchIdentity
{
    internal static void Verify(string repositoryRoot, IEnumerable<string> assemblies)
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot,
            "patches", "avalonia-cross-window-layout", "baseline.json")));
        var expected = baseline.RootElement.GetProperty("dllSha256").GetString();
        if (expected is null || expected.Length != 64)
            throw new GateFailureException("Avalonia 布局补丁基线缺少固定 DLL 摘要。");
        var runtime = Path.Combine(repositoryRoot, "artifacts", "avalonia-cross-window-layout", "runtime");
        var receiptPath = Path.Combine(runtime, "receipt.json");
        if (!File.Exists(receiptPath)) throw new GateFailureException("Avalonia 布局补丁缺少测试收据。");
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var facts = receipt.RootElement;
        if (facts.GetProperty("identity").GetString() != baseline.RootElement.GetProperty("identity").GetString() ||
            !string.Equals(facts.GetProperty("dllSha256").GetString(), expected, StringComparison.OrdinalIgnoreCase) ||
            facts.GetProperty("passed").GetInt32() < baseline.RootElement.GetProperty("minimumTests").GetInt32() ||
            facts.GetProperty("failed").GetInt32() != 0 || facts.GetProperty("skipped").GetInt32() != 0)
            throw new GateFailureException("Avalonia 布局补丁缓存身份或测试结果无效。");
        foreach (var path in assemblies.Prepend(Path.Combine(runtime, "Avalonia.Base.dll")))
        {
            if (!File.Exists(path)) throw new GateFailureException($"缺少布局补丁运行时：{path}");
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase))
                throw new GateFailureException($"实际 Avalonia.Base.dll 与固定布局补丁不一致：{path}");
        }
    }
}
