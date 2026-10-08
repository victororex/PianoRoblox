using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

/// <summary>Playback order only: never modifies a playlist or sends keyboard input.</summary>
public sealed class PlaybackQueueService
{
    private readonly Random random;
    private readonly Func<Guid, bool> available;
    private readonly List<Guid> playlist = [];
    private readonly List<QueueItem> pending = [];
    private readonly List<QueueItem> queued = [];
    private readonly List<QueueItem> history = [];
    private int historyIndex = -1;
    private bool shuffle;
    private RepeatMode repeatMode;

    public PlaybackQueueService(Func<Guid, bool>? isAvailable = null, int? randomSeed = null)
    {
        available = isAvailable ?? (_ => true);
        random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
    }

    public event Action? Changed;
    public QueueItem? Current => historyIndex >= 0 && historyIndex < history.Count ? history[historyIndex] : null;
    public bool HasContext => Current != null || queued.Count > 0 || pending.Count > 0;
    public IReadOnlyList<QueueItem> Upcoming => history.Skip(historyIndex + 1).Concat(queued).Concat(pending)
        .Where(item => available(item.SongId)).ToList();
    public IReadOnlyList<QueueItem> History => history.Take(historyIndex + 1).ToList();
    public RepeatMode RepeatMode
    {
        get => repeatMode;
        set { if (repeatMode == value) return; repeatMode = value; Changed?.Invoke(); }
    }
    public bool Shuffle
    {
        get => shuffle;
        set
        {
            if (shuffle == value) return;
            shuffle = value;
            if (shuffle) ShuffleInPlace(pending);
            else pending.Sort((a, b) => playlist.IndexOf(a.SongId).CompareTo(playlist.IndexOf(b.SongId)));
            Changed?.Invoke();
        }
    }

    public QueueItem? StartPlaylist(IEnumerable<Guid> songIds, Guid? startSongId = null)
    {
        playlist.Clear(); playlist.AddRange(songIds.Where(available).Distinct());
        pending.Clear(); history.Clear(); historyIndex = -1;
        if (playlist.Count == 0) { Changed?.Invoke(); return null; }
        int start = startSongId.HasValue ? playlist.IndexOf(startSongId.Value) : 0;
        if (start < 0) start = 0;
        var current = NewItem(playlist[start], true);
        pending.AddRange((shuffle ? playlist.Where(id => id != current.SongId) : playlist.Skip(start + 1)).Select(id => NewItem(id, true)));
        if (shuffle) ShuffleInPlace(pending);
        Visit(current);
        return current;
    }

    public QueueItem? StartSong(Guid songId)
    {
        if (!available(songId)) return null;
        playlist.Clear(); pending.Clear(); history.Clear(); historyIndex = -1;
        var item = NewItem(songId, false); Visit(item); return item;
    }

    public QueueItem Enqueue(Guid songId)
    {
        var item = NewItem(songId, false);
        queued.Add(item); Changed?.Invoke(); return item;
    }

    public bool Remove(Guid entryId)
    {
        bool removed = queued.RemoveAll(item => item.Id == entryId) > 0;
        removed |= pending.RemoveAll(item => item.Id == entryId) > 0;
        // An item revisited through Previous may also be displayed in the forward history.
        for (int i = history.Count - 1; i > historyIndex; i--)
            if (history[i].Id == entryId) { history.RemoveAt(i); removed = true; }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public void ClearUpcoming()
    {
        queued.Clear(); pending.Clear(); playlist.Clear();
        if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
        Changed?.Invoke();
    }

    public void Clear()
    {
        queued.Clear(); pending.Clear(); playlist.Clear(); history.Clear(); historyIndex = -1;
        Changed?.Invoke();
    }

    public QueueItem? Next(bool automatic = true)
    {
        if (automatic && RepeatMode == RepeatMode.One && Current is { } repeating && available(repeating.SongId))
            return repeating;
        while (historyIndex + 1 < history.Count)
        {
            historyIndex++;
            if (available(history[historyIndex].SongId)) { Changed?.Invoke(); return history[historyIndex]; }
        }
        var next = TakeAvailable(queued) ?? TakeAvailable(pending);
        if (next == null && RepeatMode == RepeatMode.Playlist)
        {
            pending.AddRange(playlist.Where(available).Select(id => NewItem(id, true)));
            if (shuffle)
            {
                ShuffleInPlace(pending);
                // Across cycle boundaries, also avoid immediately repeating the last song when possible.
                if (pending.Count > 1 && pending[0].SongId == Current?.SongId)
                    (pending[0], pending[1]) = (pending[1], pending[0]);
            }
            next = TakeAvailable(pending);
        }
        if (next == null) { Changed?.Invoke(); return null; }
        Visit(next); return next;
    }

    public QueueItem? Previous()
    {
        for (int i = historyIndex - 1; i >= 0; i--)
        {
            if (!available(history[i].SongId)) continue;
            historyIndex = i; Changed?.Invoke(); return history[i];
        }
        return Current is { } current && available(current.SongId) ? current : null;
    }

    public void RefreshAvailability() => Changed?.Invoke();
    private static QueueItem NewItem(Guid songId, bool fromPlaylist) => new(Guid.NewGuid(), songId, fromPlaylist);
    private QueueItem? TakeAvailable(List<QueueItem> items)
    {
        while (items.Count > 0)
        {
            var item = items[0]; items.RemoveAt(0);
            if (available(item.SongId)) return item;
        }
        return null;
    }
    private void Visit(QueueItem item)
    {
        history.Add(item); historyIndex = history.Count - 1; Changed?.Invoke();
    }
    private void ShuffleInPlace(List<QueueItem> items)
    {
        // One Fisher-Yates permutation per order/cycle, never a random draw for each Next.
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
