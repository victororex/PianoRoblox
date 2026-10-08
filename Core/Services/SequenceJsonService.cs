using System.Text.Json;
using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class SequenceJsonService(PianoMappingService mapping)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public sealed record KeyEvent(double TimeMs, string[] Keys);
    public sealed record Document(string Title, List<KeyEvent> Events, double Bpm = 120, double DurationMs = 0);

    public async Task ExportAsync(string path, MusicSequence sequence)
    {
        var doc = new Document(sequence.Title, sequence.Events.Select(e => new KeyEvent(e.TimeMs,
            e.Notes.Select(n => mapping.FromMidi(n)?.KeyboardKey.ToString() ?? throw new InvalidDataException("Nota fora do alcance.")).ToArray())).ToList(), sequence.InitialBpm, sequence.DurationMs);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(doc, Options));
    }

    public async Task<MusicSequence> ImportAsync(string path)
    {
        var doc = JsonSerializer.Deserialize<Document>(await File.ReadAllTextAsync(path), Options)
            ?? throw new InvalidDataException("JSON vazio.");
        if (doc.Events == null || doc.Events.Count == 0) throw new InvalidDataException("JSON sem eventos.");
        var events = new List<MusicEvent>();
        foreach (var e in doc.Events)
        {
            if (e == null || !double.IsFinite(e.TimeMs) || e.TimeMs < 0 || e.Keys == null || e.Keys.Length == 0) throw new InvalidDataException("Evento JSON inválido.");
            var notes = e.Keys.Select(k => k is { Length: 1 } && mapping.FromKey(k[0]) is { } note ? note.MidiNote : throw new InvalidDataException($"Tecla inválida: {k}")).Distinct().ToArray();
            events.Add(new(e.TimeMs, notes));
        }
        if (!double.IsFinite(doc.Bpm) || doc.Bpm <= 0 || !double.IsFinite(doc.DurationMs) || doc.DurationMs < 0) throw new InvalidDataException("BPM ou duração inválida.");
        events = events.GroupBy(e => e.TimeMs).OrderBy(g => g.Key).Select(g => new MusicEvent(g.Key, g.SelectMany(e => e.Notes).Distinct().ToArray())).ToList();
        return new(doc.Title ?? "Sequência", doc.Bpm, Math.Max(doc.DurationMs, events[^1].TimeMs + 100), events);
    }
}

