namespace BiliDownloader.Models;

public sealed record DeleteTaskOptions(bool DeleteTemporaryFiles, bool DeleteOutputFile)
{
    public static DeleteTaskOptions RecordOnly { get; } = new(false, false);
}

public sealed record DeleteTaskPromptResult(
    bool Confirmed,
    bool DeleteTemporaryFiles,
    bool DeleteOutputFile)
{
    public static DeleteTaskPromptResult Cancelled { get; } = new(false, false, false);
}
