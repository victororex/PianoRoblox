namespace PianoRoblox.Core.Models;

public sealed record QueueItem(Guid Id, Guid SongId, bool FromPlaylist = false);

public enum RepeatMode { Off, Playlist, One }
