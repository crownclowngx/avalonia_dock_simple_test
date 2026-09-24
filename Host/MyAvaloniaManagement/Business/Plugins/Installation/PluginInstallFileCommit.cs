using System;
using System.IO;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>仅抽象不可逆文件提交，便于逐步注入真实 I/O 故障；不建立通用虚拟文件系统。</summary>
internal interface IPluginInstallFileCommit
{
    void WriteAtomic(string path, byte[] bytes);
    void MoveDirectory(string source, string destination);
}

internal sealed class PluginInstallFileCommit : IPluginInstallFileCommit
{
    public void WriteAtomic(string path, byte[] bytes)
    {
        PluginInstallPaths.AssertNoLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            PluginInstallPaths.AssertNoLinks(path);
            if (File.Exists(path))
            {
                PluginInstallPaths.AssertNoLinks(path + ".previous");
                File.Replace(temporary, path, path + ".previous");
            }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void MoveDirectory(string source, string destination)
    {
        PluginInstallPaths.AssertNoLinks(source); PluginInstallPaths.AssertNoLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
    }
}
