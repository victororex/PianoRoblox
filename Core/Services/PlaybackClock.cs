using System.Diagnostics;

namespace PianoRoblox.Core.Services;

// Anchoring to Stopwatch avoids accumulating scheduling/keyboard overhead as drift.
public sealed class PlaybackClock
{
    private readonly object sync = new();
    private readonly Stopwatch watch = new();
    private double position, speed = 100;
    public double PositionMs { get { lock (sync) return position + watch.Elapsed.TotalMilliseconds * speed / 100; } }
    public double Speed
    {
        get { lock (sync) return speed; }
        set
        {
            if (!double.IsFinite(value) || value is < 50 or > 150) throw new ArgumentOutOfRangeException(nameof(value));
            lock (sync) { Capture(); speed = value; }
        }
    }
    public void Start(double fromMs) { lock (sync) { position = fromMs; watch.Restart(); } }
    public void Reset(double fromMs = 0) { lock (sync) { position = fromMs; watch.Reset(); } }
    public void Pause() { lock (sync) { Capture(); watch.Stop(); } }
    public void Resume() { lock (sync) watch.Start(); }
    private void Capture() { position += watch.Elapsed.TotalMilliseconds * speed / 100; bool running = watch.IsRunning; watch.Reset(); if (running) watch.Start(); }
}

