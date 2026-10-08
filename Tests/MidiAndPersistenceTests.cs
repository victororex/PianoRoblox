using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using System.IO;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class MidiAndPersistenceTests
{
    [Fact] public void MidiRespectsTempoChangesChordsAndZeroVelocity()
    {
        var file = new MidiFile(new TrackChunk(
            new SetTempoEvent(500000),
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100),
            new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100),
            new NoteOnEvent((SevenBitNumber)67, (SevenBitNumber)0),
            new SetTempoEvent(1000000) { DeltaTime = 480 },
            new NoteOnEvent((SevenBitNumber)62, (SevenBitNumber)100),
            new NoteOnEvent((SevenBitNumber)65, (SevenBitNumber)100) { DeltaTime = 480 },
            new NoteOffEvent((SevenBitNumber)65, (SevenBitNumber)0) { DeltaTime = 480 }))
        { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        var sequence = new MidiService().Convert(file, "Tempo changes");
        Assert.Equal(120, sequence.InitialBpm, 5); Assert.Equal(4, sequence.NoteCount);
        Assert.Equal(1, sequence.ChordCount);
        Assert.Equal(new double[] { 0, 500, 1500 }, sequence.Events.Select(e => e.TimeMs));
        Assert.Equal(2500, sequence.DurationMs);
    }
    [Fact] public void MidiWithOnlyNoteOffIsRejected()
    {
        var file = new MidiFile(new TrackChunk(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)0)));
        Assert.Throws<InvalidDataException>(() => new MidiService().Convert(file, "empty"));
    }
    [Fact] public async Task JsonRoundTripAndInvalidKey()
    {
        string root = Path.Combine(Path.GetTempPath(), "PianoRobloxTests", Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        var json = new SequenceJsonService(new()); string path = Path.Combine(root, "song.json");
        var original = new MusicSequence("Teste", 63, 3000, [new(0, [60, 64, 67]), new(1000, [61])]);
        await json.ExportAsync(path, original); var loaded = await json.ImportAsync(path);
        Assert.Equal(original.Title, loaded.Title); Assert.Equal(63, loaded.InitialBpm); Assert.Equal(3000, loaded.DurationMs);
        Assert.Equal(original.Events[1].Notes, loaded.Events[1].Notes);
        await File.WriteAllTextAsync(path, "{\"title\":\"x\",\"events\":[{\"timeMs\":0,\"keys\":[\"#\"]}]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => json.ImportAsync(path));
    }
    [Fact] public async Task LibrarySavesSettingsAndRejectsPathsOutsideSongs()
    {
        var root = Path.Combine(Path.GetTempPath(), "PianoRobloxTests", Guid.NewGuid().ToString());
        var library = new SongLibraryService(root);
        var song = new Song { Title = "Teste", FileName = "test.mid", Transpose = 12, Speed = 110, Artist = "Artista" };
        await library.SaveAsync([song]); Assert.Equal(song.Title, (await library.LoadAsync()).Single().Title);
        await library.SaveSettingsAsync(new() { ChordSpreadMs = 90, DefaultSpeed = 110 });
        Assert.Equal(50, (await library.LoadSettingsAsync()).ChordSpreadMs);
        Assert.Throws<InvalidDataException>(() => library.GetSongPath(new Song { FileName = "../outside.mid" }));
    }
}

