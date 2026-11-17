using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed record PianoCompatibility(int Total, int Below, int Inside, int Above, int SuggestedTranspose, int SuggestedInside)
{
    public int Outside => Below + Above;
    public double Percent => Total == 0 ? 100 : Inside * 100d / Total;
    public double SuggestedPercent => Total == 0 ? 100 : SuggestedInside * 100d / Total;
}

public static class PianoCompatibilityAnalyzer
{
    public static PianoCompatibility Analyze(IEnumerable<int> source, int transpose = 0, PianoProfile? profile = null)
    {
        profile ??= PianoProfile.Legacy61;
        var pitches = source.ToArray();
        int Coverage(int shift) => pitches.Count(n => n + shift >= profile.FirstMidiNote && n + shift <= profile.LastMidiNote);
        int suggestion = Enumerable.Range(-24, 49).OrderByDescending(Coverage).ThenBy(Math.Abs).ThenBy(n => n).First();
        return new(pitches.Length, pitches.Count(n => n + transpose < profile.FirstMidiNote), Coverage(transpose),
            pitches.Count(n => n + transpose > profile.LastMidiNote), suggestion, Coverage(suggestion));
    }
}
