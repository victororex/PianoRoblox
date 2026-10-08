using System.ComponentModel;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Services;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class InputV2Tests
{
    private sealed class Transport : IKeyboardInputTransport
    {
        public List<KeyboardInput[]> Calls { get; } = [];
        public Queue<uint> Results { get; } = new();
        public int LastError => 5;
        public ushort MapScanCode(ushort key) => (ushort)(key + 1);
        public uint Send(KeyboardInput[] inputs) { Calls.Add(inputs); return Results.Count > 0 ? Results.Dequeue() : (uint)inputs.Length; }
    }
    [Theory] [InlineData(InputMode.ScanCode, 0, 8)] [InlineData(InputMode.VirtualKey, 84, 0)]
    public void BuilderKeepsModesAndKeyUpFlags(InputMode mode, int vk, int flags)
    {
        var packets = KeyboardInputBuilder.BuildBatch([new(84), new(84, true)], mode, key => (ushort)(key + 1));
        Assert.Equal(vk, packets[0].VirtualKey); Assert.Equal(85, packets[0].ScanCode);
        Assert.Equal((uint)flags, packets[0].Flags); Assert.Equal((uint)(flags | 2), packets[1].Flags);
    }
    [Fact] public async Task NaturalChordUsesTwoNativeBatchesAndReleasesModifiers()
    {
        var transport = new Transport(); var service = new KeyboardService(() => true, transport);
        await new KeyboardPlayer(new(), service).PlayChord([60, 64, 67], new(0, 1, ChordMode.Auto, InputMode.VirtualKey, true), CancellationToken.None);
        Assert.Equal(2, transport.Calls.Count); Assert.Equal(3, transport.Calls[0].Length);
        Assert.All(transport.Calls[1], packet => Assert.Equal(2u, packet.Flags));
        Assert.Contains(transport.Calls[1], packet => packet.VirtualKey == 0xA0);
        Assert.Contains(transport.Calls[1], packet => packet.VirtualKey == 0xA2);
        Assert.Contains(transport.Calls[1], packet => packet.VirtualKey == 0xA4);
    }
    [Fact] public void PartialInjectionReleasesWholeIntendedBatchAndRetainsUncertainKeys()
    {
        var transport = new Transport(); transport.Results.Enqueue(1); transport.Results.Enqueue(0);
        var sender = new KeyboardService(() => true, transport);
        Assert.Throws<Win32Exception>(() => sender.SendBatch([new(84), new(85)], InputMode.VirtualKey, true, CancellationToken.None));
        Assert.All(transport.Calls[1], packet => Assert.Equal(2u, packet.Flags));
        sender.ReleaseAllKeys(); Assert.Contains(transport.Calls[2], packet => packet.VirtualKey == 84);
        Assert.Contains(transport.Calls[2], packet => packet.VirtualKey == 85);
    }
    [Fact] public void LostFocusOrCancellationNeverSendsKeyDown()
    {
        var transport = new Transport(); var sender = new KeyboardService(() => false, transport);
        Assert.Throws<FocusLostException>(() => sender.KeyDown(84, InputMode.ScanCode, true, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => sender.KeyDown(84, InputMode.ScanCode, false, new CancellationToken(true)));
        Assert.All(transport.Calls.SelectMany(call => call), packet => Assert.True((packet.Flags & 2) != 0));
    }
    [Fact] public async Task GroupSafeCombinesShiftedNotesAndEndsWithOnlyKeyUps()
    {
        var transport = new Transport(); var sender = new KeyboardService(() => true, transport);
        await new KeyboardPlayer(new(), sender).PlayChord([61, 63], new(0, 1, ChordMode.Auto, InputMode.VirtualKey, true), CancellationToken.None);
        Assert.Contains(transport.Calls, call => call.Length == 2 && call.All(packet => packet.Flags == 0));
        Assert.All(transport.Calls[^1], packet => Assert.Equal(2u, packet.Flags));
    }
    [Fact] public void LatePolicyPreservesRecentEventsSkipsObsoleteAndBoundsCatchUp()
    {
        var policy = new LateEventPolicy(new(0, 1, ChordMode.Auto, InputMode.ScanCode, true));
        Assert.True(policy.ShouldPlay(new(0, [60]), 35, 0));
        Assert.False(policy.ShouldPlay(new(0, [60]), 90, 1));
        Assert.True(policy.ShouldPlay(new(0, [60, 64, 67, 72]) { Priority = .95 }, 90, 2));
        Assert.True(policy.ShouldPlay(new(0, [60, 64, 67, 72]) { Priority = .95 }, 90, 3));
        Assert.False(policy.ShouldPlay(new(0, [60]) { Priority = .95 }, 90, 4));
        Assert.False(policy.ShouldPlay(new(0, [60]) { Priority = 1 }, 161, 200));
    }
    [Fact] public void FpsFriendlyLimitsRealDispatchEvenWithOnTimeEvents()
    {
        var policy = new LateEventPolicy(new(0, 1, ChordMode.Auto, InputMode.ScanCode, true, PlaybackPerformanceMode.FpsFriendly));
        for (int i = 0; i < 4; i++) Assert.True(policy.ShouldPlay(new(i * 100, Enumerable.Range(60, 8).ToArray()), i * 100, i * 100));
        Assert.False(policy.ShouldPlay(new(400, [60]), 400, 400));
        Assert.True(policy.ShouldPlay(new(1000, [60]), 1000, 1000));
    }
    private sealed class SlowPlayer : IKeyboardPlayer
    {
        public int Calls, Releases;
        public async Task PlayChord(IReadOnlyList<int> notes, PlaybackOptions options, CancellationToken token)
        { Calls++; if (Calls == 1) await Task.Delay(200, token); }
        public void ReleaseAllKeys() => Releases++;
    }
    [Fact] public async Task PlaybackDropsBacklogAndCompletesOnlyAfterRelease()
    {
        var fake = new SlowPlayer(); using var player = new PlaybackService(fake, () => true);
        await player.StartAsync(new("Backlog", 200, 260, Enumerable.Range(0, 20).Select(i => new MusicEvent(i * 5, [60])).ToList()),
            new(0, 1, ChordMode.Auto, InputMode.ScanCode, true), 0);
        Assert.True(player.SkippedEventCount > 0); Assert.True(fake.Calls < 20);
        Assert.Equal(PlaybackState.Completed, player.State); Assert.True(fake.Releases > 0);
    }
}
