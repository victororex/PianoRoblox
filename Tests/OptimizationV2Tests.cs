using System.IO;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class OptimizationV2Tests
{
    internal static MidiNoteEvent Note(double time, int pitch, int track = 0, int velocity = 100, bool drum = false) =>
        new(time, pitch, velocity, drum ? 9 : 0, track, $"Track {track}", 0, drum ? "Drum Kit" : "Piano", 80, drum);
    private static MidiImportResult Import(params MidiNoteEvent[] notes) => new("Test", 200, 3000, notes, [], new MidiComplexityAnalyzer().Analyze(notes, 3000));
    private static OptimizationResult Optimize(MidiNoteEvent[] notes, OptimizationSettings? settings = null, int[]? tracks = null) =>
        new MidiOptimizationService().Optimize(Import(notes), settings ?? OptimizationSettings.ForPreset(OptimizationPreset.Original), tracks);

    [Fact] public void OriginalFiltersPercussionAndTracksButKeepsQuietNotes()
    {
        var notes = new[] { Note(0, 60, velocity: 1), Note(0, 36, 1, drum: true), Note(200, 67, 2) };
        var result = Optimize(notes, tracks: [0, 1]);
        Assert.Equal(new[] { 60 }, result.Sequence.Events.Single().Notes); Assert.Equal(1, result.PercussionRemoved);
        Assert.Equal(0, result.WeakNotesRemoved);
        Assert.Equal(3, Optimize(notes, new() { Preset = OptimizationPreset.Original, RemovePercussion = false }).After.NoteCount);
        Assert.Empty(Optimize(notes, tracks: []).Sequence.Events);
    }
    [Fact] public void ExactDuplicatesCollapseAcrossTracksAndKeepProvenance()
    {
        var result = Optimize([Note(1000, 60, 1), Note(1000, 60, 2)]);
        Assert.Equal(1, result.After.NoteCount); Assert.Equal(1, result.DuplicatesRemoved);
        Assert.Equal(new[] { 1, 2 }, result.Sequence.Events.Single().SourceTracks);
    }
    [Fact] public void MergeIsAnchoredAndDoesNotChainIntoLaterRhythm()
    {
        var result = Optimize([Note(1000, 60), Note(1002, 64), Note(1004, 67), Note(1008, 72)], new() { Preset = OptimizationPreset.Balanced });
        Assert.Equal(new double[] { 1000, 1008 }, result.Sequence.Events.Select(e => e.TimeMs));
        Assert.Equal(new[] { 60, 64, 67 }, result.Sequence.Events[0].Notes);
        Assert.Equal(200, result.Sequence.InitialBpm); Assert.Equal(3000, result.Sequence.DurationMs);
    }
    [Fact] public void MergeDeduplicatesNearbyPitchButOriginalKeepsIt()
    {
        var notes = new[] { Note(1000, 60), Note(1003, 60) };
        Assert.Equal(2, Optimize(notes).After.NoteCount);
        Assert.Equal(1, Optimize(notes, new() { Preset = OptimizationPreset.Balanced }).After.NoteCount);
    }
    [Fact] public void ChordSelectionPreservesBassMelodyAndStrongHarmony()
    {
        var result = Optimize(Enumerable.Range(0, 10).Select(i => Note(0, 36 + i * 4, velocity: i == 5 ? 127 : 40)).ToArray(),
            new() { Preset = OptimizationPreset.Custom, MaxChordNotes = 4 });
        var chord = result.Sequence.Events.Single().Notes;
        Assert.Equal(4, chord.Length); Assert.Contains(36, chord); Assert.Contains(72, chord); Assert.Contains(56, chord);
    }
    [Fact] public void DensityCanReplaceEarlierWeakAccompanimentWithLaterMelody()
    {
        var result = Optimize([Note(0, 40, velocity: 30), Note(100, 80, velocity: 127)],
            new() { Preset = OptimizationPreset.Custom, MaxNotesPer250Ms = 1, MaxNotesPerSecond = 1 });
        Assert.Equal(80, result.Sequence.Events.Single().Notes.Single()); Assert.Equal(100, result.Sequence.Events.Single().TimeMs);
        Assert.Equal(1, result.DensityNotesRemoved);
    }
    [Theory] [InlineData(OptimizationPreset.Balanced, 6, 10, 32)] [InlineData(OptimizationPreset.Performance, 4, 7, 22)]
    public void DenseMidiRespectsEverySlidingWindowWithoutChangingBpm(OptimizationPreset preset, int chord, int shortLimit, int longLimit)
    {
        var data = new MidiService().Analyze(ComplexMidi(), "Synthetic complex");
        var service = new MidiOptimizationService(); var result = service.Optimize(data, new() { Preset = preset });
        Assert.Equal(200, result.Sequence.InitialBpm, 5); Assert.Equal(data.DurationMs, result.Sequence.DurationMs);
        Assert.True(result.After.NoteCount < result.Before.NoteCount); Assert.True(result.After.EventCount <= result.Before.EventCount);
        Assert.True(result.After.MaxChordNotes <= chord);
        var all = result.Notes;
        foreach (var note in all)
        {
            Assert.True(all.Count(n => n.TimeMs >= note.TimeMs && n.TimeMs < note.TimeMs + 250) <= shortLimit);
            Assert.True(all.Count(n => n.TimeMs >= note.TimeMs && n.TimeMs < note.TimeMs + 1000) <= longLimit);
            Assert.Contains(data.Notes, original => original.TimeMs == note.TimeMs && original.MidiNote == note.MidiNote);
        }
        Assert.Equal(result.Sequence.Events.SelectMany(e => e.Notes), service.Optimize(data, new() { Preset = preset }).Sequence.Events.SelectMany(e => e.Notes));
    }
    [Fact] public void AutoProtectsSimpleMusicAndChoosesDensePreset()
    {
        var simple = Optimize([Note(0, 60), Note(1000, 62, velocity: 1)], new());
        Assert.Equal(OptimizationPreset.Original, simple.EffectivePreset); Assert.Equal(2, simple.After.NoteCount);
        var medium = new MidiAnalysis(8, 1, 1, 0, 8, 8, 45, 1, 8);
        Assert.Equal(OptimizationPreset.Balanced, MidiComplexityAnalyzer.Recommend(medium));
        var dense = new MidiService().Analyze(ComplexMidi(), "Complex");
        Assert.Equal(OptimizationPreset.Performance, new MidiOptimizationService().Optimize(dense, new()).EffectivePreset);
    }
    [Fact] public void TrackChannelProgramVelocityAndDurationSurviveImport()
    {
        var midi = new MidiFile(new TrackChunk(new SetTempoEvent(300000), new SequenceTrackNameEvent("Piano"),
            new ProgramChangeEvent((SevenBitNumber)4), new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)73),
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480 }),
            new TrackChunk(new SequenceTrackNameEvent("Drums"), new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)99) { Channel = (FourBitNumber)9 }))
            { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        var data = new MidiService().Analyze(midi, "Metadata"); var note = data.Notes.First(n => !n.IsPercussion);
        Assert.Equal(4, note.ProgramNumber); Assert.Equal(73, note.Velocity); Assert.Equal(300, note.DurationMs);
        Assert.Equal("Piano", note.TrackName); Assert.False(string.IsNullOrEmpty(note.InstrumentName));
        Assert.True(data.Tracks[1].IsPercussion); Assert.Equal(new[] { 9 }, data.Tracks[1].Channels);
    }
    [Fact] public void OverlappingSamePitchInDifferentTracksKeepsCorrectDurations()
    {
        TrackChunk Track(int ticks) => new(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100),
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = ticks });
        var data = new MidiService().Analyze(new MidiFile(Track(480), Track(240)) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) }, "Overlap");
        Assert.Equal(500, data.Notes.Single(n => n.TrackIndex == 0).DurationMs);
        Assert.Equal(250, data.Notes.Single(n => n.TrackIndex == 1).DurationMs);
    }
    [Fact] public void AnalyzerUsesHalfOpenWindowsAndBenchmarkUsesActualOutput()
    {
        var data = Import(Note(0, 60), Note(250, 62), Note(1000, 64));
        Assert.Equal(1, data.Analysis.PeakNotesPer250Ms); Assert.Equal(2, data.Analysis.PeakNotesPerSecond);
        var rows = new MidiOptimizationService().Benchmark(data, new());
        Assert.Equal(3, rows.Count); Assert.All(rows, row => { Assert.Equal(3, row.Notes); Assert.Equal(6, row.EstimatedSendInputCalls); });
    }
    [Fact] public void OptimizationCancellationIsObserved()
    {
        var data = new MidiService().Analyze(ComplexMidi(), "Cancel");
        Assert.ThrowsAny<OperationCanceledException>(() => new MidiOptimizationService().Optimize(data, new(), cancellationToken: new CancellationToken(true)));
    }

    internal static MidiFile ComplexMidi()
    {
        var tracks = new List<TrackChunk> { new(new SetTempoEvent(300000), new SequenceTrackNameEvent("Tempo 200 BPM")) };
        for (int track = 0; track < 6; track++)
        {
            int channel = track == 5 ? 9 : track;
            var events = new List<MidiEvent> { new SequenceTrackNameEvent(track == 5 ? "Drums" : $"Instrument {track + 1}"),
                new ProgramChangeEvent((SevenBitNumber)(track * 8)) { Channel = (FourBitNumber)channel } };
            for (int step = 0; step < 64; step++)
            {
                int[] pitches = track == 5 ? [36, 42, 46] : [36 + track * 3, 60 + track * 2 + step % 4, 80 + track + step % 3];
                for (int n = 0; n < pitches.Length; n++) events.Add(new NoteOnEvent((SevenBitNumber)pitches[n], (SevenBitNumber)(40 + (step + n * 17) % 85))
                { Channel = (FourBitNumber)channel, DeltaTime = n == 0 && step > 0 ? 60 : 0 });
                for (int n = 0; n < pitches.Length; n++) events.Add(new NoteOffEvent((SevenBitNumber)pitches[n], (SevenBitNumber)0)
                { Channel = (FourBitNumber)channel, DeltaTime = n == 0 ? 60 : 0 });
            }
            tracks.Add(new TrackChunk(events));
        }
        return new MidiFile(tracks) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
    }
}
