namespace PianoRoblox.Core.Models;

public sealed record MidiNoteEvent(double TimeMs, int MidiNote, int Velocity, int Channel,
    int TrackIndex, string TrackName, int ProgramNumber, string InstrumentName, double DurationMs, bool IsPercussion);

public sealed record MidiTrackInfo(int TrackIndex, string Name, string InstrumentName, int[] Channels,
    int[] Programs, int NoteCount, int? LowestNote, int? HighestNote, int OutOfRangeCount, int PercussionNoteCount)
{
    public bool IsPercussion => NoteCount > 0 && PercussionNoteCount == NoteCount;
}

public sealed record MidiAnalysis(int NoteCount, int EventCount, int TrackCount, int PercussionNotes,
    int MaxChordNotes, int PeakNotesPer250Ms, int PeakNotesPerSecond, int PeakEventsPerSecond,
    double AverageNotesPerSecond)
{
    public string Complexity => MaxChordNotes <= 4 && PeakNotesPerSecond <= 25 ? "Baixa" :
        MaxChordNotes <= 8 && PeakNotesPerSecond <= 45 ? "Moderada" : "Muito alta";
}

public sealed record MidiImportResult(string Title, double InitialBpm, double DurationMs,
    IReadOnlyList<MidiNoteEvent> Notes, IReadOnlyList<MidiTrackInfo> Tracks, MidiAnalysis Analysis);

public sealed record OptimizationResult(MusicSequence Sequence, IReadOnlyList<MidiNoteEvent> Notes,
    MidiAnalysis Before, MidiAnalysis After, OptimizationPreset EffectivePreset, OptimizationPreset RecommendedPreset,
    int PercussionFound, int PercussionRemoved, int RemovedByTrackSelection, int DuplicatesRemoved,
    int WeakNotesRemoved, int ChordNotesRemoved, int DensityNotesRemoved)
{
    public double ReductionPercent => Before.NoteCount == 0 ? 0 : 100d * (Before.NoteCount - After.NoteCount) / Before.NoteCount;
}

public sealed record MidiBenchmarkRow(OptimizationPreset Preset, int Events, int Notes, int PeakEventsPerSecond,
    int PeakNotesPerSecond, int MaxChordNotes, int EstimatedSendInputCalls);
