using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class MappingAndSheetTests
{
    private readonly PianoMappingService mapping = new();
    [Theory]
    [InlineData(36, '1')][InlineData(37, '!')][InlineData(60, 't')][InlineData(61, 'T')][InlineData(72, 's')][InlineData(96, 'm')]
    public void ExactMapping(int midi, char key) => Assert.Equal(key, mapping.FromMidi(midi)!.KeyboardKey);
    [Fact] public void All61NotesAreUniqueAndReversible()
    {
        Assert.Equal(61, mapping.All.Count);
        Assert.Equal(61, mapping.All.Select(n => n.KeyboardKey).Distinct().Count());
        foreach (var note in mapping.All) Assert.Equal(note, mapping.FromKey(note.KeyboardKey));
        Assert.Null(mapping.FromMidi(35)); Assert.Null(mapping.FromMidi(97));
        Assert.Null(mapping.FromKey('#')); Assert.Null(mapping.FromKey('&')); Assert.Null(mapping.FromKey(')'));
        Assert.True(mapping.FromMidi(61)!.RequiresShift); Assert.False(mapping.FromMidi(60)!.RequiresShift);
        Assert.Equal((ushort)'T', mapping.FromMidi(61)!.VirtualKey);
    }
    [Fact] public void RequestedSheetHasFourChordsAndThreeDoublePauses()
    {
        var result = new SheetParser(mapping).Parse("[6rtu]--[rtu]--[etu]--[etu]", 120);
        Assert.Empty(result.Errors); Assert.Equal(4, result.Sequence.ChordCount); Assert.Equal(13, result.Sequence.NoteCount);
        Assert.Equal(new double[] { 0, 500, 1000, 1500 }, result.Sequence.Events.Select(e => e.TimeMs));
        Assert.Equal(new[] { 45, 59, 60, 64 }, result.Sequence.Events[0].Notes);
    }
    [Fact] public void SpacesAndRapidSequenceHaveExplicitEstimatedTiming()
    {
        var parser = new SheetParser(mapping);
        Assert.Equal(new double[] { 0, 125, 250, 375 }, parser.Parse("k l x z").Sequence.Events.Select(e => e.TimeMs));
        Assert.Equal(new double[] { 0, 31.25, 62.5 }, parser.Parse("{tuo}").Sequence.Events.Select(e => e.TimeMs));
        Assert.Equal(new double[] { 0, 125, 250 }, parser.Parse("tuo").Sequence.Events.Select(e => e.TimeMs));
        Assert.Equal(new double[] { 0, 125 }, parser.Parse("t\r\nu").Sequence.Events.Select(e => e.TimeMs));
    }
    [Theory][InlineData("[abc")][InlineData("[]")][InlineData("t?u")][InlineData("[a[b]]")][InlineData("#&)")]
    public void InvalidSheetsProduceDiagnostics(string text) => Assert.NotEmpty(new SheetParser(mapping).Parse(text).Errors);
    [Fact] public void EmptySheetIsEmpty() => Assert.Empty(new SheetParser(mapping).Parse("").Sequence.Events);
    [Fact] public void NonFiniteBpmRejected() => Assert.Throws<ArgumentException>(() => new SheetParser(mapping).Parse("t", double.NaN));
    [Fact] public void RequestedThreeChordsMatch()
    {
        int[][] notes = [[60, 64, 67], [62, 65, 69], [64, 67, 71]];
        Assert.Equal(new[] { "tuo", "yip", "uoa" }, notes.Select(chord => new string(chord.Select(n => mapping.FromMidi(n)!.KeyboardKey).ToArray())));
    }
    [Fact] public void TranspositionAndRangePolicies()
    {
        var source = new MusicSequence("Range", 120, 100, [new(0, [33, 60, 96, 108])]);
        var ignored = SequenceTransformer.Apply(source, 0, RangePolicy.Ignore);
        Assert.Equal(2, ignored.Ignored); Assert.Equal(new[] { 60, 96 }, ignored.Sequence.Events[0].Notes);
        var folded = SequenceTransformer.Apply(source, 0, RangePolicy.Octave);
        Assert.Equal(new[] { 45, 60, 96 }, folded.Sequence.Events[0].Notes); Assert.Equal(2, folded.Folded);
        Assert.Throws<InvalidOperationException>(() => SequenceTransformer.Apply(source, 0, RangePolicy.Ask));
        var transposed = SequenceTransformer.Apply(new("x", 120, 100, [new(0, [60, 96])]), 12, RangePolicy.Ignore);
        Assert.Equal(new[] { 72 }, transposed.Sequence.Events[0].Notes); Assert.Equal(1, transposed.Ignored);
        Assert.Equal(60, source.Events[0].Notes[1]); // Original remains reusable.
    }
    [Theory][InlineData(100, 1000)][InlineData(50, 2000)][InlineData(150, 666.6666667)][InlineData(110, 909.0909091)]
    public void TimingAdjustment(double speed, double expected) => Assert.Equal(expected, SequenceTransformer.AdjustTime(1000, speed), 5);
}

