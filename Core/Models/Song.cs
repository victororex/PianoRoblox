namespace PianoRoblox.Core.Models;

public sealed class Song
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Sem título";
    public string Artist { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Kind { get; set; } = "Sheet";
    public string FileName { get; set; } = "";
    public double Bpm { get; set; } = 120;
    public int Transpose { get; set; }
    public double Speed { get; set; } = 100;
    public int ChordSpread { get; set; } = 8;
    public double DurationMs { get; set; }
    public double SpaceBeats { get; set; } = .25;
    public double DashBeats { get; set; } = .5;
    public OptimizationSettings Optimization { get; set; } = new();
    public int[]? SelectedTracks { get; set; }
    public PlaybackPerformanceMode PerformanceMode { get; set; } = PlaybackPerformanceMode.Normal;
    public string PianoProfileId { get; set; } = "legacy-61";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}

