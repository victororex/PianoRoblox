using PianoRoblox.Core.Models;
using PianoRoblox.ViewModels;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class PresentationV2Tests
{
    [Fact] public void TimelineReusesRowsAndKeepsOnlySmallVisibleWindow()
    {
        var sequence = new MusicSequence("Long", 200, 100000, Enumerable.Range(0, 10000).Select(i => new MusicEvent(i * 10, [60])).ToList());
        var vm = new TimelineViewModel(new()); vm.SetSequence(sequence); vm.Update(0, false);
        Assert.DoesNotContain(vm.Rows, row => row.Current);
        vm.Update(0, true); Assert.True(vm.Rows[0].Current);
        vm.Update(100); var sameRow = vm.Rows.Single(row => row.Index == 102);
        vm.SetSequence(sequence); vm.Update(101);
        Assert.Same(sameRow, vm.Rows.Single(row => row.Current)); Assert.Equal(16, vm.Rows.Count);
        vm.Update(9999); Assert.Equal(6, vm.Rows.Count);
    }
    [Fact] public void VisualBufferCoalescesBurstsIntoOneLatestEvent()
    {
        var buffer = new PlaybackVisualBuffer();
        for (int i = 0; i < 10000; i++) buffer.Capture(i, new(i, [60]));
        Assert.True(buffer.TryTake(out int index, out var value)); Assert.Equal(9999, index); Assert.Equal(9999, value!.TimeMs);
        Assert.False(buffer.TryTake(out _, out _)); buffer.Capture(1, new(1, [60])); buffer.Clear(); Assert.False(buffer.TryTake(out _, out _));
    }
}
