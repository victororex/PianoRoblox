using System.Diagnostics;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Services;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class PlaybackTests
{
    private static readonly PlaybackOptions Options = new(8, 2, ChordMode.Auto, InputMode.ScanCode, true);
    private sealed class FakePlayer : IKeyboardPlayer
    {
        public List<(int Note, double Time)> Played { get; } = [];
        public Stopwatch Watch { get; } = Stopwatch.StartNew();
        public int Releases;
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PlayChord(IReadOnlyList<int> notes, PlaybackOptions options, CancellationToken token)
        {
            lock (Played) Played.Add((notes[0], Watch.Elapsed.TotalMilliseconds));
            First.TrySetResult(); await Task.Delay(5, token);
        }
        public void ReleaseAllKeys() => Interlocked.Increment(ref Releases);
    }
    [Fact] public async Task PauseFreezesTimelineAndResumePlaysRemainingEvents()
    {
        var fake = new FakePlayer(); using var player = new PlaybackService(fake, () => true);
        var task = player.StartAsync(new("test", 120, 420, [new(0, [60]), new(300, [62])]), Options, 0);
        await fake.First.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(30); player.Pause();
        double atPause = player.PositionMs; await Task.Delay(100);
        Assert.InRange(Math.Abs(player.PositionMs - atPause), 0, 1); Assert.Equal(PlaybackState.Paused, player.State);
        player.Resume(); await task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Contains(fake.Played, n => n.Note == 62); Assert.Equal(PlaybackState.Completed, player.State); Assert.True(fake.Releases > 0);
    }
    [Fact] public async Task FocusGuardSendsNothingAndStopCancelsCountdown()
    {
        var fake = new FakePlayer(); using var player = new PlaybackService(fake, () => false);
        var sequence = new MusicSequence("test", 120, 500, [new(0, [60])]);
        var task = player.StartAsync(sequence, Options, 0); await Task.Delay(100);
        Assert.Empty(fake.Played); Assert.Equal(PlaybackState.Paused, player.State); await player.StopAsync(); await task;
        task = player.StartAsync(sequence, Options, 3); await Task.Delay(30);
        var watch = Stopwatch.StartNew(); await player.StopAsync(); await task;
        Assert.True(watch.ElapsedMilliseconds < 500); Assert.Empty(fake.Played);
        Assert.Equal(0, player.PositionMs);
    }
    [Fact] public async Task SeekSkipsEarlierNoteOns()
    {
        var fake = new FakePlayer(); using var player = new PlaybackService(fake, () => true);
        await player.StartAsync(new("seek", 120, 220, [new(0, [60]), new(100, [62]), new(200, [64])]), Options, 0, 100);
        Assert.Equal(new[] { 62, 64 }, fake.Played.Select(p => p.Note));
    }
    [Fact] public async Task AbsoluteTimingDoesNotAccumulateKeyboardDelay()
    {
        var fake = new FakePlayer(); using var player = new PlaybackService(fake, () => true);
        await player.StartAsync(new("timing", 120, 650, Enumerable.Range(0, 7).Select(i => new MusicEvent(i * 100, [60])).ToList()), Options, 0);
        Assert.Equal(7, fake.Played.Count);
        Assert.InRange(fake.Played[^1].Time - fake.Played[0].Time, 540, 720);
    }
    [Fact] public async Task LiveSpeedChangeDoesNotJumpTimeline()
    {
        var clock = new PlaybackClock(); clock.Start(0); await Task.Delay(40);
        double before = clock.PositionMs; clock.Speed = 150; double after = clock.PositionMs;
        Assert.InRange(after - before, 0, 20); clock.Pause(); before = clock.PositionMs;
        clock.Speed = 50; await Task.Delay(30); Assert.Equal(before, clock.PositionMs, 4);
    }
    private sealed class RecordingSender : IKeySender
    {
        public List<string> Calls { get; } = [];
        public HashSet<ushort> Held { get; } = [];
        public void KeyDown(ushort key, InputMode mode, bool requireFocus, CancellationToken token) { token.ThrowIfCancellationRequested(); Calls.Add($"down:{key}"); Held.Add(key); }
        public void KeyUp(ushort key) { Calls.Add($"up:{key}"); Held.Remove(key); }
        public void ReleaseAllKeys() { Calls.Add("release"); Held.Clear(); }
    }
    [Fact] public async Task ShiftIsReleasedBeforeNextNaturalNote()
    {
        var sender = new RecordingSender(); var player = new KeyboardPlayer(new(), sender);
        await player.PlayChord([61, 60], Options, CancellationToken.None);
        Assert.Equal(new[] { "down:160", "down:84", "up:84", "up:160", "down:84", "up:84", "release" }, sender.Calls);
        Assert.Empty(sender.Held);
    }
    [Fact] public async Task CancellationDuringHoldReleasesKeys()
    {
        var sender = new RecordingSender(); var player = new KeyboardPlayer(new(), sender);
        using var cts = new CancellationTokenSource();
        var task = player.PlayChord([60, 64, 67], Options with { HoldMs = 100 }, cts.Token); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.Empty(sender.Held);
    }
}

