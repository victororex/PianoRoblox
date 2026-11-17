namespace PianoRoblox.Core.Models;

// Null duration means the source does not provide a Note Off (sheets / V2 JSON).
public sealed record MusicNote(int MidiNote, double? DurationMs = null, int Velocity = 100,
    int TrackIndex = -1, double Priority = .5);

public sealed record MusicEvent(double TimeMs, int[] Notes)
{
    // Optional metadata leaves V1 sequences and sheets fully compatible.
    public double Priority { get; init; } = .5;
    public int[] SourceTracks { get; init; } = [];
    public MusicNote[] NoteDetails { get; init; } = [];
    public IEnumerable<MusicNote> GetNotes() => Notes.Select(pitch =>
        NoteDetails.FirstOrDefault(note => note.MidiNote == pitch) ?? new MusicNote(pitch, Priority: Priority));
}

public sealed record MusicSequence(string Title, double InitialBpm, double DurationMs, List<MusicEvent> Events)
{
    public int NoteCount => Events.Sum(e => e.Notes.Length);
    public int ChordCount => Events.Count(e => e.Notes.Length > 1);
    public int? LowestNote => Events.SelectMany(e => e.Notes).Cast<int?>().Min();
    public int? HighestNote => Events.SelectMany(e => e.Notes).Cast<int?>().Max();
    public int OutOfRangeCount => Events.Sum(e => e.Notes.Count(n => n < 36 || n > 96));
    public bool HasExactDurations => Events.Any(e => e.NoteDetails.Any(n => n.DurationMs.HasValue));
}

