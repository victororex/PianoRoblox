using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public interface IKeyboardPlayer
{
    Task PlayChord(IReadOnlyList<int> notes, PlaybackOptions options, CancellationToken token);
    void ReleaseAllKeys();
}

public enum PlaybackState { Stopped, Countdown, Playing, Paused, Completed }

public sealed class PlaybackService(IKeyboardPlayer keyboard, Func<bool> isFocused) : IDisposable
{
    private readonly object sync = new();
    private readonly PlaybackClock clock = new();
    private CancellationTokenSource? session, chord;
    private Task? worker;
    private volatile PlaybackState state;
    public int SkippedEventCount { get; private set; }
    public PlaybackState State => state;
    public double PositionMs => clock.PositionMs;
    public double Speed { get => clock.Speed; set => clock.Speed = value; }
    public event Action<string>? Status;
    public event Action<int, MusicEvent>? EventPlayed;
    public event Action<Exception>? Failed;

    public Task StartAsync(MusicSequence sequence, PlaybackOptions options, int countdown, double fromMs = 0)
    {
        lock (sync)
        {
            if (worker is { IsCompleted: false }) return worker;
            session?.Dispose();
            session = new();
            state = PlaybackState.Countdown;
            SkippedEventCount = 0;
            clock.Reset(Math.Clamp(fromMs, 0, sequence.DurationMs));
            worker = Task.Run(() => RunAsync(sequence, options, countdown, session.Token));
            return worker;
        }
    }

    public void Pause()
    {
        lock (sync)
        {
            if (state != PlaybackState.Playing) return;
            state = PlaybackState.Paused; clock.Pause(); chord?.Cancel(); keyboard.ReleaseAllKeys();
        }
        Status?.Invoke("Playback pausado. F7 para continuar.");
    }

    public void Resume()
    {
        lock (sync)
        {
            if (state != PlaybackState.Paused) return;
            clock.Resume(); state = PlaybackState.Playing;
        }
        Status?.Invoke("Playback retomado.");
    }

    public async Task StopAsync()
    {
        Task? current;
        lock (sync)
        {
            session?.Cancel(); chord?.Cancel(); clock.Pause(); state = PlaybackState.Stopped;
            keyboard.ReleaseAllKeys(); current = worker;
        }
        if (current != null) await current.ConfigureAwait(false);
        lock (sync)
        {
            if (worker == current && state == PlaybackState.Stopped) clock.Reset();
        }
    }

    private async Task RunAsync(MusicSequence sequence, PlaybackOptions options, int countdown, CancellationToken token)
    {
        using var resolution = new TimerResolution();
        var latePolicy = new LateEventPolicy(options);
        var dispatchClock = System.Diagnostics.Stopwatch.StartNew();
        bool completed = false;
        try
        {
            for (int i = countdown; i > 0; i--)
            { Status?.Invoke($"Tocando em: {i}"); await Task.Delay(1000, token).ConfigureAwait(false); }
            double startAt = clock.PositionMs;
            lock (sync) { token.ThrowIfCancellationRequested(); state = PlaybackState.Playing; clock.Resume(); }
            Status?.Invoke("Playback iniciado.");
            int index = sequence.Events.FindIndex(e => e.TimeMs >= startAt);
            if (index < 0) index = sequence.Events.Count;
            while (index < sequence.Events.Count || clock.PositionMs < sequence.DurationMs)
            {
                token.ThrowIfCancellationRequested();
                if (state == PlaybackState.Paused) { await Task.Delay(10, token).ConfigureAwait(false); continue; }
                if (options.RequireFocus && !isFocused())
                { Pause(); Status?.Invoke("Roblox não está em foco. Volte ao jogo e pressione F7."); continue; }
                double target = index < sequence.Events.Count ? sequence.Events[index].TimeMs : sequence.DurationMs;
                double remaining = (target - clock.PositionMs) * 100 / clock.Speed;
                if (remaining > 0)
                {
                    // Bounded waits also allow prompt focus-loss detection. No CPU-burning spin loop.
                    int maximumWait = options.PerformanceMode == PlaybackPerformanceMode.LowLatency ? 2 : 10;
                    await Task.Delay(remaining > 3 ? (int)Math.Min(maximumWait, Math.Max(1, remaining - 2)) : 1, token).ConfigureAwait(false);
                    continue;
                }
                if (index == sequence.Events.Count) break;
                CancellationToken chordToken;
                lock (sync)
                {
                    token.ThrowIfCancellationRequested();
                    if (state != PlaybackState.Playing) continue;
                    chord?.Dispose(); chord = CancellationTokenSource.CreateLinkedTokenSource(token); chordToken = chord.Token;
                }
                try
                {
                    var e = sequence.Events[index];
                    if (!latePolicy.ShouldPlay(e, clock.PositionMs, dispatchClock.Elapsed.TotalMilliseconds))
                    { SkippedEventCount = latePolicy.SkippedEvents; index++; continue; }
                    EventPlayed?.Invoke(index, e);
                    await keyboard.PlayChord(e.Notes, options, chordToken).ConfigureAwait(false);
                    index++;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { /* Resume retries the interrupted chord. */ }
                catch (FocusLostException) { Pause(); Status?.Invoke("Roblox não está em foco. Volte ao jogo e pressione F7."); }
            }
            token.ThrowIfCancellationRequested();
            completed = true;
        }
        catch (OperationCanceledException) { state = PlaybackState.Stopped; Status?.Invoke("Playback parado."); }
        catch (Exception ex) { state = PlaybackState.Stopped; Failed?.Invoke(ex); }
        finally
        {
            lock (sync)
            {
                clock.Pause(); keyboard.ReleaseAllKeys();
                if (completed && !token.IsCancellationRequested) state = PlaybackState.Completed;
            }
        }
        if (state == PlaybackState.Completed) Status?.Invoke("Playback concluído.");
    }

    public void Dispose()
    {
        lock (sync) { session?.Cancel(); chord?.Cancel(); clock.Pause(); keyboard.ReleaseAllKeys(); }
    }
}

public sealed class FocusLostException() : Exception("Roblox não está em foco.");

