using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.ViewModels;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class LibraryV2Tests
{
    internal static string TempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "PianoRobloxTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    [Fact] public async Task MigrationCopiesOldDataOnlyOnceAndPreservesOriginal()
    {
        string appData = TempFolder(), old = Path.Combine(appData, "GakuranAutoPiano"), current = Path.Combine(appData, "PianoRoblox");
        Directory.CreateDirectory(Path.Combine(old, "Songs"));
        await File.WriteAllTextAsync(Path.Combine(old, "songs.json"), "[{\"Title\":\"V1\",\"FileName\":\"old.mid\"}]");
        await File.WriteAllTextAsync(Path.Combine(old, "settings.json"), "{\"DefaultSpeed\":110}");
        await File.WriteAllBytesAsync(Path.Combine(old, "Songs", "old.mid"), [1, 2, 3]);
        var migration = new DataMigrationService(); Assert.True(migration.MigrateIfNeeded(appData));
        Assert.Equal(File.ReadAllText(Path.Combine(old, "songs.json")), File.ReadAllText(Path.Combine(current, "songs.json")));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(current, "Songs", "old.mid")));
        var library = new SongLibraryService(current); var song = (await library.LoadAsync()).Single();
        Assert.Equal("V1", song.Title); Assert.Equal(OptimizationPreset.Auto, song.Optimization.Preset);
        Assert.Null(song.SelectedTracks); Assert.Equal(110, (await library.LoadSettingsAsync()).DefaultSpeed);
        await File.WriteAllTextAsync(Path.Combine(current, "songs.json"), "[]");
        Assert.False(migration.MigrateIfNeeded(appData)); Assert.Equal("[]", File.ReadAllText(Path.Combine(current, "songs.json")));
        Assert.Contains("V1", File.ReadAllText(Path.Combine(old, "songs.json")));
    }
    [Fact] public void MigrationSkipsMissingOldFolderAndExistingNewFolder()
    {
        string root = TempFolder(); var migration = new DataMigrationService();
        Assert.False(migration.MigrateIfNeeded(root));
        Directory.CreateDirectory(Path.Combine(root, "GakuranAutoPiano")); Directory.CreateDirectory(Path.Combine(root, "PianoRoblox"));
        Assert.False(migration.MigrateIfNeeded(root));
    }
    [Fact] public async Task PlaylistSerializationKeepsOrderAndBackup()
    {
        var service = new PlaylistService(TempFolder()); var playlist = new Playlist { Name = "Anime", SongIds = [Guid.NewGuid(), Guid.NewGuid()] };
        await service.SaveAsync([playlist]); var loaded = (await service.LoadAsync()).Single();
        Assert.Equal(playlist.Id, loaded.Id); Assert.Equal(playlist.SongIds, loaded.SongIds);
        loaded.Name = "Favoritas"; loaded.SongIds.Reverse(); await service.SaveAsync([loaded]);
        Assert.True(File.Exists(service.FilePath + ".bak"));
        Assert.Equal("Anime", JsonSerializer.Deserialize<List<Playlist>>(File.ReadAllText(service.FilePath + ".bak"))!.Single().Name);
        Assert.Equal(loaded.SongIds, (await service.LoadAsync()).Single().SongIds);
    }
    [Fact] public async Task SongOptimizationSettingsRoundTripAndV1HotkeysRemainUsable()
    {
        var library = new SongLibraryService(TempFolder());
        var song = new Song { FileName = "x.mid", Optimization = new() { Preset = OptimizationPreset.Custom, MaxChordNotes = 4, RemovePercussion = false },
            SelectedTracks = [1, 3], PerformanceMode = PlaybackPerformanceMode.FpsFriendly, Transpose = -12, Speed = 125 };
        await library.SaveAsync([song]); var loaded = (await library.LoadAsync()).Single();
        Assert.Equal(song.Optimization, loaded.Optimization); Assert.Equal(song.SelectedTracks, loaded.SelectedTracks);
        Assert.Equal(song.PerformanceMode, loaded.PerformanceMode); Assert.Equal(-12, loaded.Transpose);
        var settings = new AppSettings { Hotkeys = new() { ["Play"] = 0x73, ["Stop"] = 0x77 } }; settings.Validate();
        Assert.Equal(0x73, settings.Hotkeys["Play"]); Assert.False(settings.Hotkeys.ContainsKey("Previous"));
        Assert.Equal(0x74, settings.Hotkeys["Next"]); Assert.Equal(0, settings.CountdownBetweenSongs);
    }
    [Fact] public void ShuffleVisitsEverySongOnceAndPreviousUsesHistory()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        var queue = new PlaybackQueueService(randomSeed: 37) { Shuffle = true };
        queue.StartPlaylist(ids, ids[7]); var order = new List<Guid> { queue.Current!.SongId };
        while (queue.Next() is { } next) order.Add(next.SongId);
        Assert.Equal(20, order.Count); Assert.Equal(20, order.Distinct().Count()); Assert.Equal(ids[7], order[0]);
        Assert.NotEqual(ids.Skip(8).Concat(ids.Take(7)), order.Skip(1));
        Assert.Equal(order[^2], queue.Previous()!.SongId); Assert.Equal(order[^1], queue.Next(false)!.SongId);
    }
    [Fact] public void RepeatOneAffectsAutomaticAdvanceOnlyAndPlaylistStartsNewCycle()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(); var queue = new PlaybackQueueService();
        queue.StartPlaylist([a, b]); queue.RepeatMode = RepeatMode.One;
        Assert.Equal(a, queue.Next()!.SongId); Assert.Equal(b, queue.Next(false)!.SongId);
        queue.RepeatMode = RepeatMode.Playlist; Assert.Equal(a, queue.Next()!.SongId);
        queue.RepeatMode = RepeatMode.Off; Assert.Equal(b, queue.Next()!.SongId); Assert.Null(queue.Next());
    }
    [Fact] public void ExplicitQueueHasPriorityWithoutMutatingPlaylistAndEntriesCanBeRemoved()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), extra = Guid.NewGuid(); var ids = new List<Guid> { a, b };
        var queue = new PlaybackQueueService(); queue.StartPlaylist(ids);
        var first = queue.Enqueue(extra); var second = queue.Enqueue(extra);
        Assert.True(queue.Remove(first.Id)); Assert.Equal(second.Id, queue.Upcoming[0].Id);
        Assert.Equal(extra, queue.Next()!.SongId); Assert.Equal(b, queue.Next()!.SongId); Assert.Equal(new[] { a, b }, ids);
        queue.Enqueue(extra); queue.ClearUpcoming(); Assert.Empty(queue.Upcoming); Assert.Null(queue.Next());
    }
    [Fact] public void QueueSkipsMissingFilesAndShuffleDoesNotRepeatAtCycleBoundary()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), missing = Guid.NewGuid();
        var queue = new PlaybackQueueService(id => id != missing, 4) { Shuffle = true, RepeatMode = RepeatMode.Playlist };
        queue.StartPlaylist([a, missing, b]); queue.Enqueue(missing);
        Assert.Equal(b, queue.Next()!.SongId); Assert.Equal(a, queue.Next()!.SongId);
    }
    [Fact] public async Task PreviousRestartsAfterThreeSecondsAndNavigatesBeforeThen()
    {
        var library = new SongLibraryService(TempFolder()); var songs = new ObservableCollection<Song>();
        foreach (string name in new[] { "a", "b", "c" })
        { var song = new Song { Title = name, FileName = name + ".txt" }; songs.Add(song); await File.WriteAllTextAsync(library.GetSongPath(song), "t"); }
        var played = new List<Guid>();
        var vm = new LibraryViewModel(library, songs, _ => Task.CompletedTask,
            (song, _) => { played.Add(song.Id); return Task.CompletedTask; }, ex => throw ex, _ => { });
        vm.Queue.StartPlaylist(songs.Select(s => s.Id)); await vm.AdvanceAsync(false);
        await vm.PreviousAsync(3500); Assert.Equal(songs[1].Id, played[^1]);
        await vm.PreviousAsync(2000); Assert.Equal(songs[0].Id, played[^1]);
        await vm.AdvanceAsync(false); Assert.Equal(songs[1].Id, played[^1]);
        Assert.False(await vm.AdvanceAsync(true, () => false)); Assert.Equal(songs[1].Id, vm.Queue.Current!.SongId);
    }
    [Fact] public async Task PlaylistCrudAndReorderNeverDeleteLibrarySongs()
    {
        var library = new SongLibraryService(TempFolder());
        var songs = new ObservableCollection<Song> { new() { Title = "A" }, new() { Title = "B" } };
        var errors = new List<Exception>();
        var vm = new LibraryViewModel(library, songs, _ => Task.CompletedTask, (_, _) => Task.CompletedTask, errors.Add, _ => { });
        async Task Run(System.Windows.Input.ICommand command)
        {
            command.Execute(null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!command.CanExecute(null)) await Task.Delay(5, timeout.Token);
            Assert.Empty(errors);
        }
        vm.PlaylistName = "Anime"; await Run(vm.CreatePlaylistCommand);
        await Run(vm.ShowAllCommand); vm.SelectedSong = songs[0]; await Run(vm.AddToPlaylistCommand);
        vm.SelectedSong = songs[1]; await Run(vm.AddToPlaylistCommand);
        vm.SelectedPlaylist = vm.Playlists.Single(); vm.SelectedSong = songs[1]; await Run(vm.MoveSongUpCommand);
        Assert.Equal(new[] { songs[1].Id, songs[0].Id }, vm.SelectedPlaylist!.SongIds);
        await Run(vm.RemoveFromPlaylistCommand); Assert.Equal(new[] { songs[0].Id }, vm.SelectedPlaylist!.SongIds);
        vm.PlaylistName = "Favoritas"; await Run(vm.RenamePlaylistCommand); Assert.Equal("Favoritas", vm.SelectedPlaylist!.Name);
        await Run(vm.DeletePlaylistCommand);
        Assert.Empty(vm.Playlists); Assert.Equal(2, songs.Count); Assert.Empty(await new PlaylistService(library.Root).LoadAsync());
    }
}
