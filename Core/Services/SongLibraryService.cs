using System.Text.Json;
using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class SongLibraryService
{
    public string Root { get; }
    public string SongsFolder => Path.Combine(Root, "Songs");
    public bool MigratedFromV1 { get; }
    public string? MigrationMessage => MigratedFromV1 ? DataMigrationService.SuccessMessage : null;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly SemaphoreSlim writeGate = new(1, 1);
    public SongLibraryService(string? root = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (root == null) MigratedFromV1 = new DataMigrationService().MigrateIfNeeded(appData);
        Root = root ?? Path.Combine(appData, "PianoRoblox");
        Directory.CreateDirectory(SongsFolder);
    }
    public async Task<List<Song>> LoadAsync() => await ReadAsync<List<Song>>("songs.json") ?? [];
    public async Task<AppSettings> LoadSettingsAsync()
    {
        var settings = await ReadAsync<AppSettings>("settings.json") ?? new(); settings.Validate(); return settings;
    }
    public Task SaveSettingsAsync(AppSettings settings) { settings.Validate(); return WriteAsync("settings.json", settings); }
    public Task SaveAsync(IEnumerable<Song> songs) => WriteAsync("songs.json", songs.ToList());
    public string GetSongPath(Song song)
    {
        if (string.IsNullOrWhiteSpace(song.FileName) || Path.GetFileName(song.FileName) != song.FileName) throw new InvalidDataException("Caminho inválido na biblioteca.");
        return Path.Combine(SongsFolder, song.FileName);
    }
    private async Task<T?> ReadAsync<T>(string file)
    {
        string path = Path.Combine(Root, file);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Options) : default;
    }
    private async Task WriteAsync<T>(string file, T value)
    {
        await writeGate.WaitAsync();
        string path = Path.Combine(Root, file), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, Options));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            writeGate.Release();
        }
    }
}

