using System.Diagnostics;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;

namespace PianoRoblox.Services;

public sealed class KeyboardPlayer(PianoMappingService mapping, IKeySender sender) : IKeyboardPlayer
{
    private const ushort Shift = 0xA0;
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
                    await Task.Delay(options.HoldMs, token).ConfigureAwait(false);
                    sender.SendBatch(keys[i..end].Select(note => new KeyInput(note.VirtualKey, true)).ToArray(),
                        options.InputMode, false, CancellationToken.None);
                    if (key.RequiresShift) sender.KeyUp(Shift);
                    i = end;
                }
            }
        }
        finally { sender.ReleaseAllKeys(); }
    }
    public void ReleaseAllKeys() => sender.ReleaseAllKeys();
}

