using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Utilities;

namespace PianoRoblox.ViewModels;

public sealed record QueueSongRow(Guid EntryId, Song Song, int Position = 0)
{
    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public string DisplayTitle => $"{Position}. {Title}";
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
    private string searchText = "", kindFilter = "Todos", sortOrder = "Ordem salva", playbackCollectionName = "";
    public ObservableCollection<Song> SelectedSongs { get; } = [];
    public string SearchText { get => searchText; set { if (Set(ref searchText, value)) RefreshSongs(); } }
    public string KindFilter { get => kindFilter; set { if (Set(ref kindFilter, value)) RefreshSongs(); } }
    public string SortOrder { get => sortOrder; set { if (Set(ref sortOrder, value)) RefreshSongs(); } }
    public string[] KindFilters { get; } = ["Todos", "MIDI", "Sheet", "JSON"];
    public string[] SortOrders { get; } = ["Ordem salva", "Título", "Artista", "Duração", "Data de adição"];
    public string CollectionMessage { get; private set; } = "";
    public string PlaybackCollectionName => playbackCollectionName;
    public string TrackLabel => Queue.TrackNumber > 0 ? $"{Queue.TrackNumber} de {Queue.TrackCount}" : "Fila manual";
    public string NextTitle => Queue.NextPreview is { } next ? Songs.FirstOrDefault(s => s.Id == next.SongId)?.Title ?? "—" : "Fim da lista";
    public string PlayAllLabel => SelectedPlaylist == null ? "▶ Reproduzir biblioteca inteira" : "▶ Reproduzir tudo";

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
            RefreshSongs(); Raise(nameof(CollectionTitle)); Raise(nameof(PlayAllLabel));
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
    public ICommand PlayAllPlaylistCommand { get; }
    public ICommand ShufflePlayPlaylistCommand { get; }
    public ICommand QueueAllPlaylistCommand { get; }
    public ICommand PlayFromHereCommand { get; }

    public LibraryViewModel(SongLibraryService library, ObservableCollection<Song> songs,
        Func<Song, Task> load, Func<Song, bool, Task> play, Action<Exception> onError, Action<string> log)
    {
        this.library = library; Songs = songs; this.load = load; this.play = play; this.log = log;
        playlistService = new(library.Root);
        playlistService.Warning += message => Message(message);
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
        PlayFromHereCommand = PlaySelectedCommand;
        PlayAllPlaylistCommand = Command(() => PlayAllAsync(false));
        ShufflePlayPlaylistCommand = Command(() => PlayAllAsync(true));
        QueueAllPlaylistCommand = Command(QueueAllAsync);
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
        playbackCollectionName = CollectionTitle;
        if (SelectedPlaylist is { } playlist) Queue.StartPlaylist(playlist.SongIds, song.Id);
        else Queue.StartPlaylist(OrderedCollection().Select(s => s.Id), song.Id);
    }
    public async Task PlayAllAsync(bool shufflePlay = false)
    {
        await navigationGate.WaitAsync();
        try
        {
            var songs = AvailableCollection();
            if (songs.Count == 0) return;
            playbackCollectionName = CollectionTitle; Queue.Shuffle = shufflePlay;
            var first = Queue.StartPlaylist(songs.Select(s => s.Id), shufflePlay ? songs[Random.Shared.Next(songs.Count)].Id : null);
            var song = songs.First(s => s.Id == first!.SongId);
            SelectedSong = song; await play(song, false);
        }
        finally { navigationGate.Release(); }
    }
    private Task QueueAllAsync()
    {
        var songs = AvailableCollection();
        foreach (var song in songs) Queue.Enqueue(song.Id);
        if (songs.Count > 0) Message($"{songs.Count} músicas adicionadas à fila.");
        return Task.CompletedTask;
    }
    private List<Song> AvailableCollection()
    {
        var songs = OrderedCollection().ToList();
        var available = songs.Where(s => IsAvailable(s.Id)).ToList();
        int missing = songs.Count - available.Count + (SelectedPlaylist?.SongIds.Count(id => Songs.All(s => s.Id != id)) ?? 0);
        Message(available.Count == 0 ? "Esta coleção não possui músicas disponíveis. Importe músicas ou adicione-as à playlist."
            : missing > 0 ? $"{missing} arquivos ausentes foram ignorados." : $"{available.Count} músicas disponíveis.");
        return available;
    }
    private void Message(string message) { CollectionMessage = message; Raise(nameof(CollectionMessage)); log(message); }
    private Song RequireSong() => SelectedSong ?? throw new InvalidOperationException("Selecione uma música na biblioteca.");
    private Playlist RequirePlaylist() => SelectedPlaylist ?? throw new InvalidOperationException("Selecione uma playlist.");
    private string RequireName() => string.IsNullOrWhiteSpace(PlaylistName)
        ? throw new InvalidOperationException("Digite um nome para a playlist.") : PlaylistName.Trim();
    private bool IsAvailable(Guid id)
    {
        var song = Songs.FirstOrDefault(s => s.Id == id);
        if (song == null) return false;
        try
        {
            bool exists = File.Exists(library.GetSongPath(song));
            if (!exists && Queue.Current != null) MissingFileNotice(song);
            return exists;
        }
        catch (InvalidDataException) { return false; }
    }
    private readonly HashSet<Guid> missingWarnings = [];
    private void MissingFileNotice(Song song)
    { if (missingWarnings.Add(song.Id)) log($"Arquivo ausente ignorado: {song.Title}."); }
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
        var playlist = TargetPlaylist ?? SelectedPlaylist ?? throw new InvalidOperationException("Escolha a playlist de destino.");
        var selection = SelectedSongs.Count > 0 ? SelectedSongs.ToArray() : new[] { RequireSong() };
        var updated = Copy(playlist);
        foreach (var song in selection) if (!updated.SongIds.Contains(song.Id)) updated.SongIds.Add(song.Id);
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
        IEnumerable<Song> visible = OrderedCollection().Where(s =>
            (KindFilter == "Todos" || s.Kind == KindFilter) &&
            (string.IsNullOrWhiteSpace(SearchText) || s.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
                s.Artist.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)));
        foreach (var song in visible) VisibleSongs.Add(song);
        if (oldSong != null && VisibleSongs.Contains(oldSong)) SelectedSong = oldSong;
        else SelectedSong = null;
    }
    private IEnumerable<Song> OrderedCollection()
    {
        var byId = Songs.GroupBy(song => song.Id).ToDictionary(group => group.Key, group => group.First());
        IEnumerable<Song> songs = SelectedPlaylist is { } playlist
            ? playlist.SongIds.Where(byId.ContainsKey).Select(id => byId[id]) : Songs;
        return SortOrder switch
        {
            "Título" => songs.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase),
            "Artista" => songs.OrderBy(s => s.Artist, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Title),
            "Duração" => songs.OrderBy(s => s.DurationMs), "Data de adição" => songs.OrderByDescending(s => s.AddedAt), _ => songs
        };
    }
    private void RefreshQueue()
    {
        var selectedId = SelectedQueueItem?.EntryId;
        Upcoming.Clear();
        foreach (var item in Queue.Upcoming)
        {
            var song = Songs.FirstOrDefault(s => s.Id == item.SongId);
            if (song != null) Upcoming.Add(new(item.Id, song, Upcoming.Count + 1));
        }
        SelectedQueueItem = Upcoming.FirstOrDefault(item => item.EntryId == selectedId);
        Raise(nameof(CurrentSong)); Raise(nameof(CurrentTitle)); Raise(nameof(HasContext));
        Raise(nameof(Shuffle)); Raise(nameof(RepeatMode)); Raise(nameof(RepeatLabel));
        Raise(nameof(PlaybackCollectionName)); Raise(nameof(TrackLabel)); Raise(nameof(NextTitle));
    }
}
