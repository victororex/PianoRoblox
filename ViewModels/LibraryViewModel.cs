using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Utilities;

namespace PianoRoblox.ViewModels;

public sealed record QueueSongRow(Guid EntryId, Song Song)
{
    public string Title => Song.Title;
    public string Artist => Song.Artist;
}

/// <summary>Library, playlist editing and queue navigation without owning playback or MIDI conversion.</summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly SongLibraryService library;
    private readonly PlaylistService playlistService;
    private readonly Func<Song, Task> load;
    private readonly Func<Song, bool, Task> play;
    private readonly Action<string> log;
    private readonly SemaphoreSlim navigationGate = new(1, 1);
    private readonly SemaphoreSlim editingGate = new(1, 1);
    private Playlist? selectedPlaylist, targetPlaylist;
    private Song? selectedSong;
    private QueueSongRow? selectedQueueItem;
    private string playlistName = "";

    public ObservableCollection<Song> Songs { get; }
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<Song> VisibleSongs { get; } = [];
    public ObservableCollection<QueueSongRow> Upcoming { get; } = [];
    public PlaybackQueueService Queue { get; }
    public Playlist? SelectedPlaylist
    {
        get => selectedPlaylist;
        set
        {
            if (!Set(ref selectedPlaylist, value)) return;
            PlaylistName = value?.Name ?? "";
            if (value != null) TargetPlaylist = value;
            RefreshSongs(); Raise(nameof(CollectionTitle));
        }
    }
    public Playlist? TargetPlaylist { get => targetPlaylist; set => Set(ref targetPlaylist, value); }
    public Song? SelectedSong { get => selectedSong; set => Set(ref selectedSong, value); }
    public QueueSongRow? SelectedQueueItem { get => selectedQueueItem; set => Set(ref selectedQueueItem, value); }
    public string PlaylistName { get => playlistName; set => Set(ref playlistName, value); }
    public string CollectionTitle => SelectedPlaylist?.Name ?? "Todas as músicas";
    public Song? CurrentSong => Queue.Current is { } item ? Songs.FirstOrDefault(song => song.Id == item.SongId) : null;
    public string CurrentTitle => CurrentSong?.Title ?? "Nenhuma música na fila";
    public bool HasContext => Queue.HasContext;
    public bool Shuffle { get => Queue.Shuffle; set => Queue.Shuffle = value; }
    public RepeatMode RepeatMode { get => Queue.RepeatMode; set => Queue.RepeatMode = value; }
    public string RepeatLabel => RepeatMode switch { RepeatMode.One => "Repeat: uma", RepeatMode.Playlist => "Repeat: playlist", _ => "Repeat: desligado" };
    public Array RepeatModes { get; } = Enum.GetValues<RepeatMode>();

    public ICommand ShowAllCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand RenamePlaylistCommand { get; }
    public ICommand DeletePlaylistCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand RemoveFromPlaylistCommand { get; }
    public ICommand MoveSongUpCommand { get; }
    public ICommand MoveSongDownCommand { get; }
    public ICommand LoadSelectedCommand { get; }
    public ICommand PlaySelectedCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand RemoveFromQueueCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand ToggleShuffleCommand { get; }
    public ICommand CycleRepeatCommand { get; }
    public ICommand NextCommand { get; }

    public LibraryViewModel(SongLibraryService library, ObservableCollection<Song> songs,
        Func<Song, Task> load, Func<Song, bool, Task> play, Action<Exception> onError, Action<string> log)
    {
        this.library = library; Songs = songs; this.load = load; this.play = play; this.log = log;
        playlistService = new(library.Root);
        Queue = new(IsAvailable);
        Queue.Changed += RefreshQueue;
        Songs.CollectionChanged += (_, _) => { RefreshSongs(); Queue.RefreshAvailability(); };
        ICommand Command(Func<Task> action) => new AsyncCommand(action, onError);
        ShowAllCommand = Command(() => { SelectedPlaylist = null; return Task.CompletedTask; });
        CreatePlaylistCommand = Command(() => EditAsync(CreatePlaylistAsync));
        RenamePlaylistCommand = Command(() => EditAsync(RenamePlaylistAsync));
        DeletePlaylistCommand = Command(() => EditAsync(DeletePlaylistAsync));
        AddToPlaylistCommand = Command(() => EditAsync(AddToPlaylistAsync));
        RemoveFromPlaylistCommand = Command(() => EditAsync(RemoveFromPlaylistAsync));
        MoveSongUpCommand = Command(() => EditAsync(() => MoveSongAsync(-1)));
        MoveSongDownCommand = Command(() => EditAsync(() => MoveSongAsync(1)));
        LoadSelectedCommand = Command(LoadSelectedAsync);
        PlaySelectedCommand = Command(StartSelectedAsync);
        AddToQueueCommand = Command(() => { var song = RequireSong(); EnsureFile(song); Queue.Enqueue(song.Id); return Task.CompletedTask; });
        RemoveFromQueueCommand = Command(() => { if (SelectedQueueItem is { } item) Queue.Remove(item.EntryId); return Task.CompletedTask; });
        ClearQueueCommand = Command(() => { Queue.ClearUpcoming(); return Task.CompletedTask; });
        ToggleShuffleCommand = Command(() => { Shuffle = !Shuffle; return Task.CompletedTask; });
        CycleRepeatCommand = Command(() => { RepeatMode = (RepeatMode)(((int)RepeatMode + 1) % 3); return Task.CompletedTask; });
        NextCommand = Command(async () => { await AdvanceAsync(false); });
        RefreshSongs();
    }

    public async Task InitializeAsync()
    {
        Playlists.Clear();
        foreach (var playlist in await playlistService.LoadAsync()) Playlists.Add(playlist);
        RefreshSongs(); RefreshQueue();
    }

    public async Task LoadSelectedAsync()
    {
        await navigationGate.WaitAsync();
        try { var song = RequireSong(); EnsureFile(song); SetSelectedContext(song); await load(song); }
        finally { navigationGate.Release(); }
    }

    public async Task StartSelectedAsync()
    {
        await navigationGate.WaitAsync();
        try { var song = RequireSong(); EnsureFile(song); SetSelectedContext(song); await play(song, false); }
        finally { navigationGate.Release(); }
    }

    public async Task<bool> AdvanceAsync(bool automatic, Func<bool>? canAdvance = null)
    {
        await navigationGate.WaitAsync();
        try
        {
            if (canAdvance != null && !canAdvance()) return false;
            var item = Queue.Next(automatic);
            if (item == null) return false;
            var song = Songs.FirstOrDefault(s => s.Id == item.SongId);
            if (song == null) return false;
            await play(song, true); return true;
        }
        finally { navigationGate.Release(); }
    }

    public async Task<bool> PreviousAsync(double positionMs)
    {
        await navigationGate.WaitAsync();
        try
        {
            var item = positionMs >= 3000 ? Queue.Current : Queue.Previous();
            var song = item == null ? null : Songs.FirstOrDefault(s => s.Id == item.SongId);
            if (song == null || !IsAvailable(song.Id)) return false;
            await play(song, true); return true;
        }
        finally { navigationGate.Release(); }
    }

    private void SetSelectedContext(Song song)
    {
        if (SelectedPlaylist is { } playlist) Queue.StartPlaylist(playlist.SongIds, song.Id);
        else Queue.StartSong(song.Id);
    }
    private Song RequireSong() => SelectedSong ?? throw new InvalidOperationException("Selecione uma música na biblioteca.");
    private Playlist RequirePlaylist() => SelectedPlaylist ?? throw new InvalidOperationException("Selecione uma playlist.");
    private string RequireName() => string.IsNullOrWhiteSpace(PlaylistName)
        ? throw new InvalidOperationException("Digite um nome para a playlist.") : PlaylistName.Trim();
    private bool IsAvailable(Guid id)
    {
        var song = Songs.FirstOrDefault(s => s.Id == id);
        if (song == null) return false;
        try { return File.Exists(library.GetSongPath(song)); }
        catch (InvalidDataException) { return false; }
    }
    private void EnsureFile(Song song)
    {
        if (!File.Exists(library.GetSongPath(song)))
            throw new FileNotFoundException($"O arquivo de '{song.Title}' não está mais na biblioteca. Importe a música novamente.");
    }
    private async Task EditAsync(Func<Task> action)
    {
        await editingGate.WaitAsync();
        try { await action(); }
        finally { editingGate.Release(); }
    }
    private async Task CreatePlaylistAsync()
    {
        var playlist = new Playlist { Name = RequireName() };
        await playlistService.SaveAsync(Playlists.Append(playlist));
        Playlists.Add(playlist); SelectedPlaylist = playlist;
        log($"Playlist criada: {playlist.Name}.");
    }
    private async Task RenamePlaylistAsync()
    {
        var playlist = RequirePlaylist(); var renamed = Copy(playlist); renamed.Name = RequireName();
        await SaveReplacementAsync(playlist, renamed);
    }
    private async Task DeletePlaylistAsync()
    {
        var playlist = RequirePlaylist();
        await playlistService.SaveAsync(Playlists.Where(p => p.Id != playlist.Id));
        Playlists.Remove(playlist); SelectedPlaylist = null;
        if (TargetPlaylist?.Id == playlist.Id) TargetPlaylist = null;
        log("Playlist excluída. As músicas continuam na biblioteca.");
    }
    private async Task AddToPlaylistAsync()
    {
        var song = RequireSong();
        var playlist = TargetPlaylist ?? SelectedPlaylist ?? throw new InvalidOperationException("Escolha a playlist de destino.");
        if (playlist.SongIds.Contains(song.Id)) return;
        var updated = Copy(playlist); updated.SongIds.Add(song.Id);
        await SaveReplacementAsync(playlist, updated);
    }
    private async Task RemoveFromPlaylistAsync()
    {
        var song = RequireSong(); var playlist = RequirePlaylist(); var updated = Copy(playlist);
        updated.SongIds.RemoveAll(id => id == song.Id);
        await SaveReplacementAsync(playlist, updated);
    }
    private async Task MoveSongAsync(int offset)
    {
        var song = RequireSong(); var playlist = RequirePlaylist(); var updated = Copy(playlist);
        int index = updated.SongIds.IndexOf(song.Id), target = index + offset;
        if (index < 0 || target < 0 || target >= updated.SongIds.Count) return;
        (updated.SongIds[index], updated.SongIds[target]) = (updated.SongIds[target], updated.SongIds[index]);
        await SaveReplacementAsync(playlist, updated); SelectedSong = song;
    }
    private async Task SaveReplacementAsync(Playlist oldPlaylist, Playlist updated)
    {
        updated.ModifiedAt = DateTimeOffset.Now;
        await playlistService.SaveAsync(Playlists.Select(p => p.Id == updated.Id ? updated : p));
        int index = Playlists.IndexOf(oldPlaylist);
        bool selected = SelectedPlaylist?.Id == updated.Id, target = TargetPlaylist?.Id == updated.Id;
        var oldSong = SelectedSong;
        if (index >= 0) Playlists[index] = updated;
        if (selected) SelectedPlaylist = updated;
        if (target) TargetPlaylist = updated;
        if (oldSong != null && VisibleSongs.Contains(oldSong)) SelectedSong = oldSong;
    }
    private static Playlist Copy(Playlist playlist) => new()
    {
        Id = playlist.Id, Name = playlist.Name, SongIds = [.. playlist.SongIds],
        CreatedAt = playlist.CreatedAt, ModifiedAt = playlist.ModifiedAt
    };
    private void RefreshSongs()
    {
        var oldSong = SelectedSong;
        VisibleSongs.Clear();
        var byId = Songs.GroupBy(song => song.Id).ToDictionary(group => group.Key, group => group.First());
        IEnumerable<Song> visible = SelectedPlaylist is { } playlist
            ? playlist.SongIds.Where(byId.ContainsKey).Select(id => byId[id]) : Songs;
        foreach (var song in visible) VisibleSongs.Add(song);
        if (oldSong != null && VisibleSongs.Contains(oldSong)) SelectedSong = oldSong;
        else SelectedSong = null;
    }
    private void RefreshQueue()
    {
        var selectedId = SelectedQueueItem?.EntryId;
        Upcoming.Clear();
        foreach (var item in Queue.Upcoming)
        {
            var song = Songs.FirstOrDefault(s => s.Id == item.SongId);
            if (song != null) Upcoming.Add(new(item.Id, song));
        }
        SelectedQueueItem = Upcoming.FirstOrDefault(item => item.EntryId == selectedId);
        Raise(nameof(CurrentSong)); Raise(nameof(CurrentTitle)); Raise(nameof(HasContext));
        Raise(nameof(Shuffle)); Raise(nameof(RepeatMode)); Raise(nameof(RepeatLabel));
    }
}
