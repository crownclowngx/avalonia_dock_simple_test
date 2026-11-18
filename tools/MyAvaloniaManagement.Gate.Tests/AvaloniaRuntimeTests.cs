using System.Text.Json;

namespace MyAvaloniaManagement.Gate.Tests;

public sealed class AvaloniaRuntimeTests
{
    [Theory]
    [InlineData("matching")]
    [InlineData("missing-output")]
    [InlineData("stale-output")]
    [InlineData("missing-package")]
    public void 输出必须使用锁定还原的Avalonia运行时(string mode)
    {
        using var temporary = new TemporaryDirectory();
        var obj = Directory.CreateDirectory(Path.Combine(temporary.Path, "Host", "MyAvaloniaManagement", "obj"));
        var packages = Path.Combine(temporary.Path, "packages");
        var library = Directory.CreateDirectory(Path.Combine(packages, "avalonia", "12.1.6", "lib", "net8.0"));
        File.WriteAllText(Path.Combine(obj.FullName, "project.assets.json"), $$"""
            {
              "targets": {
                "net10.0": {
                  "Avalonia/12.1.6": { "runtime": { "lib/net8.0/Avalonia.Base.dll": {} } }
                }
              },
              "libraries": { "Avalonia/12.1.6": { "path": "avalonia/12.1.6" } },
              "packageFolders": { {{JsonSerializer.Serialize(packages)}}: {} }
            }
            """);
        byte[] official = [1, 2, 3];
        if (mode != "missing-package")
            File.WriteAllBytes(Path.Combine(library.FullName, "Avalonia.Base.dll"), official);
        var output = Path.Combine(temporary.Path, "Avalonia.Base.dll");
        if (mode != "missing-output")
            File.WriteAllBytes(output, mode == "stale-output" ? [3, 2, 1] : official);

        if (mode == "matching") AvaloniaRuntimeIdentity.Verify(temporary.Path, [output]);
        else Assert.Throws<GateFailureException>(() => AvaloniaRuntimeIdentity.Verify(temporary.Path, [output]));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("MAVG-avalonia-tests-");
        internal string Path => directory.FullName;
        public void Dispose() => directory.Delete(recursive: true);
    }
}
