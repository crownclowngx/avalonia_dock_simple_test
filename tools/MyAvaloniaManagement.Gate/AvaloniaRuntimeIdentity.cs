using System.Security.Cryptography;
using System.Text.Json;

namespace MyAvaloniaManagement.Gate;

internal static class AvaloniaRuntimeIdentity
{
    internal static void Verify(string repositoryRoot, IEnumerable<string> assemblies)
    {
        const string assemblyName = "Avalonia.Base.dll";
        var assetsPath = Path.Combine(repositoryRoot, "Host", "MyAvaloniaManagement", "obj", "project.assets.json");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var restored = assets.RootElement;
        var avalonia = restored.GetProperty("targets").GetProperty("net10.0").EnumerateObject()
            .Single(package => package.Name.StartsWith("Avalonia/", StringComparison.OrdinalIgnoreCase));
        var runtimePath = avalonia.Value.GetProperty("runtime").EnumerateObject()
            .Single(asset => Path.GetFileName(asset.Name) == assemblyName).Name;
        var packagePath = restored.GetProperty("libraries").GetProperty(avalonia.Name).GetProperty("path").GetString()!;
        var restoredAssembly = restored.GetProperty("packageFolders").EnumerateObject()
            .Select(folder => Path.Combine(folder.Name, packagePath, runtimePath))
            .FirstOrDefault(File.Exists);
        if (restoredAssembly is null)
            throw new GateFailureException($"缺少已还原的 {avalonia.Name} 运行时资产：{runtimePath}");

        var expected = ComputeHash(restoredAssembly);
        foreach (var path in assemblies)
        {
            if (!File.Exists(path) || !ComputeHash(path).AsSpan().SequenceEqual(expected))
                throw new GateFailureException($"实际 {assemblyName} 与已锁定的 {avalonia.Name} 运行时不一致：{path}");
        }
    }

    private static byte[] ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
