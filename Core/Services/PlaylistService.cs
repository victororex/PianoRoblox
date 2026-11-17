using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class PlaylistService
{
    private readonly JsonDataStore store;
    public string FilePath { get; }
    public event Action<string>? Warning;
    public PlaylistService(string root)
    { FilePath = Path.Combine(root, "playlists.json"); store = new(root); store.Warning += message => Warning?.Invoke(message); }
    public async Task<List<Playlist>> LoadAsync()
    {
        var result = await store.ReadAsync<List<Playlist>>("playlists.json") ?? [];
        if (result.Any(p => p == null)) throw new InvalidDataException("Playlist inválida. Arquivo preservado.");
        foreach (var playlist in result)
        { playlist.SongIds ??= []; playlist.Name = string.IsNullOrWhiteSpace(playlist.Name) ? "Playlist" : playlist.Name.Trim(); }
        return result;
    }
    public Task SaveAsync(IEnumerable<Playlist> playlists) => store.WriteAsync("playlists.json", playlists.ToList());
}
