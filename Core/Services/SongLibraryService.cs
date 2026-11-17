using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class SongLibraryService
{
    public string Root { get; }
    public string SongsFolder => Path.Combine(Root, "Songs");
    public bool MigratedFromV1 { get; }
    public string? MigrationMessage => MigratedFromV1 ? DataMigrationService.SuccessMessage : null;
    private readonly JsonDataStore store;
    public event Action<string>? Warning;
    public SongLibraryService(string? root = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (root == null) MigratedFromV1 = new DataMigrationService().MigrateIfNeeded(appData);
        Root = root ?? Path.Combine(appData, "PianoRoblox");
        Directory.CreateDirectory(SongsFolder);
        store = new(Root); store.Warning += message => Warning?.Invoke(message);
    }
    public async Task<List<Song>> LoadAsync()
    {
        var songs = await store.ReadAsync<List<Song>>("songs.json") ?? [];
        if (songs.Any(s => s == null)) throw new InvalidDataException("Biblioteca contém entrada inválida. Arquivo preservado.");
        foreach (var song in songs)
        {
            song.Title ??= "Sem título"; song.Artist ??= ""; song.Origin ??= "";
            song.Optimization ??= new(); song.NoteDuration = (song.NoteDuration ?? new()).Validated();
        }
        return songs;
    }
    public async Task<AppSettings> LoadSettingsAsync()
    { var settings = await store.ReadAsync<AppSettings>("settings.json") ?? new(); settings.Validate(); return settings; }
    public Task SaveSettingsAsync(AppSettings settings) { settings.Validate(); return store.WriteAsync("settings.json", settings); }
    public Task SaveAsync(IEnumerable<Song> songs) => store.WriteAsync("songs.json", songs.ToList());
    public string GetSongPath(Song song)
    {
        if (string.IsNullOrWhiteSpace(song.FileName) || Path.GetFileName(song.FileName) != song.FileName)
            throw new InvalidDataException("Caminho inválido na biblioteca.");
        return Path.Combine(SongsFolder, song.FileName);
    }
}
