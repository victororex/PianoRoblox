using System.Diagnostics;
using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed record PlaybackDiagnostics(long ExecutedNotes, long NoteOns, long NoteOffs, long SkippedNotes,
    long LateEvents, double MaximumDelayMs, int HeldKeys, long PhysicalConflicts);

public sealed partial class PlaybackService
{
    private long executedNotes, skippedNotes, lateEvents;
    private double maximumDelay;
    private int plannedConflicts;
    public PlaybackDiagnostics Diagnostics
    {
        get
        {
            lock (sync)
            {
                var input = (keyboard as INoteKeyboardPlayer)?.InputStatistics ?? new(0, 0, 0, 0);
                return new(executedNotes, input.NoteOns, input.NoteOffs, skippedNotes, lateEvents,
                    maximumDelay, input.HeldKeys, input.Conflicts + plannedConflicts);
            }
        }
    }
    public int LimitedDurationNotes { get; private set; }
    private void ResetDiagnostics()
    {
        executedNotes = skippedNotes = lateEvents = 0; maximumDelay = 0; plannedConflicts = LimitedDurationNotes = 0;
        (keyboard as INoteKeyboardPlayer)?.ResetStatistics();
    }

    private async Task RunSustainAsync(NoteSchedule schedule, PlaybackOptions options, INoteKeyboardPlayer output, CancellationToken token)
    {
        lock (sync)
        {
            LimitedDurationNotes = schedule.LimitedNotes; plannedConflicts = schedule.PhysicalConflicts;
            skippedNotes = schedule.SuppressedAttacks;
        }
        if (schedule.SuppressedAttacks > 0) Status?.Invoke($"{schedule.SuppressedAttacks} ataques impossíveis com o Release Gap/Shift configurado foram ignorados.");
        if (schedule.LimitedNotes > 0) Status?.Invoke($"Hybrid: {schedule.LimitedNotes} notas limitadas pelo Maximum Hold configurado.");
        if (schedule.PhysicalConflicts > 0) Status?.Invoke($"{schedule.PhysicalConflicts} conflitos de Shift/tecla física: sustentações incompatíveis serão liberadas antes do novo ataque.");
        if (schedule.EstimatedNotes > 0) Status?.Invoke($"{schedule.EstimatedNotes} notas sem Note Off: hold estimado de {options.NoteDuration!.EstimatedHoldMs} ms.");
        var latePolicy = new LateEventPolicy(options);
        var watch = Stopwatch.StartNew();
        var sounding = new Dictionary<long, ScheduledNote>();
        var releasedKeys = new Dictionary<ushort, double>();
        var deferred = new PriorityQueue<ScheduledNote, double>();
        long revision = releaseRevision;
        double startAt = sequenceStart, lastRelease = double.NegativeInfinity;
        bool? lastReleasedShift = null;
        int index = schedule.FindTransition(startAt);
        // Seek policy: reattack spanning notes for the remaining time. Finished notes stay silent.
        var restore = schedule.Notes.Where(n => n.StartTimeMs < startAt && n.EndTimeMs > startAt).ToArray();
        while (index < schedule.Transitions.Count || clock.PositionMs < schedule.DurationMs || restore.Length > 0 || deferred.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            if (state == PlaybackState.Paused) { await Task.Delay(10, token).ConfigureAwait(false); continue; }
            if (options.RequireFocus && !isFocused()) { Pause(true); continue; }
            double target = restore.Length > 0 ? clock.PositionMs : index < schedule.Transitions.Count
                ? schedule.Transitions[index].TimeMs : schedule.DurationMs;
            double remaining = (target - clock.PositionMs) * 100 / clock.Speed;
            if (index >= schedule.Transitions.Count && clock.PositionMs >= schedule.DurationMs && restore.Length == 0)
                remaining = double.PositiveInfinity;
            if (deferred.TryPeek(out _, out double ready)) remaining = Math.Min(remaining, ready - watch.Elapsed.TotalMilliseconds);
            if (remaining > 0 && revision == releaseRevision)
            {
                int maximum = options.PerformanceMode == PlaybackPerformanceMode.LowLatency ? 2 : 10;
                await Task.Delay((int)Math.Clamp(remaining - 1, 1, maximum), token).ConfigureAwait(false); continue;
            }
            try
            {
                lock (sync)
                {
                    token.ThrowIfCancellationRequested();
                    if (state != PlaybackState.Playing) continue;
                    double position = clock.PositionMs;
                    if (revision != releaseRevision)
                    {
                        revision = releaseRevision;
                        restore = sounding.Values.Where(n => n.EndTimeMs > position).ToArray(); sounding.Clear();
                        releasedKeys.Clear(); lastReleasedShift = null;
                    }
                    if (restore.Length > 0)
                    {
                        Dispatch(restore.Where(n => n.EndTimeMs > position).ToArray(), position, true); restore = [];
                    }
                    // Process releases before any delayed attacks. The physical retrigger gap never blocks Note Off.
                    if (index < schedule.Transitions.Count && schedule.Transitions[index].TimeMs <= position)
                    {
                        target = schedule.Transitions[index].TimeMs;
                        int end = index + 1;
                        while (end < schedule.Transitions.Count && schedule.Transitions[end].TimeMs == target) end++;
                        var batch = new List<NoteTransition>(end - index);
                        for (int i = index; i < end; i++) batch.Add(schedule.Transitions[i]);
                        var releases = batch.Where(t => !t.IsOn).Select(t => t.Note).ToArray();
                        output.NoteOff(releases.Select(n => n.Id).ToArray(), options);
                        foreach (var n in releases)
                            if (sounding.Remove(n.Id))
                            {
                                lastRelease = watch.Elapsed.TotalMilliseconds;
                                releasedKeys[n.PhysicalKey] = lastRelease; lastReleasedShift = n.RequiresShift;
                            }
                        var attacks = batch.Where(t => t.IsOn).Select(t => t.Note).ToArray();
                        Dispatch(attacks, position, false);
                        index = end;
                    }
                    var due = new List<ScheduledNote>();
                    while (deferred.TryPeek(out _, out ready) && ready <= watch.Elapsed.TotalMilliseconds)
                        due.Add(deferred.Dequeue());
                    if (due.Count > 0) Dispatch(due.ToArray(), position, false);
                }
            }
            catch (FocusLostException) { Pause(true); }
        }

        void Dispatch(ScheduledNote[] notes, double position, bool restoring)
        {
            var attacks = new List<ScheduledNote>();
            double real = watch.Elapsed.TotalMilliseconds;
            int gap = options.NoteDuration!.ReleaseGapMs;
            foreach (var n in notes)
            {
                if (n.EndTimeMs <= position) { skippedNotes++; continue; }
                double readyAt = releasedKeys.GetValueOrDefault(n.PhysicalKey, double.NegativeInfinity) + gap;
                if (lastReleasedShift.HasValue && lastReleasedShift != n.RequiresShift) readyAt = Math.Max(readyAt, lastRelease + gap);
                if (!restoring && readyAt > real) { deferred.Enqueue(n, readyAt); continue; }
                attacks.Add(n);
            }
            if (attacks.Count == 0) return;
            double target = attacks.Min(n => n.StartTimeMs);
            double delay = Math.Max(0, position - target);
            if (!restoring) maximumDelay = Math.Max(maximumDelay, delay);
            if (!restoring && delay > 10) lateEvents++;
            var e = new MusicEvent(target, attacks.Select(n => n.Note.MidiNote).ToArray()) { Priority = attacks.Max(n => n.Note.Priority) };
            if (restoring || latePolicy.ShouldPlay(e, position, real))
            {
                output.NoteOn(attacks.Select(n => new NoteAttack(n.Id, n.Note)).ToArray(), options, token);
                foreach (var n in attacks) sounding[n.Id] = n;
                executedNotes += attacks.Count; EventPlayed?.Invoke(attacks[^1].EventIndex, e);
            }
            else skippedNotes += attacks.Count;
            SkippedEventCount = latePolicy.SkippedEvents;
        }
    }
}
