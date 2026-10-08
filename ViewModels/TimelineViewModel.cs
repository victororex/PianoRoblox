using System.Collections.ObjectModel;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Utilities;

namespace PianoRoblox.ViewModels;

public sealed class TimelineRow(int index, MusicEvent musicEvent, PianoMappingService mapping) : ObservableObject
{
    public int Index { get; } = index + 1;
    public string Time { get; } = TimeSpan.FromMilliseconds(musicEvent.TimeMs).ToString(@"mm\:ss\.fff");
    public string Keys { get; } = string.Join(' ', musicEvent.Notes.Select(n => mapping.FromMidi(n)?.KeyboardKey.ToString() ?? "—"));
    public string Notes { get; } = string.Join(' ', musicEvent.Notes.Select(PianoMappingService.Name));
    private bool current;
    public bool Current { get => current; set => Set(ref current, value); }
}

public sealed class TimelineViewModel(PianoMappingService mapping)
{
    private MusicSequence? sequence;
    private int lastIndex = int.MinValue;
    private bool lastHighlight;
    public ObservableCollection<TimelineRow> Rows { get; } = [];
    public void SetSequence(MusicSequence? value)
    {
        if (ReferenceEquals(sequence, value)) return;
        sequence = value; lastIndex = int.MinValue; Rows.Clear();
    }
    public void Update(int index, bool highlight = true)
    {
        if (sequence == null || (index == lastIndex && highlight == lastHighlight)) return;
        lastIndex = index; lastHighlight = highlight;
        int first = Math.Max(0, index - 5), last = Math.Min(sequence.Events.Count - 1, index + 10);
        // Retain existing rows and only slide the small visible window.
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (Rows[i].Index - 1 < first || Rows[i].Index - 1 > last) Rows.RemoveAt(i);
        for (int i = first; i <= last; i++)
        {
            var row = Rows.FirstOrDefault(r => r.Index == i + 1);
            if (row == null)
            {
                row = new(i, sequence.Events[i], mapping);
                int position = 0; while (position < Rows.Count && Rows[position].Index < row.Index) position++;
                Rows.Insert(position, row);
            }
            row.Current = highlight && i == index;
        }
    }
}

/// <summary>Playback only writes one slot. WPF consumes the newest event at 25 FPS.</summary>
public sealed class PlaybackVisualBuffer
{
    private readonly object sync = new();
    private MusicEvent? latest;
    private int latestIndex;
    public void Capture(int index, MusicEvent value) { lock (sync) { latestIndex = index; latest = value; } }
    public bool TryTake(out int index, out MusicEvent? value)
    {
        lock (sync) { index = latestIndex; value = latest; latest = null; return value != null; }
    }
    public void Clear() { lock (sync) latest = null; }
}
