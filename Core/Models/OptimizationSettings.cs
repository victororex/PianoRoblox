namespace PianoRoblox.Core.Models;

public enum OptimizationPreset { Auto, Original, Balanced, Performance, Custom }

public sealed record OptimizationSettings
{
    public OptimizationPreset Preset { get; set; } = OptimizationPreset.Auto;
    public bool RemovePercussion { get; set; } = true;
    public double MergeWindowMs { get; set; } = 6;
    public int MaxChordNotes { get; set; } = 6;
    public int MinimumVelocity { get; set; } = 15;
    public int MaxNotesPer250Ms { get; set; } = 10;
    public int MaxNotesPerSecond { get; set; } = 32;
    public bool MelodyPriority { get; set; } = true;

    public OptimizationSettings Validated() => this with
    {
        Preset = Enum.IsDefined(Preset) ? Preset : OptimizationPreset.Auto,
        MergeWindowMs = double.IsFinite(MergeWindowMs) ? Math.Clamp(MergeWindowMs, 0, 30) : 6,
        MaxChordNotes = Math.Clamp(MaxChordNotes, 1, 128),
        MinimumVelocity = Math.Clamp(MinimumVelocity, 0, 127),
        MaxNotesPer250Ms = Math.Clamp(MaxNotesPer250Ms, 1, 10000),
        MaxNotesPerSecond = Math.Clamp(MaxNotesPerSecond, 1, 40000)
    };

    public static OptimizationSettings ForPreset(OptimizationPreset preset) => preset switch
    {
        OptimizationPreset.Original => new() { Preset = preset, MergeWindowMs = 0, MaxChordNotes = 128,
            MinimumVelocity = 0, MaxNotesPer250Ms = 10000, MaxNotesPerSecond = 40000 },
        OptimizationPreset.Performance => new() { Preset = preset, MergeWindowMs = 8, MaxChordNotes = 4,
            MinimumVelocity = 25, MaxNotesPer250Ms = 7, MaxNotesPerSecond = 22 },
        _ => new() { Preset = preset }
    };
}
