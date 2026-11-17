using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed record NoteAttack(long Id, MusicNote Note);
public sealed record NoteInputStatistics(long NoteOns, long NoteOffs, long Conflicts, int HeldKeys);

public interface INoteKeyboardPlayer : IKeyboardPlayer
{
    PianoNote? ResolveNote(int midi);
    void NoteOn(IReadOnlyList<NoteAttack> notes, PlaybackOptions options, CancellationToken token);
    void NoteOff(IReadOnlyList<long> ids, PlaybackOptions options);
    NoteInputStatistics InputStatistics { get; }
    IReadOnlyList<int> ActiveMidiNotes => [];
    void ResetStatistics();
}

public sealed record ScheduledNote(long Id, MusicNote Note, double StartTimeMs, double EndTimeMs, int EventIndex)
{
    public double DurationMs => EndTimeMs - StartTimeMs;
    public ushort PhysicalKey { get; init; }
    public bool RequiresShift { get; init; }
}
public sealed record NoteTransition(double TimeMs, bool IsOn, ScheduledNote Note);

/// <summary>One immutable O(n log n) timeline. Releases are never subject to density/late-event dropping.</summary>
public sealed class NoteSchedule
{
    public required IReadOnlyList<ScheduledNote> Notes { get; init; }
    public required IReadOnlyList<NoteTransition> Transitions { get; init; }
    public int LimitedNotes { get; init; }
    public int EstimatedNotes { get; init; }
    public int PhysicalConflicts { get; init; }
    public int SuppressedAttacks { get; init; }
    public double DurationMs { get; init; }

    public static NoteSchedule Build(MusicSequence sequence, PlaybackOptions options, Func<int, PianoNote?> resolve, CancellationToken token = default)
    {
        var settings = (options.NoteDuration ?? new()).Validated();
        int limited = 0, estimated = 0, conflicts = 0;
        var builders = new List<Builder>();
        long id = 0;
        for (int eventIndex = 0; eventIndex < sequence.Events.Count; eventIndex++)
        {
            token.ThrowIfCancellationRequested();
            var e = sequence.Events[eventIndex];
            double offset = 0;
            PianoNote? previous = null;
            foreach (var note in e.GetNotes())
            {
                var key = resolve(note.MidiNote) ?? throw new InvalidDataException("Nota fora do perfil de teclado.");
                if (previous != null)
                {
                    if (options.ChordMode == ChordMode.Spread)
                        offset += Math.Max(1, options.SpreadMs);
                    else if (previous.RequiresShift != key.RequiresShift || previous.VirtualKey == key.VirtualKey ||
                        (key.RequiresShift && options.ShiftStrategy == ShiftStrategy.Sequential))
                        offset += Math.Max(settings.MinimumHoldMs, options.SpreadMs) + settings.ReleaseGapMs;
                }
                double hold = note.DurationMs ?? settings.EstimatedHoldMs;
                if (note.DurationMs == null) estimated++;
                if (settings.Mode == NoteDurationMode.Hybrid)
                {
                    hold = Math.Max(settings.MinimumHoldMs, hold);
                    if (settings.MaximumHoldMs > 0 && hold > settings.MaximumHoldMs)
                    { hold = settings.MaximumHoldMs; limited++; }
                }
                if (!double.IsFinite(hold) || hold < 0 || !double.IsFinite(e.TimeMs) || e.TimeMs < 0)
                    throw new InvalidDataException("Duração ou instante de nota inválido.");
                double start = e.TimeMs + offset;
                builders.Add(new(++id, note, start, start + Math.Max(.1, hold), eventIndex, key));
                previous = key;
            }
        }

        builders = builders.OrderBy(n => n.Start).ThenBy(n => n.Id).ToList();
        var active = new List<Builder>(); // Bounded by the physical keyboard, never one timer/task per note.
        foreach (var current in builders)
        {
            token.ThrowIfCancellationRequested();
            active.RemoveAll(n => n.End <= current.Start - settings.ReleaseGapMs);
            var same = active.LastOrDefault(n => !n.Removed && n.Note.MidiNote == current.Note.MidiNote && n.End > current.Start);
            if (same != null && !settings.RetriggerSameNote)
            {
                same.End = Math.Max(same.End, current.End); current.Removed = true; continue;
            }
            foreach (var prior in active)
            {
                bool physical = prior.Key.VirtualKey == current.Key.VirtualKey || prior.Key.RequiresShift != current.Key.RequiresShift;
                bool noOverlap = !settings.NoteOverlap && prior.Start < current.Start;
                bool sequential = options.ShiftStrategy == ShiftStrategy.Sequential && current.Key.RequiresShift && prior.Start < current.Start;
                if (!physical && !noOverlap && !sequential) continue;
                if (prior.End > current.Start - settings.ReleaseGapMs)
                {
                    if (physical && prior.Note.MidiNote != current.Note.MidiNote) conflicts++;
                    prior.End = Math.Max(prior.Start, current.Start - settings.ReleaseGapMs);
                }
            }
            active.RemoveAll(n => n.End <= current.Start);
            active.Add(current);
        }
        var notes = builders.Where(n => !n.Removed && n.End > n.Start)
            .Select(n => new ScheduledNote(n.Id, n.Note, n.Start, n.End, n.EventIndex)
            { PhysicalKey = n.Key.VirtualKey, RequiresShift = n.Key.RequiresShift }).ToArray();
        var transitions = notes.SelectMany(n => new[] { new NoteTransition(n.StartTimeMs, true, n), new NoteTransition(n.EndTimeMs, false, n) })
            .OrderBy(t => t.TimeMs).ThenBy(t => t.IsOn).ThenBy(t => t.Note.Id).ToArray();
        return new() { Notes = notes, Transitions = transitions, LimitedNotes = limited, EstimatedNotes = estimated,
            PhysicalConflicts = conflicts, SuppressedAttacks = builders.Count(n => !n.Removed && n.End <= n.Start),
            DurationMs = Math.Max(sequence.DurationMs, notes.Select(n => n.EndTimeMs).DefaultIfEmpty(0).Max()) };
    }

    public int FindTransition(double positionMs)
    {
        int low = 0, high = Transitions.Count;
        while (low < high)
        { int middle = (low + high) / 2; if (Transitions[middle].TimeMs < positionMs) low = middle + 1; else high = middle; }
        return low;
    }

    private sealed class Builder(long id, MusicNote note, double start, double end, int eventIndex, PianoNote key)
    {
        public long Id { get; } = id;
        public MusicNote Note { get; } = note;
        public double Start { get; } = start;
        public double End { get; set; } = end;
        public int EventIndex { get; } = eventIndex;
        public PianoNote Key { get; } = key;
        public bool Removed { get; set; }
    }
}
