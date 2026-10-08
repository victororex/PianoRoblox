using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class PianoMappingService
{
    public PianoProfile Profile { get; }
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    public IReadOnlyList<PianoNote> All { get; }
    private readonly Dictionary<char, PianoNote> byKey;

    public PianoMappingService(PianoProfile? profile = null)
    {
        Profile = profile ?? PianoProfile.Legacy61;
        if (Profile.FirstMidiNote < 0 || Profile.LastMidiNote > 127 || Profile.KeyboardCharacters.Length == 0 ||
            Profile.KeyboardCharacters.Distinct().Count() != Profile.KeyboardCharacters.Length)
            throw new ArgumentException("Perfil de piano inválido.", nameof(profile));
        All = Profile.KeyboardCharacters.Select((key, i) => new PianoNote(i + Profile.FirstMidiNote, Name(i + Profile.FirstMidiNote), key, BaseKey(key), char.IsUpper(key) || "!@#$%^&*()".Contains(key))).ToArray();
        byKey = All.ToDictionary(k => k.KeyboardKey);
    }

    public PianoNote? FromMidi(int midi) => midi >= Profile.FirstMidiNote && midi <= Profile.LastMidiNote ? All[midi - Profile.FirstMidiNote] : null;
    public PianoNote? FromKey(char key) => byKey.GetValueOrDefault(key);
    public static string Name(int midi) => $"{Names[((midi % 12) + 12) % 12]}{(int)Math.Floor(midi / 12d) - 1}";
    private static ushort BaseKey(char key)
    {
        const string shifted = "!@#$%^&*()";
        var index = shifted.IndexOf(key);
        return index >= 0 ? (ushort)"1234567890"[index] : char.ToUpperInvariant(key);
    }
}

