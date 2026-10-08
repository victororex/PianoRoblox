namespace PianoRoblox.Core.Models;

public enum RangePolicy { Ignore, Octave, Ask }
public enum ChordMode { Auto, Simultaneous, Spread }
public enum InputMode { ScanCode, VirtualKey }
public enum PlaybackPerformanceMode { Normal, LowLatency, FpsFriendly }
public enum ShiftStrategy { Sequential, GroupSafe }

public sealed class AppSettings
{
    public int ChordSpreadMs { get; set; } = 8;
    public int KeyHoldMs { get; set; } = 8;
    public int CountdownSeconds { get; set; } = 3;
    public int CountdownBetweenSongs { get; set; }
    public double DefaultSpeed { get; set; } = 100;
    public InputMode InputMode { get; set; } = InputMode.ScanCode;
    public ChordMode ChordMode { get; set; } = ChordMode.Auto;
    public PlaybackPerformanceMode PerformanceMode { get; set; } = PlaybackPerformanceMode.Normal;
    public bool RequireRobloxFocus { get; set; } = true;
    public RangePolicy RangePolicy { get; set; } = RangePolicy.Ignore;
    public bool ShowKeyboard { get; set; } = true;
    public bool DarkTheme { get; set; } = true;
    public bool DetailedLog { get; set; }
    public Dictionary<string, int> Hotkeys { get; set; } = new()
    {
        ["Play"] = 0x75, ["Pause"] = 0x76, ["Stop"] = 0x77,
        ["Faster"] = 0x78, ["Slower"] = 0x79, ["Normal"] = 0x7A,
        ["Previous"] = 0x73, ["Next"] = 0x74
    };

    public void Validate()
    {
        ChordSpreadMs = Math.Clamp(ChordSpreadMs, 0, 50);
        KeyHoldMs = Math.Clamp(KeyHoldMs, 1, 100);
        CountdownSeconds = Math.Clamp(CountdownSeconds, 0, 10);
        CountdownBetweenSongs = Math.Clamp(CountdownBetweenSongs, 0, 10);
        DefaultSpeed = double.IsFinite(DefaultSpeed) ? Math.Clamp(DefaultSpeed, 50, 150) : 100;
        if (!Enum.IsDefined(InputMode)) InputMode = InputMode.ScanCode;
        if (!Enum.IsDefined(ChordMode)) ChordMode = ChordMode.Auto;
        if (!Enum.IsDefined(PerformanceMode)) PerformanceMode = PlaybackPerformanceMode.Normal;
        if (!Enum.IsDefined(RangePolicy)) RangePolicy = RangePolicy.Ignore;
        Hotkeys ??= new AppSettings().Hotkeys;
        if (!Hotkeys.ContainsKey("Previous") && !Hotkeys.ContainsValue(0x73)) Hotkeys["Previous"] = 0x73;
        if (!Hotkeys.ContainsKey("Next") && !Hotkeys.ContainsValue(0x74)) Hotkeys["Next"] = 0x74;
    }
}

public sealed record PlaybackOptions(int SpreadMs, int HoldMs, ChordMode ChordMode, InputMode InputMode, bool RequireFocus,
    PlaybackPerformanceMode PerformanceMode = PlaybackPerformanceMode.Normal, int MaxCatchUpDelayMs = 80,
    ShiftStrategy ShiftStrategy = ShiftStrategy.GroupSafe);

