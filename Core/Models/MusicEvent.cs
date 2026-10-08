namespace PianoRoblox.Core.Models;

public sealed record MusicEvent(double TimeMs, int[] Notes)
{
    // Optional metadata leaves V1 sequences and sheets fully compatible.
    public double Priority { get; init; } = .5;
    public int[] SourceTracks { get; init; } = [];
}

public sealed record MusicSequence(string Title, double InitialBpm, double DurationMs, List<MusicEvent> Events)
{
    public int NoteCount => Events.Sum(e => e.Notes.Length);
    public int ChordCount => Events.Count(e => e.Notes.Length > 1);
    public int? LowestNote => Events.SelectMany(e => e.Notes).Cast<int?>().Min();
    public int? HighestNote => Events.SelectMany(e => e.Notes).Cast<int?>().Max();
    public int OutOfRangeCount => Events.Sum(e => e.Notes.Count(n => n < 36 || n > 96));
}

