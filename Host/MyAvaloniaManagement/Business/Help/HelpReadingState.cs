using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyAvaloniaManagement.Business.Storage;

namespace MyAvaloniaManagement.Business.Help;

internal sealed record HelpPagePosition
{
    public double ScrollTop { get; set; }
    public string Anchor { get; set; } = string.Empty;
    public bool FullTextExpanded { get; set; }
}

internal sealed record HelpReadingState
{
    public int SchemaVersion { get; init; } = 1;
    public string ArticleId { get; set; } = "product";
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 800;
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Maximized { get; set; }
    public int FontSize { get; set; } = 17;
    public bool ReduceMotion { get; set; }
    public Dictionary<string, HelpPagePosition> Pages { get; set; } = new(StringComparer.Ordinal);

    internal HelpPagePosition PositionFor(string articleId)
    {
        if (!Pages.TryGetValue(articleId, out var position)) Pages[articleId] = position = new();
        return position;
    }
}

internal sealed class HelpReadingStateStore
{
    internal string FilePath { get; }
    public HelpReadingStateStore() : this(Path.Combine(HostDataRootPolicy.ResolveDefault(), "help-v1.json")) { }
    internal HelpReadingStateStore(string filePath) => FilePath = filePath;

    internal HelpReadingState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            using var stream = File.OpenRead(FilePath);
            if (stream.Length > 256 * 1024) return new();
            var state = JsonSerializer.Deserialize<HelpReadingState>(stream);
            if (state is null || state.SchemaVersion != 1) return new();
            state.Width = double.IsFinite(state.Width) ? Math.Clamp(state.Width, 640, 3840) : 1180;
            state.Height = double.IsFinite(state.Height) ? Math.Clamp(state.Height, 480, 2160) : 800;
            state.FontSize = Math.Clamp(state.FontSize, 14, 26);
            state.ArticleId ??= "product";
            state.Pages = (state.Pages ?? []).Where(pair => pair.Value is not null)
                .Take(300).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var position in state.Pages.Values)
            {
                position.ScrollTop = double.IsFinite(position.ScrollTop) ? Math.Max(0, position.ScrollTop) : 0;
                position.Anchor ??= string.Empty;
            }
            return state;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return new(); }
    }

    internal bool Save(HelpReadingState state)
    {
        try { AtomicFileTransaction.Write(FilePath, stream => JsonSerializer.Serialize(stream, state)); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine("HelpSettings errorCode=HELP_STATE_WRITE_FAILED"); return false; }
    }
}
