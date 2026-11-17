namespace PianoRoblox.Core.Models;

public enum NoteDurationMode { LegacyTap, MidiSustain, Hybrid }

public sealed record NoteDurationSettings
{
    // Missing fields in V2 settings retain V2 playback. New MIDI imports opt into Hybrid.
    public NoteDurationMode Mode { get; set; } = NoteDurationMode.LegacyTap;
    public bool SustainEnabled { get; set; } = true;
    public int MinimumHoldMs { get; set; } = 8;
    public int MaximumHoldMs { get; set; } // 0 = unlimited; never silently truncate long MIDI notes.
    public int ReleaseGapMs { get; set; } = 2;
    public bool NoteOverlap { get; set; } = true;
    public bool RetriggerSameNote { get; set; } = true;
    public int EstimatedHoldMs { get; set; } = 8;

    public NoteDurationSettings Validated() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : NoteDurationMode.LegacyTap,
        MinimumHoldMs = Math.Clamp(MinimumHoldMs, 1, 1000),
        MaximumHoldMs = MaximumHoldMs <= 0 ? 0 : Math.Clamp(MaximumHoldMs, Math.Clamp(MinimumHoldMs, 1, 1000), 600000),
        ReleaseGapMs = Math.Clamp(ReleaseGapMs, 1, 100),
        EstimatedHoldMs = Math.Clamp(EstimatedHoldMs, 1, 1000)
    };
}
