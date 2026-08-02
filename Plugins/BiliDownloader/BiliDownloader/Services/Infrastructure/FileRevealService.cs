using System.Diagnostics;

namespace BiliDownloader.Services.Infrastructure;

public interface IFileRevealService
{
    Task RevealAsync(string path);
}

public sealed class FileRevealService : IFileRevealService
{
    public Task RevealAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("文件路径为空。", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath)! : fullPath;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            startInfo = File.Exists(fullPath)
                ? new ProcessStartInfo("explorer.exe") { ArgumentList = { $"/select,{fullPath}" }, UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe") { ArgumentList = { directory }, UseShellExecute = true };
        }
        else
        {
            startInfo = new ProcessStartInfo("xdg-open") { ArgumentList = { directory }, UseShellExecute = false };
        }
        Process.Start(startInfo);
        return Task.CompletedTask;
    }
}
