using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class MidiOptimizationService
{
    private readonly MidiComplexityAnalyzer analyzer = new();

    public OptimizationResult Optimize(MidiImportResult import, OptimizationSettings settings,
        IReadOnlyCollection<int>? selectedTracks = null, CancellationToken cancellationToken = default)
    {
        settings = settings.Validated();
        var trackSet = selectedTracks?.ToHashSet();
        var selected = import.Notes.Where(n => trackSet == null || trackSet.Contains(n.TrackIndex)).ToArray();
        var eligible = selected.Where(n => !settings.RemovePercussion || !n.IsPercussion)
            .OrderBy(n => n.TimeMs).ThenBy(n => n.TrackIndex).ThenBy(n => n.MidiNote).ToArray();
        // Repeated instrumentation must not make an otherwise simple tune look artificially complex.
        var recommendationNotes = eligible.GroupBy(n => (n.TimeMs, n.MidiNote)).Select(g => g.First());
        var recommended = MidiComplexityAnalyzer.Recommend(analyzer.Analyze(recommendationNotes, import.DurationMs));
        var preset = settings.Preset == OptimizationPreset.Auto ? recommended : settings.Preset;
        var effective = preset == OptimizationPreset.Custom ? settings : OptimizationSettings.ForPreset(preset) with
        { RemovePercussion = settings.RemovePercussion, MelodyPriority = settings.MelodyPriority };
        bool original = preset == OptimizationPreset.Original;
        int mainTrack = eligible.GroupBy(n => n.TrackIndex).OrderByDescending(g =>
                g.Average(n => n.MidiNote) + g.Average(n => n.Velocity) * .15 + Math.Log2(g.Count() + 1))
            .ThenBy(g => g.Key).Select(g => g.Key).DefaultIfEmpty(-1).First();
        var strong = eligible.Where(n => original || n.Velocity >= effective.MinimumVelocity).ToArray();
        int weakRemoved = eligible.Length - strong.Length, duplicates = 0, chordRemoved = 0;
        var candidates = new List<Candidate>();
        int? previousMelody = null;
        for (int start = 0; start < strong.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double anchor = strong[start].TimeMs;
            int end = start + 1;
            // Anchor every group at its first event; never chain small merges into a large rhythmic shift.
            while (end < strong.Length && strong[end].TimeMs - anchor <= effective.MergeWindowMs) end++;
            var group = strong[start..end].GroupBy(n => n.MidiNote).Select(g =>
            {
                var best = g.OrderByDescending(n => n.Velocity).ThenByDescending(n => n.DurationMs).ThenBy(n => n.TrackIndex).First();
                var moved = best with { TimeMs = anchor,
                    DurationMs = best.DurationMs > 0 ? best.DurationMs + best.TimeMs - anchor : 0 };
                return new Candidate(moved, g.Select(n => n.TrackIndex).Distinct().Order().ToArray());
            }).OrderBy(n => n.Note.MidiNote).ToList();
            duplicates += end - start - group.Count;
            int highest = group[^1].Note.MidiNote, lowest = group[0].Note.MidiNote;
            foreach (var candidate in group)
            {
                var note = candidate.Note;
                candidate.Score = note.Velocity / 127d * .4 + note.MidiNote / 127d * .15;
                if (effective.MelodyPriority)
                {
                    if (candidate.SourceTracks.Contains(mainTrack)) candidate.Score += .1;
                    if (note.MidiNote == highest) candidate.Score += .25 + (previousMelody != highest ? .05 : 0);
                }
                if (note.MidiNote == lowest) candidate.Score += .17;
                candidate.Score = Math.Min(1, candidate.Score);
            }
            var kept = original || group.Count <= effective.MaxChordNotes ? group : SelectChord(group, effective.MaxChordNotes, effective.MelodyPriority);
            chordRemoved += group.Count - kept.Count;
            candidates.AddRange(kept);
            previousMelody = highest; start = end;
        }
        int densityRemoved = original ? 0 : LimitDensity(candidates, effective, cancellationToken);
        var retained = candidates.Where(c => c.Kept).OrderBy(c => c.Note.TimeMs).ThenBy(c => c.Note.MidiNote).ToArray();
        var outputNotes = retained.Select(c => c.Note).ToArray();
        var events = retained.GroupBy(c => c.Note.TimeMs).Select(g => new MusicEvent(g.Key, g.Select(c => c.Note.MidiNote).ToArray())
        {
            Priority = g.Max(c => c.Score), SourceTracks = g.SelectMany(c => c.SourceTracks).Distinct().Order().ToArray()
        }).ToList();
        var sequence = new MusicSequence(import.Title, import.InitialBpm, import.DurationMs, events);
        return new(sequence, outputNotes, import.Analysis, analyzer.Analyze(outputNotes, import.DurationMs), preset, recommended,
            import.Analysis.PercussionNotes, import.Analysis.PercussionNotes - outputNotes.Count(n => n.IsPercussion),
            import.Notes.Count - selected.Length, duplicates, weakRemoved, chordRemoved, densityRemoved);
    }

    private static List<Candidate> SelectChord(List<Candidate> chord, int limit, bool melodyPriority)
    {
        var kept = new List<Candidate>();
        if (melodyPriority) kept.Add(chord[^1]);
        if (kept.Count < limit && !kept.Contains(chord[0])) kept.Add(chord[0]);
        while (kept.Count < limit)
        {
            // Reward register coverage; high velocity still wins among similarly useful harmony notes.
            var next = chord.Where(c => !kept.Contains(c)).OrderByDescending(c => c.Score +
                    (kept.Count == 0 ? 0 : Math.Min(12, kept.Min(k => Math.Abs(k.Note.MidiNote - c.Note.MidiNote))) / 12d * .25))
                .ThenByDescending(c => c.Note.Velocity).ThenByDescending(c => c.Note.MidiNote).ThenBy(c => c.Note.TrackIndex).First();
            kept.Add(next);
        }
        return kept;
    }

    private static int LimitDensity(List<Candidate> notes, OptimizationSettings settings, CancellationToken token)
    {
        var active = new LinkedList<Candidate>();
        int removed = 0;
        foreach (var note in notes.OrderBy(n => n.Note.TimeMs).ThenByDescending(n => n.Score).ThenBy(n => n.Note.MidiNote))
        {
            token.ThrowIfCancellationRequested();
            while (active.First is { } oldest && note.Note.TimeMs - oldest.Value.Note.TimeMs >= 1000) active.RemoveFirst();
            active.AddLast(note);
            // Removing the weakest member may evict an earlier accompaniment note to preserve a later melody.
            var shortWindow = active.Where(n => note.Note.TimeMs - n.Note.TimeMs < 250).ToList();
            if (shortWindow.Count > settings.MaxNotesPer250Ms) RemoveWeakest(shortWindow);
            if (active.Count > settings.MaxNotesPerSecond) RemoveWeakest(active);
        }
        return removed;

        void RemoveWeakest(IEnumerable<Candidate> window)
        {
            var weakest = window.OrderBy(n => n.Score).ThenBy(n => n.Note.Velocity)
                .ThenByDescending(n => n.Note.TimeMs).ThenBy(n => n.Note.MidiNote).ThenBy(n => n.Note.TrackIndex).First();
            weakest.Kept = false; active.Remove(weakest); removed++;
        }
    }

    // Pure analysis: this service has no dependency on the keyboard, window focus, or playback services.
    public IReadOnlyList<MidiBenchmarkRow> Benchmark(MidiImportResult import, OptimizationSettings settings,
        IReadOnlyCollection<int>? selectedTracks = null, CancellationToken cancellationToken = default)
    {
        var mapping = new PianoMappingService();
        return new[] { OptimizationPreset.Original, OptimizationPreset.Balanced, OptimizationPreset.Performance }.Select(preset =>
        {
            var result = Optimize(import, settings with { Preset = preset }, selectedTracks, cancellationToken);
            // Match Auto/GroupSafe contiguous groups, including the final release batch.
            int calls = result.Sequence.Events.Sum(e =>
            {
                var keys = e.Notes.Select(mapping.FromMidi).Where(n => n != null).ToArray();
                if (keys.Length == 0) return 0;
                if (keys.All(n => !n!.RequiresShift)) return 2;
                int count = 1;
                for (int i = 0; i < keys.Length;)
                {
                    bool shift = keys[i]!.RequiresShift;
                    var physical = new HashSet<ushort> { keys[i]!.VirtualKey };
                    int end = i + 1;
                    while (end < keys.Length && keys[end]!.RequiresShift == shift && physical.Add(keys[end]!.VirtualKey)) end++;
                    count += shift ? 4 : 2; i = end;
                }
                return count;
            });
            var after = result.After;
            return new MidiBenchmarkRow(preset, after.EventCount, after.NoteCount, after.PeakEventsPerSecond,
                after.PeakNotesPerSecond, after.MaxChordNotes, calls);
        }).ToArray();
    }

    private sealed class Candidate(MidiNoteEvent note, int[] sourceTracks)
    {
        public MidiNoteEvent Note { get; } = note;
        public int[] SourceTracks { get; } = sourceTracks;
        public double Score { get; set; }
        public bool Kept { get; set; } = true;
    }
}
