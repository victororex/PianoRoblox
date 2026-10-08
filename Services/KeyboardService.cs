using System.ComponentModel;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;

namespace PianoRoblox.Services;

public interface IKeySender
{
    void KeyDown(ushort key, InputMode mode, bool requireFocus, CancellationToken token);
    void KeyUp(ushort key);
    void ReleaseAllKeys();
    // V1 integrations remain compatible; the native sender overrides this with one SendInput call.
    void SendBatch(IReadOnlyList<KeyInput> inputs, InputMode mode, bool requireFocus, CancellationToken token)
    {
        foreach (var input in inputs)
        {
            if (input.IsKeyUp) KeyUp(input.Key);
            else KeyDown(input.Key, mode, requireFocus, token);
        }
    }
}

public sealed class KeyboardService(Func<bool> isFocused, IKeyboardInputTransport? transport = null) : IKeySender
{
    private readonly object sync = new();
    private readonly Dictionary<ushort, InputMode> held = new();
    private readonly IKeyboardInputTransport inputTransport = transport ?? new User32KeyboardInputTransport();
    private static readonly ushort[] Modifiers = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];
    public void KeyDown(ushort key, InputMode mode, bool requireFocus, CancellationToken token) =>
        SendBatch([new(key)], mode, requireFocus, token);

    public void SendBatch(IReadOnlyList<KeyInput> inputs, InputMode mode, bool requireFocus, CancellationToken token)
    {
        lock (sync)
        {
            try
            {
                // The focus/token checks share the held-key lock with injection and emergency cleanup.
                token.ThrowIfCancellationRequested();
                if (inputs.Any(input => !input.IsKeyUp) && requireFocus && !isFocused()) throw new FocusLostException();
                var packets = new List<KeyboardInput>(inputs.Count);
                var releases = new List<ushort>();
                foreach (var input in inputs)
                {
                    if (input.IsKeyUp)
                    {
                        if (!held.TryGetValue(input.Key, out var heldMode)) continue;
                        packets.Add(KeyboardInputBuilder.Build(input, heldMode, inputTransport.MapScanCode));
                        releases.Add(input.Key);
                    }
                    else
                    {
                        if (held.ContainsKey(input.Key)) continue;
                        // Track the entire intended down batch before SendInput: partial injection is uncertain.
                        held[input.Key] = mode;
                        packets.Add(KeyboardInputBuilder.Build(input, mode, inputTransport.MapScanCode));
                    }
                }
                if (packets.Count == 0) return;
                SendChecked(packets.ToArray());
                foreach (ushort key in releases) held.Remove(key);
            }
            catch
            {
                ReleaseAllKeysCore();
                throw;
            }
        }
    }

    public void KeyUp(ushort key) => SendBatch([new(key, true)], InputMode.ScanCode, false, CancellationToken.None);
    public void ReleaseAllKeys() { lock (sync) ReleaseAllKeysCore(); }

    private void ReleaseAllKeysCore()
    {
        // Notes precede modifiers. Explicit modifier ups also cover an interrupted Shift sequence.
        var releases = held.Where(pair => !Modifiers.Contains(pair.Key)).ToList();
        releases.AddRange(Modifiers.Select(key => new KeyValuePair<ushort, InputMode>(key,
            held.GetValueOrDefault(key, InputMode.VirtualKey))));
        foreach (var pair in releases) held.TryAdd(pair.Key, pair.Value);
        try
        {
            var packets = releases.Select(pair => KeyboardInputBuilder.Build(new(pair.Key, true), pair.Value,
                inputTransport.MapScanCode)).ToArray();
            uint sent = inputTransport.Send(packets);
            // SendInput inserts packets in order; retain the unsent suffix for another cleanup attempt.
            for (int i = 0; i < Math.Min(sent, (uint)releases.Count); i++) held.Remove(releases[i].Key);
        }
        catch (Exception)
        {
            // Cleanup is best effort; retain uncertain keys without masking the original exception.
        }
    }

    private void SendChecked(KeyboardInput[] inputs)
    {
        if (inputTransport.Send(inputs) != (uint)inputs.Length)
            throw new Win32Exception(inputTransport.LastError,
                "SendInput enviou apenas parte do lote ou falhou. Verifique permissões e o modo de envio nas configurações.");
    }
}
