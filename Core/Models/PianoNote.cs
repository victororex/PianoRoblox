namespace PianoRoblox.Core.Models;

public sealed record PianoNote(int MidiNote, string NoteName, char KeyboardKey, ushort VirtualKey, bool RequiresShift)
{
    public bool IsBlack => RequiresShift;
    public int Octave => MidiNote / 12 - 1;
}

