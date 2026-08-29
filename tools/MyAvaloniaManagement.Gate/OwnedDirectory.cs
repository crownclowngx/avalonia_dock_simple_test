namespace MyAvaloniaManagement.Gate;

internal sealed class OwnedDirectory
{
    private const string MarkerName = ".myavalonia-gate-owned";
    private readonly string allowedParent;

    private OwnedDirectory(string path, string allowedParent)
    {
        Path = path;
        this.allowedParent = allowedParent;
    }

    public string Path { get; }

    public static OwnedDirectory Create(string allowedParent, string name)
    {
        var parent = System.IO.Path.GetFullPath(allowedParent);
        Directory.CreateDirectory(parent);
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(parent, name));
        AssertChild(path, parent);
        if (Directory.Exists(path))
        {
            throw new GateFailureException($"Gate 自有目录已存在：{path}。");
        }

        Directory.CreateDirectory(path);
        File.WriteAllText(System.IO.Path.Combine(path, MarkerName), "MyAvaloniaManagement.Gate\n");
        return new(path, parent);
    }

    public void Delete()
    {
        AssertChild(Path, allowedParent);
        if (!File.Exists(System.IO.Path.Combine(Path, MarkerName)))
        {
            throw new GateFailureException($"拒绝删除没有 Gate 所有权标记的目录：{Path}。");
        }

        Directory.Delete(Path, recursive: true);
    }

    internal static void AssertChild(string candidate, string parent)
    {
        var fullCandidate = System.IO.Path.GetFullPath(candidate);
        var prefix = System.IO.Path.GetFullPath(parent)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) +
            System.IO.Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new GateFailureException($"路径越界：{fullCandidate} 不在 {prefix} 内。");
        }
    }
}
