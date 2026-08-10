using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Storage;

/// <summary>
/// 在目标文件同目录写入完整临时文件，再通过替换或移动一次性提交。
/// 写入失败时保留旧文件并清理临时文件，使文档和布局共享同一种事务语义。
/// </summary>
internal static class AtomicFileTransaction
{
    internal static void Write(
        string destinationPath,
        Action<Stream> writeContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeContent);
        var fullPath = Path.GetFullPath(destinationPath);
        var temporaryPath = CreateTemporaryPath(fullPath);

        try
        {
            using (var stream = CreateWriteStream(temporaryPath))
            {
                writeContent(stream);
                stream.Flush(flushToDisk: true);
            }

            Commit(temporaryPath, fullPath);
            temporaryPath = string.Empty;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static async Task WriteAllTextAsync(
        string destinationPath,
        string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = Path.GetFullPath(destinationPath);
        var temporaryPath = CreateTemporaryPath(fullPath);

        try
        {
            await using (var stream = CreateWriteStream(temporaryPath))
            await using (var writer = new StreamWriter(
                             stream,
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(content);
                await writer.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            Commit(temporaryPath, fullPath);
            temporaryPath = string.Empty;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static string CreateTemporaryPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath)
                        ?? throw new InvalidOperationException(
                            "Destination file has no parent directory.");
        Directory.CreateDirectory(directory);
        return Path.Combine(
            directory,
            $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static FileStream CreateWriteStream(string temporaryPath) =>
        new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.WriteThrough | FileOptions.Asynchronous);

    private static void Commit(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
        }
        else
        {
            File.Move(temporaryPath, destinationPath);
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
