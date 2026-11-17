using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed record TransformResult(MusicSequence Sequence, int OutOfRange, int Ignored, int Folded, int Collisions = 0);

public static class SequenceTransformer
{
    public static TransformResult Apply(MusicSequence source, int transpose, RangePolicy policy, PianoProfile? profile = null)
    {
        profile ??= PianoProfile.Legacy61;
        if (transpose is < -24 or > 24) throw new ArgumentOutOfRangeException(nameof(transpose));
        int outside = 0, ignored = 0, folded = 0, collisions = 0;
        var events = new List<MusicEvent>();
        foreach (var e in source.Events)
        {
            var notes = new List<MusicNote>();
            foreach (var original in e.GetNotes())
            {
                int note = original.MidiNote + transpose;
                if (note < profile.FirstMidiNote || note > profile.LastMidiNote)
                {
                    outside++;
                    if (policy == RangePolicy.Ask) throw new InvalidOperationException("Escolha ignorar ou transpor por oitava as notas fora do alcance.");
                    if (policy == RangePolicy.Ignore) { ignored++; continue; }
                    while (note < profile.FirstMidiNote) note += 12;
                    while (note > profile.LastMidiNote) note -= 12;
                    if (note < profile.FirstMidiNote) { ignored++; continue; }
                    folded++;
                }
                notes.Add(original with { MidiNote = note });
            }
            if (notes.Count > 0)
            {
                var merged = notes.GroupBy(n => n.MidiNote).OrderBy(g => g.Key).Select(g =>
                {
                    var best = g.OrderByDescending(n => n.Velocity).ThenBy(n => n.TrackIndex).First();
                    return best with { DurationMs = g.Max(n => n.DurationMs) };
                }).ToArray();
                collisions += notes.Count - merged.Length;
                events.Add(e with { Notes = merged.Select(n => n.MidiNote).ToArray(), NoteDetails = merged });
            }
        }
        return new(source with { Events = events }, outside, ignored, folded, collisions);
    }

    public static double AdjustTime(double originalMs, double speedPercent)
    {
        if (!double.IsFinite(speedPercent) || speedPercent is < 50 or > 150) throw new ArgumentOutOfRangeException(nameof(speedPercent));
        return originalMs * 100 / speedPercent;
    }
}

