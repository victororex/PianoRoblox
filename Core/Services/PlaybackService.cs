using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public interface IKeyboardPlayer
{
    Task PlayChord(IReadOnlyList<int> notes, PlaybackOptions options, CancellationToken token);
    void ReleaseAllKeys();
}

public enum PlaybackState { Stopped, Countdown, Playing, Paused, Completed, Error }
public enum PlaybackPauseReason { None, User, FocusLost }

public sealed partial class PlaybackService(IKeyboardPlayer keyboard, Func<bool> isFocused) : IDisposable
{
    private readonly object sync = new();
    private readonly PlaybackClock clock = new();
    private CancellationTokenSource? session, chord;
    private Task? worker;
    private volatile PlaybackState state;
    private bool focusRequired;
    private long releaseRevision;
    private double sequenceStart;
    public PlaybackPauseReason PauseReason { get; private set; }
    public NoteDurationMode ActiveDurationMode { get; private set; }
    public bool UsesSustain => ActiveDurationMode != NoteDurationMode.LegacyTap;
    public IReadOnlyList<int> ActiveMidiNotes => (keyboard as INoteKeyboardPlayer)?.ActiveMidiNotes ?? [];
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
            // The running session owns an immutable copy of mutable per-song duration settings.
            options = options with { NoteDuration = options.NoteDuration?.Validated() };
            session?.Dispose();
            session = new();
            state = PlaybackState.Countdown;
            focusRequired = options.RequireFocus; PauseReason = PlaybackPauseReason.None;
            ActiveDurationMode = keyboard is INoteKeyboardPlayer && options.NoteDuration is { SustainEnabled: true }
                ? options.NoteDuration.Mode : NoteDurationMode.LegacyTap;
            ResetDiagnostics();
            SkippedEventCount = 0;
            clock.Reset(Math.Clamp(fromMs, 0, sequence.DurationMs));
            sequenceStart = clock.PositionMs;
            worker = Task.Run(() => RunAsync(sequence, options, countdown, session.Token));
            return worker;
        }
    }

    public void Pause(bool focusLost = false)
    {
        lock (sync)
        {
            if (state != PlaybackState.Playing) return;
            state = PlaybackState.Paused; PauseReason = focusLost ? PlaybackPauseReason.FocusLost : PlaybackPauseReason.User;
            clock.Pause(); chord?.Cancel(); keyboard.ReleaseAllKeys(); releaseRevision++;
        }
        Status?.Invoke(focusLost ? "Roblox não está em foco. Volte ao jogo e pressione F7 para continuar." : "Pausado pelo usuário. F7 para continuar.");
    }

    public void Resume()
    {
        lock (sync)
        {
            if (state != PlaybackState.Paused) return;
            if (focusRequired && !isFocused()) return;
            clock.Resume(); state = PlaybackState.Playing; PauseReason = PlaybackPauseReason.None;
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
            var durationOutput = keyboard as INoteKeyboardPlayer;
            bool useSustain = durationOutput != null && options.NoteDuration is { SustainEnabled: true } duration &&
                duration.Mode != NoteDurationMode.LegacyTap;
            var noteSchedule = useSustain ? NoteSchedule.Build(sequence, options, durationOutput!.ResolveNote, token) : null;
            for (int i = countdown; i > 0; i--)
            { Status?.Invoke($"Tocando em: {i}"); await Task.Delay(1000, token).ConfigureAwait(false); }
            double startAt = clock.PositionMs;
            lock (sync) { token.ThrowIfCancellationRequested(); state = PlaybackState.Playing; clock.Resume(); }
            Status?.Invoke("Playback iniciado.");
            if (noteSchedule != null)
                await RunSustainAsync(noteSchedule, options, durationOutput!, token).ConfigureAwait(false);
            else
            {
            int index = sequence.Events.FindIndex(e => e.TimeMs >= startAt);
            if (index < 0) index = sequence.Events.Count;
            while (index < sequence.Events.Count || clock.PositionMs < sequence.DurationMs)
            {
                token.ThrowIfCancellationRequested();
                if (state == PlaybackState.Paused) { await Task.Delay(10, token).ConfigureAwait(false); continue; }
                if (options.RequireFocus && !isFocused())
                { Pause(true); continue; }
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
                    { SkippedEventCount = latePolicy.SkippedEvents; lock (sync) skippedNotes += e.Notes.Length; index++; continue; }
                    lock (sync)
                    {
                        double delay = Math.Max(0, clock.PositionMs - e.TimeMs);
                        maximumDelay = Math.Max(maximumDelay, delay); if (delay > 10) lateEvents++;
                        executedNotes += e.Notes.Length;
                    }
                    EventPlayed?.Invoke(index, e);
                    await keyboard.PlayChord(e.Notes, options, chordToken).ConfigureAwait(false);
                    index++;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { /* Resume retries the interrupted chord. */ }
                catch (FocusLostException) { Pause(true); }
            }
            }
            token.ThrowIfCancellationRequested();
            completed = true;
        }
        catch (OperationCanceledException) { state = PlaybackState.Stopped; Status?.Invoke("Playback parado."); }
        catch (Exception ex) { state = PlaybackState.Error; Failed?.Invoke(ex); }
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

