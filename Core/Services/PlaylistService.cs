using System.Text.Json;
using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class PlaylistService(string root)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string FilePath => Path.Combine(root, "playlists.json");

    public async Task<List<Playlist>> LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!File.Exists(FilePath)) return [];
            List<Playlist> result;
            try { result = JsonSerializer.Deserialize<List<Playlist>>(await File.ReadAllTextAsync(FilePath), Options) ?? []; }
            catch (JsonException) when (File.Exists(FilePath + ".bak"))
            { result = JsonSerializer.Deserialize<List<Playlist>>(await File.ReadAllTextAsync(FilePath + ".bak"), Options) ?? []; }
            foreach (var playlist in result)
            {
                playlist.SongIds ??= [];
                playlist.Name = string.IsNullOrWhiteSpace(playlist.Name) ? "Playlist" : playlist.Name.Trim();
            }
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(IEnumerable<Playlist> playlists)
    {
        // Serialize before awaiting, so later UI edits cannot mutate this particular save.
        string contents = JsonSerializer.Serialize(playlists.ToList(), Options);
        await gate.WaitAsync();
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(temp, contents);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            gate.Release();
        }
    }
}
