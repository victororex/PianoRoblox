using System.Diagnostics;
using System.IO;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;

namespace PianoRoblox.Services;

public sealed class KeyboardPlayer(PianoMappingService mapping, IKeySender sender) : INoteKeyboardPlayer
{
    private const ushort Shift = 0xA0;
    private readonly object noteSync = new();
    private readonly Dictionary<long, PianoNote> active = new();
    private readonly HashSet<ushort> legacyHeld = [];
    private long noteOns, noteOffs, conflicts;
    public PianoNote? ResolveNote(int midi) => mapping.FromMidi(midi);
    public NoteInputStatistics InputStatistics { get { lock (noteSync) return new(noteOns, noteOffs, conflicts, active.Count + legacyHeld.Count); } }
    public IReadOnlyList<int> ActiveMidiNotes { get { lock (noteSync) return active.Values.Select(n => n.MidiNote).Distinct().ToArray(); } }
    public void ResetStatistics() { lock (noteSync) { noteOns = noteOffs = conflicts = 0; } }

    public void NoteOn(IReadOnlyList<NoteAttack> notes, PlaybackOptions options, CancellationToken token)
    {
        lock (noteSync)
        {
            try
            {
                var batch = new List<KeyInput>();
                foreach (var attack in notes)
                {
                    token.ThrowIfCancellationRequested();
                    var key = ResolveNote(attack.Note.MidiNote) ?? throw new InvalidDataException("Nota fora do perfil.");
                    var incompatible = active.Where(pair => pair.Value.VirtualKey == key.VirtualKey ||
                        pair.Value.RequiresShift != key.RequiresShift).Select(pair => pair.Key).ToArray();
                    if (incompatible.Length > 0)
                    {
                        if (batch.Count > 0) { sender.SendBatch(batch, options.InputMode, options.RequireFocus, token); batch.Clear(); }
                        conflicts += incompatible.Length;
                        NoteOff(incompatible, options);
                    }
                    if (key.RequiresShift && !active.Values.Any(n => n.RequiresShift)) batch.Add(new(Shift));
                    batch.Add(new(key.VirtualKey)); active[attack.Id] = key; noteOns++;
                }
                if (batch.Count > 0) sender.SendBatch(batch, options.InputMode, options.RequireFocus, token);
            }
            catch { ReleaseAllKeys(); throw; }
        }
    }
    public void NoteOff(IReadOnlyList<long> ids, PlaybackOptions options)
    {
        lock (noteSync)
        {
            var releases = new List<KeyInput>();
            bool hadShift = active.Values.Any(n => n.RequiresShift);
            foreach (long id in ids)
                if (active.Remove(id, out var key)) { releases.Add(new(key.VirtualKey, true)); noteOffs++; }
            if (hadShift && !active.Values.Any(n => n.RequiresShift)) releases.Add(new(Shift, true));
            try { if (releases.Count > 0) sender.SendBatch(releases, options.InputMode, false, CancellationToken.None); }
            catch { ReleaseAllKeys(); throw; }
        }
    }
    public Task PlayNote(int midi, PlaybackOptions options, CancellationToken token) => PlayChord([midi], options, token);
    public async Task PlayChord(IReadOnlyList<int> notes, PlaybackOptions options, CancellationToken token)
    {
        try
        {
            var keys = notes.Distinct().Select(n => mapping.FromMidi(n) ?? throw new ArgumentOutOfRangeException(nameof(notes))).ToArray();
            bool hasShift = keys.Any(k => k.RequiresShift);
            // Mixed shifted/unshifted chords cannot be represented by one global Shift state.
            bool spread = options.ChordMode == ChordMode.Spread || hasShift;
            if (!spread)
            {
                sender.SendBatch(keys.Select(key => new KeyInput(key.VirtualKey)).ToArray(),
                    options.InputMode, options.RequireFocus, token);
                TrackLegacyDown(keys);
                await Task.Delay(options.HoldMs, token).ConfigureAwait(false);
            }
            else
            {
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < keys.Length;)
                {
                    int wait = (int)(i * options.SpreadMs - watch.Elapsed.TotalMilliseconds);
                    if (wait > 0) await Task.Delay(wait, token).ConfigureAwait(false);
                    var key = keys[i];
                    int end = i + 1;
                    if (options.ChordMode != ChordMode.Spread && options.ShiftStrategy == ShiftStrategy.GroupSafe)
                    {
                        // Only contiguous notes sharing the same Shift state and distinct physical keys
                        // can be grouped. Never reorder mixed notes across a Shift transition.
                        var physicalKeys = new HashSet<ushort> { key.VirtualKey };
                        while (end < keys.Length && keys[end].RequiresShift == key.RequiresShift &&
                            physicalKeys.Add(keys[end].VirtualKey)) end++;
                    }
                    if (key.RequiresShift)
                    {
                        sender.KeyDown(Shift, options.InputMode, options.RequireFocus, token);
                        await Task.Delay(1, token).ConfigureAwait(false);
                    }
                    sender.SendBatch(keys[i..end].Select(note => new KeyInput(note.VirtualKey)).ToArray(),
                        options.InputMode, options.RequireFocus, token);
                    TrackLegacyDown(keys[i..end]);
                    await Task.Delay(options.HoldMs, token).ConfigureAwait(false);
                    sender.SendBatch(keys[i..end].Select(note => new KeyInput(note.VirtualKey, true)).ToArray(),
                        options.InputMode, false, CancellationToken.None);
                    lock (noteSync) foreach (var released in keys[i..end]) if (legacyHeld.Remove(released.VirtualKey)) noteOffs++;
                    if (key.RequiresShift) sender.KeyUp(Shift);
                    i = end;
                }
            }
        }
        finally { ReleaseAllKeys(); }
    }
    private void TrackLegacyDown(IEnumerable<PianoNote> keys)
    { lock (noteSync) foreach (var key in keys) if (legacyHeld.Add(key.VirtualKey)) noteOns++; }
    public void ReleaseAllKeys()
    {
        lock (noteSync) { noteOffs += active.Count + legacyHeld.Count; active.Clear(); legacyHeld.Clear(); sender.ReleaseAllKeys(); }
    }
}

