using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed record SheetResult(MusicSequence Sequence, IReadOnlyList<string> Errors, string InvalidCharacters, int UnmappedCount);

public sealed class SheetParser(PianoMappingService mapping)
{
    public SheetResult Parse(string text, double bpm = 120, double spaceBeats = .25, double dashBeats = .5)
    {
        if (!double.IsFinite(bpm) || bpm is < 20 or > 400) throw new ArgumentException("BPM deve estar entre 20 e 400.");
        if (!double.IsFinite(spaceBeats) || spaceBeats <= 0 || !double.IsFinite(dashBeats) || dashBeats <= 0)
            throw new ArgumentException("Duração do espaço e da pausa deve ser positiva.");
        var errors = new List<string>();
        var invalid = new List<char>();
        var events = new List<MusicEvent>();
        double beat = 60000 / bpm, time = 0;
        bool pendingStep = false;
        // A token has a default step only when the next token has no explicit separator.
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                // CRLF is one separator. Each other space/tab/newline counts once.
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                time += beat * spaceBeats; pendingStep = false; continue;
            }
            if (c == '-') { time += beat * dashBeats; pendingStep = false; continue; }
            if (pendingStep) time += beat * spaceBeats;
            pendingStep = false;
            if (c is '[' or '{')
            {
                char closing = c == '[' ? ']' : '}';
                int end = text.IndexOf(closing, i + 1);
                if (end < 0) { errors.Add($"Grupo aberto na posição {i + 1} sem '{closing}'."); break; }
                var notes = new List<int>();
                for (int j = i + 1; j < end; j++)
                {
                    if (mapping.FromKey(text[j]) is { } note) notes.Add(note.MidiNote);
                    else { invalid.Add(text[j]); errors.Add($"Caractere inválido '{text[j]}' na posição {j + 1}."); }
                }
                if (notes.Count == 0) errors.Add($"Grupo vazio na posição {i + 1}.");
                else if (c == '[') events.Add(new(time, notes.Distinct().ToArray()));
                else
                {
                    for (int j = 0; j < notes.Count; j++) events.Add(new(time + j * beat / 16, [notes[j]]));
                    time += (notes.Count - 1) * beat / 16;
                }
                pendingStep = notes.Count > 0;
                i = end;
            }
            else if (mapping.FromKey(c) is { } note)
            { events.Add(new(time, [note.MidiNote])); pendingStep = true; }
            else { invalid.Add(c); errors.Add($"Caractere inválido '{c}' na posição {i + 1}."); }
        }
        double duration = events.Count == 0 ? 0 : Math.Max(time, events[^1].TimeMs + beat * spaceBeats);
        return new(new("Sheet", bpm, duration, events), errors, string.Join(" ", invalid.Distinct()), invalid.Count);
    }
}

