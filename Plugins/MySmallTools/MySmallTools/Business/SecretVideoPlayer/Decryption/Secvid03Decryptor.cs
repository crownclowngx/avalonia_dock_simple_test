using System.Security.Cryptography;
using MySmallTools.Business.SecretVideoPlayer.Container;

namespace MySmallTools.Business.SecretVideoPlayer.Decryption;

/// <summary>
/// 以常量内存顺序认证并导出一个 SECVID03 容器。
/// </summary>
public sealed class Secvid03Decryptor : ISecvid03Decryptor
{
    public async Task DecryptAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<VideoDecryptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var fullInputPath = Path.GetFullPath(inputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (fullInputPath.Equals(fullOutputPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("输入文件和输出文件不能相同。", nameof(outputPath));
        if (File.Exists(fullOutputPath))
            throw new VideoDecryptionException(VideoDecryptionFailureCode.OutputConflict, "输出文件已存在。");

        var temporaryPath = fullOutputPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var input = OpenInput(fullInputPath);
            var headerBytes = new byte[Secvid03Format.FixedHeaderSize];
            await ReadExactlyAsync(input, headerBytes, cancellationToken).ConfigureAwait(false);

            Secvid03Header header;
            try
            {
                header = Secvid03Format.ParseHeader(headerBytes, input.Length);
            }
            catch (InvalidDataException ex)
            {
                throw new VideoDecryptionException(
                    VideoDecryptionFailureCode.InvalidContainer,
                    "文件不是有效的 SECVID03。",
                    ex);
            }

            var originalHeader = new byte[header.OriginalHeaderLength];
            input.Position = Secvid03Format.OriginalHeaderOffset;
            await ReadExactlyAsync(input, originalHeader, cancellationToken).ConfigureAwait(false);

            Secvid03AuthenticationContext authentication;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                authentication = await Task.Run(
                        () => Secvid03Cryptography.Authenticate(password, header, originalHeader),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Secvid03AuthenticationException ex)
            {
                throw new VideoDecryptionException(
                    VideoDecryptionFailureCode.AuthenticationFailed,
                    "密码错误或固定头已损坏。",
                    ex);
            }

            using (authentication)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputDirectory = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                await using var output = OpenTemporaryOutput(temporaryPath);
                await output.WriteAsync(originalHeader, cancellationToken).ConfigureAwait(false);
                progress?.Report(new VideoDecryptionProgress(
                    header.OriginalHeaderLength,
                    header.OriginalFileLength,
                    CalculatePercentage(header.OriginalHeaderLength, header.OriginalFileLength),
                    "正在解密..."));

                input.Position = header.EncryptedDataOffset;
                var cipher = new byte[Secvid03Format.ChunkSize];
                var plain = new byte[Secvid03Format.ChunkSize];
                var tag = new byte[Secvid03Format.TagSize];
                try
                {
                    long processedBody = 0;
                    long chunkIndex = 0;
                    while (processedBody < header.PlainBodyLength)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var length = (int)Math.Min(header.ChunkSize, header.PlainBodyLength - processedBody);
                        await ReadExactlyAsync(input, cipher.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                        await ReadExactlyAsync(input, tag, cancellationToken).ConfigureAwait(false);
                        try
                        {
                            Secvid03Cryptography.DecryptChunk(
                                authentication,
                                chunkIndex,
                                cipher.AsSpan(0, length),
                                tag,
                                plain.AsSpan(0, length));
                        }
                        catch (Secvid03ContentAuthenticationException ex)
                        {
                            throw new VideoDecryptionException(
                                VideoDecryptionFailureCode.CorruptedContent,
                                "视频内容已损坏，无法完成认证。",
                                ex);
                        }

                        await output.WriteAsync(plain.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                        CryptographicOperations.ZeroMemory(plain.AsSpan(0, length));
                        processedBody += length;
                        chunkIndex++;

                        var processed = header.OriginalHeaderLength + processedBody;
                        var percentage = CalculatePercentage(processed, header.OriginalFileLength);
                        progress?.Report(new VideoDecryptionProgress(
                            processed,
                            header.OriginalFileLength,
                            percentage,
                            $"正在解密... {percentage:F1}%"));
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(cipher);
                    CryptographicOperations.ZeroMemory(plain);
                    CryptographicOperations.ZeroMemory(tag);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                File.Move(temporaryPath, fullOutputPath, overwrite: false);
            }
            catch (IOException ex) when (File.Exists(fullOutputPath))
            {
                throw new VideoDecryptionException(
                    VideoDecryptionFailureCode.OutputConflict,
                    "输出文件已被其他程序创建。",
                    ex);
            }

            progress?.Report(new VideoDecryptionProgress(
                new FileInfo(fullOutputPath).Length,
                new FileInfo(fullOutputPath).Length,
                100,
                "解密完成"));
        }
        catch (VideoDecryptionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new VideoDecryptionException(
                File.Exists(fullInputPath)
                    ? VideoDecryptionFailureCode.OutputUnavailable
                    : VideoDecryptionFailureCode.InputUnavailable,
                "没有读取输入文件或写入输出目录的权限。",
                ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.InputUnavailable,
                "输入文件不存在或已被删除。",
                ex);
        }
        catch (EndOfStreamException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.CorruptedContent,
                "加密视频已被截断。",
                ex);
        }
        catch (IOException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.OutputUnavailable,
                "读取输入文件或写入输出文件失败。",
                ex);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch { /* 不掩盖原始错误；下次启动可识别 partial 文件。 */ }
            }
        }
    }

    private static FileStream OpenInput(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                Secvid03Format.ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.InputUnavailable,
                "输入文件不存在或已被删除。",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.InputUnavailable,
                "没有读取输入文件的权限。",
                ex);
        }
        catch (IOException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.InputUnavailable,
                "输入文件被占用或无法读取。",
                ex);
        }
    }

    private static FileStream OpenTemporaryOutput(string path)
    {
        try
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                Secvid03Format.ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.OutputUnavailable,
                "没有写入输出目录的权限。",
                ex);
        }
        catch (IOException ex)
        {
            throw new VideoDecryptionException(
                VideoDecryptionFailureCode.OutputUnavailable,
                "无法创建输出文件。",
                ex);
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var current = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (current == 0)
                throw new EndOfStreamException("加密视频已被截断。");
            read += current;
        }
    }

    private static double CalculatePercentage(long processed, long total) =>
        total == 0 ? 100 : Math.Clamp(processed * 100d / total, 0, 100);
}
