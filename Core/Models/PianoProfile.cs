namespace PianoRoblox.Core.Models;

/// <summary>One place describes the playable range and chromatic keyboard layout.</summary>
public sealed record PianoProfile(string Id, string Name, int FirstMidiNote, string KeyboardCharacters)
{
    public int LastMidiNote => FirstMidiNote + KeyboardCharacters.Length - 1;
    public static PianoProfile Legacy61 { get; } = new("legacy-61", "Legacy C2–C7 61 Keys", 36,
        "1!2@34$5%6^78*9(0qQwWeErtTyYuiIoOpPasSdDfgGhHjJklLzZxcCvVbBnm");
    public static IReadOnlyList<PianoProfile> Available { get; } = [Legacy61];
}
