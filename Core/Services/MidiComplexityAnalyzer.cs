using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class MidiComplexityAnalyzer
{
    public MidiAnalysis Analyze(IEnumerable<MidiNoteEvent> source, double durationMs)
    {
        var notes = source.OrderBy(n => n.TimeMs).ToArray();
        var groups = notes.GroupBy(n => n.TimeMs).ToArray();
        var times = notes.Select(n => n.TimeMs).ToArray();
        return new(notes.Length, groups.Length, notes.Select(n => n.TrackIndex).Distinct().Count(), notes.Count(n => n.IsPercussion),
            groups.Length == 0 ? 0 : groups.Max(g => g.Count()), Peak(times, 250), Peak(times, 1000),
            Peak(groups.Select(g => g.Key).ToArray(), 1000), durationMs > 0 ? notes.Length * 1000d / durationMs : 0);
    }

    public static OptimizationPreset Recommend(MidiAnalysis analysis) => analysis.MaxChordNotes <= 4 && analysis.PeakNotesPerSecond <= 25
        ? OptimizationPreset.Original : analysis.MaxChordNotes <= 8 && analysis.PeakNotesPerSecond <= 45
            ? OptimizationPreset.Balanced : OptimizationPreset.Performance;

    // Half-open sliding windows: two notes exactly one second apart belong to different one-second windows.
    private static int Peak(double[] sortedTimes, double windowMs)
    {
        int peak = 0, left = 0;
        for (int right = 0; right < sortedTimes.Length; right++)
        {
            while (sortedTimes[right] - sortedTimes[left] >= windowMs) left++;
            peak = Math.Max(peak, right - left + 1);
        }
        return peak;
    }
}
