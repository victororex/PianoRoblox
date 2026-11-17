using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.ViewModels;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class DataV3Tests
{
    [Fact] public async Task MidiLibraryTitleIsSeparateFromGuidFileNameAndSurvivesConcurrentOptimize()
    {
        string root = LibraryV2Tests.TempFolder(); string path = Path.Combine(root, Guid.NewGuid() + ".mid");
        OptimizationV2Tests.ComplexMidi().Write(path);
        var vm = new MidiImportViewModel(ex => throw ex);
        await vm.LoadAsync(path, displayTitle: "Treachery - Bleach");
        Assert.Equal("Treachery - Bleach", vm.Data!.Title); Assert.Contains("Treachery - Bleach", vm.MetadataSummary);
        Assert.Equal("Treachery - Bleach", vm.Sequence!.Title);
        vm.Preset = OptimizationPreset.Balanced; vm.SetDisplayTitle("Novo título"); vm.Preset = OptimizationPreset.Performance;
        await vm.EnsureReadyAsync();
        Assert.Equal("Novo título", vm.Data.Title); Assert.Equal("Novo título", vm.Sequence!.Title);
        Assert.DoesNotContain(Path.GetFileNameWithoutExtension(path), vm.MetadataSummary);
        await vm.LoadAsync(path); Assert.Equal(Path.GetFileNameWithoutExtension(path), vm.Data.Title);
    }
    [Fact] public async Task OlderMidiReadCannotReplaceNewSongOrItsTitle()
    {
        string path = Path.Combine(LibraryV2Tests.TempFolder(), "source.mid"); OptimizationV2Tests.ComplexMidi().Write(path);
        var vm = new MidiImportViewModel(ex => throw ex);
        var old = vm.LoadAsync(path, displayTitle: "Anterior");
        var current = vm.LoadAsync(path, displayTitle: "Atual");
        try { await old; } catch (OperationCanceledException) { }
        await current; await vm.EnsureReadyAsync();
        Assert.Equal("Atual", vm.Sequence!.Title); Assert.Equal("Atual", vm.Data!.Title);
    }
    [Fact] public void OptimizationKeepsLatestOffOfDuplicatesAndCompensatesMerge()
    {
        var notes = new[] { OptimizationV2Tests.Note(1000, 60, velocity: 127) with { DurationMs = 500 },
            OptimizationV2Tests.Note(1003, 60, 1, 70) with { DurationMs = 1500 },
            OptimizationV2Tests.Note(1002, 64) with { DurationMs = 800 } };
        var import = new MidiImportResult("x", 120, 3000, notes, [], new MidiComplexityAnalyzer().Analyze(notes, 3000));
        var result = new MidiOptimizationService().Optimize(import, OptimizationSettings.ForPreset(OptimizationPreset.Balanced));
        var e = result.Sequence.Events.Single();
        Assert.Equal(1000, e.TimeMs); Assert.Equal(1503, e.NoteDetails.Single(n => n.MidiNote == 60).DurationMs);
        Assert.Equal(802, e.NoteDetails.Single(n => n.MidiNote == 64).DurationMs);
        Assert.Equal(127, e.NoteDetails.Single(n => n.MidiNote == 60).Velocity);
        Assert.True(result.After.PeakTransitionsPerSecond >= result.After.PeakNotesPerSecond);
    }
    [Fact] public async Task JsonV2AndV3RoundTripOptionalPerNoteDuration()
    {
        var json = new SequenceJsonService(new()); string path = Path.Combine(LibraryV2Tests.TempFolder(), "sequence.json");
        await File.WriteAllTextAsync(path, """{"title":"V2","events":[{"timeMs":0,"keys":["t","u"]}]}""");
        var legacy = await json.ImportAsync(path); Assert.False(legacy.HasExactDurations); Assert.Equal(2, legacy.NoteCount);
        await File.WriteAllTextAsync(path, """{"title":"V3","events":[{"timeMs":1000,"notes":[{"midi":60,"durationMs":1500},{"midi":64,"durationMs":500}]}]}""");
        var original = await json.ImportAsync(path); Assert.Equal(2500, original.DurationMs);
        var transformed = SequenceTransformer.Apply(original, 12, RangePolicy.Ignore).Sequence;
        await json.ExportAsync(path, transformed); var loaded = await json.ImportAsync(path);
        Assert.Equal(new[] { 72, 76 }, loaded.Events.Single().Notes);
        Assert.Equal(new double?[] { 1500, 500 }, loaded.Events.Single().NoteDetails.Select(n => n.DurationMs));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal("s", document.RootElement.GetProperty("events")[0].GetProperty("keys")[0].GetString());
        Assert.Equal(original.InitialBpm, loaded.InitialBpm);
    }
    [Theory] [InlineData(-1)] [InlineData(-3000)]
    public async Task InvalidJsonDurationIsRejected(double duration)
    {
        string path = Path.Combine(LibraryV2Tests.TempFolder(), "bad.json");
        await File.WriteAllTextAsync(path, "{\"events\":[{\"timeMs\":0,\"notes\":[{\"midi\":60,\"durationMs\":" + duration + "}]}]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SequenceJsonService(new()).ImportAsync(path));
    }
    [Fact] public void OctaveFoldingKeepsLongestDurationWhenPitchesCollide()
    {
        var e = new MusicEvent(0, [24, 36, 108, 96]) { NoteDetails = [new(24, 1500), new(36, 400), new(108, 500), new(96, 1000)] };
        var result = SequenceTransformer.Apply(new("fold", 120, 1500, [e]), 0, RangePolicy.Octave);
        Assert.Equal(new[] { 36, 96 }, result.Sequence.Events.Single().Notes);
        Assert.Equal(new double?[] { 1500, 1000 }, result.Sequence.Events.Single().NoteDetails.Select(n => n.DurationMs));
    }
    [Fact] public void TransposeRecommendationMaximizesCoverageWithoutMutatingSource()
    {
        int[] pitches = [20, 24, 30, 60, 70, 75];
        var result = PianoCompatibilityAnalyzer.Analyze(pitches);
        Assert.Equal(6, result.Total); Assert.Equal(3, result.Below); Assert.Equal(50, result.Percent);
        int best = Enumerable.Range(-24, 49).Max(t => pitches.Count(p => p + t is >= 36 and <= 96));
        Assert.Equal(best, result.SuggestedInside); Assert.Equal(20, pitches[0]);
    }
    [Theory] [InlineData("songs.json")] [InlineData("playlists.json")] [InlineData("settings.json")]
    public async Task CorruptIndexesUseValidBackupAndPreserveOriginalOnRecoveryWrite(string name)
    {
        string root = LibraryV2Tests.TempFolder(); var store = new JsonDataStore(root); var warnings = new List<string>(); store.Warning += warnings.Add;
        if (name == "settings.json") await store.WriteAsync(name, new AppSettings { DefaultSpeed = 115 });
        else await store.WriteAsync(name, new[] { "Keep me" });
        File.Copy(Path.Combine(root, name), Path.Combine(root, name + ".bak"));
        await File.WriteAllTextAsync(Path.Combine(root, name), "{corrupt");
        if (name == "settings.json")
        { Assert.Equal(115, (await store.ReadAsync<AppSettings>(name))!.DefaultSpeed); await store.WriteAsync(name, new AppSettings { DefaultSpeed = 120 }); }
        else
        { Assert.Equal("Keep me", (await store.ReadAsync<string[]>(name))!.Single()); await store.WriteAsync(name, new[] { "Keep me", "New" }); }
        Assert.NotEmpty(warnings); Assert.Equal("{corrupt", File.ReadAllText(Directory.GetFiles(root, name + ".corrupt-*").Single()));
        Assert.DoesNotContain("corrupt", await File.ReadAllTextAsync(Path.Combine(root, name + ".bak")));
    }
    [Fact] public async Task UnrecoverableOrNullLibraryCannotBeOverwrittenWithEmptyList()
    {
        var library = new SongLibraryService(LibraryV2Tests.TempFolder()); string path = Path.Combine(library.Root, "songs.json");
        foreach (string corrupt in new[] { "{broken", "null", "[null]" })
        {
            await File.WriteAllTextAsync(path, corrupt);
            await Assert.ThrowsAsync<InvalidDataException>(() => library.LoadAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveAsync([]));
            Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
        }
    }
    [Fact] public async Task FirstV3WriteBacksUpExistingIndexesAndKeepsGuidFiles()
    {
        string root = LibraryV2Tests.TempFolder(); var library = new SongLibraryService(root);
        var song = new Song { Title = "V2", FileName = Guid.NewGuid() + ".mid" };
        string contents = JsonSerializer.Serialize(new[] { song });
        await File.WriteAllTextAsync(Path.Combine(root, "songs.json"), contents);
        await File.WriteAllBytesAsync(library.GetSongPath(song), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "{\"DefaultSpeed\":110}");
        var loaded = (await library.LoadAsync()).Single(); Assert.Equal(NoteDurationMode.LegacyTap, loaded.NoteDuration.Mode);
        loaded.NoteDuration = new() { Mode = NoteDurationMode.Hybrid, ReleaseGapMs = 4, MaximumHoldMs = 9000 };
        await library.SaveAsync([loaded]);
        Assert.Equal(contents, File.ReadAllText(Path.Combine(root, "Backups", "before-v3", "songs.json")));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(library.GetSongPath(song)));
        loaded = (await library.LoadAsync()).Single(); Assert.Equal(4, loaded.NoteDuration.ReleaseGapMs); Assert.Equal(9000, loaded.NoteDuration.MaximumHoldMs);
    }
    [Fact] public async Task MissingMainIndexWithInvalidBackupAlsoBlocksReset()
    {
        var library = new SongLibraryService(LibraryV2Tests.TempFolder());
        string backup = Path.Combine(library.Root, "songs.json.bak"); await File.WriteAllTextAsync(backup, "{broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => library.LoadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveAsync([]));
        Assert.False(File.Exists(Path.Combine(library.Root, "songs.json"))); Assert.Equal("{broken", await File.ReadAllTextAsync(backup));
    }
    [Fact] public async Task PlayAllStartsWithoutSongSelectionUsesSavedOrderAndSkipsMissing()
    {
        var library = new SongLibraryService(LibraryV2Tests.TempFolder()); var songs = new ObservableCollection<Song>();
        foreach (string title in new[] { "A", "B", "C", "Missing" })
        { var song = new Song { Title = title, FileName = title + ".txt" }; songs.Add(song); if (title != "Missing") await File.WriteAllTextAsync(library.GetSongPath(song), "t"); }
        var played = new List<string>(); var logs = new List<string>();
        var vm = new LibraryViewModel(library, songs, _ => Task.CompletedTask, (s, _) => { played.Add(s.Title); return Task.CompletedTask; }, ex => throw ex, logs.Add);
        vm.SelectedPlaylist = new() { Name = "Bleach", SongIds = songs.Select(s => s.Id).ToList() };
        Assert.Null(vm.SelectedSong); await vm.PlayAllAsync(); await vm.AdvanceAsync(true); Assert.Equal("2 de 3", vm.TrackLabel);
        await vm.AdvanceAsync(true); Assert.False(await vm.AdvanceAsync(true));
        Assert.Equal(new[] { "A", "B", "C" }, played); Assert.Contains(logs, l => l.Contains("ausentes"));
    }
    [Fact] public async Task EmptyPlaylistAndAllMissingFilesGiveFriendlyMessage()
    {
        var library = new SongLibraryService(LibraryV2Tests.TempFolder()); var songs = new ObservableCollection<Song> { new() { FileName = "missing.mid" } };
        int plays = 0; var vm = new LibraryViewModel(library, songs, _ => Task.CompletedTask, (_, _) => { plays++; return Task.CompletedTask; }, ex => throw ex, _ => { });
        vm.SelectedPlaylist = new() { SongIds = [] }; await vm.PlayAllAsync();
        Assert.Contains("não possui", vm.CollectionMessage); Assert.Equal(0, plays);
        vm.SelectedPlaylist = null; await vm.PlayAllAsync(); Assert.Equal(0, plays);
        vm.Queue.RepeatMode = RepeatMode.Playlist; Assert.Null(vm.Queue.Next());
    }
    [Fact] public async Task LibraryFilteringSortingMultiSelectAndQueueAllWorkTogether()
    {
        var library = new SongLibraryService(LibraryV2Tests.TempFolder());
        var songs = new ObservableCollection<Song> { new() { Title = "Zulu", Artist = "Bleach", Kind = "MIDI", FileName = "z.mid" },
            new() { Title = "Alpha", Artist = "Other", Kind = "Sheet", FileName = "a.txt" } };
        foreach (var song in songs) await File.WriteAllTextAsync(library.GetSongPath(song), "x");
        var vm = new LibraryViewModel(library, songs, _ => Task.CompletedTask, (_, _) => Task.CompletedTask, ex => throw ex, _ => { });
        vm.SearchText = "bleach"; Assert.Equal("Zulu", vm.VisibleSongs.Single().Title);
        vm.KindFilter = "Sheet"; Assert.Empty(vm.VisibleSongs); vm.SearchText = "";
        Assert.Equal("Alpha", vm.VisibleSongs.Single().Title); vm.KindFilter = "Todos"; vm.SortOrder = "Título";
        Assert.Equal(new[] { "Alpha", "Zulu" }, vm.VisibleSongs.Select(s => s.Title));
        vm.QueueAllPlaylistCommand.Execute(null); while (!vm.QueueAllPlaylistCommand.CanExecute(null)) await Task.Delay(5);
        Assert.Equal(new[] { "Alpha", "Zulu" }, vm.Upcoming.Select(s => s.Title));
        await vm.PlayAllAsync(); Assert.Equal("Alpha", vm.CurrentTitle); Assert.Equal("Todas as músicas", vm.PlaybackCollectionName);
        vm.TargetPlaylist = new() { Name = "Multiple" }; vm.Playlists.Add(vm.TargetPlaylist);
        foreach (var song in songs) vm.SelectedSongs.Add(song);
        vm.AddToPlaylistCommand.Execute(null); while (!vm.AddToPlaylistCommand.CanExecute(null)) await Task.Delay(5);
        Assert.Equal(2, vm.Playlists.Single().SongIds.Count);
    }
    [Fact] public void ShuffleCycleAndNextPreviewStayConsistentAcrossRepeat()
    {
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var queue = new PlaybackQueueService(randomSeed: 3) { Shuffle = true, RepeatMode = RepeatMode.Playlist };
        queue.StartPlaylist(ids); var seen = new HashSet<Guid> { queue.Current!.SongId };
        for (int i = 1; i < 10; i++) { var preview = queue.NextPreview; Assert.Equal(preview!.SongId, queue.Next()!.SongId); Assert.True(seen.Add(queue.Current!.SongId)); }
        var last = queue.Current!.SongId; var next = queue.NextPreview;
        Assert.NotEqual(last, next!.SongId); Assert.Equal(next.SongId, queue.Next()!.SongId);
    }
    [Fact] public void RealMidisKeepRetainedNoteEndsAndProvideCoverageReport()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PianoRoblox.sln"))) directory = directory.Parent;
        string folder = Path.Combine(directory?.FullName ?? "", "musicas pianoblox");
        if (!Directory.Exists(folder)) return; // Optional user-owned fixtures are never downloaded or copied into tests.
        var paths = Directory.GetFiles(folder, "*.mid").Where(p => new[] { "Never meant", "Treachery", "Black Rover" }.Any(n => Path.GetFileName(p).Contains(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        var lines = new List<string>();
        foreach (string path in paths)
        {
            var data = new MidiService().ReadDetailed(path);
            var result = new MidiOptimizationService().Optimize(data, OptimizationSettings.ForPreset(OptimizationPreset.Balanced));
            foreach (var note in result.Notes.Where(n => n.DurationMs > 0))
            {
                double end = data.Notes.Where(n => !n.IsPercussion && n.Velocity >= 15 && n.MidiNote == note.MidiNote && n.TimeMs >= note.TimeMs && n.TimeMs <= note.TimeMs + 6)
                    .Max(n => n.TimeMs + n.DurationMs);
                Assert.Equal(end, note.TimeMs + note.DurationMs, 5);
            }
            var transformed = SequenceTransformer.Apply(result.Sequence, 0, RangePolicy.Ignore).Sequence;
            var plan = NoteSchedule.Build(transformed, new(8, 8, ChordMode.Auto, InputMode.VirtualKey, true, NoteDuration: new() { Mode = NoteDurationMode.Hybrid }), new PianoMappingService().FromMidi);
            Assert.Equal(plan.Notes.Count * 2, plan.Transitions.Count);
            var coverage = PianoCompatibilityAnalyzer.Analyze(data.Notes.Select(n => n.MidiNote));
            lines.Add($"{data.Title}: {data.Notes.Count} notas, {data.DurationMs:0} ms; Balanced {result.After.NoteCount}; cobertura {coverage.Percent:0.0}%; sugestão {coverage.SuggestedTranspose}; conflitos físicos {plan.PhysicalConflicts}; durações limitadas {plan.LimitedNotes}.");
        }
        string output = Environment.GetEnvironmentVariable("PIANOROBLOX_UI_TEST_OUTPUT") ?? LibraryV2Tests.TempFolder();
        Directory.CreateDirectory(output); File.WriteAllLines(Path.Combine(output, "real-midi-v3.txt"), lines);
        Assert.True(paths.Length >= 3, "As três músicas disponíveis devem ser analisadas neste workspace.");
    }
}
