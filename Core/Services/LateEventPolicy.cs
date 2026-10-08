using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

// The policy drops events; it never moves their timestamps or changes the musical clock/BPM.
// monotonicMs is elapsed wall time, so the dispatch limits still hold at any manual playback speed.
public sealed class LateEventPolicy(PlaybackOptions options)
{
    private readonly Queue<(double TimeMs, int Notes)> sent = new();
    private readonly Queue<(double TimeMs, int Notes)> catchUp = new();
    private readonly double maximumDelay = Math.Clamp(options.MaxCatchUpDelayMs, 40, 500);
    public int SkippedEvents { get; private set; }

    public bool ShouldPlay(MusicEvent musicEvent, double positionMs, double monotonicMs)
    {
        while (sent.TryPeek(out var first) && monotonicMs - first.TimeMs >= 1000) sent.Dequeue();
        while (catchUp.TryPeek(out var first) && monotonicMs - first.TimeMs >= 50) catchUp.Dequeue();
        double delay = Math.Max(0, positionMs - musicEvent.TimeMs);
        int notes = musicEvent.Notes.Length;
        // Important recent melody can survive a delay; obsolete material cannot accumulate forever.
        if (delay > maximumDelay * 2 || (delay > maximumDelay && musicEvent.Priority < .75)) return Skip();
        bool late = delay > 40;
        if (late && catchUp.Sum(item => item.Notes) + notes > 8) return Skip();
        if (options.PerformanceMode == PlaybackPerformanceMode.FpsFriendly)
        {
            int recentNotes = sent.Where(item => monotonicMs - item.TimeMs < 50).Sum(item => item.Notes);
            if (recentNotes + notes > 8 || sent.Sum(item => item.Notes) + notes > 32) return Skip();
        }
        if (late) catchUp.Enqueue((monotonicMs, notes));
        if (options.PerformanceMode == PlaybackPerformanceMode.FpsFriendly) sent.Enqueue((monotonicMs, notes));
        return true;
    }

    private bool Skip() { SkippedEvents++; return false; }
}
