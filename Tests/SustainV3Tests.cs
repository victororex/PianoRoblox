using System.Collections.Concurrent;
using System.Diagnostics;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Services;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class SustainV3Tests
{
    private static PlaybackOptions Options(NoteDurationMode mode = NoteDurationMode.MidiSustain) =>
        new(0, 8, ChordMode.Auto, InputMode.VirtualKey, true, NoteDuration: new() { Mode = mode });
    internal static MusicEvent Note(double start, int pitch, double duration) =>
        new(start, [pitch]) { NoteDetails = [new(pitch, duration, 100, 2, .9)] };
    private static MusicSequence Sequence(params MusicEvent[] events) =>
        new("Sustain", 120, events.Max(e => e.TimeMs + e.NoteDetails.Max(n => n.DurationMs ?? 8)), events.ToList());
    private sealed record KeyCall(ushort Key, bool Up, double Time);
    private sealed class Sender : IKeySender
    {
        private readonly ConcurrentQueue<KeyCall> calls = new();
        private readonly HashSet<ushort> held = [];
        private readonly object sync = new();
        public readonly Stopwatch Watch = Stopwatch.StartNew();
        public readonly TaskCompletionSource First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public KeyCall[] Calls => calls.ToArray();
        public int HeldCount { get { lock (sync) return held.Count; } }
        public void KeyDown(ushort key, InputMode mode, bool focus, CancellationToken token)
        { token.ThrowIfCancellationRequested(); lock (sync) { held.Add(key); calls.Enqueue(new(key, false, Watch.Elapsed.TotalMilliseconds)); } if (key != 0xA0) First.TrySetResult(); }
        public void KeyUp(ushort key)
        { lock (sync) if (held.Remove(key)) calls.Enqueue(new(key, true, Watch.Elapsed.TotalMilliseconds)); }
        public void ReleaseAllKeys() { lock (sync) foreach (ushort key in held.ToArray()) KeyUp(key); }
    }
    private static (PlaybackService Player, Sender Sender) Create(Func<bool>? focused = null)
    { var sender = new Sender(); return (new(new KeyboardPlayer(new(), sender), focused ?? (() => true)), sender); }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(4000);
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    [Fact] public async Task LongNoteFollowsNoteOffInsteadOfEightMillisecondTap()
    {
        var (player, sender) = Create(); using (player)
        {
            await player.StartAsync(Sequence(Note(1000, 60, 1500)), Options(), 0).WaitAsync(TimeSpan.FromSeconds(5));
            var down = Assert.Single(sender.Calls, c => c.Key == 'T' && !c.Up);
            var up = Assert.Single(sender.Calls, c => c.Key == 'T' && c.Up);
            Assert.InRange(down.Time, 900, 1200); Assert.InRange(up.Time, 2400, 2700);
            Assert.InRange(up.Time - down.Time, 1380, 1620); Assert.Equal(0, sender.HeldCount);
            Assert.Equal(PlaybackState.Completed, player.State);
        }
    }
    [Fact] public async Task PolyphonyKeepsC4HeldAfterE4Ends()
    {
        var (player, sender) = Create(); using (player)
        {
            await player.StartAsync(Sequence(Note(100, 60, 1000), Note(300, 64, 250)), Options(), 0);
            var calls = sender.Calls;
            int cUp = Array.FindIndex(calls, c => c.Key == 'T' && c.Up), eUp = Array.FindIndex(calls, c => c.Key == 'U' && c.Up);
            Assert.True(eUp > 0 && cUp > eUp);
            Assert.InRange(calls[cUp].Time - calls[eUp].Time, 450, 650);
            Assert.Equal(2, player.Diagnostics.NoteOns); Assert.Equal(2, player.Diagnostics.NoteOffs);
        }
    }
    [Fact] public async Task RetriggerProducesSecondAttackAndOldOffCannotReleaseIt()
    {
        var sequence = Sequence(Note(0, 60, 600), Note(250, 60, 700));
        var schedule = NoteSchedule.Build(sequence, Options(), new PianoMappingService().FromMidi);
        Assert.Equal(248, schedule.Notes[0].EndTimeMs);
        var (player, sender) = Create(); using (player)
        {
            await player.StartAsync(sequence, Options(), 0);
            var calls = sender.Calls.Where(c => c.Key == 'T').ToArray();
            Assert.Equal(new[] { false, true, false, true }, calls.Select(c => c.Up));
            Assert.InRange(calls[^1].Time, 850, 1100); Assert.Equal(0, sender.HeldCount);
        }
    }
    [Fact] public void RetriggerDisabledMergesOverlapsUntilLatestEnd()
    {
        var options = Options() with { NoteDuration = new() { Mode = NoteDurationMode.MidiSustain, RetriggerSameNote = false } };
        var schedule = NoteSchedule.Build(Sequence(Note(0, 60, 1000), Note(500, 60, 900)), options, new PianoMappingService().FromMidi);
        var note = Assert.Single(schedule.Notes); Assert.Equal(1400, note.EndTimeMs);
    }
    [Fact] public async Task RetriggerGapRemainsPhysicalMinimumAtHigherSpeed()
    {
        var (player, sender) = Create(); using (player)
        {
            player.Speed = 150;
            var options = Options() with { NoteDuration = new() { Mode = NoteDurationMode.MidiSustain, ReleaseGapMs = 20 } };
            await player.StartAsync(Sequence(Note(0, 60, 600), Note(200, 60, 500)), options, 0);
            var calls = sender.Calls.Where(c => c.Key == 'T').ToArray();
            Assert.Equal(4, calls.Length); Assert.True(calls[2].Time - calls[1].Time >= 19.5);
        }
    }
    [Fact] public void DisabledOverlapTruncatesPreviousChordBeforeNewAttack()
    {
        var options = Options() with { NoteDuration = new() { Mode = NoteDurationMode.MidiSustain, NoteOverlap = false } };
        var schedule = NoteSchedule.Build(Sequence(Note(0, 60, 1000), Note(500, 64, 1000)), options, new PianoMappingService().FromMidi);
        Assert.Equal(498, schedule.Notes[0].EndTimeMs); Assert.Equal(1500, schedule.Notes[1].EndTimeMs);
    }
    [Fact] public void GroupSafeKeepsCompatibleChordTogetherAndSpreadStaggersAttacks()
    {
        var e = new MusicEvent(1000, [60, 64, 67]) { NoteDetails = [new(60, 1500), new(64, 500), new(67, 900)] };
        var sequence = new MusicSequence("Chord", 120, 2500, [e]);
        var safe = NoteSchedule.Build(sequence, Options(), new PianoMappingService().FromMidi);
        Assert.All(safe.Notes, n => Assert.Equal(1000, n.StartTimeMs));
        var spread = NoteSchedule.Build(sequence, Options() with { ChordMode = ChordMode.Spread, SpreadMs = 10 }, new PianoMappingService().FromMidi);
        Assert.Equal(new double[] { 1000, 1010, 1020 }, spread.Notes.Select(n => n.StartTimeMs));
        Assert.Equal(new double[] { 1500, 500, 900 }, spread.Notes.Select(n => n.DurationMs));
    }
    [Fact] public async Task PauseReleasesEveryKeyAndResumeReattacksOnlyRemainingDuration()
    {
        var (player, sender) = Create(); using (player)
        {
            var task = player.StartAsync(Sequence(Note(0, 60, 900)), Options(), 0);
            await sender.First.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(100);
            player.Pause(); Assert.Equal(0, sender.HeldCount); Assert.Equal(PlaybackPauseReason.User, player.PauseReason);
            double position = player.PositionMs; await Task.Delay(100); Assert.Equal(position, player.PositionMs, 3);
            player.Resume(); await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(2, sender.Calls.Count(c => c.Key == 'T' && !c.Up));
            Assert.Equal(0, sender.HeldCount); Assert.InRange(player.PositionMs, 900, 1050);
        }
    }
    [Fact] public async Task FocusPausePreventsResumeUntilGameRegainsFocus()
    {
        bool focused = true;
        var (player, sender) = Create(() => focused); using (player)
        {
            var task = player.StartAsync(Sequence(Note(0, 61, 2000)), Options(), 0);
            await sender.First.Task.WaitAsync(TimeSpan.FromSeconds(3)); focused = false;
            await Until(() => player.State == PlaybackState.Paused);
            Assert.Equal(PlaybackPauseReason.FocusLost, player.PauseReason); Assert.Equal(0, sender.HeldCount);
            player.Resume(); Assert.Equal(PlaybackState.Paused, player.State);
            focused = true; player.Resume(); await Task.Delay(20); await player.StopAsync(); await task;
            Assert.Equal(0, sender.HeldCount);
        }
    }
    [Fact] public async Task StopAndSwitchReleasePreviousSustainBeforeNewSong()
    {
        var (player, sender) = Create(); using (player)
        {
            var previous = player.StartAsync(Sequence(Note(0, 61, 5000)), Options(), 0);
            await sender.First.Task.WaitAsync(TimeSpan.FromSeconds(3)); await player.StopAsync(); await previous;
            Assert.Equal(0, sender.HeldCount);
            await player.StartAsync(Sequence(Note(0, 64, 100)), Options(), 0);
            var calls = sender.Calls;
            Assert.True(Array.FindIndex(calls, c => c.Key == 'T' && c.Up) < Array.FindIndex(calls, c => c.Key == 'U' && !c.Up));
            Assert.True(Array.FindIndex(calls, c => c.Key == 0xA0 && c.Up) < Array.FindIndex(calls, c => c.Key == 'U' && !c.Up));
        }
    }
    [Fact] public async Task SeekReattacksSpanningNotesAndIgnoresFinishedNotes()
    {
        var (player, sender) = Create(); using (player)
        {
            await player.StartAsync(Sequence(Note(0, 60, 1000), Note(0, 64, 200)), Options(), 0, 600);
            Assert.Single(sender.Calls, c => !c.Up && c.Key == 'T');
            Assert.DoesNotContain(sender.Calls, c => c.Key == 'U' && !c.Up);
            Assert.InRange(sender.Calls.Single(c => c.Key == 'T' && c.Up).Time, 300, 550);
        }
    }
    [Theory] [InlineData(50, 1600)] [InlineData(100, 800)] [InlineData(150, 533.333)]
    public async Task NoteOffScalesWithMusicalSpeed(double speed, double wallDuration)
    {
        var (player, sender) = Create(); using (player)
        {
            player.Speed = speed; await player.StartAsync(Sequence(Note(0, 60, 800)), Options(), 0);
            var down = sender.Calls.Single(c => !c.Up && c.Key == 'T'); var up = sender.Calls.Single(c => c.Up && c.Key == 'T');
            Assert.InRange(up.Time - down.Time, wallDuration - 100, wallDuration + 120);
        }
    }
    [Fact] public async Task LiveSpeedChangeMovesOffWithoutJumpingClock()
    {
        var (player, sender) = Create(); using (player)
        {
            var task = player.StartAsync(Sequence(Note(0, 60, 1000)), Options(), 0);
            await sender.First.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(200);
            double before = player.PositionMs; player.Speed = 150;
            Assert.InRange(player.PositionMs - before, 0, 20);
            await task; var calls = sender.Calls.Where(c => c.Key == 'T').ToArray();
            Assert.InRange(calls[^1].Time - calls[0].Time, 650, 850);
        }
    }
    [Fact] public async Task ShiftConflictsAreCountedAndAllModifiersReleased()
    {
        var sequence = Sequence(Note(0, 60, 600), Note(200, 61, 500), Note(300, 64, 300));
        var (player, sender) = Create(); using (player)
        {
            await player.StartAsync(sequence, Options(), 0);
            Assert.True(player.Diagnostics.PhysicalConflicts >= 2); Assert.Equal(0, sender.HeldCount);
            Assert.Contains(sender.Calls, c => c.Key == 0xA0 && c.Up);
            Assert.Equal(3, sender.Calls.Count(c => !c.Up && c.Key != 0xA0));
        }
    }
    [Fact] public void HybridCapIsExplicitAndMidiSustainDoesNotApplyIt()
    {
        var settings = new NoteDurationSettings { Mode = NoteDurationMode.Hybrid, MaximumHoldMs = 500 };
        var sequence = Sequence(Note(0, 60, 15000));
        var schedule = NoteSchedule.Build(sequence, Options() with { NoteDuration = settings }, new PianoMappingService().FromMidi);
        Assert.Equal(1, schedule.LimitedNotes); Assert.Equal(500, schedule.Notes.Single().DurationMs);
        settings.Mode = NoteDurationMode.MidiSustain;
        schedule = NoteSchedule.Build(sequence, Options() with { NoteDuration = settings }, new PianoMappingService().FromMidi);
        Assert.Equal(0, schedule.LimitedNotes); Assert.Equal(15000, schedule.Notes.Single().DurationMs);
    }
    [Fact] public void UnknownDurationsRemainExplicitlyEstimated()
    {
        var sequence = new MusicSequence("Sheet", 120, 100, [new(0, [60])]);
        var schedule = NoteSchedule.Build(sequence, Options(NoteDurationMode.Hybrid), new PianoMappingService().FromMidi);
        Assert.Equal(1, schedule.EstimatedNotes); Assert.Null(schedule.Notes.Single().Note.DurationMs);
        Assert.Equal(8, schedule.Notes.Single().DurationMs);
    }
    [Fact] public async Task DenseInputUsesOneWorkerAndNeverDropsMandatoryReleases()
    {
        var natural = new PianoMappingService().All.Where(n => !n.RequiresShift).Select(n => n.MidiNote).ToArray();
        var sequence = Sequence(Enumerable.Range(0, 2000).Select(i => Note(i * .25, natural[i % natural.Length], 20)).ToArray());
        var (player, sender) = Create(); using (player)
        {
            Exception? failure = null; player.Failed += ex => failure = ex;
            await player.StartAsync(sequence, Options() with { PerformanceMode = PlaybackPerformanceMode.FpsFriendly }, 0);
            Assert.Null(failure); Assert.Equal(PlaybackState.Completed, player.State); Assert.Equal(0, sender.HeldCount);
            Assert.Equal(player.Diagnostics.NoteOns, player.Diagnostics.NoteOffs); Assert.True(player.Diagnostics.SkippedNotes > 0);
        }
    }
    [Fact] public async Task TransportFailureStillReleasesKeysAndReportsError()
    {
        var sender = new ThrowingSender(); using var player = new PlaybackService(new KeyboardPlayer(new(), sender), () => true);
        Exception? failure = null; player.Failed += ex => failure = ex;
        await player.StartAsync(Sequence(Note(0, 60, 500)), Options(), 0);
        Assert.NotNull(failure); Assert.Equal(PlaybackState.Error, player.State); Assert.Equal(0, sender.Held);
    }
    [Fact] public async Task SessionOwnsSettingsSnapshotUntilNextStart()
    {
        var (player, sender) = Create(); using (player)
        {
            var settings = new NoteDurationSettings { Mode = NoteDurationMode.MidiSustain, ReleaseGapMs = 20 };
            var task = player.StartAsync(Sequence(Note(0, 60, 500), Note(200, 60, 500)), Options() with { NoteDuration = settings }, 0);
            await sender.First.Task.WaitAsync(TimeSpan.FromSeconds(3)); settings.ReleaseGapMs = 100; settings.Mode = NoteDurationMode.LegacyTap;
            await task; Assert.Equal(NoteDurationMode.MidiSustain, player.ActiveDurationMode);
            var calls = sender.Calls.Where(c => c.Key == 'T').ToArray(); Assert.InRange(calls[2].Time - calls[1].Time, 19.5, 80);
        }
    }
    private sealed class ThrowingSender : IKeySender
    {
        public int Held;
        public void KeyDown(ushort key, InputMode mode, bool focus, CancellationToken token) { Held++; throw new InvalidOperationException("Synthetic failure"); }
        public void KeyUp(ushort key) { Held = 0; }
        public void ReleaseAllKeys() { Held = 0; }
    }
}
